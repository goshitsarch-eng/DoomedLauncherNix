use crate::error::{Error, Result};
use std::{
    fs::File,
    io::{Read, Seek, SeekFrom},
    path::Path,
};

const MAX_LUMPS: usize = 262_144;

#[derive(Debug, Clone)]
pub(crate) struct Lump {
    pub name: String,
    pub offset: u64,
    pub size: u64,
}

pub(crate) struct Wad<R> {
    reader: R,
    pub lumps: Vec<Lump>,
    pub iwad: bool,
}

impl Wad<File> {
    pub fn open(path: &Path) -> Result<Self> {
        Self::read(File::open(path)?)
    }
}

impl<R: Read + Seek> Wad<R> {
    pub fn read(mut reader: R) -> Result<Self> {
        let length = reader.seek(SeekFrom::End(0))?;
        if length < 12 {
            return Err(Error::corrupt("Truncated WAD header"));
        }
        reader.seek(SeekFrom::Start(0))?;
        let mut header = [0; 12];
        reader.read_exact(&mut header)?;
        if &header[..4] != b"IWAD" && &header[..4] != b"PWAD" {
            return Err(Error::corrupt("Invalid WAD signature"));
        }
        let count = i32::from_le_bytes(header[4..8].try_into().unwrap());
        let offset = i32::from_le_bytes(header[8..12].try_into().unwrap());
        if count < 0 || count as usize > MAX_LUMPS || offset < 0 {
            return Err(Error::corrupt("Invalid WAD directory bounds"));
        }
        let directory_end = (offset as u64)
            .checked_add(count as u64 * 16)
            .ok_or_else(|| Error::corrupt("WAD directory offset overflow"))?;
        if directory_end > length || (count > 0 && offset < 12) {
            return Err(Error::corrupt("WAD directory extends beyond its file"));
        }
        reader.seek(SeekFrom::Start(offset as u64))?;
        let mut lumps = Vec::with_capacity(count as usize);
        for _ in 0..count {
            let mut record = [0; 16];
            reader.read_exact(&mut record)?;
            let offset = i32::from_le_bytes(record[..4].try_into().unwrap());
            let size = i32::from_le_bytes(record[4..8].try_into().unwrap());
            if offset < 0 || size < 0 || (offset as u64 + size as u64 > length) {
                return Err(Error::corrupt("WAD lump extends beyond its file"));
            }
            let raw = &record[8..];
            let end = raw.iter().position(|b| *b == 0).unwrap_or(8);
            if raw[..end].iter().any(|b| *b < 32 || *b == 127) {
                return Err(Error::corrupt("WAD lump name contains a control character"));
            }
            let name = raw[..end]
                .iter()
                .map(|&b| char::from(b))
                .collect::<String>()
                .to_ascii_uppercase();
            lumps.push(Lump {
                name,
                offset: offset as u64,
                size: size as u64,
            });
        }
        Ok(Self {
            reader,
            lumps,
            iwad: &header[..4] == b"IWAD",
        })
    }

    pub fn lump(&mut self, name: &str, limit: u64) -> Result<Option<Vec<u8>>> {
        // Doom resolves duplicate resource names using the last definition.
        let Some(lump) = self
            .lumps
            .iter()
            .rev()
            .find(|l| l.name.eq_ignore_ascii_case(name))
        else {
            return Ok(None);
        };
        if lump.size > limit {
            return Err(Error::corrupt(format!(
                "WAD lump {name} exceeds the permitted size"
            )));
        }
        let mut data = vec![0; lump.size as usize];
        self.reader.seek(SeekFrom::Start(lump.offset))?;
        self.reader.read_exact(&mut data)?;
        Ok(Some(data))
    }

    pub fn map_names(&self) -> Vec<String> {
        fn content(name: &str) -> bool {
            matches!(
                name,
                "THINGS"
                    | "LINEDEFS"
                    | "SIDEDEFS"
                    | "VERTEXES"
                    | "SEGS"
                    | "SSECTORS"
                    | "NODES"
                    | "SECTORS"
                    | "REJECT"
                    | "BLOCKMAP"
                    | "BEHAVIOR"
                    | "TEXTMAP"
                    | "ENDMAP"
                    | "ZNODES"
            )
        }
        let mut maps = Vec::new();
        for pair in self.lumps.windows(2) {
            if !pair[0].name.is_empty()
                && !content(&pair[0].name)
                && matches!(pair[1].name.as_str(), "THINGS" | "TEXTMAP" | "LINEDEFS")
                && !maps.contains(&pair[0].name)
            {
                maps.push(pair[0].name.clone());
            }
        }
        maps
    }
}

pub(crate) fn has_wad_magic(path: &Path) -> Result<bool> {
    let mut file = File::open(path)?;
    let mut magic = [0; 4];
    let size = file.read(&mut magic)?;
    Ok(size == 4 && matches!(&magic, b"IWAD" | b"PWAD"))
}

pub fn is_iwad(path: &Path) -> Result<bool> {
    Ok(Wad::open(path)?.iwad)
}

pub fn map_names(path: &Path) -> Result<Vec<String>> {
    Ok(Wad::open(path)?.map_names())
}

pub fn lump_names(path: &Path) -> Result<Vec<String>> {
    Ok(Wad::open(path)?.lumps.into_iter().map(|l| l.name).collect())
}

pub fn lump_data(path: &Path, name: &str) -> Result<Option<Vec<u8>>> {
    Wad::open(path)?.lump(name, super::MAX_WAD_BYTES)
}

pub fn iwad_display_name(file_name: &str) -> String {
    let stem = Path::new(file_name)
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or(file_name);
    match stem.to_ascii_lowercase().as_str() {
        "doom" => "The Ultimate Doom",
        "doom1" => "Doom (Shareware)",
        "doom2" => "Doom II: Hell on Earth",
        "tnt" => "Final Doom: TNT Evilution",
        "plutonia" => "Final Doom: The Plutonia Experiment",
        "heretic" => "Heretic",
        "hexen" => "Hexen",
        "hexdd" => "Hexen: Deathkings of the Dark Citadel",
        "strife1" => "Strife",
        "chex" => "Chex Quest",
        "freedoom1" => "Freedoom: Phase 1",
        "freedoom2" => "Freedoom: Phase 2",
        "doom64" => "Doom 64",
        _ => stem,
    }
    .to_owned()
}
