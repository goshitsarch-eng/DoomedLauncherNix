//! Store discovery and the idgames transport. All blocking operations belong on
//! the bridge worker; download bodies are streamed by their own bounded workers.
use crate::error::{Error, Result};
use reqwest::blocking::Client;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::{HashMap, HashSet};
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Write};
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Duration, Instant};
use url::Url;
use uuid::Uuid;
use walkdir::WalkDir;

const MAX_CONFIGURATION: u64 = 4 * 1024 * 1024;
const MAX_RESPONSE: u64 = 16 * 1024 * 1024;
const MAX_DOWNLOAD: u64 = 2 * 1024 * 1024 * 1024;
const MAX_SCAN_ENTRIES: usize = 100_000;
const MAX_ACTIVE_DOWNLOADS: usize = 8;
const MAX_RETAINED_DOWNLOADS: usize = 256;
const DEFAULT_MIRROR: &str = "https://www.quaddicted.com/files/idgames/";

#[derive(Debug, Default, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct StoreScan {
    pub iwads: Vec<PathBuf>,
    pub pwads: Vec<PathBuf>,
    pub doom64_exe: Option<PathBuf>,
}

struct StoreGame {
    id: u32,
    names: &'static [&'static str],
    iwads: &'static [&'static str],
    pwads: &'static [&'static str],
    executable: Option<&'static str>,
}

const GAMES: &[StoreGame] = &[
    StoreGame {
        id: 2280,
        names: &["DOOM + DOOM II", "Ultimate Doom", "DOOM"],
        iwads: &[
            "rerelease/doom.wad",
            "rerelease/doom2.wad",
            "rerelease/plutonia.wad",
            "rerelease/tnt.wad",
            "base/doom.wad",
            "base/doom2/doom2.wad",
            "base/plutonia/plutonia.wad",
            "base/tnt/tnt.wad",
        ],
        pwads: &[
            "rerelease/id1.wad",
            "rerelease/nerve.wad",
            "rerelease/masterlevels.wad",
            "rerelease/sigil.wad",
            "rerelease/sigil2.wad",
        ],
        executable: None,
    },
    StoreGame {
        id: 2300,
        names: &["DOOM II", "Doom 2", "DOOM 2"],
        iwads: &["base/doom2.wad"],
        pwads: &[],
        executable: None,
    },
    StoreGame {
        id: 2290,
        names: &["Final DOOM"],
        iwads: &["base/plutonia.wad", "base/tnt.wad"],
        pwads: &[],
        executable: None,
    },
    StoreGame {
        id: 3286930,
        names: &["Heretic + Hexen"],
        iwads: &["heretic.wad", "hexen.wad"],
        pwads: &["hexdd.wad"],
        executable: None,
    },
    StoreGame {
        id: 2390,
        names: &[
            "Heretic: Shadow of the Serpent Riders",
            "Heretic Shadow of the Serpent Riders",
            "Heretic",
        ],
        iwads: &["base/heretic.wad"],
        pwads: &[],
        executable: None,
    },
    StoreGame {
        id: 2360,
        names: &["Hexen: Beyond Heretic", "Hexen Beyond Heretic", "Hexen"],
        iwads: &["base/hexen.wad"],
        pwads: &[],
        executable: None,
    },
    StoreGame {
        id: 317040,
        names: &["Strife: Veteran Edition", "Strife Veteran Edition"],
        iwads: &["strife1.wad"],
        pwads: &[],
        executable: None,
    },
    StoreGame {
        id: 1148590,
        names: &["Doom 64", "DOOM 64"],
        iwads: &["doom64.wad"],
        pwads: &[],
        executable: Some("doom64_x64.exe"),
    },
];

#[derive(Debug)]
enum VdfValue {
    Text(String),
    Object(Vec<(String, VdfValue)>),
}

/// Valve's quoted strings escape quotes and backslashes. Unknown backslash
/// sequences are preserved, so hand-written Windows paths are not corrupted.
fn vdf_tokens(text: &str) -> Result<Vec<String>> {
    if text.len() as u64 > MAX_CONFIGURATION {
        return Err(Error::validation("Store configuration is too large"));
    }
    let mut chars = text.chars().peekable();
    let mut tokens = Vec::new();
    while let Some(ch) = chars.next() {
        if ch.is_whitespace() {
            continue;
        }
        if ch == '/' && chars.peek() == Some(&'/') {
            for c in chars.by_ref() {
                if c == '\n' {
                    break;
                }
            }
            continue;
        }
        let mut token = String::new();
        match ch {
            '{' | '}' => token.push(ch),
            '"' => {
                let mut closed = false;
                while let Some(c) = chars.next() {
                    if c == '"' {
                        closed = true;
                        break;
                    }
                    if c == '\\' && matches!(chars.peek(), Some('\\' | '"')) {
                        token.push(chars.next().unwrap());
                    } else {
                        token.push(c);
                    }
                }
                if !closed {
                    return Err(Error::corrupt("Unterminated store configuration string"));
                }
            }
            _ => {
                token.push(ch);
                while chars
                    .peek()
                    .is_some_and(|c| !c.is_whitespace() && *c != '{' && *c != '}')
                {
                    token.push(chars.next().unwrap());
                }
            }
        }
        tokens.push(token);
        if tokens.len() > 100_000 {
            return Err(Error::validation(
                "Store configuration has too many entries",
            ));
        }
    }
    Ok(tokens)
}

fn parse_vdf(text: &str) -> Result<Vec<(String, VdfValue)>> {
    fn entries(
        tokens: &[String],
        index: &mut usize,
        depth: usize,
    ) -> Result<Vec<(String, VdfValue)>> {
        if depth > 32 {
            return Err(Error::validation(
                "Store configuration is nested too deeply",
            ));
        }
        let mut result = Vec::new();
        while *index < tokens.len() {
            if tokens[*index] == "}" {
                if depth == 0 {
                    return Err(Error::corrupt("Unexpected store configuration brace"));
                }
                *index += 1;
                return Ok(result);
            }
            let key = tokens[*index].clone();
            *index += 1;
            let value = tokens
                .get(*index)
                .ok_or_else(|| Error::corrupt("Missing store configuration value"))?;
            *index += 1;
            let value = if value == "{" {
                VdfValue::Object(entries(tokens, index, depth + 1)?)
            } else if value == "}" {
                return Err(Error::corrupt("Missing store configuration value"));
            } else {
                VdfValue::Text(value.clone())
            };
            result.push((key, value));
        }
        if depth != 0 {
            return Err(Error::corrupt("Unclosed store configuration object"));
        }
        Ok(result)
    }
    entries(&vdf_tokens(text)?, &mut 0, 0)
}

pub fn parse_library_paths(text: &str) -> Result<Vec<PathBuf>> {
    fn visit(entries: &[(String, VdfValue)], paths: &mut Vec<PathBuf>, library: bool) {
        for (key, value) in entries {
            match value {
                VdfValue::Text(value)
                    if key.eq_ignore_ascii_case("path")
                        || (library && key.parse::<u32>().is_ok()) =>
                {
                    let path = PathBuf::from(value);
                    if !value.is_empty() && !paths.contains(&path) {
                        paths.push(path);
                    }
                }
                VdfValue::Object(children) => {
                    visit(children, paths, key.eq_ignore_ascii_case("libraryfolders"))
                }
                _ => {}
            }
        }
    }
    let mut paths = Vec::new();
    visit(&parse_vdf(text)?, &mut paths, true);
    Ok(paths)
}

pub fn parse_install_dir(text: &str) -> Result<Option<String>> {
    fn find(entries: &[(String, VdfValue)]) -> Option<String> {
        for (key, value) in entries {
            match value {
                VdfValue::Text(value) if key.eq_ignore_ascii_case("installdir") => {
                    return Some(value.clone());
                }
                VdfValue::Object(children) => {
                    if let Some(value) = find(children) {
                        return Some(value);
                    }
                }
                _ => {}
            }
        }
        None
    }
    let found = find(&parse_vdf(text)?);
    if found.as_deref().is_some_and(|p| !safe_component(p)) {
        return Err(Error::validation("Unsafe Steam install directory"));
    }
    Ok(found)
}

fn read_configuration(path: &Path) -> Result<String> {
    let file = File::open(path)?;
    if file.metadata()?.len() > MAX_CONFIGURATION {
        return Err(Error::validation("Store configuration is too large"));
    }
    let mut bytes = Vec::new();
    file.take(MAX_CONFIGURATION + 1).read_to_end(&mut bytes)?;
    if bytes.len() as u64 > MAX_CONFIGURATION {
        return Err(Error::validation("Store configuration is too large"));
    }
    String::from_utf8(bytes).map_err(|_| Error::corrupt("Store configuration is not UTF-8"))
}

fn home_directory() -> Option<PathBuf> {
    std::env::var_os(if cfg!(windows) { "USERPROFILE" } else { "HOME" }).map(PathBuf::from)
}

fn default_roots() -> (Vec<PathBuf>, Vec<PathBuf>, Vec<PathBuf>) {
    let mut steam = Vec::new();
    let mut stores = Vec::new();
    let mut configurations = Vec::new();
    if let Some(home) = home_directory() {
        for path in [
            ".steam/steam",
            ".steam/root",
            ".local/share/Steam",
            ".var/app/com.valvesoftware.Steam/.local/share/Steam",
            "snap/steam/common/.local/share/Steam",
            "Library/Application Support/Steam",
        ] {
            steam.push(home.join(path));
        }
        for path in [
            "GOG Games",
            "Games/Heroic",
            "Games/Heroic/Prefixes",
            "Games",
            ".local/share/lutris/runners/gog",
            "Applications",
        ] {
            stores.push(home.join(path));
        }
        for path in [
            ".config/heroic",
            ".config/legendary",
            ".config/lutris/games",
            ".var/app/com.heroicgameslauncher.hgl/config/heroic",
            ".var/app/net.lutris.Lutris/config/lutris/games",
            "Library/Application Support/heroic",
            "Library/Application Support/legendary",
        ] {
            configurations.push(home.join(path));
        }
    }
    if let Some(data) = std::env::var_os("XDG_DATA_HOME") {
        steam.push(PathBuf::from(data).join("Steam"));
    }
    if let Some(config) = std::env::var_os("XDG_CONFIG_HOME") {
        for suffix in ["heroic", "legendary", "lutris/games"] {
            configurations.push(PathBuf::from(&config).join(suffix));
        }
    }
    if let Some(appdata) = std::env::var_os("APPDATA") {
        configurations.push(PathBuf::from(appdata).join("heroic"));
    }
    #[cfg(windows)]
    {
        for variable in ["ProgramFiles(x86)", "ProgramFiles", "ProgramW6432"] {
            if let Some(path) = std::env::var_os(variable) {
                steam.push(PathBuf::from(&path).join("Steam"));
                stores.push(PathBuf::from(path).join("GOG Galaxy/Games"));
            }
        }
        stores.push(PathBuf::from("C:\\GOG Games"));
        if let Ok(output) = std::process::Command::new("reg")
            .args(["query", "HKCU\\Software\\Valve\\Steam", "/v", "SteamPath"])
            .output()
        {
            if output.status.success() {
                for line in String::from_utf8_lossy(&output.stdout).lines() {
                    if let Some((_, path)) = line.split_once("REG_SZ") {
                        steam.push(PathBuf::from(path.trim()));
                    }
                }
            }
        }
    }
    (steam, stores, configurations)
}

fn configured_game_roots(configurations: &[PathBuf]) -> Vec<PathBuf> {
    fn json_paths(value: &Value, paths: &mut Vec<PathBuf>, depth: usize) {
        if depth > 32 {
            return;
        }
        match value {
            Value::Object(object) => {
                for (key, value) in object {
                    if matches!(
                        key.as_str(),
                        "install_path" | "installPath" | "installDir" | "install_dir"
                    ) {
                        if let Some(value) = value.as_str() {
                            paths.push(PathBuf::from(value));
                        }
                    } else {
                        json_paths(value, paths, depth + 1);
                    }
                }
            }
            Value::Array(array) => {
                for entry in array {
                    json_paths(entry, paths, depth + 1);
                }
            }
            _ => {}
        }
    }
    let mut paths = Vec::new();
    for root in configurations {
        for entry in WalkDir::new(root)
            .max_depth(3)
            .follow_links(false)
            .into_iter()
            .take(5000)
            .filter_map(std::result::Result::ok)
        {
            if !entry.file_type().is_file() {
                continue;
            }
            let path = entry.path();
            let Some(extension) = path.extension().and_then(|s| s.to_str()) else {
                continue;
            };
            if !matches!(extension, "json" | "yml" | "yaml") {
                continue;
            }
            let Ok(text) = read_configuration(path) else {
                continue;
            };
            if extension == "json" {
                if let Ok(value) = serde_json::from_str::<Value>(&text) {
                    json_paths(&value, &mut paths, 0);
                }
            } else {
                for line in text.lines() {
                    if let Some((key, value)) = line.trim().split_once(':')
                        && matches!(key, "exe" | "prefix" | "working_dir" | "directory")
                    {
                        let value = value.trim().trim_matches(['\'', '"']);
                        if value.starts_with('/') || Path::new(value).is_absolute() {
                            let path = PathBuf::from(value.replace("\\\\", "\\"));
                            paths.push(if key == "exe" {
                                path.parent().unwrap_or(&path).to_path_buf()
                            } else {
                                path
                            });
                        }
                    }
                }
            }
        }
    }
    paths
}

fn find_case_insensitive(root: &Path, relative: &str) -> Option<PathBuf> {
    let mut current = root.to_path_buf();
    for part in relative.split('/') {
        if !safe_component(part) {
            return None;
        }
        let exact = current.join(part);
        current = if exact.exists() {
            exact
        } else {
            fs::read_dir(&current)
                .ok()?
                .take(10_000)
                .filter_map(std::result::Result::ok)
                .find(|entry| {
                    entry
                        .file_name()
                        .to_string_lossy()
                        .eq_ignore_ascii_case(part)
                })?
                .path()
        };
    }
    if current.is_file() {
        fs::canonicalize(current).ok()
    } else {
        None
    }
}

fn add_unique(paths: &mut Vec<PathBuf>, path: PathBuf) {
    if !paths.contains(&path) {
        paths.push(path);
    }
}

fn scan_game(path: &Path, game: &StoreGame, found: &mut StoreScan) {
    for relative in game.iwads {
        if let Some(path) = find_case_insensitive(path, relative) {
            add_unique(&mut found.iwads, path);
        }
    }
    for relative in game.pwads {
        if let Some(path) = find_case_insensitive(path, relative) {
            add_unique(&mut found.pwads, path);
        }
    }
    if found.doom64_exe.is_none()
        && let Some(executable) = game.executable
    {
        found.doom64_exe = find_case_insensitive(path, executable)
            .or_else(|| find_case_insensitive(path, executable.trim_end_matches(".exe")));
    }
}

/// Scan default Steam libraries, GOG/Heroic/Lutris locations and user-selected
/// roots. No links below a recursive root are followed; traversal has a shared
/// entry/depth budget and only known game data names are proposed for import.
pub fn scan_stores(extra_roots: &[PathBuf]) -> Result<StoreScan> {
    if extra_roots.len() > 128 {
        return Err(Error::validation("Choose at most 128 store roots"));
    }
    let (mut steam, mut stores, configurations) = default_roots();
    steam.extend_from_slice(extra_roots);
    stores.extend(configured_game_roots(&configurations));
    stores.extend_from_slice(extra_roots);
    scan_roots(&steam, &stores)
}

fn scan_roots(steam_roots: &[PathBuf], store_roots: &[PathBuf]) -> Result<StoreScan> {
    let mut libraries = Vec::new();
    let mut found = StoreScan::default();
    for root in steam_roots {
        add_unique(&mut libraries, root.clone());
        for relative in ["steamapps/libraryfolders.vdf", "config/libraryfolders.vdf"] {
            if let Ok(text) = read_configuration(&root.join(relative))
                && let Ok(paths) = parse_library_paths(&text)
            {
                for path in paths {
                    add_unique(&mut libraries, path);
                }
            }
        }
    }
    for game in GAMES {
        for library in &libraries {
            let manifest = library.join(format!("steamapps/appmanifest_{}.acf", game.id));
            if let Ok(text) = read_configuration(&manifest)
                && let Ok(Some(directory)) = parse_install_dir(&text)
            {
                scan_game(
                    &library.join("steamapps/common").join(directory),
                    game,
                    &mut found,
                );
            }
        }
    }
    let mut roots = Vec::new();
    for path in store_roots {
        if let Ok(path) = fs::canonicalize(path) {
            add_unique(&mut roots, path);
        }
    }
    for root in &roots {
        for game in GAMES {
            scan_game(root, game, &mut found);
            for name in game.names {
                scan_game(&root.join(name), game, &mut found);
            }
        }
    }
    // GOG and Wine prefixes do not share one directory layout. A bounded walk
    // finds catalogue data in those layouts and configured Heroic installations.
    let mut remaining = MAX_SCAN_ENTRIES;
    let mut visited = HashSet::new();
    for root in roots {
        for entry in WalkDir::new(root)
            .max_depth(10)
            .follow_links(false)
            .sort_by_file_name()
            .into_iter()
            .take(remaining)
            .filter_map(std::result::Result::ok)
        {
            remaining = remaining.saturating_sub(1);
            if !entry.file_type().is_file() {
                continue;
            }
            let name = entry.file_name().to_string_lossy().to_ascii_lowercase();
            if !matches!(
                name.as_str(),
                "doom.wad"
                    | "doom1.wad"
                    | "doom2.wad"
                    | "plutonia.wad"
                    | "tnt.wad"
                    | "heretic.wad"
                    | "hexen.wad"
                    | "strife1.wad"
                    | "doom64.wad"
                    | "freedoom1.wad"
                    | "freedoom2.wad"
                    | "hacx.wad"
                    | "hexdd.wad"
                    | "nerve.wad"
                    | "id1.wad"
                    | "masterlevels.wad"
                    | "sigil.wad"
                    | "sigil2.wad"
                    | "doom64_x64.exe"
                    | "doom64_x64"
            ) {
                continue;
            }
            let Ok(path) = fs::canonicalize(entry.path()) else {
                continue;
            };
            if !visited.insert(path.clone()) {
                continue;
            }
            match name.as_str() {
                "doom64_x64.exe" | "doom64_x64" => {
                    if found.doom64_exe.is_none() {
                        found.doom64_exe = Some(path);
                    }
                }
                "hexdd.wad" | "nerve.wad" | "id1.wad" | "masterlevels.wad" | "sigil.wad"
                | "sigil2.wad" => add_unique(&mut found.pwads, path),
                _ => add_unique(&mut found.iwads, path),
            }
        }
        if remaining == 0 {
            break;
        }
    }
    Ok(found)
}

#[derive(Debug, Default, Clone, Serialize, Deserialize, PartialEq)]
pub struct RemoteFile {
    pub id: i64,
    pub file_name: String,
    pub title: String,
    pub author: String,
    pub description: String,
    pub release_date: String,
    pub directory: String,
    pub url: String,
    pub rating: f64,
    pub size: u64,
}

fn network_error(error: reqwest::Error) -> Error {
    Error::new("network", error.without_url().to_string())
}

fn valid_url(value: &str) -> Result<Url> {
    let url = Url::parse(value).map_err(|_| Error::validation("Invalid HTTP or HTTPS URL"))?;
    if !matches!(url.scheme(), "http" | "https")
        || url.host_str().is_none()
        || !url.username().is_empty()
        || url.password().is_some()
    {
        return Err(Error::validation(
            "Use an HTTP or HTTPS URL without embedded credentials",
        ));
    }
    Ok(url)
}

fn http_client(timeout: Duration) -> Result<Client> {
    Client::builder()
        .user_agent(concat!("DoomedLauncher/", env!("CARGO_PKG_VERSION")))
        .connect_timeout(Duration::from_secs(10))
        .timeout(timeout)
        .redirect(reqwest::redirect::Policy::custom(|attempt| {
            if let Some(error) = redirect_error(attempt.previous(), attempt.url()) {
                return attempt.error(error);
            }
            attempt.follow()
        }))
        .build()
        .map_err(network_error)
}

fn redirect_error(previous: &[Url], next: &Url) -> Option<&'static str> {
    if previous.len() >= 10 {
        Some("Too many HTTP redirects")
    } else if !matches!(next.scheme(), "http" | "https")
        || !next.username().is_empty()
        || next.password().is_some()
    {
        Some("Unsafe HTTP redirect")
    } else if previous.iter().any(|url| url.scheme() == "https") && next.scheme() != "https" {
        Some("HTTPS downgrade redirect rejected")
    } else {
        None
    }
}

fn safe_component(value: &str) -> bool {
    !value.is_empty()
        && value != "."
        && value != ".."
        && !value.ends_with(['.', ' '])
        && !value.chars().any(|c| {
            c.is_control() || matches!(c, '/' | '\\' | ':' | '*' | '?' | '"' | '<' | '>' | '|')
        })
        && !matches!(
            value
                .split('.')
                .next()
                .unwrap_or("")
                .to_ascii_uppercase()
                .as_str(),
            "CON"
                | "PRN"
                | "AUX"
                | "NUL"
                | "COM1"
                | "COM2"
                | "COM3"
                | "COM4"
                | "COM5"
                | "COM6"
                | "COM7"
                | "COM8"
                | "COM9"
                | "LPT1"
                | "LPT2"
                | "LPT3"
                | "LPT4"
                | "LPT5"
                | "LPT6"
                | "LPT7"
                | "LPT8"
                | "LPT9"
        )
        && Path::new(value)
            .components()
            .all(|part| matches!(part, Component::Normal(_)))
}

fn append_url_path(mut base: Url, directory: &str, file_name: &str) -> Result<Url> {
    if directory.starts_with('/')
        || directory.contains('\\')
        || !safe_component(file_name)
        || directory
            .split('/')
            .filter(|part| !part.is_empty())
            .any(|part| !safe_component(part))
    {
        return Err(Error::validation("Unsafe archive directory or filename"));
    }
    base.set_query(None);
    base.set_fragment(None);
    {
        let mut path = base
            .path_segments_mut()
            .map_err(|_| Error::validation("URL has no hierarchical path"))?;
        path.pop_if_empty();
        for segment in directory.split('/').filter(|s| !s.is_empty()) {
            path.push(segment);
        }
        path.push(file_name);
    }
    Ok(base)
}

pub fn search_idgames(
    base: &str,
    api_page: &str,
    query: &str,
    search_type: &str,
    latest: bool,
) -> Result<Vec<RemoteFile>> {
    if query.len() > 2048 {
        return Err(Error::validation("Search query is too long"));
    }
    if !latest
        && !matches!(
            search_type,
            "title" | "filename" | "author" | "email" | "description" | "credits" | "textfile"
        )
    {
        return Err(Error::validation("Unsupported archive search field"));
    }
    let (directory, page) = api_page.rsplit_once('/').unwrap_or(("", api_page));
    let mut url = append_url_path(valid_url(base)?, directory, page)?;
    {
        let mut pairs = url.query_pairs_mut();
        pairs.append_pair("out", "json");
        if latest {
            pairs
                .append_pair("action", "latestfiles")
                .append_pair("limit", "50");
        } else {
            pairs
                .append_pair("action", "search")
                .append_pair("type", search_type)
                .append_pair("query", query)
                .append_pair("sort", "rating")
                .append_pair("dir", "desc");
        }
    }
    let started = Instant::now();
    let mut response = http_client(Duration::from_secs(30))?
        .get(url)
        .send()
        .map_err(network_error)?
        .error_for_status()
        .map_err(network_error)?;
    if response
        .content_length()
        .is_some_and(|size| size > MAX_RESPONSE)
    {
        return Err(Error::new("limit", "Archive response is too large"));
    }
    let mut bytes = Vec::new();
    let mut buffer = [0_u8; 64 * 1024];
    loop {
        if started.elapsed() > Duration::from_secs(60) {
            return Err(Error::new(
                "network",
                "Archive search exceeded its deadline",
            ));
        }
        let count = response.read(&mut buffer)?;
        if count == 0 {
            break;
        }
        if bytes.len() as u64 + count as u64 > MAX_RESPONSE {
            return Err(Error::new("limit", "Archive response is too large"));
        }
        bytes.extend_from_slice(&buffer[..count]);
    }
    parse_remote_files(&bytes)
}

fn parse_remote_files(bytes: &[u8]) -> Result<Vec<RemoteFile>> {
    let value: Value = serde_json::from_slice(bytes)
        .map_err(|_| Error::corrupt("The archive returned invalid JSON"))?;
    let object = value
        .as_object()
        .ok_or_else(|| Error::corrupt("The archive returned an invalid response"))?;
    if let Some(error) = object.get("error") {
        return Err(Error::new(
            "archive",
            error
                .get("message")
                .and_then(Value::as_str)
                .unwrap_or("Archive request failed"),
        ));
    }
    let content = object
        .get("content")
        .and_then(Value::as_object)
        .ok_or_else(|| Error::corrupt("The archive response has no content"))?;
    let items: Vec<&Value> = match content.get("file") {
        Some(Value::Array(files)) => files.iter().collect(),
        Some(value @ Value::Object(_)) => vec![value],
        None | Some(Value::Null) => Vec::new(),
        _ => return Err(Error::corrupt("The archive returned an invalid file list")),
    };
    if items.len() > 10_000 {
        return Err(Error::new("limit", "The archive returned too many files"));
    }
    items
        .into_iter()
        .map(|item| {
            if !item.is_object() {
                return Err(Error::corrupt("The archive returned an invalid file"));
            }
            let string = |key: &str| {
                item.get(key)
                    .and_then(Value::as_str)
                    .unwrap_or("")
                    .to_owned()
            };
            let number = |key: &str| {
                item.get(key)
                    .map(|v| v.to_string().trim_matches('"').to_owned())
                    .unwrap_or_default()
            };
            let description = string("description")
                .replace("<br>", "\n")
                .replace("<br/>", "\n")
                .replace("<br />", "\n");
            let rating = number("rating")
                .parse::<f64>()
                .ok()
                .filter(|value| value.is_finite())
                .unwrap_or(0.0);
            Ok(RemoteFile {
                id: number("id").parse().unwrap_or(0),
                file_name: string("filename"),
                title: string("title"),
                author: string("author"),
                description,
                release_date: string("date"),
                directory: string("dir"),
                url: string("url"),
                rating,
                size: number("size").parse().unwrap_or(0),
            })
        })
        .collect()
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct DownloadStatus {
    pub id: String,
    pub status: String,
    pub received: u64,
    pub total: Option<u64>,
    pub error: Option<String>,
    pub local_path: Option<PathBuf>,
}

struct DownloadJob {
    state: Mutex<DownloadStatus>,
    cancelled: AtomicBool,
    finished: AtomicBool,
    directory: PathBuf,
    created: Instant,
}

static DOWNLOADS: OnceLock<Mutex<HashMap<String, Arc<DownloadJob>>>> = OnceLock::new();
fn downloads() -> &'static Mutex<HashMap<String, Arc<DownloadJob>>> {
    DOWNLOADS.get_or_init(|| Mutex::new(HashMap::new()))
}
fn lock_error() -> Error {
    Error::new("internal", "Download state is unavailable")
}
fn active(status: &str) -> bool {
    matches!(status, "pending" | "downloading")
}

pub fn start_download(
    mirror: &str,
    directory: &str,
    file_name: &str,
    temp_root: &Path,
) -> Result<DownloadStatus> {
    let mirror = if mirror.to_ascii_lowercase().starts_with("ftp://") {
        DEFAULT_MIRROR
    } else {
        mirror
    };
    let url = append_url_path(valid_url(mirror)?, directory, file_name)?;
    let mut jobs = downloads().lock().map_err(|_| lock_error())?;
    if jobs
        .values()
        .filter(|job| !job.finished.load(Ordering::Acquire))
        .count()
        >= MAX_ACTIVE_DOWNLOADS
    {
        return Err(Error::new(
            "busy",
            "At most eight downloads may run at once",
        ));
    }
    // Terminal states remain addressable until the caller discards them. Never
    // delete a successful file merely because another download is started.
    if jobs.len() >= MAX_RETAINED_DOWNLOADS {
        let oldest_failed = jobs
            .iter()
            .filter(|(_, job)| {
                job.finished.load(Ordering::Acquire)
                    && job
                        .state
                        .lock()
                        .map(|s| matches!(s.status.as_str(), "failed" | "cancelled"))
                        .unwrap_or(false)
            })
            .min_by_key(|(_, job)| job.created)
            .map(|(id, _)| id.clone());
        if let Some(id) = oldest_failed {
            jobs.remove(&id);
        } else {
            return Err(Error::new(
                "busy",
                "Import or discard finished downloads before starting more",
            ));
        }
    }
    fs::create_dir_all(temp_root)?;
    let temp_root = fs::canonicalize(temp_root)?;
    let id = Uuid::new_v4().to_string();
    let job_directory = temp_root.join(format!("download-{id}"));
    fs::create_dir(&job_directory)?;
    let initial = DownloadStatus {
        id: id.clone(),
        status: "pending".into(),
        received: 0,
        total: None,
        error: None,
        local_path: None,
    };
    let job = Arc::new(DownloadJob {
        state: Mutex::new(initial.clone()),
        cancelled: AtomicBool::new(false),
        finished: AtomicBool::new(false),
        directory: job_directory,
        created: Instant::now(),
    });
    jobs.insert(id.clone(), job.clone());
    let worker = job.clone();
    let name = file_name.to_owned();
    if let Err(error) = std::thread::Builder::new()
        .name(format!("idgames-{id}"))
        .spawn(move || download_worker(worker, url, &name))
    {
        jobs.remove(&id);
        let _ = fs::remove_dir_all(&job.directory);
        return Err(error.into());
    }
    Ok(initial)
}

fn download_worker(job: Arc<DownloadJob>, url: Url, name: &str) {
    let result = stream_download(&job, url, name);
    if let Ok(mut state) = job.state.lock() {
        if job.cancelled.load(Ordering::Acquire) {
            let _ = fs::remove_dir_all(&job.directory);
            state.status = "cancelled".into();
            state.local_path = None;
            state.error = None;
        } else {
            match result {
                Ok(path) => {
                    state.status = "completed".into();
                    state.local_path = Some(path);
                }
                Err(error) => {
                    let _ = fs::remove_dir_all(&job.directory);
                    state.status = "failed".into();
                    state.error = Some(error.message);
                    state.local_path = None;
                }
            }
        }
        job.finished.store(true, Ordering::Release);
    }
}

fn stream_download(job: &DownloadJob, url: Url, name: &str) -> Result<PathBuf> {
    let cancelled = || job.cancelled.load(Ordering::Acquire);
    if cancelled() {
        return Err(Error::new("cancelled", "Download cancelled"));
    }
    {
        let mut state = job.state.lock().map_err(|_| lock_error())?;
        if cancelled() {
            return Err(Error::new("cancelled", "Download cancelled"));
        }
        state.status = "downloading".into();
    }
    // Blocking reqwest applies this timeout to each read. A separate deadline
    // below bounds slow trickle responses as well as stalled connections.
    let started = Instant::now();
    let mut response = http_client(Duration::from_secs(30))?
        .get(url)
        .send()
        .map_err(network_error)?
        .error_for_status()
        .map_err(network_error)?;
    let total = response.content_length();
    if total.is_some_and(|size| size > MAX_DOWNLOAD) {
        return Err(Error::new("limit", "Download exceeds the 2 GiB limit"));
    }
    job.state.lock().map_err(|_| lock_error())?.total = total;
    let partial = job.directory.join("payload.part");
    let path = job.directory.join(name);
    let mut file = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&partial)?;
    let mut buffer = [0_u8; 64 * 1024];
    let mut received = 0_u64;
    loop {
        if cancelled() {
            return Err(Error::new("cancelled", "Download cancelled"));
        }
        if started.elapsed() > Duration::from_secs(30 * 60) {
            return Err(Error::new(
                "network",
                "Download exceeded the 30 minute deadline",
            ));
        }
        let count = response.read(&mut buffer)?;
        if cancelled() {
            return Err(Error::new("cancelled", "Download cancelled"));
        }
        if count == 0 {
            break;
        }
        received += count as u64;
        if received > MAX_DOWNLOAD {
            return Err(Error::new("limit", "Download exceeds the 2 GiB limit"));
        }
        file.write_all(&buffer[..count])?;
        job.state.lock().map_err(|_| lock_error())?.received = received;
    }
    if total.is_some_and(|total| total != received) {
        return Err(Error::corrupt(
            "Download ended before its advertised length",
        ));
    }
    file.sync_all()?;
    drop(file);
    fs::rename(partial, &path)?;
    Ok(path)
}

fn find_download(id: &str) -> Result<Arc<DownloadJob>> {
    downloads()
        .lock()
        .map_err(|_| lock_error())?
        .get(id)
        .cloned()
        .ok_or_else(|| Error::new("not_found", "Download not found"))
}

pub fn download_status(id: &str) -> Result<DownloadStatus> {
    Ok(find_download(id)?
        .state
        .lock()
        .map_err(|_| lock_error())?
        .clone())
}

/// Includes cancelled transfers whose worker is still draining a blocked read.
pub fn active_downloads() -> Result<Vec<DownloadStatus>> {
    let jobs = downloads()
        .lock()
        .map_err(|_| lock_error())?
        .values()
        .cloned()
        .collect::<Vec<_>>();
    let mut states = Vec::new();
    for job in jobs {
        if !job.finished.load(Ordering::Acquire) {
            states.push(job.state.lock().map_err(|_| lock_error())?.clone());
        }
    }
    Ok(states)
}

/// Cancel active jobs and wait for their workers to remove partial files. The
/// read timeout is 30 seconds; callers should run this on the bridge worker.
pub fn shutdown_downloads() -> Result<()> {
    let jobs = downloads()
        .lock()
        .map_err(|_| lock_error())?
        .values()
        .cloned()
        .collect::<Vec<_>>();
    shutdown_jobs(&jobs)
}

fn shutdown_jobs(jobs: &[Arc<DownloadJob>]) -> Result<()> {
    for job in jobs {
        let mut state = job.state.lock().map_err(|_| lock_error())?;
        if !job.finished.load(Ordering::Acquire) {
            job.cancelled.store(true, Ordering::Release);
            state.status = "cancelled".into();
        }
    }
    let started = Instant::now();
    while jobs.iter().any(|job| !job.finished.load(Ordering::Acquire)) {
        if started.elapsed() > Duration::from_secs(35) {
            return Err(Error::new(
                "busy",
                "Some downloads are still stopping; wait and retry before exiting",
            ));
        }
        std::thread::sleep(Duration::from_millis(25));
    }
    Ok(())
}

pub fn cancel_download(id: &str) -> Result<DownloadStatus> {
    let job = find_download(id)?;
    let mut state = job.state.lock().map_err(|_| lock_error())?;
    if active(&state.status) {
        job.cancelled.store(true, Ordering::Release);
        state.status = "cancelled".into();
    }
    Ok(state.clone())
}

/// Call after successful import, or to discard a finished download. Data is
/// never auto-launched and is retained until this explicit lifecycle operation.
pub fn discard_download(id: &str) -> Result<()> {
    let mut jobs = downloads().lock().map_err(|_| lock_error())?;
    let job = jobs
        .get(id)
        .ok_or_else(|| Error::new("not_found", "Download not found"))?;
    let state = job.state.lock().map_err(|_| lock_error())?;
    if !job.finished.load(Ordering::Acquire) {
        return Err(Error::new(
            "busy",
            "Wait for the download to finish or its cancellation to complete before discarding it",
        ));
    }
    match fs::remove_dir_all(&job.directory) {
        Ok(()) => {}
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.into()),
    }
    drop(state);
    jobs.remove(id);
    Ok(())
}

#[cfg(test)]
#[path = "discovery/tests.rs"]
mod tests;
