use super::{
    MAX_IMAGE_BYTES, MAX_TEXT_BYTES, MAX_WAD_BYTES, archive, decode_text, extension, wad::Wad,
};
use crate::error::{Error, Result};
use image::{DynamicImage, ImageFormat, ImageReader, Rgba, RgbaImage};
use regex::Regex;
use sha2::{Digest, Sha256};
use std::{
    fs::File,
    io::{Cursor, Read, Seek},
    path::{Path, PathBuf},
    sync::OnceLock,
};

const MAX_DIMENSION: u32 = 8192;
const MAX_PIXELS: u64 = 16 * 1024 * 1024;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Palette {
    Doom,
    Heretic,
    Hexen,
}

pub fn default_palette(game: Palette) -> &'static [u8] {
    let bytes: &[u8] = match game {
        Palette::Doom => include_bytes!("../../assets/PLAYPAL.LMP"),
        Palette::Heretic => include_bytes!("../../assets/HereticPalette.pal"),
        Palette::Hexen => include_bytes!("../../assets/HexenPalette.pal"),
    };
    &bytes[..768]
}

fn color(palette: &[u8], index: u8) -> Rgba<u8> {
    let offset = usize::from(index) * 3;
    Rgba([
        palette[offset],
        palette[offset + 1],
        palette[offset + 2],
        255,
    ])
}

fn bounded_image_reader(data: &[u8]) -> Result<ImageReader<Cursor<&[u8]>>> {
    let mut reader = ImageReader::new(Cursor::new(data)).with_guessed_format()?;
    let mut limits = image::Limits::default();
    limits.max_image_width = Some(MAX_DIMENSION);
    limits.max_image_height = Some(MAX_DIMENSION);
    limits.max_alloc = Some(MAX_PIXELS * 4);
    reader.limits(limits);
    Ok(reader)
}

/// Decode PNG/JPEG/BMP, Doom column patches (including tall patches), or
/// recognized flat/title sizes. Resource limits also apply to encoded images.
pub fn decode_picture(data: &[u8], palette: &[u8]) -> Result<RgbaImage> {
    if data.len() as u64 > MAX_IMAGE_BYTES {
        return Err(Error::corrupt("Title image is too large"));
    }
    if data.starts_with(b"\x89PNG\r\n\x1a\n")
        || data.starts_with(&[0xff, 0xd8])
        || data.starts_with(b"BM")
    {
        let reader = bounded_image_reader(data)?;
        let (width, height) = reader
            .into_dimensions()
            .map_err(|e| Error::corrupt(e.to_string()))?;
        if width == 0
            || height == 0
            || width > MAX_DIMENSION
            || height > MAX_DIMENSION
            || u64::from(width) * u64::from(height) > MAX_PIXELS
        {
            return Err(Error::corrupt(
                "Title image dimensions exceed decoding limits",
            ));
        }
        return bounded_image_reader(data)?
            .decode()
            .map(|i| i.to_rgba8())
            .map_err(|e| Error::corrupt(e.to_string()));
    }
    if palette.len() < 768 {
        return Err(Error::corrupt(
            "Doom pictures require a 256-color RGB palette",
        ));
    }
    let flat_size = match data.len() {
        4096 => Some((64, 64)),
        16384 => Some((128, 128)),
        65536 => Some((256, 256)),
        64000 => Some((320, 200)),
        112000 => Some((560, 200)),
        _ => None,
    };
    if let Some((width, height)) = flat_size {
        return Ok(RgbaImage::from_fn(width, height, |x, y| {
            color(palette, data[(y * width + x) as usize])
        }));
    }
    if data.len() < 12 {
        return Err(Error::corrupt("Truncated Doom picture header"));
    }
    let width = i16::from_le_bytes(data[..2].try_into().unwrap());
    let height = i16::from_le_bytes(data[2..4].try_into().unwrap());
    if width <= 0 || height <= 0 || width > 4096 || height > 4096 {
        return Err(Error::corrupt("Invalid Doom picture dimensions"));
    }
    let directory_end = 8 + width as usize * 4;
    if data.len() < directory_end {
        return Err(Error::corrupt("Truncated Doom picture column directory"));
    }
    let mut image = RgbaImage::new(width as u32, height as u32);
    let mut work = 0_u64;
    for x in 0..width as usize {
        let mut position =
            u32::from_le_bytes(data[8 + x * 4..12 + x * 4].try_into().unwrap()) as usize;
        if position < directory_end || position >= data.len() {
            return Err(Error::corrupt(
                "Doom picture column offset is outside its data",
            ));
        }
        let mut top = -1_i64;
        loop {
            let delta = *data
                .get(position)
                .ok_or_else(|| Error::corrupt("Doom picture column lacks a terminator"))?;
            position += 1;
            if delta == 255 {
                break;
            }
            let count = *data
                .get(position)
                .ok_or_else(|| Error::corrupt("Truncated Doom picture post"))?
                as usize;
            position += 2; // count and leading pad
            let end = position
                .checked_add(count + 1)
                .ok_or_else(|| Error::corrupt("Doom picture offset overflow"))?;
            if end > data.len() {
                return Err(Error::corrupt("Doom picture post extends beyond its data"));
            }
            work += count as u64 + 4;
            if work > MAX_PIXELS * 8 {
                return Err(Error::corrupt(
                    "Doom picture has excessive repeated column data",
                ));
            }
            let delta = i64::from(delta);
            top = if delta <= top { top + delta } else { delta };
            for (offset, index) in data[position..position + count].iter().enumerate() {
                let y = top + offset as i64;
                if y >= 0 && y < i64::from(height) {
                    image.put_pixel(x as u32, y as u32, color(palette, *index));
                }
            }
            position = end;
        }
    }
    Ok(image)
}

fn fallback_palette(hint: &Path, title_lump: bool) -> Palette {
    let hint = hint
        .file_name()
        .unwrap_or_default()
        .to_string_lossy()
        .to_ascii_lowercase();
    if hint.contains("hexen") || hint.contains("hexdd") {
        Palette::Hexen
    } else if hint.contains("heretic") || title_lump {
        Palette::Heretic
    } else {
        Palette::Doom
    }
}

fn title_page(data: &[u8]) -> Option<String> {
    static REGEX: OnceLock<Regex> = OnceLock::new();
    let regex = REGEX.get_or_init(|| {
        Regex::new(r#"(?i)\btitlepage\s*=?\s*(?:"([^"\r\n]+)"|([a-z0-9_./\\-]+))"#).unwrap()
    });
    // Preserve string literals while removing comments, so a commented-out
    // GameInfo block cannot override the active titlepage setting.
    let text = decode_text(data);
    let mut filtered = String::with_capacity(text.len());
    let mut chars = text.chars().peekable();
    let mut quoted = false;
    while let Some(c) = chars.next() {
        if quoted {
            filtered.push(c);
            if c == '\\' {
                if let Some(next) = chars.next() {
                    filtered.push(next);
                }
            } else if c == '"' {
                quoted = false;
            }
        } else if c == '"' {
            quoted = true;
            filtered.push(c);
        } else if c == '/' && chars.peek() == Some(&'/') {
            chars.next();
            for c in chars.by_ref() {
                if c == '\n' {
                    break;
                }
            }
            filtered.push('\n');
        } else if c == '/' && chars.peek() == Some(&'*') {
            chars.next();
            while let Some(c) = chars.next() {
                if c == '*' && chars.peek() == Some(&'/') {
                    chars.next();
                    break;
                }
            }
            filtered.push(' ');
        } else {
            filtered.push(c);
        }
    }
    regex
        .captures_iter(&filtered)
        .last()
        .and_then(|m| m.get(1).or_else(|| m.get(2)))
        .map(|m| m.as_str().to_owned())
}

pub(crate) fn wad_picture<R: Read + Seek>(
    wad: &mut Wad<R>,
    hint: &Path,
) -> Result<Option<RgbaImage>> {
    let mut candidates = Vec::new();
    for name in ["ZMAPINFO", "MAPINFO", "UMAPINFO"] {
        if let Some(data) = wad.lump(name, MAX_TEXT_BYTES)?
            && let Some(name) = title_page(&data)
        {
            candidates.push(name);
            break;
        }
    }
    candidates.extend(["TITLEPIC".to_owned(), "TITLE".to_owned()]);
    let palette = wad.lump("PLAYPAL", MAX_IMAGE_BYTES)?;
    for name in candidates {
        if let Some(data) = wad.lump(&name, MAX_IMAGE_BYTES)? {
            let fallback =
                default_palette(fallback_palette(hint, name.eq_ignore_ascii_case("TITLE")));
            let palette = palette
                .as_deref()
                .filter(|p| p.len() >= 768)
                .unwrap_or(fallback);
            if let Ok(image) = decode_picture(&data, palette) {
                return Ok(Some(image));
            }
        }
    }
    Ok(None)
}

fn resource_stem(name: &str) -> String {
    Path::new(name)
        .file_stem()
        .unwrap_or_default()
        .to_string_lossy()
        .to_ascii_lowercase()
}

pub(crate) fn pk3_content(
    path: &Path,
    hint: &Path,
    want_picture: bool,
) -> Result<(Vec<String>, Option<RgbaImage>)> {
    let mut archive = archive::open_archive(path)?;
    let mut names = Vec::new();
    for index in 0..archive.len() {
        let entry = archive.by_index_raw(index)?;
        if !entry.is_dir() {
            names.push(entry.name().to_owned());
        }
    }
    let mut maps = Vec::new();
    let mut read_budget = 2 * 1024 * 1024 * 1024_u64;
    for name in &names {
        let normalized = name.to_ascii_lowercase();
        if normalized.starts_with("maps/")
            && matches!(extension(Path::new(name)).as_str(), "wad" | "iwad")
        {
            let size = archive.by_name(name)?.size();
            read_budget = read_budget
                .checked_sub(size)
                .ok_or_else(|| Error::corrupt("PK3 maps exceed the inspection size limit"))?;
            let data = archive::read_archive_member(&mut archive, name, MAX_WAD_BYTES)?;
            let wad = Wad::read(Cursor::new(data))?;
            let mut member_maps = wad.map_names();
            // A map namespace WAD may omit its marker; its PK3 filename is then
            // the engine-visible map name.
            if member_maps.is_empty()
                && wad
                    .lumps
                    .iter()
                    .any(|l| matches!(l.name.as_str(), "THINGS" | "TEXTMAP"))
            {
                member_maps.push(resource_stem(name).to_ascii_uppercase());
            }
            for map in member_maps {
                if !maps.contains(&map) {
                    maps.push(map);
                }
            }
        }
    }
    if !want_picture {
        return Ok((maps, None));
    }
    let mut candidates = Vec::new();
    for desired in ["zmapinfo", "mapinfo", "umapinfo"] {
        if let Some(name) = names.iter().find(|n| resource_stem(n) == desired) {
            let data = archive::read_archive_member(&mut archive, name, MAX_TEXT_BYTES)?;
            if let Some(name) = title_page(&data) {
                candidates.push(name);
                break;
            }
        }
    }
    candidates.extend(["TITLEPIC".to_owned(), "TITLE".to_owned()]);
    let palette = if let Some(name) = names.iter().find(|n| resource_stem(n) == "playpal") {
        Some(archive::read_archive_member(
            &mut archive,
            name,
            MAX_IMAGE_BYTES,
        )?)
    } else {
        None
    };
    for candidate in candidates {
        let wanted = candidate.replace('\\', "/").to_ascii_lowercase();
        let wanted_stem = resource_stem(&wanted);
        let mut matching = names
            .iter()
            .filter(|n| n.eq_ignore_ascii_case(&wanted) || resource_stem(n) == wanted_stem)
            .collect::<Vec<_>>();
        matching.sort_by_key(|name| {
            (
                if name.eq_ignore_ascii_case(&wanted) {
                    0
                } else if name.to_ascii_lowercase().starts_with("graphics/") {
                    1
                } else {
                    2
                },
                (*name).clone(),
            )
        });
        for name in matching {
            let data = archive::read_archive_member(&mut archive, name, MAX_IMAGE_BYTES)?;
            let fallback = default_palette(fallback_palette(hint, wanted_stem == "title"));
            if let Ok(image) = decode_picture(
                &data,
                palette
                    .as_deref()
                    .filter(|p| p.len() >= 768)
                    .unwrap_or(fallback),
            ) {
                return Ok((maps, Some(image)));
            }
        }
    }
    Ok((maps, None))
}

pub(crate) fn save_picture(image: &RgbaImage, cache: &Path) -> Result<PathBuf> {
    let mut digest = Sha256::new();
    digest.update(image.width().to_le_bytes());
    digest.update(image.height().to_le_bytes());
    digest.update(image.as_raw());
    let name = format!("{:x}.png", digest.finalize());
    archive::write_cached(
        &archive::cache_root(cache)?.join("artwork"),
        &name,
        |file: &mut File| {
            DynamicImage::ImageRgba8(image.clone())
                .write_to(file, ImageFormat::Png)
                .map_err(|e| Error::new("image", e.to_string()))
        },
    )
}
