//! Bounded content inspection and isolated extraction for Doom engine resources.
//!
//! ZIP files are import containers; PK3/IPK3 files are resources passed to the
//! engine intact. All names and offsets are checked before content is used.

mod archive;
mod artwork;
mod wad;

use crate::error::{Error, Result};
use serde::{Deserialize, Serialize};
use std::{
    collections::HashSet,
    fs,
    io::Read,
    path::{Path, PathBuf},
};

pub use archive::{ArchiveEntry, entries, prepare_files, safe_filename};
pub use artwork::{Palette, decode_picture, default_palette};
pub use wad::{is_iwad, iwad_display_name, lump_data, lump_names, map_names};

pub(crate) const MAX_TEXT_BYTES: u64 = 8 * 1024 * 1024;
pub(crate) const MAX_IMAGE_BYTES: u64 = 64 * 1024 * 1024;
pub(crate) const MAX_WAD_BYTES: u64 = 512 * 1024 * 1024;

#[derive(Debug, Clone, Default, Serialize, Deserialize, PartialEq, Eq)]
pub struct Metadata {
    pub title: Option<String>,
    pub author: Option<String>,
    pub description: Option<String>,
    pub release_date: Option<String>,
    pub maps: Vec<String>,
    pub title_picture: Option<PathBuf>,
}

pub(crate) fn extension(path: &Path) -> String {
    path.extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase()
}

pub(crate) fn is_archive(path: &Path) -> bool {
    matches!(extension(path).as_str(), "zip" | "pk3" | "ipk3")
}

pub(crate) fn read_bounded(path: &Path, limit: u64) -> Result<Vec<u8>> {
    let file = fs::File::open(path)?;
    if !file.metadata()?.is_file() || file.metadata()?.len() > limit {
        return Err(Error::validation(
            "Content exceeds the permitted size or is not a regular file",
        ));
    }
    let mut bytes = Vec::new();
    file.take(limit + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 > limit {
        return Err(Error::validation("Content exceeds the permitted size"));
    }
    Ok(bytes)
}

/// idgames historically used Latin-1; use UTF-8 whenever the input is valid.
pub(crate) fn decode_text(bytes: &[u8]) -> String {
    std::str::from_utf8(bytes)
        .map(str::to_owned)
        .unwrap_or_else(|_| bytes.iter().map(|&b| char::from(b)).collect())
}

/// Parse an idgames template, including indented multiline descriptions.
pub fn parse_metadata_text(text: &str) -> Metadata {
    let mut result = Metadata::default();
    let mut describing = false;
    let mut description = Vec::<String>::new();
    for line in text.lines() {
        let trimmed = line.trim().trim_start_matches('\u{feff}');
        let field = trimmed
            .split_once(':')
            .map(|(k, v)| (k.trim().to_ascii_lowercase(), v.trim()));
        let known = field.as_ref().map(|(k, _)| {
            matches!(
                k.as_str(),
                "title"
                    | "author"
                    | "author(s)"
                    | "authors"
                    | "release date"
                    | "date"
                    | "description"
            )
        });
        if let Some((key, value)) = &field {
            let target = match key.as_str() {
                "title" => Some(&mut result.title),
                "author" | "author(s)" | "authors" => Some(&mut result.author),
                "date" | "release date" => Some(&mut result.release_date),
                _ => None,
            };
            if let Some(target) = target {
                if target.is_none() && !value.is_empty() {
                    *target = Some((*value).to_owned());
                }
                describing = false;
                continue;
            }
            if key == "description" {
                describing = description.is_empty();
                if describing && !value.is_empty() {
                    description.push((*value).to_owned());
                }
                continue;
            }
        }
        if describing {
            // Template headings terminate the description; a colon in a URL or
            // an indented continuation is ordinary description text.
            let template_field = field.as_ref().is_some_and(|(k, _)| {
                !k.is_empty()
                    && k.len() <= 40
                    && !k.contains('/')
                    && !k.contains('.')
                    && (k
                        .chars()
                        .all(|c| c.is_alphanumeric() || " ()/-".contains(c)))
                    && (!line.starts_with(char::is_whitespace) || line.find(':').unwrap_or(0) >= 12)
            });
            if known == Some(true)
                || template_field
                || trimmed.starts_with("===")
                || trimmed.starts_with("---")
                || trimmed.starts_with("* ")
            {
                describing = false;
            } else if trimmed.is_empty() {
                if !description.is_empty() {
                    description.push(String::new());
                }
            } else {
                description.push(trimmed.trim_start_matches(':').trim().to_owned());
            }
        }
    }
    while description.last().is_some_and(String::is_empty) {
        description.pop();
    }
    if !description.is_empty() {
        result.description = Some(description.join("\n"));
    }
    result
}

/// Text members of ZIP/PK3 files, or a matching sidecar beside a loose file.
pub fn text_files(path: &Path) -> Result<Vec<String>> {
    if is_archive(path) {
        return Ok(entries(path)?
            .into_iter()
            .filter(|e| extension(Path::new(&e.name)) == "txt")
            .map(|e| e.name)
            .collect());
    }
    let canonical = fs::canonicalize(path)?;
    if !canonical.is_file() {
        return Err(Error::validation("Expected a regular content file"));
    }
    let stem = canonical.file_stem().and_then(|s| s.to_str()).unwrap_or("");
    let mut names = Vec::new();
    for entry in fs::read_dir(
        canonical
            .parent()
            .ok_or_else(|| Error::validation("File has no parent"))?,
    )? {
        let entry = entry?;
        let p = entry.path();
        if extension(&p) == "txt"
            && p.file_stem()
                .and_then(|s| s.to_str())
                .is_some_and(|s| s.eq_ignore_ascii_case(stem))
            && entry.file_type()?.is_file()
        {
            let name = entry.file_name().to_string_lossy().into_owned();
            if safe_filename(&name) {
                names.push(name);
            }
        }
    }
    names.sort();
    Ok(names)
}

/// Resolve a text member without permitting callers to select arbitrary paths.
pub fn extract_text(path: &Path, member: &str, cache: &Path) -> Result<PathBuf> {
    if extension(Path::new(member)) != "txt" || !text_files(path)?.iter().any(|n| n == member) {
        return Err(Error::validation(
            "The selected text file is not part of this content",
        ));
    }
    if is_archive(path) {
        return archive::extract_one(path, member, cache, MAX_TEXT_BYTES);
    }
    if !safe_filename(member) {
        return Err(Error::validation("Unsafe text file name"));
    }
    let parent = fs::canonicalize(path)?
        .parent()
        .ok_or_else(|| Error::validation("File has no parent"))?
        .to_owned();
    let candidate = parent.join(member);
    if fs::symlink_metadata(&candidate)?.file_type().is_symlink() {
        return Err(Error::validation(
            "Text sidecars must not be symbolic links",
        ));
    }
    let _ = read_bounded(&candidate, MAX_TEXT_BYTES)?;
    Ok(candidate)
}

/// Inspect a resource or an import ZIP and write any decoded artwork to cache.
pub fn inspect(path: &Path, cache: &Path) -> Result<Metadata> {
    let mut result = Metadata::default();
    let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("");
    let mut texts = text_files(path)?;
    texts.sort_by_key(|n| {
        (
            Path::new(n)
                .file_stem()
                .and_then(|s| s.to_str())
                .is_none_or(|s| !s.eq_ignore_ascii_case(stem)),
            n.clone(),
        )
    });
    if let Some(name) = texts.first() {
        let bytes = if is_archive(path) {
            archive::read_member(path, name, MAX_TEXT_BYTES)?
        } else {
            read_bounded(&extract_text(path, name, cache)?, MAX_TEXT_BYTES)?
        };
        result = parse_metadata_text(&decode_text(&bytes));
    }
    let resources = if extension(path) == "zip" {
        prepare_files(
            path,
            &["wad".into(), "iwad".into(), "pk3".into(), "ipk3".into()],
            &[],
            cache,
        )?
    } else {
        vec![path.to_owned()]
    };
    let mut seen = HashSet::new();
    for resource in resources {
        let ext = extension(&resource);
        if wad::has_wad_magic(&resource)? || matches!(ext.as_str(), "wad" | "iwad") {
            let mut wad = wad::Wad::open(&resource)?;
            for name in wad.map_names() {
                if seen.insert(name.clone()) {
                    result.maps.push(name);
                }
            }
            if result.title_picture.is_none()
                && let Some(picture) = artwork::wad_picture(&mut wad, path)?
            {
                result.title_picture = Some(artwork::save_picture(&picture, cache)?);
            }
        } else if matches!(ext.as_str(), "pk3" | "ipk3") {
            let (maps, picture) =
                artwork::pk3_content(&resource, path, result.title_picture.is_none())?;
            for name in maps {
                if seen.insert(name.clone()) {
                    result.maps.push(name);
                }
            }
            if let Some(picture) = picture {
                result.title_picture = Some(artwork::save_picture(&picture, cache)?);
            }
        }
    }
    Ok(result)
}

#[cfg(test)]
mod tests;
