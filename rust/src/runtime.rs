//! Source-port discovery, typed process launches, and durable background sessions.
//!
//! Arguments are passed directly to the OS. In Flatpak, only the typed
//! `flatpak-spawn --host --watch-bus -- ...` boundary is used; user strings are
//! never evaluated by a shell. Session journals allow the database layer to
//! reconcile completed launches exactly once after a UI restart.

use crate::error::{Error, Result};
use chrono::Utc;
use regex::Regex;
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, HashSet};
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::{Mutex, OnceLock, mpsc};
use std::thread;
use std::time::{Duration, Instant, SystemTime};
use uuid::Uuid;

mod process;

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default)]
pub struct Port {
    pub id: i64,
    pub name: String,
    pub executable: String,
    pub directory: String,
    pub supported_extensions: String,
    pub file_option: String,
    pub extra_parameters: String,
    pub alt_save_directory: String,
    pub launch_type: i64,
}

impl Default for Port {
    fn default() -> Self {
        Self {
            id: 0,
            name: String::new(),
            executable: String::new(),
            directory: String::new(),
            supported_extensions: "wad,pk3,pk7,ipk3,ipk7,deh,bex".into(),
            file_option: "-file".into(),
            extra_parameters: String::new(),
            alt_save_directory: String::new(),
            launch_type: 0,
        }
    }
}

#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(default)]
pub struct LaunchOptions {
    pub game_file_id: i64,
    pub source_port_id: i64,
    pub iwad_id: Option<i64>,
    pub map: String,
    pub skill: Option<u8>,
    pub extra_params: String,
    pub extra_params_only: bool,
    pub load_latest_save: bool,
    pub save_statistics: bool,
    pub record_demo: bool,
    pub play_demo_file: Option<String>,
    pub additional_files: Vec<String>,
    pub remember: bool,
}

impl Default for LaunchOptions {
    fn default() -> Self {
        Self {
            game_file_id: 0,
            source_port_id: 0,
            iwad_id: None,
            map: String::new(),
            skill: None,
            extra_params: String::new(),
            extra_params_only: false,
            load_latest_save: false,
            save_statistics: true,
            record_demo: false,
            play_demo_file: None,
            additional_files: vec![],
            remember: true,
        }
    }
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum StatisticsKind {
    #[default]
    None,
    Levelstat,
    Statdump,
    ZdoomSave,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct LaunchPlan {
    pub program: String,
    pub arguments: Vec<String>,
    pub working_directory: String,
    pub host_bridge: bool,
    pub statistics_kind: StatisticsKind,
    pub statistics_file: Option<PathBuf>,
    pub save_directories: Vec<PathBuf>,
    pub demo_path: Option<String>,
    pub skill: Option<u8>,
}

impl LaunchPlan {
    /// Display only. Launching always uses the original argument vector.
    pub fn display_command(&self) -> String {
        let (program, arguments, _) = self.process_arguments();
        std::iter::once(program)
            .chain(arguments)
            .map(|s| {
                if s.is_empty()
                    || s.chars()
                        .any(|c| c.is_whitespace() || "\"'\\;$`()".contains(c))
                {
                    format!("\"{}\"", s.replace('\\', "\\\\").replace('"', "\\\""))
                } else {
                    s
                }
            })
            .collect::<Vec<_>>()
            .join(" ")
    }

    /// Centralizes the sandbox boundary so preview, discovery and launch use
    /// the same argv semantics, including empty arguments and hostile paths.
    pub fn process_arguments(&self) -> (String, Vec<String>, Option<String>) {
        process_arguments(
            &self.program,
            &self.arguments,
            &self.working_directory,
            self.host_bridge,
        )
    }
}

#[derive(Clone, Debug, Serialize, Deserialize, PartialEq)]
pub struct LevelStatistics {
    pub map_name: String,
    pub kill_count: i64,
    pub total_kills: i64,
    pub item_count: i64,
    pub total_items: i64,
    pub secret_count: i64,
    pub total_secrets: i64,
    pub level_time: f64,
    pub skill: i64,
}

#[derive(Clone, Copy, Debug, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum SessionStatus {
    Running,
    Completed,
    Failed,
    Stopped,
    /// The launcher exited while the engine was running; its exit status
    /// cannot be inferred from a stale PID, which may have been reused.
    Interrupted,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Session {
    pub id: String,
    pub game_file_id: i64,
    pub source_port_id: i64,
    pub process_id: u32,
    pub started_at: String,
    pub finished_at: Option<String>,
    pub status: SessionStatus,
    pub exit_code: Option<i32>,
    pub elapsed_seconds: u64,
    pub error: Option<String>,
    pub statistics: Vec<LevelStatistics>,
    pub log_path: String,
    pub demo_path: Option<String>,
}

struct ActiveSession {
    session: Session,
    started: Instant,
    stop: mpsc::Sender<()>,
    data_root: PathBuf,
    statistics_file: Option<PathBuf>,
}

static SESSIONS: OnceLock<Mutex<HashMap<String, ActiveSession>>> = OnceLock::new();

fn registry() -> &'static Mutex<HashMap<String, ActiveSession>> {
    SESSIONS.get_or_init(|| Mutex::new(HashMap::new()))
}

fn locked_sessions() -> Result<std::sync::MutexGuard<'static, HashMap<String, ActiveSession>>> {
    registry()
        .lock()
        .map_err(|_| Error::new("runtime", "Session registry is unavailable"))
}

pub fn is_flatpak() -> bool {
    cfg!(target_os = "linux")
        && (Path::new("/.flatpak-info").exists()
            || std::env::var_os("FLATPAK_ID").is_some_and(|s| !s.is_empty()))
}

fn process_arguments(
    program: &str,
    arguments: &[String],
    working_directory: &str,
    host_bridge: bool,
) -> (String, Vec<String>, Option<String>) {
    if host_bridge {
        let mut args = vec!["--host".into(), "--watch-bus".into()];
        if !working_directory.is_empty() {
            args.push(format!("--directory={working_directory}"));
        }
        args.push("--".into());
        args.push(program.into());
        args.extend_from_slice(arguments);
        ("flatpak-spawn".into(), args, None)
    } else {
        (
            program.into(),
            arguments.to_vec(),
            (!working_directory.is_empty()).then(|| working_directory.into()),
        )
    }
}

fn configured_command(
    program: &str,
    arguments: &[String],
    directory: &str,
    host_bridge: bool,
) -> Command {
    let (program, arguments, directory) =
        process_arguments(program, arguments, directory, host_bridge);
    let mut command = Command::new(program);
    command.args(arguments);
    if let Some(directory) = directory {
        command.current_dir(directory);
    }
    command
}

/// Bounded capture without pipe-reader deadlocks, including on timeout.
fn capture(program: &str, args: &[String], bridge: bool, timeout: Duration) -> Result<Vec<u8>> {
    let output_path =
        std::env::temp_dir().join(format!("doomedlauncher-capture-{}", Uuid::new_v4()));
    let result = (|| {
        let output = private_file(&output_path)?;
        let mut child = configured_command(program, args, "", bridge)
            .stdin(Stdio::null())
            .stdout(Stdio::from(output))
            .stderr(Stdio::null())
            .spawn()
            .map_err(|e| Error::new("process", format!("Could not run {program}: {e}")))?;
        let deadline = Instant::now() + timeout;
        loop {
            if let Some(status) = child.try_wait()? {
                if !status.success() {
                    return Err(Error::new(
                        "process",
                        format!("{program} exited with {status}"),
                    ));
                }
                let mut bytes = Vec::new();
                File::open(&output_path)?
                    .take(8 * 1024 * 1024 + 1)
                    .read_to_end(&mut bytes)?;
                if bytes.len() > 8 * 1024 * 1024 {
                    return Err(Error::new(
                        "process",
                        format!("{program} produced too much discovery output"),
                    ));
                }
                return Ok(bytes);
            }
            if Instant::now() >= deadline {
                let _ = child.kill();
                let _ = child.wait();
                return Err(Error::new(
                    "timeout",
                    format!(
                        "{program} did not finish within {} seconds",
                        timeout.as_secs_f64()
                    ),
                ));
            }
            thread::sleep(Duration::from_millis(10));
        }
    })();
    let _ = fs::remove_file(output_path);
    result
}

fn private_file(path: &Path) -> std::io::Result<File> {
    let mut options = OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }
    options.open(path)
}

fn host_variable(name: &str, bridge: bool) -> Option<String> {
    if bridge {
        capture(
            "/usr/bin/printenv",
            &[name.into()],
            true,
            Duration::from_secs(2),
        )
        .ok()
        .and_then(|v| String::from_utf8(v).ok())
        .map(|v| v.trim_end_matches(['\r', '\n']).to_string())
    } else {
        std::env::var(name).ok()
    }
}

fn host_home(bridge: bool) -> Option<PathBuf> {
    host_variable(if cfg!(windows) { "USERPROFILE" } else { "HOME" }, bridge)
        .map(PathBuf::from)
        .filter(|p| p.is_absolute())
}

fn resolve_stored(path: &str, root: &Path) -> PathBuf {
    let path = Path::new(path);
    if path.is_absolute() {
        path.into()
    } else {
        root.join(path)
    }
}

fn executable_file(path: &Path, bridge: bool) -> bool {
    if !path.is_absolute() {
        return false;
    }
    if bridge {
        let path = path.to_string_lossy().into_owned();
        return capture(
            "/usr/bin/test",
            &["-f".into(), path.clone()],
            true,
            Duration::from_secs(2),
        )
        .is_ok()
            && capture(
                "/usr/bin/test",
                &["-x".into(), path],
                true,
                Duration::from_secs(2),
            )
            .is_ok();
    }
    let Ok(metadata) = fs::metadata(path) else {
        return false;
    };
    if !metadata.is_file() {
        return false;
    }
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        metadata.permissions().mode() & 0o111 != 0
    }
    #[cfg(not(unix))]
    {
        path.extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("exe") || e.eq_ignore_ascii_case("com"))
    }
}

fn search_directories(bridge: bool) -> Vec<PathBuf> {
    let mut dirs: Vec<PathBuf> = host_variable("PATH", bridge)
        .map(|s| std::env::split_paths(&s).collect())
        .unwrap_or_default();
    if cfg!(target_os = "linux") {
        dirs.extend(["/usr/games", "/usr/local/games", "/opt"].map(PathBuf::from));
    }
    if cfg!(target_os = "macos") {
        dirs.push("/Applications".into());
    }
    if let Some(home) = host_home(bridge) {
        for suffix in [
            ".local/bin",
            "bin",
            "Applications",
            "AppImages",
            "Downloads",
        ] {
            dirs.push(home.join(suffix));
        }
        if cfg!(windows) {
            dirs.push(home.join("AppData/Local/Programs"));
        }
    }
    if cfg!(windows) {
        for var in ["ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA"] {
            if let Some(p) = std::env::var_os(var) {
                dirs.push(p.into());
            }
        }
    }
    let mut seen = HashSet::new();
    dirs.retain(|p| p.is_absolute() && seen.insert(p.clone()));
    dirs
}

fn directory_executables(dirs: &[PathBuf], bridge: bool) -> Vec<PathBuf> {
    let mut files = Vec::new();
    for directory in dirs.iter().filter(|p| p.is_absolute()) {
        if bridge {
            if let Ok(output) = capture(
                "/usr/bin/find",
                &[
                    "-L".into(),
                    directory.to_string_lossy().into_owned(),
                    "-maxdepth".into(),
                    "1".into(),
                    "-type".into(),
                    "f".into(),
                    "-executable".into(),
                    "-print0".into(),
                ],
                true,
                Duration::from_secs(2),
            ) {
                files.extend(
                    output
                        .split(|&b| b == 0)
                        .filter(|p| !p.is_empty())
                        .map(|p| PathBuf::from(String::from_utf8_lossy(p).into_owned())),
                );
            }
            continue;
        }
        if let Ok(entries) = fs::read_dir(directory) {
            let mut entries = entries
                .filter_map(|v| v.ok())
                .map(|v| v.path())
                .collect::<Vec<_>>();
            entries.sort();
            for path in entries {
                if executable_file(&path, false)
                    || (cfg!(target_os = "macos")
                        && path
                            .extension()
                            .is_some_and(|e| e.eq_ignore_ascii_case("app")))
                {
                    files.push(path);
                } else if cfg!(windows) && path.is_dir() {
                    // Installed Windows engines conventionally live one
                    // directory beneath Program Files or Local/Programs.
                    if let Ok(children) = fs::read_dir(path) {
                        files.extend(
                            children
                                .filter_map(|e| e.ok())
                                .map(|e| e.path())
                                .filter(|p| executable_file(p, false)),
                        );
                    }
                }
            }
        }
    }
    files
}

fn normalized(s: &str) -> String {
    s.chars()
        .filter(|c| c.is_alphanumeric())
        .flat_map(char::to_lowercase)
        .collect()
}

fn find_in_files(name: &str, files: &[PathBuf]) -> Option<PathBuf> {
    let name = name.trim();
    files
        .iter()
        .find(|p| {
            p.file_name()
                .is_some_and(|n| n.to_string_lossy().eq_ignore_ascii_case(name))
        })
        .or_else(|| {
            files.iter().find(|p| {
                let ext = p.extension().unwrap_or_default().to_string_lossy();
                (ext.eq_ignore_ascii_case("exe") || ext.eq_ignore_ascii_case("app"))
                    && p.file_stem()
                        .is_some_and(|n| n.to_string_lossy().eq_ignore_ascii_case(name))
            })
        })
        .or_else(|| {
            files.iter().find(|p| {
                p.extension()
                    .is_some_and(|e| e.eq_ignore_ascii_case("AppImage"))
                    && p.file_stem().is_some_and(|n| {
                        normalized(&n.to_string_lossy()).starts_with(&normalized(name))
                    })
            })
        })
        .cloned()
}

pub fn find_executable(name: &str) -> Result<PathBuf> {
    let path = Path::new(name);
    let bridge = is_flatpak();
    if path.is_absolute() {
        if path
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("app"))
        {
            return resolve_app_bundle(path);
        }
        if executable_file(path, bridge) {
            return Ok(path.into());
        }
    } else if !name.is_empty()
        && !name.contains(['/', '\\'])
        && let Some(path) = find_in_files(
            name,
            &directory_executables(&search_directories(bridge), bridge),
        )
    {
        if path
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("app"))
        {
            return resolve_app_bundle(&path);
        }
        return Ok(path);
    }
    Err(Error::new(
        "source_port_missing",
        format!(
            "Source port executable '{name}' was not found. Detect an installed port or enter its full path."
        ),
    ))
}

fn resolve_app_bundle(bundle: &Path) -> Result<PathBuf> {
    if !cfg!(target_os = "macos") {
        return Err(Error::validation(
            "macOS .app bundles can only be launched on macOS",
        ));
    }
    let info = bundle.join("Contents/Info.plist");
    let output = capture(
        "/usr/libexec/PlistBuddy",
        &[
            "-c".into(),
            "Print :CFBundleExecutable".into(),
            info.to_string_lossy().into_owned(),
        ],
        false,
        Duration::from_secs(2),
    )?;
    let name = String::from_utf8_lossy(&output).trim().to_string();
    if name.is_empty() || name.contains(['/', '\\']) || name == "." || name == ".." {
        return Err(Error::validation(
            "The application bundle has an invalid executable name",
        ));
    }
    let executable = bundle.join("Contents/MacOS").join(name);
    if !executable_file(&executable, false) {
        return Err(Error::validation(
            "The application bundle's executable is missing or not executable",
        ));
    }
    Ok(executable)
}

const KNOWN_PORTS: &[(&str, &str)] = &[
    ("gzdoom", "GZDoom"),
    ("uzdoom", "UZDoom"),
    ("vkdoom", "VKDoom"),
    ("lzdoom", "LZDoom"),
    ("zdoom", "ZDoom"),
    ("zandronum", "Zandronum"),
    ("crispy-doom", "Crispy Doom"),
    ("chocolate-doom", "Chocolate Doom"),
    ("prboom-plus", "PrBoom+"),
    ("dsda-doom", "DSDA-Doom"),
    ("woof", "Woof!"),
    ("helion", "Helion"),
    ("nyan-doom", "Nyan Doom"),
    ("nugget-doom", "Nugget Doom"),
    ("doomretro", "DOOM Retro"),
    ("eternity", "Eternity Engine"),
    ("odamex", "Odamex"),
    ("doomsday", "Doomsday"),
    ("doom64ex", "Doom 64 EX"),
];

pub fn detect_ports() -> Result<Vec<Port>> {
    let bridge = is_flatpak();
    if bridge {
        // Distinguish a denied host boundary from simply having no engines.
        capture("/usr/bin/true", &[], true, Duration::from_secs(2)).map_err(|e|
            Error::new("host_access", format!("Host source-port discovery is unavailable: {e}. Check the Flatpak org.freedesktop.Flatpak permission.")))?;
    }
    let files = directory_executables(&search_directories(bridge), bridge);
    let mut result = Vec::new();
    for (binary, name) in KNOWN_PORTS {
        if let Some(path) = find_in_files(binary, &files) {
            result.push(Port {
                name: (*name).into(),
                executable: path
                    .file_name()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .into_owned(),
                directory: path
                    .parent()
                    .unwrap_or(Path::new(""))
                    .to_string_lossy()
                    .into_owned(),
                launch_type: 0,
                ..Port::default()
            });
        }
    }
    if cfg!(target_os = "linux") {
        if let Ok(output) = capture(
            "flatpak",
            &[
                "list".into(),
                "--app".into(),
                "--columns=application".into(),
            ],
            bridge,
            Duration::from_secs(3),
        ) {
            let output = String::from_utf8_lossy(&output);
            for (app, name) in [
                ("org.zdoom.GZDoom", "GZDoom"),
                ("org.zdoom.UZDoom", "UZDoom"),
                ("org.zdoom.VKDoom", "VKDoom"),
            ] {
                if output.lines().any(|line| line.trim() == app) {
                    result.push(Port {
                        name: format!("{name} (Flatpak)"),
                        executable: format!("flatpak:{app}"),
                        ..Port::default()
                    });
                }
            }
        }
        if let Ok(output) = capture("snap", &["list".into()], bridge, Duration::from_secs(3))
            && String::from_utf8_lossy(&output)
                .lines()
                .any(|line| line.split_whitespace().next() == Some("gzdoom"))
        {
            result.push(Port {
                name: "GZDoom (Snap)".into(),
                executable: "snap:gzdoom".into(),
                ..Port::default()
            });
        }
    }
    result.sort_by_key(|p| (!p.name.starts_with("GZDoom"), p.name.to_lowercase()));
    let mut seen = HashSet::new();
    result.retain(|p| seen.insert((p.directory.to_lowercase(), p.executable.to_lowercase())));
    Ok(result)
}

fn split_parameters(parameters: &str) -> Result<Vec<String>> {
    // Preserve Qt's existing double-quoted parameter contract. Backslashes
    // are literal (important for Windows paths), and shell syntax has no
    // special meaning. Triple quotes encode a literal quote, as in Qt.
    let mut result = Vec::new();
    let mut word = String::new();
    let mut quoted = false;
    let mut started = false;
    let chars: Vec<char> = parameters.chars().collect();
    let mut i = 0;
    while i < chars.len() {
        let c = chars[i];
        if c == '\0' {
            return Err(Error::validation(
                "Launch parameters cannot contain NUL bytes",
            ));
        }
        if c == '"' {
            started = true;
            if chars.get(i + 1) == Some(&'"') && chars.get(i + 2) == Some(&'"') {
                word.push('"');
                i += 3;
                continue;
            }
            quoted = !quoted;
        } else if c.is_whitespace() && !quoted {
            if started {
                result.push(std::mem::take(&mut word));
                started = false;
            }
        } else {
            started = true;
            word.push(c);
        }
        i += 1;
    }
    if quoted {
        return Err(Error::validation(
            "Launch parameters contain an unmatched double quote",
        ));
    }
    if started {
        result.push(word);
    }
    Ok(result)
}

pub fn warp_arguments(map: &str) -> Vec<String> {
    let name = map.trim();
    let bytes = name.as_bytes();
    if bytes.len() == 4
        && bytes[0].eq_ignore_ascii_case(&b'E')
        && bytes[2].eq_ignore_ascii_case(&b'M')
        && bytes[1].is_ascii_digit()
        && bytes[3].is_ascii_digit()
    {
        return vec![
            "-warp".into(),
            (bytes[1] - b'0').to_string(),
            (bytes[3] - b'0').to_string(),
        ];
    }
    if bytes.len() == 5
        && bytes[..3].eq_ignore_ascii_case(b"MAP")
        && bytes[3..].iter().all(u8::is_ascii_digit)
    {
        return vec![
            "-warp".into(),
            name[3..].parse::<u8>().unwrap_or(0).to_string(),
        ];
    }
    vec!["+map".into(), name.into()]
}

pub fn statistics_kind(executable: &str) -> StatisticsKind {
    let name = executable.to_ascii_lowercase();
    if [
        "gzdoom",
        "uzdoom",
        "vkdoom",
        "lzdoom",
        "zandronum",
        "zdoom",
        "org.zdoom",
    ]
    .iter()
    .any(|v| name.contains(v))
    {
        return StatisticsKind::ZdoomSave;
    }
    if [
        "crispy-doom",
        "crispy-heretic",
        "so-doom",
        "inter-heretic",
        "boom-plus",
        "prboom-plus",
        "dsda-doom",
        "nyan-doom",
        "fdwl",
        "woof",
        "nugget-doom",
        "cherry-doom",
    ]
    .iter()
    .any(|v| name.contains(v))
    {
        return StatisticsKind::Levelstat;
    }
    if ["chocolate-doom", "cndoom", "crl-doom", "inter-doom"]
        .iter()
        .any(|v| name.contains(v))
    {
        return StatisticsKind::Statdump;
    }
    StatisticsKind::None
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum SaveContract {
    Zdoom,
    Vanilla,
    Boom,
    None,
}

fn save_contract(executable: &str) -> SaveContract {
    let name = executable.to_ascii_lowercase();
    if statistics_kind(&name) == StatisticsKind::ZdoomSave {
        SaveContract::Zdoom
    } else if ["chocolate-doom", "crispy-doom"]
        .iter()
        .any(|family| name.contains(family))
    {
        SaveContract::Vanilla
    } else if [
        "dsda-doom",
        "prboom-plus",
        "woof",
        "nugget-doom",
        "eternity",
    ]
    .iter()
    .any(|family| name.contains(family))
    {
        SaveContract::Boom
    } else {
        SaveContract::None
    }
}

fn managed_save_directory(port: &Port, options: &LaunchOptions, data_root: &Path) -> PathBuf {
    let root = if port.alt_save_directory.is_empty() {
        data_root.join("GameFiles/SaveGames")
    } else {
        resolve_stored(&port.alt_save_directory, data_root)
    };
    root.join(options.game_file_id.to_string())
        .join(options.source_port_id.to_string())
}

fn save_slot(executable: &str, path: &Path) -> Option<u32> {
    let name = path.file_name()?.to_str()?.to_ascii_lowercase();
    let executable = executable.to_ascii_lowercase();
    if name == "autosave.dsg" && (executable.contains("woof") || executable.contains("nugget")) {
        return Some(255);
    }
    let prefixes: &[&str] = if executable.contains("woof") {
        &["woofsav"]
    } else if executable.contains("nugget") {
        &["nuggsav"]
    } else if executable.contains("eternity") {
        &["etersav"]
    } else if executable.contains("dsda-doom") || executable.contains("prboom-plus") {
        // BEX SAVEGAMENAME can override these engines' prefixes. The
        // directory is already isolated to this game and source port.
        let stem = name.strip_suffix(".dsg")?;
        if stem.starts_with("demosav") {
            return None;
        }
        let split = stem
            .rfind(|c: char| !c.is_ascii_digit())
            .map_or(0, |i| i + 1);
        return stem[split..].parse::<u32>().ok().filter(|slot| {
            *slot
                <= if executable.contains("dsda-doom") {
                    118
                } else {
                    i32::MAX as u32
                }
        });
    } else {
        &["doomsav"]
    };
    prefixes
        .iter()
        .find_map(|prefix| {
            name.strip_prefix(prefix)?
                .strip_suffix(".dsg")?
                .parse::<u32>()
                .ok()
        })
        .filter(|slot| {
            !(executable.contains("woof") || executable.contains("nugget"))
                || (*slot <= 77 && slot % 10 <= 7)
        })
}

pub fn latest_save(directories: &[PathBuf]) -> Option<PathBuf> {
    latest_save_matching(directories, |path| {
        path.extension().is_some_and(|e| {
            ["zds", "dsg", "esg", "hsg"]
                .iter()
                .any(|ext| e.eq_ignore_ascii_case(ext))
        })
    })
}

fn latest_save_matching(
    directories: &[PathBuf],
    matches: impl Fn(&Path) -> bool,
) -> Option<PathBuf> {
    directories
        .iter()
        .flat_map(|dir| {
            walkdir::WalkDir::new(dir)
                .max_depth(5)
                .follow_links(false)
                .into_iter()
                .filter_map(|v| v.ok())
        })
        .filter(|entry| entry.file_type().is_file() && matches(entry.path()))
        .filter_map(|entry| {
            entry
                .metadata()
                .ok()?
                .modified()
                .ok()
                .map(|time| (time, entry.into_path()))
        })
        .max_by_key(|(time, _)| *time)
        .map(|(_, path)| path)
}

fn absolute_path(path: &Path) -> Result<PathBuf> {
    if path.is_absolute() {
        Ok(path.into())
    } else {
        Ok(std::env::current_dir()?.join(path))
    }
}

/// Grant an engine the managed game directory plus the input locations it
/// actually needs. The launcher data root (database, preferences, journals)
/// is deliberately not writable by sibling Flatpak engines.
fn flatpak_filesystem_arguments(
    data_root: &Path,
    files: &[PathBuf],
    save_directory: Option<&Path>,
) -> Result<Vec<String>> {
    let managed = absolute_path(&data_root.join("GameFiles"))?;
    let canonical_managed = fs::canonicalize(&managed).unwrap_or_else(|_| managed.clone());
    let mut grants: HashMap<PathBuf, bool> = HashMap::new();
    grants.insert(managed.clone(), true);
    if canonical_managed != managed {
        grants.insert(canonical_managed.clone(), true);
    }
    if let Some(directory) = save_directory {
        let directory = absolute_path(directory)?;
        if !directory.starts_with(&managed) {
            grants.insert(directory.clone(), true);
        }
        if let Ok(canonical) = fs::canonicalize(&directory)
            && !canonical.starts_with(&canonical_managed)
        {
            grants.insert(canonical, true);
        }
    }
    for file in files {
        let file = absolute_path(file)?;
        let resolved = fs::canonicalize(&file).unwrap_or_else(|_| file.clone());
        for file in [&file, &resolved] {
            if file.starts_with(&managed) || file.starts_with(&canonical_managed) {
                continue;
            }
            // Flatpak reserves these destinations for its own runtime and
            // cannot bind host files onto them with --filesystem.
            if [
                "/usr", "/etc", "/bin", "/sbin", "/lib", "/lib64", "/proc", "/sys", "/dev",
            ]
            .iter()
            .any(|p| file.starts_with(p))
            {
                return Err(Error::validation(format!(
                    "Flatpak cannot expose the host file {} at that reserved path. Import it into the managed library first.",
                    file.display()
                )));
            }
            let parent = if file.is_dir() {
                file.as_path()
            } else {
                file.parent().unwrap_or(file)
            };
            // A file at / must not turn into a read grant for the whole host.
            let grant = if parent.parent().is_none() {
                file.clone()
            } else {
                parent.to_path_buf()
            };
            grants.entry(grant).or_insert(false);
        }
    }
    let writable: Vec<_> = grants
        .iter()
        .filter_map(|(p, rw)| rw.then_some(p.clone()))
        .collect();
    grants.retain(|path, rw| *rw || !writable.iter().any(|root| path.starts_with(root)));
    let mut grants = grants.into_iter().collect::<Vec<_>>();
    grants.sort_by(|a, b| a.0.cmp(&b.0));
    grants
        .into_iter()
        .map(|(path, write)| {
            let path = path
                .to_str()
                .ok_or_else(|| Error::validation("Flatpak file paths must be valid Unicode"))?;
            if path.contains('\0') {
                return Err(Error::validation(
                    "Flatpak file paths cannot contain NUL bytes",
                ));
            }
            Ok(format!(
                "--filesystem={path}:{}",
                if write { "rw" } else { "ro" }
            ))
        })
        .collect()
}

fn add_flatpak_grants(
    arguments: &mut Vec<String>,
    port: &Port,
    data_root: &Path,
    files: &[PathBuf],
) -> Result<()> {
    if port
        .executable
        .trim()
        .to_ascii_lowercase()
        .starts_with("flatpak:")
    {
        let save = (!port.alt_save_directory.is_empty())
            .then(|| resolve_stored(&port.alt_save_directory, data_root));
        arguments.splice(
            1..1,
            flatpak_filesystem_arguments(data_root, files, save.as_deref())?,
        );
    }
    Ok(())
}

pub fn build_plan(
    port: &Port,
    game: &Path,
    iwad: Option<&Path>,
    additional: &[PathBuf],
    options: &LaunchOptions,
    data_root: &Path,
) -> Result<LaunchPlan> {
    build_plan_paths(port, &[game.into()], iwad, additional, options, data_root)
}

pub fn build_plan_paths(
    port: &Port,
    selected: &[PathBuf],
    iwad: Option<&Path>,
    additional: &[PathBuf],
    options: &LaunchOptions,
    data_root: &Path,
) -> Result<LaunchPlan> {
    let executable = port.executable.trim();
    if executable.is_empty() {
        return Err(Error::validation("Select a source port before launching"));
    }
    if executable.contains('\0') {
        return Err(Error::validation(
            "Source port paths cannot contain NUL bytes",
        ));
    }
    if !cfg!(windows)
        && Path::new(executable)
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("exe"))
    {
        return Err(Error::new(
            "platform_incompatible",
            "Windows .exe engines cannot be launched natively on this platform. Select a native engine or configure an executable compatibility wrapper.",
        ));
    }
    let bridge = is_flatpak();
    let mut arguments = Vec::new();
    let configured_directory =
        (!port.directory.is_empty()).then(|| resolve_stored(&port.directory, data_root));
    let program = if executable.to_ascii_lowercase().starts_with("flatpak:") {
        if !cfg!(target_os = "linux") {
            return Err(Error::validation("Flatpak engines require Linux"));
        }
        let id = executable[8..].trim();
        if !Regex::new(r"^[A-Za-z_][A-Za-z0-9_-]*(\.[A-Za-z_][A-Za-z0-9_-]*){2,}$")
            .unwrap()
            .is_match(id)
        {
            return Err(Error::validation("Invalid Flatpak application ID"));
        }
        arguments.extend(["run", id, "--"].map(str::to_string));
        "flatpak".to_string()
    } else if executable.to_ascii_lowercase().starts_with("snap:") {
        if !cfg!(target_os = "linux") {
            return Err(Error::validation("Snap engines require Linux"));
        }
        let name = executable[5..].trim();
        if !Regex::new(r"^[a-z0-9][a-z0-9_-]*(\.[a-zA-Z0-9_-]+)?$")
            .unwrap()
            .is_match(name)
        {
            return Err(Error::validation("Invalid Snap application name"));
        }
        arguments.extend(["run", name].map(str::to_string));
        "snap".to_string()
    } else {
        let configured = configured_directory.as_ref().map(|d| d.join(executable));
        let path = if let Some(path) = configured.filter(|p| {
            executable_file(p, bridge)
                || (cfg!(target_os = "macos")
                    && p.extension().is_some_and(|e| e.eq_ignore_ascii_case("app")))
        }) {
            if path
                .extension()
                .is_some_and(|e| e.eq_ignore_ascii_case("app"))
            {
                resolve_app_bundle(&path)?
            } else {
                path
            }
        } else {
            find_executable(executable)?
        };
        path.to_string_lossy().into_owned()
    };
    let directory = configured_directory.or_else(|| {
        if Path::new(&program).is_absolute() {
            Path::new(&program).parent().map(Path::to_path_buf)
        } else {
            host_home(bridge)
        }
    });
    let mut plan = LaunchPlan {
        program,
        arguments: vec![],
        working_directory: directory
            .unwrap_or_else(|| data_root.into())
            .to_string_lossy()
            .into_owned(),
        host_bridge: bridge,
        statistics_kind: StatisticsKind::None,
        statistics_file: None,
        save_directories: vec![],
        demo_path: None,
        skill: options.skill,
    };
    arguments.extend(split_parameters(&port.extra_parameters)?);
    if port.launch_type == 2 {
        if !options.map.trim().is_empty()
            || options.skill.is_some()
            || options.record_demo
            || options
                .play_demo_file
                .as_ref()
                .is_some_and(|v| !v.is_empty())
            || options.load_latest_save
            || options.save_statistics
            || !options.additional_files.is_empty()
            || !additional.is_empty()
        {
            return Err(Error::validation(
                "Doom 64 rerelease launches its installed game with custom parameters and play-time tracking. Automatic map, skill, mods, demos, save loading and level statistics are unavailable for this engine; disable those options or use a compatible Doom 64 source port.",
            ));
        }
        // KEX owns its installed IWAD and has a different option contract.
        // Launch it faithfully without inventing classic Doom arguments.
        arguments.extend(split_parameters(&options.extra_params)?);
        add_flatpak_grants(&mut arguments, port, data_root, &[])?;
        plan.arguments = arguments;
        return Ok(plan);
    }
    if options.extra_params_only {
        arguments.extend(split_parameters(&options.extra_params)?);
        add_flatpak_grants(&mut arguments, port, data_root, &[])?;
        plan.arguments = arguments;
        return Ok(plan);
    }
    if options.load_latest_save
        && (options.record_demo
            || options
                .play_demo_file
                .as_ref()
                .is_some_and(|v| !v.is_empty()))
    {
        return Err(Error::validation(
            "Load latest save cannot be combined with demo recording or playback. Disable one option; recorded demos start from a new game.",
        ));
    }
    if let Some(skill) = options.skill
        && !(1..=5).contains(&skill)
    {
        return Err(Error::validation("Skill must be between 1 and 5"));
    }
    if selected.is_empty() && iwad.is_none() && port.launch_type != 1 {
        return Err(Error::validation("No game file selected"));
    }
    let mut access_files: Vec<PathBuf> = additional
        .iter()
        .chain(selected)
        .cloned()
        .chain(iwad.map(Path::to_path_buf))
        .collect();
    for path in additional
        .iter()
        .chain(selected)
        .map(PathBuf::as_path)
        .chain(iwad)
    {
        if !path.exists() {
            return Err(Error::new(
                "not_found",
                format!("Game file does not exist: {}", path.display()),
            ));
        }
    }
    if let Some(iwad) = iwad {
        arguments.extend(["-iwad".into(), iwad.to_string_lossy().into_owned()]);
    }
    let mut normal = Vec::new();
    let mut deh = Vec::new();
    for path in additional.iter().chain(selected) {
        // An IWAD-only launch passes the selected file as the IWAD. Never
        // load it again as a PWAD, but keep all explicitly ordered add-ons.
        if iwad.is_some_and(|iwad| iwad == path) {
            continue;
        }
        let value = path.to_string_lossy().into_owned();
        if path
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("deh") || e.eq_ignore_ascii_case("bex"))
        {
            deh.push(value);
        } else {
            normal.push(value);
        }
    }
    if !normal.is_empty() {
        let file_option = port.file_option.trim();
        if !file_option.is_empty()
            && (!file_option.starts_with(['-', '+'])
                || file_option.chars().any(|c| c.is_whitespace() || c == '\0'))
        {
            return Err(Error::validation(
                "The source port file option must be one option beginning with '-' or '+', such as -file",
            ));
        }
        arguments.push(if file_option.is_empty() {
            "-file".into()
        } else {
            file_option.into()
        });
        arguments.extend(normal);
    }
    if !deh.is_empty() {
        arguments.push("-deh".into());
        arguments.extend(deh);
    }
    if !options.map.trim().is_empty() {
        arguments.extend(warp_arguments(&options.map));
    }
    if let Some(skill) = options.skill {
        arguments.extend(["-skill".into(), skill.to_string()]);
    }
    if let Some(demo) = options.play_demo_file.as_ref().filter(|v| !v.is_empty()) {
        let path = resolve_stored(demo, data_root);
        if !path.is_file() {
            return Err(Error::new(
                "not_found",
                format!("Demo file does not exist: {}", path.display()),
            ));
        }
        access_files.push(path.clone());
        arguments.extend(["-playdemo".into(), path.to_string_lossy().into_owned()]);
    } else if options.record_demo {
        let path = data_root.join("GameFiles/Demos").join(format!(
            "{}-{}.lmp",
            options.game_file_id,
            Uuid::new_v4()
        ));
        let value = path.to_string_lossy().into_owned();
        // Doom engines add the .lmp extension themselves. Vanilla-derived
        // ports append it even when it is already present (foo.lmp.lmp).
        arguments.extend([
            "-record".into(),
            path.with_extension("").to_string_lossy().into_owned(),
        ]);
        plan.demo_path = Some(value);
    }
    let contract = save_contract(executable);
    if contract != SaveContract::None {
        let directory = managed_save_directory(port, options, data_root);
        arguments.extend([
            if contract == SaveContract::Boom {
                "-save"
            } else {
                "-savedir"
            }
            .into(),
            directory.to_string_lossy().into_owned(),
        ]);
        plan.save_directories.push(directory);
    }
    if options.load_latest_save {
        if contract == SaveContract::None {
            return Err(Error::validation(
                "Automatic save loading is unavailable for this source port. Use its own load menu or enter its documented load parameters in Extra parameters only mode.",
            ));
        }
        let path = latest_save_matching(&plan.save_directories, |path| {
            if contract == SaveContract::Zdoom {
                path.extension().is_some_and(|ext| ext.eq_ignore_ascii_case("zds"))
            } else {
                path.parent() == plan.save_directories.first().map(PathBuf::as_path)
                    && save_slot(executable, path).is_some()
            }
        }).ok_or_else(|| Error::new(
            "save_not_found",
            "No save exists for this game and source port. Start the game and save first, or disable Load latest save.",
        ))?;
        let value = if contract != SaveContract::Zdoom {
            save_slot(executable, &path).ok_or_else(|| Error::validation(
                "The latest save filename does not identify a compatible numbered save slot.",
            ))?.to_string()
        } else if ["gzdoom", "uzdoom", "vkdoom"]
            .iter()
            .any(|family| executable.to_ascii_lowercase().contains(family))
        {
            // Current GZDoom-derived loaders prepend -savedir themselves.
            // Legacy ZDoom/LZDoom/Zandronum load the supplied full path.
            path.strip_prefix(&plan.save_directories[0])
                .map_err(|_| Error::validation("Save file is outside the selected game directory"))?
                .to_string_lossy()
                .into_owned()
        } else {
            path.to_string_lossy().into_owned()
        };
        access_files.push(path);
        arguments.extend(["-loadgame".into(), value]);
    }
    arguments.extend(split_parameters(&options.extra_params)?);
    if options.save_statistics {
        plan.statistics_kind = statistics_kind(executable);
        match plan.statistics_kind {
            StatisticsKind::Levelstat => {
                // Installed engines commonly live in read-only directories.
                // Give their fixed levelstat filename a private writable cwd.
                // Keep it outside Temp: engines may also create manual saves,
                // screenshots or configuration files in their cwd.
                // An explicit custom cwd is retained for relative asset paths.
                if port.directory.is_empty()
                    || Path::new(&plan.program).parent() == Some(Path::new(&plan.working_directory))
                    || executable.starts_with("flatpak:")
                    || executable.starts_with("snap:")
                {
                    plan.working_directory = data_root
                        .join("GameFiles/EngineData")
                        .join(options.game_file_id.to_string())
                        .join(options.source_port_id.to_string())
                        .to_string_lossy()
                        .into_owned();
                }
                plan.statistics_file =
                    Some(Path::new(&plan.working_directory).join("levelstat.txt"));
                arguments.push("-levelstat".into());
            }
            StatisticsKind::Statdump => {
                let path = data_root
                    .join("GameFiles/Temp")
                    .join(format!("statdump-{}.txt", Uuid::new_v4()));
                arguments.extend(["-statdump".into(), path.to_string_lossy().into_owned()]);
                plan.statistics_file = Some(path);
            }
            _ => (),
        }
    }
    add_flatpak_grants(&mut arguments, port, data_root, &access_files)?;
    plan.arguments = arguments;
    Ok(plan)
}

pub fn parse_levelstat(text: &str) -> Vec<LevelStatistics> {
    static RE: OnceLock<Regex> = OnceLock::new();
    let re = RE.get_or_init(|| Regex::new(r"(?m)^\s*(\S+)\s*-\s*((?:\d+:)?\d+:\d+(?:\.\d+)?)\s*\([^)]*\)\s*K:\s*(\d+)/(\d+)[^I\r\n]*I:\s*(\d+)/(\d+)[^S\r\n]*S:\s*(\d+)/(\d+)").unwrap());
    re.captures_iter(text)
        .filter_map(|c| {
            let total_kills = c[4].parse().ok()?;
            Some(LevelStatistics {
                map_name: c[1].into(),
                level_time: c[2].split(':').try_fold(0.0, |sum, part| {
                    part.parse::<f64>().ok().map(|v| sum * 60.0 + v)
                })?,
                kill_count: c[3].parse::<i64>().ok()?.min(total_kills),
                total_kills,
                item_count: c[5].parse().ok()?,
                total_items: c[6].parse().ok()?,
                secret_count: c[7].parse().ok()?,
                total_secrets: c[8].parse().ok()?,
                skill: -1,
            })
        })
        .collect()
}

pub fn parse_statdump(text: &str) -> Vec<LevelStatistics> {
    static RE: OnceLock<Regex> = OnceLock::new();
    let re = RE.get_or_init(|| Regex::new(r"=+(\w+(?:/\w+)?)=+Time:(\d+):(\d+)\(par:\d+:\d+\)\w+\(\w+\):Kills:(\d+)(?:/(\d+)(?:\(-?\d+%\))?)?Items:(\d+)(?:/(\d+)(?:\(-?\d+%\))?)?Secrets:(\d+)(?:/(\d+))?").unwrap());
    let stripped: String = text.chars().filter(|c| !c.is_whitespace()).collect();
    re.captures_iter(&stripped)
        .filter_map(|c| {
            let kills: i64 = c[4].parse().ok()?;
            let items = c[6].parse().ok()?;
            let secrets = c[8].parse().ok()?;
            let total_kills = c
                .get(5)
                .and_then(|v| v.as_str().parse().ok())
                .unwrap_or(kills);
            Some(LevelStatistics {
                map_name: c[1].into(),
                level_time: c[2].parse::<f64>().ok()? * 60.0 + c[3].parse::<f64>().ok()?,
                kill_count: kills.min(total_kills),
                total_kills,
                item_count: items,
                total_items: c
                    .get(7)
                    .and_then(|v| v.as_str().parse().ok())
                    .unwrap_or(items),
                secret_count: secrets,
                total_secrets: c
                    .get(9)
                    .and_then(|v| v.as_str().parse().ok())
                    .unwrap_or(secrets),
                skill: -1,
            })
        })
        .collect()
}

pub fn parse_zdoom_save(path: &Path) -> Result<Vec<LevelStatistics>> {
    let mut zip = zip::ZipArchive::new(File::open(path)?)?;
    let mut entry = match zip.by_name("globals.json") {
        Ok(entry) => entry,
        Err(zip::result::ZipError::FileNotFound) => return Ok(vec![]),
        Err(error) => return Err(error.into()),
    };
    if entry.size() > 16 * 1024 * 1024 {
        return Err(Error::corrupt("Save game statistics exceed 16 MiB"));
    }
    let mut bytes = Vec::new();
    (&mut entry)
        .take(16 * 1024 * 1024 + 1)
        .read_to_end(&mut bytes)?;
    if bytes.len() > 16 * 1024 * 1024 {
        return Err(Error::corrupt("Save game statistics exceed 16 MiB"));
    }
    let value: serde_json::Value = serde_json::from_slice(&bytes)?;
    let mut skill = -1;
    if let Some(cvars) = value["importantcvars"].as_str().filter(|s| !s.is_empty()) {
        let parts = cvars.split('\\').collect::<Vec<_>>();
        for pair in parts.windows(2) {
            if pair[0] == "skill" {
                skill = pair[1]
                    .parse::<i64>()
                    .ok()
                    .and_then(|v| v.checked_add(1))
                    .unwrap_or(-1);
                break;
            }
        }
    } else {
        let v = &value["servercvars"]["skill"];
        skill = v
            .as_i64()
            .or_else(|| v.as_str().and_then(|s| s.parse().ok()))
            .and_then(|v| v.checked_add(1))
            .unwrap_or(-1);
    }
    let Some(levels) = value["statistics"]["levels"].as_array() else {
        return Ok(vec![]);
    };
    Ok(levels
        .iter()
        .filter_map(|level| {
            let name = level["levelname"].as_str().filter(|s| !s.is_empty())?;
            let number = |key: &str| level[key].as_i64().unwrap_or(0).max(0);
            let level_time = level["leveltime"].as_f64().unwrap_or(0.0).max(0.0) / 35.0;
            Some(LevelStatistics {
                map_name: name.into(),
                kill_count: number("killcount").min(number("totalkills")),
                total_kills: number("totalkills"),
                item_count: number("itemcount"),
                total_items: number("totalitems"),
                secret_count: number("secretcount"),
                total_secrets: number("totalsecrets"),
                level_time,
                skill,
            })
        })
        .collect())
}

#[derive(Clone, Debug, PartialEq)]
struct Fingerprint {
    modified: Option<SystemTime>,
    length: u64,
}

fn fingerprint(path: &Path) -> Option<Fingerprint> {
    fs::metadata(path).ok().map(|m| Fingerprint {
        modified: m.modified().ok(),
        length: m.len(),
    })
}

fn save_files(dirs: &[PathBuf]) -> Vec<PathBuf> {
    let mut seen = HashSet::new();
    dirs.iter()
        .flat_map(|d| {
            walkdir::WalkDir::new(d)
                .max_depth(6)
                .follow_links(false)
                .into_iter()
                .filter_map(|v| v.ok())
        })
        .filter(|e| {
            e.file_type().is_file()
                && e.path()
                    .extension()
                    .is_some_and(|x| x.eq_ignore_ascii_case("zds"))
        })
        .map(|e| e.into_path())
        .filter(|p| seen.insert(p.clone()))
        .collect()
}

struct StatisticsSnapshot {
    files: HashMap<PathBuf, Fingerprint>,
    previous_text: Option<String>,
}

impl StatisticsSnapshot {
    fn capture(plan: &LaunchPlan) -> Self {
        let paths = if plan.statistics_kind == StatisticsKind::ZdoomSave {
            save_files(&plan.save_directories)
        } else {
            plan.statistics_file.iter().cloned().collect()
        };
        Self {
            files: paths
                .into_iter()
                .filter_map(|p| fingerprint(&p).map(|f| (p, f)))
                .collect(),
            previous_text: plan
                .statistics_file
                .as_ref()
                .and_then(|path| read_bounded_text(path, 16 * 1024 * 1024).ok()),
        }
    }

    fn collect(&self, plan: &LaunchPlan) -> (Vec<LevelStatistics>, Vec<String>) {
        let mut stats = Vec::new();
        let mut warnings = Vec::new();
        match plan.statistics_kind {
            StatisticsKind::None => (),
            StatisticsKind::Levelstat | StatisticsKind::Statdump => {
                if let Some(path) = &plan.statistics_file
                    && let Some(current) = fingerprint(path)
                    && self.files.get(path) != Some(&current)
                {
                    match read_bounded_text(path, 16 * 1024 * 1024) {
                        Ok(text) => {
                            // Owned EngineData output is rotated before launch.
                            // Preserve append semantics for custom directories;
                            // known Crispy Doom truncates on its first level.
                            let rewritten =
                                plan.program.to_ascii_lowercase().contains("crispy-doom")
                                    || plan.statistics_kind == StatisticsKind::Statdump;
                            let fresh = if rewritten {
                                text.as_str()
                            } else {
                                self.previous_text
                                    .as_ref()
                                    .filter(|old| text.len() > old.len())
                                    .and_then(|old| text.strip_prefix(old))
                                    .unwrap_or(&text)
                            };
                            stats = if plan.statistics_kind == StatisticsKind::Levelstat {
                                parse_levelstat(fresh)
                            } else {
                                parse_statdump(fresh)
                            };
                        }
                        Err(e) => warnings
                            .push(format!("Could not read statistics {}: {e}", path.display())),
                    }
                }
            }
            StatisticsKind::ZdoomSave => {
                for path in save_files(&plan.save_directories) {
                    if fingerprint(&path)
                        .as_ref()
                        .is_some_and(|current| self.files.get(&path) != Some(current))
                    {
                        match parse_zdoom_save(&path) {
                            Ok(levels) => stats.extend(levels),
                            Err(e) => warnings.push(format!(
                                "Could not read save statistics {}: {e}",
                                path.display()
                            )),
                        }
                    }
                }
            }
        }
        for stat in &mut stats {
            if stat.skill < 1 {
                stat.skill = plan.skill.map(i64::from).unwrap_or(-1);
            }
        }
        let mut unique: Vec<LevelStatistics> = Vec::new();
        for stat in stats {
            if !unique.iter().any(|old| {
                old.map_name.eq_ignore_ascii_case(&stat.map_name)
                    && old.kill_count == stat.kill_count
                    && old.total_kills == stat.total_kills
                    && old.item_count == stat.item_count
                    && old.total_items == stat.total_items
                    && old.secret_count == stat.secret_count
                    && old.total_secrets == stat.total_secrets
                    && (old.level_time - stat.level_time).abs() < 0.01
                    && old.skill == stat.skill
            }) {
                unique.push(stat);
            }
        }
        (unique, warnings)
    }
}

fn read_bounded_text(path: &Path, limit: u64) -> Result<String> {
    let mut data = Vec::new();
    File::open(path)?.take(limit + 1).read_to_end(&mut data)?;
    if data.len() as u64 > limit {
        return Err(Error::corrupt(format!(
            "File is too large: {}",
            path.display()
        )));
    }
    Ok(String::from_utf8_lossy(&data).into_owned())
}

fn save_session(data_root: &Path, session: &Session) -> Result<()> {
    let directory = data_root.join("Sessions");
    fs::create_dir_all(&directory)?;
    let target = directory.join(format!("{}.json", session.id));
    let temporary = directory.join(format!(".{}-{}.tmp", session.id, Uuid::new_v4()));
    let result = (|| {
        let mut file = private_file(&temporary)?;
        file.write_all(&serde_json::to_vec_pretty(session)?)?;
        file.sync_all()?;
        drop(file);
        // std::fs::rename replaces existing files on Unix and Windows; keep
        // the old journal intact until the new complete document is ready.
        fs::rename(&temporary, &target)?;
        #[cfg(unix)]
        File::open(&directory)?.sync_all()?;
        Ok(())
    })();
    if result.is_err() {
        let _ = fs::remove_file(temporary);
    }
    result
}

fn successful_vanilla_recording(plan: &LaunchPlan, log_path: &Path) -> bool {
    if !["chocolate-doom", "crispy-doom"].iter().any(|name| {
        plan.program.to_ascii_lowercase().contains(name)
            || plan
                .arguments
                .iter()
                .any(|arg| arg.to_ascii_lowercase().contains(name))
    }) {
        return false;
    }
    let Some(path) = plan.demo_path.as_ref().filter(|path| {
        fs::metadata(path).is_ok_and(|metadata| metadata.is_file() && metadata.len() > 13)
    }) else {
        return false;
    };
    fs::read_to_string(log_path).is_ok_and(|log| {
        let expected = format!("Demo {path} recorded");
        // Crispy's success format ends with a literal NUL from its %c slot.
        log.lines()
            .any(|line| line.trim_matches(|c: char| c.is_whitespace() || c == '\0') == expected)
    })
}

pub fn start_session(
    plan: LaunchPlan,
    game_file_id: i64,
    source_port_id: i64,
    data_root: PathBuf,
) -> Result<Session> {
    fs::create_dir_all(data_root.join("Sessions"))?;
    if let Some(path) = &plan.demo_path
        && let Some(dir) = Path::new(path).parent()
    {
        fs::create_dir_all(dir)?;
    }
    for directory in &plan.save_directories {
        fs::create_dir_all(directory)?;
    }
    if matches!(
        plan.statistics_kind,
        StatisticsKind::Statdump | StatisticsKind::Levelstat
    ) && let Some(dir) = plan.statistics_file.as_ref().and_then(|p| p.parent())
    {
        fs::create_dir_all(dir)?;
        // Surface permission failures before the engine starts: fixed-output
        // ports otherwise continue playing while silently losing statistics.
        let probe = dir.join(format!(".doomedlauncher-write-{}", Uuid::new_v4()));
        private_file(&probe).map_err(|error| Error::new(
            "statistics_directory",
            format!("Cannot write statistics in {}: {error}. Choose a writable source-port working directory or disable statistics.", dir.display()),
        ))?;
        fs::remove_file(probe)?;
    }
    let id = Uuid::new_v4().to_string();
    let log_path = data_root.join("Sessions").join(format!("{id}.log"));
    let log = private_file(&log_path)?;
    // Levelstat ports write one fixed filename. Refuse a second writer to
    // that file instead of attributing one game's statistics to another.
    let mut active = locked_sessions()?;
    if plan.statistics_kind == StatisticsKind::Levelstat
        && active.values().any(|entry| {
            entry.session.status == SessionStatus::Running
                && entry.statistics_file == plan.statistics_file
        })
    {
        return Err(Error::new(
            "session_conflict",
            "This engine already has a statistics session in its working directory. Wait for it to finish or disable statistics for the new launch.",
        ));
    }
    // Preserve each previous output before launching in our own working
    // directory. Engines differ in whether they overwrite or append; a fresh
    // fixed filename handles both without discarding identical new results.
    if plan.statistics_kind == StatisticsKind::Levelstat
        && let Some(path) = plan.statistics_file.as_ref()
        && path.starts_with(data_root.join("GameFiles/EngineData"))
        && path.is_file()
    {
        let history = path.parent().unwrap().join("levelstat-history");
        fs::create_dir_all(&history)?;
        fs::rename(path, history.join(format!("{}.txt", Uuid::new_v4())))?;
    }
    let snapshot = StatisticsSnapshot::capture(&plan);
    let mut command = configured_command(
        &plan.program,
        &plan.arguments,
        &plan.working_directory,
        plan.host_bridge,
    );
    command
        .stdin(Stdio::null())
        .stdout(Stdio::from(log.try_clone()?))
        .stderr(Stdio::from(log));
    let mut child = process::OwnedProcess::spawn(&mut command).map_err(|e| {
        Error::new(
            "launch_failed",
            format!("Failed to start {}: {e}", plan.program),
        )
    })?;
    let started = Instant::now();
    let session = Session {
        id: id.clone(),
        game_file_id,
        source_port_id,
        process_id: child.id(),
        started_at: Utc::now().to_rfc3339(),
        finished_at: None,
        status: SessionStatus::Running,
        exit_code: None,
        elapsed_seconds: 0,
        error: None,
        statistics: vec![],
        log_path: log_path.to_string_lossy().into_owned(),
        demo_path: plan.demo_path.clone(),
    };
    if let Err(e) = save_session(&data_root, &session) {
        let _ = child.kill();
        let _ = child.wait();
        return Err(e);
    }
    let (sender, receiver) = mpsc::channel();
    active.insert(
        id.clone(),
        ActiveSession {
            session: session.clone(),
            started,
            stop: sender,
            data_root: data_root.clone(),
            statistics_file: plan.statistics_file.clone(),
        },
    );
    drop(active);
    // Keep a second handle until the monitoring thread starts so failure to
    // create that thread cannot leave an unmonitored engine behind.
    let child = std::sync::Arc::new(Mutex::new(child));
    let thread_child = child.clone();
    let thread_id = id.clone();
    let monitor_root = data_root.clone();
    let mut completed = session.clone();
    let thread_result = thread::Builder::new()
        .name(format!("engine-{id}"))
        .spawn(move || {
            let mut stopped = false;
            let outcome = loop {
                let mut process = match thread_child.lock() {
                    Ok(child) => child,
                    Err(_) => {
                        break Err(std::io::Error::other("Engine process state is unavailable"));
                    }
                };
                if receiver.try_recv().is_ok() {
                    stopped = true;
                    if let Err(e) = process.kill()
                        && process.try_wait().ok().flatten().is_none()
                    {
                        break Err(e);
                    }
                }
                match process.try_wait() {
                    Ok(Some(status)) => break Ok(status),
                    Ok(None) => (),
                    Err(e) => {
                        let _ = process.kill();
                        let _ = process.wait();
                        break Err(e);
                    }
                }
                drop(process);
                thread::sleep(Duration::from_millis(100));
            };
            completed.finished_at = Some(Utc::now().to_rfc3339());
            completed.elapsed_seconds = started.elapsed().as_secs();
            match outcome {
                Ok(status) => {
                    completed.exit_code = status.code();
                    // Vanilla-derived Doom reports a successfully finalized
                    // recording through I_Error, which intentionally exits -1.
                    let success = status.success()
                        || (matches!(status.code(), Some(255 | -1))
                            && successful_vanilla_recording(&plan, Path::new(&completed.log_path)));
                    completed.status = if stopped {
                        SessionStatus::Stopped
                    } else if success {
                        SessionStatus::Completed
                    } else {
                        SessionStatus::Failed
                    };
                    if !success && !stopped {
                        completed.error = Some(format!(
                            "Source port exited with {status}. See {}",
                            completed.log_path
                        ));
                        if plan.program == "snap"
                            && read_bounded_text(&log_path, 1024 * 1024).is_ok_and(|text| {
                                let text = text.to_ascii_lowercase();
                                text.contains("permission denied") || text.contains("cannot open")
                            })
                            && let Some(error) = completed.error.as_mut()
                        {
                            error.push_str(" This Snap may be strictly confined and unable to read hidden library directories. A native or Flatpak source port can use this managed library directly.");
                        }
                    }
                }
                Err(e) => {
                    completed.status = SessionStatus::Failed;
                    completed.error = Some(format!("Could not monitor source port: {e}"));
                }
            }
            // Useful saves may have been written even when an engine later
            // failed. Record those and the actual elapsed time for every result.
            let (statistics, warnings) = snapshot.collect(&plan);
            completed.statistics = statistics;
            if !warnings.is_empty()
                && let Ok(mut log) = OpenOptions::new().append(true).open(&log_path)
            {
                for warning in warnings {
                    let _ = writeln!(log, "[DoomedLauncher] {warning}");
                }
            }
            if let Err(e) = save_session(&monitor_root, &completed) {
                completed.error = Some(format!(
                    "{}Could not save session: {e}",
                    completed
                        .error
                        .as_ref()
                        .map(|v| format!("{v}. "))
                        .unwrap_or_default()
                ));
            }
            if let Ok(mut active) = locked_sessions()
                && let Some(entry) = active.get_mut(&thread_id)
            {
                entry.session = completed;
            }
        });
    if let Err(e) = thread_result {
        if let Ok(mut child) = child.lock() {
            let _ = child.kill();
            let _ = child.wait();
        }
        let mut failed = session;
        failed.status = SessionStatus::Failed;
        failed.finished_at = Some(Utc::now().to_rfc3339());
        failed.error = Some(format!("Could not start session monitor: {e}"));
        let _ = save_session(&data_root, &failed);
        if let Ok(mut active) = locked_sessions()
            && let Some(entry) = active.get_mut(&id)
        {
            entry.session = failed;
        }
        return Err(Error::new(
            "launch_failed",
            format!("Could not start session monitor: {e}"),
        ));
    }
    Ok(session)
}

pub fn sessions() -> Result<Vec<Session>> {
    let active = locked_sessions()?;
    let mut result = active
        .values()
        .map(|entry| {
            let mut session = entry.session.clone();
            if session.status == SessionStatus::Running {
                session.elapsed_seconds = entry.started.elapsed().as_secs();
            }
            session
        })
        .collect::<Vec<_>>();
    result.sort_by(|a, b| b.started_at.cmp(&a.started_at));
    Ok(result)
}

pub fn stop_session(id: &str) -> Result<Session> {
    let active = locked_sessions()?;
    let entry = active
        .get(id)
        .ok_or_else(|| Error::new("not_found", "No active session has that ID"))?;
    if entry.session.status == SessionStatus::Running {
        entry.stop.send(()).map_err(|_| {
            Error::new(
                "runtime",
                "This session is no longer accepting stop requests",
            )
        })?;
    }
    Ok(entry.session.clone())
}

pub fn session_history(data_root: &Path) -> Result<Vec<Session>> {
    let mut result: HashMap<String, Session> = {
        let active = locked_sessions()?;
        active
            .values()
            .filter(|entry| entry.data_root == data_root)
            .map(|entry| {
                let mut session = entry.session.clone();
                if session.status == SessionStatus::Running {
                    session.elapsed_seconds = entry.started.elapsed().as_secs();
                }
                (session.id.clone(), session)
            })
            .collect()
    };
    let directory = data_root.join("Sessions");
    if directory.exists() {
        for entry in fs::read_dir(&directory)? {
            let path = entry?.path();
            if path.extension().is_none_or(|e| e != "json") {
                continue;
            }
            let file_id = path
                .file_stem()
                .unwrap_or_default()
                .to_string_lossy()
                .into_owned();
            // In-memory completion is authoritative, including a surfaced
            // error if writing that completed journal failed.
            if result.contains_key(&file_id) {
                continue;
            }
            let loaded = read_bounded_text(&path, 16 * 1024 * 1024)
                .and_then(|text| serde_json::from_str::<Session>(&text).map_err(Error::from));
            match loaded {
                Ok(mut session) => {
                    if session.status == SessionStatus::Running {
                        session.status = SessionStatus::Interrupted;
                        session.error = Some(
                            "The launcher restarted before this engine's exit status was recorded"
                                .into(),
                        );
                    }
                    result.entry(session.id.clone()).or_insert(session);
                }
                Err(error) => {
                    // Preserve library access while exposing corrupt journals.
                    // No finish timestamp means no statistics or time harvest.
                    result.insert(
                        file_id.clone(),
                        Session {
                            id: file_id,
                            game_file_id: 0,
                            source_port_id: 0,
                            process_id: 0,
                            started_at: String::new(),
                            finished_at: None,
                            status: SessionStatus::Interrupted,
                            exit_code: None,
                            elapsed_seconds: 0,
                            statistics: vec![],
                            demo_path: None,
                            error: Some(format!(
                                "Could not read session journal {}: {error}",
                                path.display()
                            )),
                            log_path: path.with_extension("log").to_string_lossy().into_owned(),
                        },
                    );
                }
            }
        }
    }
    // A session may have started or completed while journals were read.
    // Overlay the current registry once more before publishing the snapshot.
    for entry in locked_sessions()?
        .values()
        .filter(|entry| entry.data_root == data_root)
    {
        let mut session = entry.session.clone();
        if session.status == SessionStatus::Running {
            session.elapsed_seconds = entry.started.elapsed().as_secs();
        }
        result.insert(session.id.clone(), session);
    }
    let mut result: Vec<_> = result.into_values().collect();
    result.sort_by(|a, b| b.started_at.cmp(&a.started_at));
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    #[test]
    fn typed_flatpak_boundary_preserves_every_argument() {
        let input = vec![
            "a b".into(),
            ";touch injected".into(),
            "$(id)".into(),
            "--host".into(),
            "".into(),
        ];
        let (program, args, directory) = process_arguments(
            "/host/bin/engine",
            &input,
            "/host/a b;$(touch injected)",
            true,
        );
        assert_eq!(program, "flatpak-spawn");
        assert_eq!(
            &args[..5],
            [
                "--host",
                "--watch-bus",
                "--directory=/host/a b;$(touch injected)",
                "--",
                "/host/bin/engine"
            ]
        );
        assert_eq!(&args[5..], input);
        assert_eq!(directory, None);
    }

    #[test]
    fn parameter_parser_preserves_qt_and_windows_paths() {
        assert_eq!(
            split_parameters(r#""a b" ";touch injected" "$(id)" "" "C:\Doom Files\doom.wad""#)
                .unwrap(),
            vec![
                "a b",
                ";touch injected",
                "$(id)",
                "",
                "C:\\Doom Files\\doom.wad"
            ]
        );
        assert_eq!(split_parameters(r#"a"""b"#).unwrap(), vec!["a\"b"]);
        assert!(split_parameters("\"unclosed").is_err());
        assert!(split_parameters("nul\0byte").is_err());
    }

    #[test]
    fn maps_warp_without_shell_or_lossy_case_changes() {
        assert_eq!(warp_arguments("e2m3"), vec!["-warp", "2", "3"]);
        assert_eq!(warp_arguments("MAP09"), vec!["-warp", "9"]);
        assert_eq!(warp_arguments("my_map"), vec!["+map", "my_map"]);
        assert_eq!(warp_arguments("$(id)"), vec!["+map", "$(id)"]);
    }

    #[test]
    fn detects_capitalization_appimages_exe_and_bundles() {
        let paths = [
            "/host/bin/GZDoom",
            "/host/bin/UZDoom-test.AppImage",
            "/games/woof.exe",
            "/Applications/VKDoom.app",
        ]
        .map(PathBuf::from);
        assert_eq!(find_in_files("gzdoom", &paths), Some(paths[0].clone()));
        assert_eq!(find_in_files("uzdoom", &paths), Some(paths[1].clone()));
        assert_eq!(find_in_files("woof", &paths), Some(paths[2].clone()));
        assert_eq!(find_in_files("vkdoom", &paths), Some(paths[3].clone()));
        assert!(find_in_files("missing", &paths).is_none());
    }

    #[cfg(unix)]
    fn engine(dir: &Path, name: &str, script: &str) -> Port {
        use std::os::unix::fs::PermissionsExt;
        let path = dir.join(name);
        fs::write(&path, format!("#!/bin/sh\n{script}\n")).unwrap();
        fs::set_permissions(&path, fs::Permissions::from_mode(0o700)).unwrap();
        Port {
            executable: path.to_string_lossy().into_owned(),
            directory: dir.to_string_lossy().into_owned(),
            ..Port::default()
        }
    }

    #[cfg(unix)]
    #[test]
    fn launch_plan_preserves_addon_order_iwad_deh_and_map() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "gzdoom", "exit 0");
        let names = [
            "doom.wad",
            "mod one.pk3",
            "mod-two.wad",
            "fix.bex",
            "selected.wad",
        ];
        let paths = names.map(|name| {
            let path = tmp.path().join(name);
            fs::write(&path, b"fixture").unwrap();
            path
        });
        let options = LaunchOptions {
            map: "MAP02".into(),
            skill: Some(4),
            extra_params: r#"+name "a b""#.into(),
            ..LaunchOptions::default()
        };
        let plan = build_plan(
            &port,
            &paths[4],
            Some(&paths[0]),
            &paths[1..4],
            &options,
            tmp.path(),
        )
        .unwrap();
        let expected = vec![
            "-iwad".into(),
            paths[0].to_string_lossy().into_owned(),
            "-file".into(),
            paths[1].to_string_lossy().into_owned(),
            paths[2].to_string_lossy().into_owned(),
            paths[4].to_string_lossy().into_owned(),
            "-deh".into(),
            paths[3].to_string_lossy().into_owned(),
            "-warp".into(),
            "2".into(),
            "-skill".into(),
            "4".into(),
            "-savedir".into(),
            managed_save_directory(&port, &options, tmp.path())
                .to_string_lossy()
                .into_owned(),
            "+name".into(),
            "a b".into(),
        ];
        assert_eq!(plan.arguments, expected);
        assert_eq!(plan.statistics_kind, StatisticsKind::ZdoomSave);
        let iwad_plan = build_plan(
            &port,
            &paths[0],
            Some(&paths[0]),
            &[],
            &LaunchOptions::default(),
            tmp.path(),
        )
        .unwrap();
        assert_eq!(
            iwad_plan.arguments[..2],
            ["-iwad", paths[0].to_str().unwrap()]
        );
        assert!(!iwad_plan.arguments.contains(&"-file".into()));
    }

    #[cfg(unix)]
    #[test]
    fn extra_only_needs_no_game_and_keeps_port_parameters() {
        let tmp = TempDir::new().unwrap();
        let mut port = engine(tmp.path(), "gzdoom", "exit 0");
        port.extra_parameters = "+set foo bar".into();
        let options = LaunchOptions {
            extra_params_only: true,
            extra_params: "\"a b\"".into(),
            ..LaunchOptions::default()
        };
        let plan = build_plan(
            &port,
            Path::new("missing.wad"),
            None,
            &[],
            &options,
            tmp.path(),
        )
        .unwrap();
        assert_eq!(plan.arguments, ["+set", "foo", "bar", "a b"]);
        assert_eq!(plan.statistics_kind, StatisticsKind::None);
    }

    #[test]
    fn dangerous_package_ids_and_missing_native_are_rejected() {
        let tmp = TempDir::new().unwrap();
        for executable in ["flatpak:--command=sh", "snap:--shell", "/missing/engine"] {
            let port = Port {
                executable: executable.into(),
                ..Port::default()
            };
            assert!(
                build_plan(
                    &port,
                    Path::new("missing"),
                    None,
                    &[],
                    &LaunchOptions {
                        extra_params_only: true,
                        ..LaunchOptions::default()
                    },
                    tmp.path()
                )
                .is_err()
            );
        }
    }

    #[test]
    fn levelstat_parses_clamps_and_handles_hours() {
        let stats = parse_levelstat(
            "header\nE1M1 - 0:56.63 (0:56)  K: 9/6  I: 4/37  S: 0/3\nMAP02 - 1:02:03.50 (1:02) K: 1/2 I: 2/3 S: 3/4\ninvalid",
        );
        assert_eq!(stats.len(), 2);
        assert_eq!(stats[0].kill_count, 6);
        assert_eq!(stats[0].level_time, 56.63);
        assert_eq!(stats[1].level_time, 3723.5);
    }

    #[test]
    fn statdump_optional_totals_and_multiple_levels() {
        let stats = parse_statdump(
            "==== E1M1 ====\nTime: 01:12 (par: 00:30)\nPlayer (green):\nKills: 4/3 (-33%) Items: 2/7 (28%) Secrets: 0/1\n==== MAP02 ==== Time: 2:03 (par: 1:00) Player (green): Kills: 6 Items: 4 Secrets: 2",
        );
        assert_eq!(stats.len(), 2);
        assert_eq!(stats[0].kill_count, 3);
        assert_eq!(stats[0].level_time, 72.0);
        assert_eq!(stats[1].total_items, 4);
    }

    #[test]
    fn zdoom_save_reads_tics_skill_and_resurrection_clamp() {
        let tmp = TempDir::new().unwrap();
        let path = tmp.path().join("save.zds");
        let mut zip = zip::ZipWriter::new(File::create(&path).unwrap());
        zip.start_file("globals.json", zip::write::SimpleFileOptions::default())
            .unwrap();
        zip.write_all(br#"{"importantcvars":"\\skill\\3","statistics":{"levels":[{"levelname":"MAP01","killcount":9,"totalkills":5,"itemcount":2,"totalitems":3,"secretcount":1,"totalsecrets":2,"leveltime":350}]}}"#).unwrap();
        zip.finish().unwrap();
        let stats = parse_zdoom_save(&path).unwrap();
        assert_eq!(stats.len(), 1);
        assert_eq!(stats[0].skill, 4);
        assert_eq!(stats[0].level_time, 10.0);
        assert_eq!(stats[0].kill_count, 5);
    }

    #[cfg(unix)]
    #[test]
    fn captures_failures_and_enforces_timeout() {
        assert!(
            capture(
                "/bin/sh",
                &["-c".into(), "exit 7".into()],
                false,
                Duration::from_secs(1)
            )
            .is_err()
        );
        let started = Instant::now();
        let result = capture(
            "/bin/sleep",
            &["5".into()],
            false,
            Duration::from_millis(30),
        );
        assert_eq!(result.unwrap_err().code, "timeout");
        assert!(started.elapsed() < Duration::from_secs(2));
    }

    #[cfg(unix)]
    fn wait_finished(id: &str) -> Session {
        let deadline = Instant::now() + Duration::from_secs(5);
        loop {
            let session = sessions()
                .unwrap()
                .into_iter()
                .find(|s| s.id == id)
                .unwrap();
            if session.status != SessionStatus::Running {
                return session;
            }
            assert!(Instant::now() < deadline, "session did not finish");
            thread::sleep(Duration::from_millis(20));
        }
    }

    #[cfg(unix)]
    #[test]
    fn session_runs_in_background_records_stats_log_and_journal() {
        let tmp = TempDir::new().unwrap();
        let port = engine(
            tmp.path(),
            "dsda-doom",
            "echo engine-output\nprintf 'MAP01 - 0:20.00 (0:20) K: 3/4 I: 2/3 S: 1/1\\n' > levelstat.txt\nsleep 0.2",
        );
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let plan = build_plan(
            &port,
            &game,
            None,
            &[],
            &LaunchOptions {
                skill: Some(3),
                ..LaunchOptions::default()
            },
            tmp.path(),
        )
        .unwrap();
        let session = start_session(plan.clone(), 21, 7, tmp.path().into()).unwrap();
        assert_eq!(session.status, SessionStatus::Running);
        let finished = wait_finished(&session.id);
        assert_eq!(finished.status, SessionStatus::Completed);
        assert_eq!(finished.exit_code, Some(0));
        assert_eq!(finished.statistics.len(), 1);
        assert_eq!(finished.statistics[0].skill, 3);
        assert!(
            fs::read_to_string(&finished.log_path)
                .unwrap()
                .contains("engine-output")
        );
        let journal: Session = serde_json::from_slice(
            &fs::read(
                tmp.path()
                    .join("Sessions")
                    .join(format!("{}.json", session.id)),
            )
            .unwrap(),
        )
        .unwrap();
        assert_eq!(journal.status, SessionStatus::Completed);
        assert_eq!(session_history(tmp.path()).unwrap().len(), 1);
        let history = Path::new(&plan.working_directory).join("levelstat-history");
        let repeated = start_session(plan, 21, 7, tmp.path().into()).unwrap();
        let repeated = wait_finished(&repeated.id);
        assert_eq!(repeated.status, SessionStatus::Completed);
        assert_eq!(repeated.statistics, finished.statistics);
        assert_eq!(fs::read_dir(history).unwrap().count(), 1);
    }

    #[cfg(unix)]
    #[test]
    fn session_reports_failed_exit_and_stop() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "failure", "echo failure >&2\nexit 7");
        let options = LaunchOptions {
            extra_params_only: true,
            ..LaunchOptions::default()
        };
        let plan =
            build_plan(&port, Path::new("missing"), None, &[], &options, tmp.path()).unwrap();
        let session = start_session(plan, 1, 1, tmp.path().into()).unwrap();
        let finished = wait_finished(&session.id);
        assert_eq!(finished.status, SessionStatus::Failed);
        assert_eq!(finished.exit_code, Some(7));
        assert!(finished.error.unwrap().contains('7'));
        let port = engine(tmp.path(), "running", "exec sleep 20");
        let plan =
            build_plan(&port, Path::new("missing"), None, &[], &options, tmp.path()).unwrap();
        let session = start_session(plan, 1, 1, tmp.path().into()).unwrap();
        stop_session(&session.id).unwrap();
        assert_eq!(wait_finished(&session.id).status, SessionStatus::Stopped);
    }

    #[cfg(unix)]
    #[test]
    fn unchanged_statistics_are_not_reimported_or_deleted() {
        let tmp = TempDir::new().unwrap();
        let path = tmp.path().join("levelstat.txt");
        let original = "MAP01 - 0:20.00 (0:20) K: 3/4 I: 2/3 S: 1/1\n";
        fs::write(&path, original).unwrap();
        let port = engine(tmp.path(), "woof", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let plan = build_plan(
            &port,
            &game,
            None,
            &[],
            &LaunchOptions::default(),
            tmp.path(),
        )
        .unwrap();
        let session = start_session(plan, 1, 1, tmp.path().into()).unwrap();
        assert!(wait_finished(&session.id).statistics.is_empty());
        assert_eq!(fs::read_to_string(path).unwrap(), original);
    }

    #[test]
    fn latest_save_uses_actual_timestamp_and_supported_extensions() {
        let tmp = TempDir::new().unwrap();
        let nested = tmp.path().join("nested");
        fs::create_dir(&nested).unwrap();
        for (name, seconds) in [("first.zds", 10), ("newest.DSG", 20), ("unrelated.txt", 30)] {
            let file = File::create(nested.join(name)).unwrap();
            file.set_times(
                std::fs::FileTimes::new()
                    .set_modified(SystemTime::UNIX_EPOCH + Duration::from_secs(seconds)),
            )
            .unwrap();
        }
        assert_eq!(
            latest_save(&[tmp.path().into()]),
            Some(nested.join("newest.DSG"))
        );
    }

    #[cfg(unix)]
    #[test]
    fn custom_statistics_distinguish_append_and_identical_rewrite() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "dsda-doom", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let mut plan = build_plan(
            &port,
            &game,
            None,
            &[],
            &LaunchOptions::default(),
            tmp.path(),
        )
        .unwrap();
        let path = tmp.path().join("levelstat.txt");
        plan.statistics_file = Some(path.clone());
        let first = "MAP01 - 0:20.00 (0:20) K: 3/4 I: 2/3 S: 1/1\n";
        let second = "MAP02 - 0:30.00 (0:30) K: 4/4 I: 3/3 S: 1/1\n";
        let write = |text: &str, seconds| {
            fs::write(&path, text).unwrap();
            File::options()
                .write(true)
                .open(&path)
                .unwrap()
                .set_times(
                    fs::FileTimes::new()
                        .set_modified(SystemTime::UNIX_EPOCH + Duration::from_secs(seconds)),
                )
                .unwrap();
        };
        write(first, 10);
        let snapshot = StatisticsSnapshot::capture(&plan);
        assert!(snapshot.collect(&plan).0.is_empty());
        write(&format!("{first}{second}"), 20);
        let appended = snapshot.collect(&plan).0;
        assert_eq!(appended.len(), 1);
        assert_eq!(appended[0].map_name, "MAP02");
        write(first, 30);
        assert_eq!(snapshot.collect(&plan).0[0].map_name, "MAP01");
        plan.program = "crispy-doom".into();
        write(&format!("{first}{second}"), 40);
        assert_eq!(snapshot.collect(&plan).0.len(), 2);
    }

    #[cfg(unix)]
    #[test]
    fn save_loading_isolated_by_game_and_engine_uses_vanilla_slot() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "chocolate-doom", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let options = LaunchOptions {
            game_file_id: 12,
            source_port_id: 4,
            load_latest_save: true,
            ..LaunchOptions::default()
        };
        let other_game = tmp.path().join("GameFiles/SaveGames/99/4");
        let other_port = tmp.path().join("GameFiles/SaveGames/12/99");
        for directory in [&other_game, &other_port] {
            fs::create_dir_all(directory).unwrap();
            fs::write(directory.join("doomsav0.dsg"), b"unrelated").unwrap();
        }
        let missing = build_plan(&port, &game, None, &[], &options, tmp.path()).unwrap_err();
        assert_eq!(missing.code, "save_not_found");
        let directory = managed_save_directory(&port, &options, tmp.path());
        fs::create_dir_all(&directory).unwrap();
        fs::write(directory.join("doomsav3.dsg"), b"this game and engine").unwrap();
        let plan = build_plan(&port, &game, None, &[], &options, tmp.path()).unwrap();
        assert_eq!(plan.save_directories, [directory]);
        let load = plan
            .arguments
            .iter()
            .position(|v| v == "-loadgame")
            .unwrap();
        assert_eq!(plan.arguments[load + 1], "3");
        let zdoom = engine(tmp.path(), "gzdoom", "exit 0");
        let options = LaunchOptions {
            source_port_id: 8,
            ..options
        };
        let directory = managed_save_directory(&zdoom, &options, tmp.path());
        fs::create_dir_all(&directory).unwrap();
        let save = directory.join("save1.zds");
        fs::write(&save, b"zdoom save").unwrap();
        let plan = build_plan(&zdoom, &game, None, &[], &options, tmp.path()).unwrap();
        let load = plan
            .arguments
            .iter()
            .position(|v| v == "-loadgame")
            .unwrap();
        assert_eq!(plan.arguments[load + 1], "save1.zds");
        let legacy = engine(tmp.path(), "lzdoom", "exit 0");
        let plan = build_plan(&legacy, &game, None, &[], &options, tmp.path()).unwrap();
        let load = plan
            .arguments
            .iter()
            .position(|v| v == "-loadgame")
            .unwrap();
        assert_eq!(plan.arguments[load + 1], save.to_string_lossy());
    }

    #[cfg(unix)]
    #[test]
    fn installed_levelstat_ports_use_private_writable_directory_without_preview_mutation() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "crispy-doom", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let plan = build_plan(
            &port,
            &game,
            None,
            &[],
            &LaunchOptions::default(),
            tmp.path(),
        )
        .unwrap();
        assert!(
            Path::new(&plan.working_directory).starts_with(tmp.path().join("GameFiles/EngineData"))
        );
        assert!(!Path::new(&plan.working_directory).exists());
        assert_eq!(
            plan.statistics_file,
            Some(Path::new(&plan.working_directory).join("levelstat.txt"))
        );
        let session = start_session(plan.clone(), 1, 1, tmp.path().into()).unwrap();
        assert_eq!(wait_finished(&session.id).status, SessionStatus::Completed);
        assert!(Path::new(&plan.working_directory).is_dir());
    }

    #[test]
    fn upstream_slot_contracts_exclude_non_loadable_quick_and_demo_saves() {
        for (engine, file, slot) in [
            ("dsda-doom", "dsda-doom-savegame118.dsg", Some(118)),
            ("dsda-doom", "dsda-doom-savegame119.dsg", None),
            ("prboom-plus", "prboom-plus-savegame119.dsg", Some(119)),
            ("dsda-doom", "custom2bexname3.dsg", Some(3)),
            ("prboom-plus", "demosav0.dsg", None),
            ("woof", "woofsav77.dsg", Some(77)),
            ("woof", "woofsav78.dsg", None),
            ("woof", "woofq1.dsg", None),
            ("nugget-doom", "nuggsav20.dsg", Some(20)),
            ("nugget-doom", "autosave.dsg", Some(255)),
            ("nugget-doom", "autosav1.dsg", None),
            ("eternity", "etersav5.dsg", Some(5)),
        ] {
            assert_eq!(save_slot(engine, Path::new(file)), slot, "{engine}: {file}");
        }
    }

    #[cfg(unix)]
    #[test]
    fn vanilla_recording_success_requires_expected_file_and_exact_log_message() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "chocolate-doom", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let options = LaunchOptions {
            record_demo: true,
            ..LaunchOptions::default()
        };
        let plan = build_plan(&port, &game, None, &[], &options, tmp.path()).unwrap();
        let demo = plan.demo_path.as_ref().unwrap();
        let log = tmp.path().join("engine.log");
        fs::write(&log, format!("Demo {demo} recorded\n")).unwrap();
        assert!(!successful_vanilla_recording(&plan, &log));
        fs::create_dir_all(Path::new(demo).parent().unwrap()).unwrap();
        fs::write(demo, [0u8; 14]).unwrap();
        assert!(successful_vanilla_recording(&plan, &log));
        fs::write(&log, format!("Demo {demo} recorded\0\n")).unwrap();
        assert!(successful_vanilla_recording(&plan, &log));
        fs::write(&log, format!("Failed to record Demo {demo}\n")).unwrap();
        assert!(!successful_vanilla_recording(&plan, &log));
        fs::write(&log, "Demo unrelated.lmp recorded\n").unwrap();
        assert!(!successful_vanilla_recording(&plan, &log));
    }

    #[cfg(unix)]
    #[test]
    fn actual_host_bridge_does_not_interpret_arguments_or_cwd() {
        let tmp = TempDir::new().unwrap();
        engine(tmp.path(), "flatpak-spawn", "printf '%s\\n' \"$@\"");
        let args = vec![
            "a b".into(),
            ";touch injected".into(),
            "$(id)".into(),
            "".into(),
        ];
        let output = configured_command("/host/engine", &args, "/host/a b;$(id)", true)
            .env("PATH", tmp.path())
            .output()
            .unwrap();
        assert!(output.status.success());
        assert_eq!(
            String::from_utf8(output.stdout).unwrap(),
            "--host\n--watch-bus\n--directory=/host/a b;$(id)\n--\n/host/engine\na b\n;touch injected\n$(id)\n\n"
        );
        assert!(!tmp.path().join("injected").exists());
    }

    #[cfg(unix)]
    #[test]
    fn demo_plans_validate_playback_and_create_unique_recordings() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "gzdoom", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let options = LaunchOptions {
            record_demo: true,
            game_file_id: 42,
            ..LaunchOptions::default()
        };
        let first = build_plan(&port, &game, None, &[], &options, tmp.path()).unwrap();
        let second = build_plan(&port, &game, None, &[], &options, tmp.path()).unwrap();
        assert_ne!(first.demo_path, second.demo_path);
        assert!(first.demo_path.as_ref().unwrap().contains("42-"));
        assert!(first.arguments.contains(&"-record".into()));
        let recorded = first.arguments.iter().position(|v| v == "-record").unwrap() + 1;
        assert_eq!(
            format!("{}.lmp", first.arguments[recorded]),
            first.demo_path.as_deref().unwrap()
        );
        // Command preview does not create files or remove old statistics.
        assert!(!tmp.path().join("GameFiles").exists());
        let options = LaunchOptions {
            play_demo_file: Some("missing.lmp".into()),
            ..options
        };
        assert!(build_plan(&port, &game, None, &[], &options, tmp.path()).is_err());
        fs::write(tmp.path().join("saved.lmp"), b"demo").unwrap();
        let options = LaunchOptions {
            play_demo_file: Some("saved.lmp".into()),
            ..options
        };
        let play = build_plan(&port, &game, None, &[], &options, tmp.path()).unwrap();
        assert!(play.arguments.contains(&"-playdemo".into()));
        assert!(!play.arguments.contains(&"-record".into()));
        assert!(play.demo_path.is_none());
    }

    #[cfg(unix)]
    #[test]
    fn concurrent_levelstat_writers_are_rejected_before_spawn() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "dsda-doom", "exec sleep 20");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        let plan = build_plan(
            &port,
            &game,
            None,
            &[],
            &LaunchOptions::default(),
            tmp.path(),
        )
        .unwrap();
        let first = start_session(plan.clone(), 1, 1, tmp.path().into()).unwrap();
        let error = start_session(plan, 2, 1, tmp.path().into()).unwrap_err();
        assert_eq!(error.code, "session_conflict");
        stop_session(&first.id).unwrap();
        assert_eq!(wait_finished(&first.id).status, SessionStatus::Stopped);
    }

    #[test]
    fn stale_session_journals_do_not_claim_a_successful_exit() {
        let tmp = TempDir::new().unwrap();
        let journal = Session {
            id: Uuid::new_v4().to_string(),
            game_file_id: 5,
            source_port_id: 2,
            process_id: 12345,
            started_at: "2025-01-01T00:00:00Z".into(),
            finished_at: None,
            status: SessionStatus::Running,
            exit_code: None,
            elapsed_seconds: 0,
            error: None,
            statistics: vec![],
            log_path: "engine.log".into(),
            demo_path: None,
        };
        save_session(tmp.path(), &journal).unwrap();
        let result = session_history(tmp.path()).unwrap();
        assert_eq!(result.len(), 1);
        assert_eq!(result[0].status, SessionStatus::Interrupted);
        assert!(result[0].finished_at.is_none());
        assert!(result[0].exit_code.is_none());
    }

    #[cfg(target_os = "linux")]
    #[test]
    fn flatpak_engine_receives_only_managed_and_required_external_grants() {
        let tmp = TempDir::new().unwrap();
        let root = tmp.path().join("launcher");
        let managed = root.join("GameFiles");
        let external = tmp.path().join("legacy games;$(id)");
        fs::create_dir_all(&managed).unwrap();
        fs::create_dir_all(&external).unwrap();
        let game = managed.join("mod.wad");
        let iwad = external.join("doom.wad");
        fs::write(&game, b"game").unwrap();
        fs::write(&iwad, b"iwad").unwrap();
        let port = Port {
            executable: "flatpak:org.zdoom.GZDoom".into(),
            ..Port::default()
        };
        let plan = build_plan(
            &port,
            &game,
            Some(&iwad),
            &[],
            &LaunchOptions::default(),
            &root,
        )
        .unwrap();
        assert!(
            plan.arguments
                .contains(&format!("--filesystem={}:rw", managed.display()))
        );
        assert!(
            plan.arguments
                .contains(&format!("--filesystem={}:ro", external.display()))
        );
        assert!(
            !plan
                .arguments
                .iter()
                .any(|a| a == "--filesystem=home" || a == "--filesystem=host")
        );
        assert!(
            !plan
                .arguments
                .iter()
                .any(|a| a == &format!("--filesystem={}:rw", root.display()))
        );
        let id_index = plan
            .arguments
            .iter()
            .position(|a| a == "org.zdoom.GZDoom")
            .unwrap();
        assert_eq!(plan.arguments[id_index + 1], "--");
        assert!(
            plan.arguments[1..id_index]
                .iter()
                .all(|a| a.starts_with("--filesystem="))
        );
    }

    #[cfg(unix)]
    #[test]
    fn flatpak_symlink_inputs_grant_the_external_target_read_only() {
        use std::os::unix::fs::symlink;
        let tmp = TempDir::new().unwrap();
        let root = tmp.path().join("launcher");
        fs::create_dir_all(root.join("GameFiles")).unwrap();
        let external = tmp.path().join("external");
        fs::create_dir(&external).unwrap();
        fs::write(external.join("doom.wad"), b"test").unwrap();
        let linked = root.join("GameFiles/doom.wad");
        symlink(external.join("doom.wad"), &linked).unwrap();
        let args = flatpak_filesystem_arguments(&root, &[linked], None).unwrap();
        let canonical_external = fs::canonicalize(&external).unwrap();
        assert!(args.contains(&format!("--filesystem={}:ro", canonical_external.display())));
        assert!(!args.contains(&format!("--filesystem={}:rw", canonical_external.display())));
    }

    #[cfg(unix)]
    #[test]
    fn doom64_standalone_plan_adds_no_unverified_classic_doom_flags() {
        let tmp = TempDir::new().unwrap();
        let mut port = engine(tmp.path(), "doom64_native", "exit 0");
        port.launch_type = 2;
        port.extra_parameters = "-skipmovies".into();
        let options = LaunchOptions {
            save_statistics: false,
            extra_params: "+set custom 1".into(),
            ..LaunchOptions::default()
        };
        let plan = build_plan(
            &port,
            Path::new("not-used.wad"),
            None,
            &[],
            &options,
            tmp.path(),
        )
        .unwrap();
        assert_eq!(plan.arguments, ["-skipmovies", "+set", "custom", "1"]);
        assert_eq!(plan.statistics_kind, StatisticsKind::None);
        for options in [
            LaunchOptions {
                map: "MAP01".into(),
                ..options.clone()
            },
            LaunchOptions {
                skill: Some(4),
                ..options.clone()
            },
            LaunchOptions {
                save_statistics: true,
                ..options.clone()
            },
        ] {
            assert!(
                build_plan(
                    &port,
                    Path::new("not-used.wad"),
                    None,
                    &[],
                    &options,
                    tmp.path()
                )
                .is_err()
            );
        }
    }

    #[cfg(unix)]
    #[test]
    fn windows_exe_is_rejected_before_native_execution_on_unix() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "doom64_x64.exe", "exit 0");
        let options = LaunchOptions {
            extra_params_only: true,
            ..LaunchOptions::default()
        };
        let error =
            build_plan(&port, Path::new("unused"), None, &[], &options, tmp.path()).unwrap_err();
        assert_eq!(error.code, "platform_incompatible");
    }

    #[cfg(unix)]
    #[test]
    fn file_option_must_be_one_typed_flag() {
        let tmp = TempDir::new().unwrap();
        let mut port = engine(tmp.path(), "engine", "exit 0");
        let game = tmp.path().join("game.wad");
        fs::write(&game, b"test").unwrap();
        for option in ["-file -exec", "file", "-file\0x"] {
            port.file_option = option.into();
            assert!(
                build_plan(
                    &port,
                    &game,
                    None,
                    &[],
                    &LaunchOptions::default(),
                    tmp.path()
                )
                .is_err()
            );
        }
        port.file_option = "-file;$(id)".into();
        // Punctuation remains one literal argv item, never a shell program.
        let plan = build_plan(
            &port,
            &game,
            None,
            &[],
            &LaunchOptions::default(),
            tmp.path(),
        )
        .unwrap();
        assert_eq!(plan.arguments[0], "-file;$(id)");
    }

    #[test]
    fn damaged_journal_is_reported_without_blocking_library_history() {
        let tmp = TempDir::new().unwrap();
        fs::create_dir(tmp.path().join("Sessions")).unwrap();
        fs::write(tmp.path().join("Sessions/damaged.json"), b"{broken").unwrap();
        let history = session_history(tmp.path()).unwrap();
        assert_eq!(history.len(), 1);
        assert_eq!(history[0].status, SessionStatus::Interrupted);
        assert!(history[0].finished_at.is_none());
        assert!(
            history[0]
                .error
                .as_ref()
                .unwrap()
                .contains("Could not read session journal")
        );
    }

    #[cfg(unix)]
    #[test]
    fn completion_write_failure_preserves_live_result_and_surfaces_error() {
        let tmp = TempDir::new().unwrap();
        let port = engine(tmp.path(), "engine", "exec sleep 0.2");
        let options = LaunchOptions {
            extra_params_only: true,
            ..LaunchOptions::default()
        };
        let plan = build_plan(&port, Path::new("unused"), None, &[], &options, tmp.path()).unwrap();
        let session = start_session(plan, 10, 20, tmp.path().into()).unwrap();
        let journal = tmp
            .path()
            .join("Sessions")
            .join(format!("{}.json", session.id));
        fs::remove_file(&journal).unwrap();
        fs::create_dir(&journal).unwrap();
        let finished = wait_finished(&session.id);
        assert_eq!(finished.status, SessionStatus::Completed);
        assert_eq!(finished.exit_code, Some(0));
        assert!(
            finished
                .error
                .as_ref()
                .unwrap()
                .contains("Could not save session")
        );
        let history = session_history(tmp.path()).unwrap();
        assert_eq!(history[0].status, SessionStatus::Completed);
        assert!(
            history[0]
                .error
                .as_ref()
                .unwrap()
                .contains("Could not save session")
        );
    }
}
