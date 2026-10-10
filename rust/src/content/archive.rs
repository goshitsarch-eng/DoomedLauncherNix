use super::{extension, is_archive};
use crate::error::{Error, Result};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::HashSet,
    fs::{self, File, OpenOptions},
    io::{Read, Seek, SeekFrom, Write},
    path::{Component, Path, PathBuf},
};
use zip::ZipArchive;

const MAX_ARCHIVE_ENTRIES: usize = 65_536;
const MAX_MEMBER_BYTES: u64 = 1024 * 1024 * 1024;
const MAX_ARCHIVE_BYTES: u64 = 8 * 1024 * 1024 * 1024;
const MAX_EXTRACTED_BYTES: u64 = 2 * 1024 * 1024 * 1024;
const MAX_DIRECTORY_BYTES: u64 = 64 * 1024 * 1024;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ArchiveEntry {
    pub name: String,
    pub size: u64,
    pub extract_required: bool,
    pub full_path: Option<PathBuf>,
}

/// Portable basename validation, including Windows device names and streams.
pub fn safe_filename(name: &str) -> bool {
    if name.is_empty()
        || name == "."
        || name == ".."
        || name.len() > 255
        || name.ends_with(['.', ' '])
        || name
            .chars()
            .any(|c| c.is_control() || "/\\:*?\"<>|".contains(c))
    {
        return false;
    }
    let stem = name
        .split('.')
        .next()
        .unwrap_or("")
        .trim_end()
        .to_ascii_uppercase();
    if matches!(
        stem.as_str(),
        "CON" | "PRN" | "AUX" | "NUL" | "CONIN$" | "CONOUT$"
    ) {
        return false;
    }
    if let Some(number) = stem
        .strip_prefix("COM")
        .or_else(|| stem.strip_prefix("LPT"))
        && matches!(
            number,
            "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" | "¹" | "²" | "³"
        )
    {
        return false;
    }
    true
}

fn safe_member(name: &str) -> bool {
    !name.is_empty()
        && name.len() <= 4096
        && !name.contains('\\')
        && name.trim_end_matches('/').split('/').all(safe_filename)
}

pub(crate) fn open_archive(path: &Path) -> Result<ZipArchive<File>> {
    let mut file = File::open(path)?;
    let metadata = file.metadata()?;
    if !metadata.is_file() || metadata.len() > MAX_ARCHIVE_BYTES {
        return Err(Error::validation(
            "Archive is not a regular file or exceeds 8 GiB",
        ));
    }
    check_directory_limits(&mut file, metadata.len())?;
    let mut archive = ZipArchive::new(file)?;
    if archive.len() > MAX_ARCHIVE_ENTRIES {
        return Err(Error::corrupt("Archive contains too many members"));
    }
    let mut names = HashSet::new();
    for i in 0..archive.len() {
        let entry = archive.by_index_raw(i)?;
        if !safe_member(entry.name()) || !names.insert(entry.name().to_owned()) {
            return Err(Error::corrupt(
                "Archive contains an unsafe or duplicated member name",
            ));
        }
        if entry.unix_mode().is_some_and(|mode| {
            let kind = mode & 0o170000;
            kind != 0 && kind != 0o100000 && kind != 0o040000
        }) {
            return Err(Error::corrupt(
                "Archive links and special files are not supported",
            ));
        }
    }
    Ok(archive)
}

/// Check ZIP/ZIP64 resource bounds before ZipArchive allocates its directory.
/// Only the bounded EOCD search window and fixed ZIP64 records are read here.
fn check_directory_limits(file: &mut File, length: u64) -> Result<()> {
    let window = length.min(65_535 + 22) as usize;
    if window < 22 {
        return Err(Error::corrupt("Truncated ZIP directory"));
    }
    file.seek(SeekFrom::End(-(window as i64)))?;
    let mut tail = vec![0; window];
    file.read_exact(&mut tail)?;
    let position = (0..=window - 22)
        .rev()
        .find(|&position| {
            tail[position..position + 4] == *b"PK\x05\x06"
                && position
                    + 22
                    + usize::from(u16::from_le_bytes(
                        tail[position + 20..position + 22].try_into().unwrap(),
                    ))
                    == window
        })
        .ok_or_else(|| Error::corrupt("ZIP end-of-directory record is missing"))?;
    let record = &tail[position..position + 22];
    let u16_at = |offset| u16::from_le_bytes(record[offset..offset + 2].try_into().unwrap());
    let u32_at = |offset| u32::from_le_bytes(record[offset..offset + 4].try_into().unwrap());
    if u16_at(4) != 0 || u16_at(6) != 0 || u16_at(8) != u16_at(10) {
        return Err(Error::corrupt(
            "Multi-volume ZIP archives are not supported",
        ));
    }
    let eocd_offset = length - window as u64 + position as u64;
    let mut count = u64::from(u16_at(10));
    let mut directory_size = u64::from(u32_at(12));
    let mut directory_offset = u64::from(u32_at(16));
    if eocd_offset >= 20 {
        file.seek(SeekFrom::Start(eocd_offset - 20))?;
        let mut locator = [0; 20];
        file.read_exact(&mut locator)?;
        if locator.starts_with(b"PK\x06\x07") {
            let zip64_offset = u64::from_le_bytes(locator[8..16].try_into().unwrap());
            if locator[4..8] != [0; 4]
                || u32::from_le_bytes(locator[16..20].try_into().unwrap()) != 1
                || zip64_offset
                    .checked_add(56)
                    .is_none_or(|end| end > eocd_offset - 20)
            {
                return Err(Error::corrupt("Invalid ZIP64 locator"));
            }
            file.seek(SeekFrom::Start(zip64_offset))?;
            let mut zip64 = [0; 56];
            file.read_exact(&mut zip64)?;
            if !zip64.starts_with(b"PK\x06\x06")
                || zip64[16..24] != [0; 8]
                || zip64[24..32] != zip64[32..40]
            {
                return Err(Error::corrupt("Invalid ZIP64 directory"));
            }
            count = u64::from_le_bytes(zip64[32..40].try_into().unwrap());
            directory_size = u64::from_le_bytes(zip64[40..48].try_into().unwrap());
            directory_offset = u64::from_le_bytes(zip64[48..56].try_into().unwrap());
        }
    }
    if count > MAX_ARCHIVE_ENTRIES as u64
        || directory_size > MAX_DIRECTORY_BYTES
        || directory_offset
            .checked_add(directory_size)
            .is_none_or(|end| end > eocd_offset)
    {
        return Err(Error::corrupt(
            "ZIP directory exceeds resource limits or file bounds",
        ));
    }
    file.seek(SeekFrom::Start(0))?;
    Ok(())
}

pub fn entries(path: &Path) -> Result<Vec<ArchiveEntry>> {
    if !is_archive(path) {
        let canonical = fs::canonicalize(path)?;
        let metadata = canonical.metadata()?;
        if !metadata.is_file() {
            return Err(Error::validation("Expected a regular file"));
        }
        let name = canonical
            .file_name()
            .and_then(|s| s.to_str())
            .ok_or_else(|| Error::validation("File name is not valid Unicode"))?
            .to_owned();
        return Ok(vec![ArchiveEntry {
            name,
            size: metadata.len(),
            extract_required: false,
            full_path: Some(canonical),
        }]);
    }
    let mut archive = open_archive(path)?;
    let mut result = Vec::new();
    for index in 0..archive.len() {
        let entry = archive.by_index_raw(index)?;
        if !entry.is_dir() {
            result.push(ArchiveEntry {
                name: entry.name().to_owned(),
                size: entry.size(),
                extract_required: true,
                full_path: None,
            });
        }
    }
    Ok(result)
}

pub(crate) fn read_member(path: &Path, name: &str, limit: u64) -> Result<Vec<u8>> {
    read_archive_member(&mut open_archive(path)?, name, limit)
}

pub(crate) fn read_archive_member(
    archive: &mut ZipArchive<File>,
    name: &str,
    limit: u64,
) -> Result<Vec<u8>> {
    let mut member = archive.by_name(name)?;
    if member.is_dir() || member.size() > limit {
        return Err(Error::corrupt(format!(
            "Archive member {name} exceeds its size limit"
        )));
    }
    let expected = member.size();
    let mut bytes = Vec::new();
    (&mut member).take(limit + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 != expected || bytes.len() as u64 > limit {
        return Err(Error::corrupt(
            "Archive member has an invalid expanded size",
        ));
    }
    Ok(bytes)
}

pub(crate) fn safe_directory(path: &Path) -> Result<PathBuf> {
    let absolute = if path.is_absolute() {
        path.to_owned()
    } else {
        std::env::current_dir()?.join(path)
    };
    let mut cursor = PathBuf::new();
    for component in absolute.components() {
        match component {
            Component::ParentDir => {
                return Err(Error::validation(
                    "Cache directory must not contain parent traversals",
                ));
            }
            Component::CurDir => continue,
            _ => cursor.push(component.as_os_str()),
        }
        match fs::symlink_metadata(&cursor) {
            Ok(metadata) if metadata.is_dir() && !metadata.file_type().is_symlink() => {}
            Ok(_) => {
                return Err(Error::validation(
                    "Cache directories must not contain links or files",
                ));
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => {
                match fs::create_dir(&cursor) {
                    Ok(()) => {}
                    Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {}
                    Err(e) => return Err(e.into()),
                }
                let metadata = fs::symlink_metadata(&cursor)?;
                if !metadata.is_dir() || metadata.file_type().is_symlink() {
                    return Err(Error::validation("Unsafe cache directory"));
                }
            }
            Err(e) => return Err(e.into()),
        }
    }
    Ok(absolute)
}

/// Resolve the caller-selected cache boundary once. Trusted system ancestors
/// can be aliases (for example macOS /var -> /private/var); the cache root
/// itself and every directory created beneath it must never be a link.
pub(crate) fn cache_root(path: &Path) -> Result<PathBuf> {
    if path.components().any(|c| matches!(c, Component::ParentDir)) {
        return Err(Error::validation(
            "Cache directory must not contain parent traversals",
        ));
    }
    match fs::symlink_metadata(path) {
        Ok(metadata) if metadata.file_type().is_symlink() || !metadata.is_dir() => {
            return Err(Error::validation(
                "Cache root must be a directory, not a link",
            ));
        }
        Ok(_) => {}
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            fs::create_dir_all(path)?;
            let metadata = fs::symlink_metadata(path)?;
            if metadata.file_type().is_symlink() || !metadata.is_dir() {
                return Err(Error::validation("Unsafe cache root"));
            }
        }
        Err(error) => return Err(error.into()),
    }
    Ok(fs::canonicalize(path)?)
}

fn hash_file(path: &Path) -> Result<Vec<u8>> {
    let mut source = File::open(path)?;
    let mut digest = Sha256::new();
    let mut buffer = [0; 64 * 1024];
    loop {
        let size = source.read(&mut buffer)?;
        if size == 0 {
            break;
        }
        digest.update(&buffer[..size]);
    }
    Ok(digest.finalize().to_vec())
}

fn archive_key(path: &Path) -> Result<Vec<u8>> {
    let mut digest = Sha256::new();
    digest.update(fs::canonicalize(path)?.as_os_str().as_encoded_bytes());
    digest.update([0]);
    digest.update(hash_file(path)?);
    Ok(digest.finalize().to_vec())
}

struct Temporary {
    path: PathBuf,
}
impl Drop for Temporary {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.path);
    }
}

/// Atomic, no-overwrite cache writes. Existing output is checked before reuse.
pub(crate) fn write_cached<F>(directory: &Path, name: &str, write: F) -> Result<PathBuf>
where
    F: FnOnce(&mut File) -> Result<()>,
{
    if !safe_filename(name) {
        return Err(Error::validation("Invalid cache file name"));
    }
    let directory = safe_directory(directory)?;
    let target = directory.join(name);
    let temporary = Temporary {
        path: directory.join(format!(".{}.tmp", uuid::Uuid::new_v4())),
    };
    let mut output = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary.path)?;
    write(&mut output)?;
    output.flush()?;
    output.sync_all()?;
    drop(output);
    // Hard-link publication is atomic and, unlike rename, never replaces a
    // pre-existing destination. All supported native platforms support it.
    match fs::hard_link(&temporary.path, &target) {
        Ok(()) => {}
        Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {
            let metadata = fs::symlink_metadata(&target)?;
            if !metadata.is_file()
                || metadata.file_type().is_symlink()
                || metadata.len() != fs::metadata(&temporary.path)?.len()
                || hash_file(&target)? != hash_file(&temporary.path)?
            {
                return Err(Error::corrupt(
                    "Existing cache file does not match extracted content",
                ));
            }
        }
        Err(e) => return Err(e.into()),
    }
    Ok(target)
}

fn extract_member(
    archive: &mut ZipArchive<File>,
    key: &[u8],
    name: &str,
    cache: &Path,
    limit: u64,
) -> Result<PathBuf> {
    let mut member = archive.by_name(name)?;
    if !safe_member(name) || member.is_dir() || member.size() > limit {
        return Err(Error::corrupt("Unsafe or oversized archive member"));
    }
    let mut digest = Sha256::new();
    digest.update(key);
    digest.update([0]);
    digest.update(name.as_bytes());
    let directory = cache_root(cache)?
        .join("members")
        .join(format!("{:x}", digest.finalize()));
    let basename = name
        .rsplit('/')
        .next()
        .ok_or_else(|| Error::corrupt("Missing member basename"))?;
    let expected = member.size();
    write_cached(&directory, basename, |output| {
        let count = std::io::copy(&mut (&mut member).take(limit + 1), output)?;
        if count != expected || count > limit {
            return Err(Error::corrupt("Archive expanded beyond its declared size"));
        }
        Ok(())
    })
}

pub(crate) fn extract_one(path: &Path, name: &str, cache: &Path, limit: u64) -> Result<PathBuf> {
    let mut archive = open_archive(path)?;
    let key = archive_key(path)?;
    extract_member(&mut archive, &key, name, cache, limit)
}

/// Resolve launch inputs, preserving the requested member order. PK3 files
/// remain intact. Empty member selection selects all matching ZIP members.
pub fn prepare_files(
    path: &Path,
    extensions: &[String],
    specific: &[String],
    cache: &Path,
) -> Result<Vec<PathBuf>> {
    let accepted: HashSet<String> = extensions
        .iter()
        .map(|s| {
            s.trim()
                .trim_start_matches("*.")
                .trim_start_matches('.')
                .to_ascii_lowercase()
        })
        .collect();
    let accepts = |name: &Path| accepted.is_empty() || accepted.contains(&extension(name));
    if extension(path) != "zip" {
        let canonical = fs::canonicalize(path)?;
        if !canonical.is_file() {
            return Err(Error::validation("Launch input must be a regular file"));
        }
        if !specific.is_empty()
            && (specific.len() != 1
                || canonical.file_name().and_then(|s| s.to_str()) != Some(specific[0].as_str()))
        {
            return Err(Error::validation(
                "Requested member does not exist in this file",
            ));
        }
        if !accepts(&canonical) {
            return Err(Error::validation(
                "Source port does not support this file extension",
            ));
        }
        return Ok(vec![canonical]);
    }
    let mut archive = open_archive(path)?;
    let mut selected = Vec::new();
    if specific.is_empty() {
        for i in 0..archive.len() {
            let entry = archive.by_index_raw(i)?;
            if !entry.is_dir() && accepts(Path::new(entry.name())) {
                selected.push(entry.name().to_owned());
            }
        }
    } else {
        let mut unique = HashSet::new();
        for name in specific {
            if !safe_member(name) || !accepts(Path::new(name)) || !unique.insert(name) {
                return Err(Error::validation(
                    "Invalid or duplicated archive member selection",
                ));
            }
            selected.push(name.clone());
        }
    }
    let mut total = 0_u64;
    for name in &selected {
        let entry = archive.by_name(name)?;
        total = total
            .checked_add(entry.size())
            .ok_or_else(|| Error::corrupt("Archive size overflow"))?;
        if entry.is_dir() || entry.size() > MAX_MEMBER_BYTES || total > MAX_EXTRACTED_BYTES {
            return Err(Error::corrupt(
                "Selected archive contents exceed extraction limits",
            ));
        }
    }
    if selected.is_empty() {
        return Ok(Vec::new());
    }
    let key = archive_key(path)?;
    selected
        .iter()
        .map(|name| extract_member(&mut archive, &key, name, cache, MAX_MEMBER_BYTES))
        .collect()
}
