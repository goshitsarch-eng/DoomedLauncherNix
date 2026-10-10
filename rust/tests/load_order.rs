//! Exercise SettingsFiles through public commands and the actual SQLite/filesystem
//! boundary. A tiny native Rust engine fixture keeps these checks shell-free on
//! Windows, macOS, and Linux.
use doomedlauncher_core::api::{Request, dispatch};
use doomedlauncher_core::error::Result;
use rusqlite::{Connection, params};
use serde_json::{Value, json};
use std::fs;
use std::path::PathBuf;
use std::process::Command;
use std::time::{Duration, Instant};
use tempfile::TempDir;

struct Library {
    directory: TempDir,
    root: PathBuf,
}

impl Library {
    fn new() -> Self {
        let directory = TempDir::new().unwrap();
        let root = directory.path().join("library 日本語");
        let library = Self { directory, root };
        library.call(json!({"op":"snapshot"}));
        library
    }

    fn db(&self) -> Connection {
        Connection::open(self.root.join("DoomLauncher.sqlite")).unwrap()
    }

    fn try_call(&self, mut command: Value) -> Result<Value> {
        command["data_root"] = json!(self.root);
        dispatch(serde_json::from_value::<Request>(command).unwrap())
    }

    fn call(&self, command: Value) -> Value {
        self.try_call(command).unwrap()
    }

    fn import(&self, name: &str, as_iwad: bool) -> i64 {
        let source = self.directory.path().join(name);
        let mut wad = b"PWAD".to_vec();
        wad.extend_from_slice(&0_u32.to_le_bytes());
        wad.extend_from_slice(&12_u32.to_le_bytes());
        fs::write(&source, wad).unwrap();
        let result = self.call(json!({"op":"import_files","paths":[source],"as_iwads":as_iwad}));
        assert_eq!(result["failed"], json!([]));
        assert_eq!(result["added"], json!([name]));
        self.db()
            .query_row(
                "SELECT GameFileID FROM GameFiles WHERE FileName=?1",
                [name],
                |row| row.get(0),
            )
            .unwrap()
    }

    fn engine(&self) -> (i64, PathBuf) {
        let source = self.directory.path().join("engine.rs");
        fs::write(
            &source,
            r#"fn main() {
            let args = std::env::args().skip(1).collect::<Vec<_>>();
            std::fs::write("arguments.txt", args.join("\n")).unwrap();
        }"#,
        )
        .unwrap();
        let engine = self.directory.path().join(if cfg!(windows) {
            "engine.exe"
        } else {
            "engine"
        });
        let output = Command::new(std::env::var_os("RUSTC").unwrap_or_else(|| "rustc".into()))
            .arg("--edition=2024")
            .arg(&source)
            .arg("-o")
            .arg(&engine)
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        self.call(json!({"op":"save_port","port":{"name":"Fixture engine","executable":engine,"directory":self.directory.path(),"supported_extensions":"wad","launch_type":0}}));
        let id = self
            .db()
            .query_row(
                "SELECT SourcePortID FROM SourcePorts WHERE Name='Fixture engine'",
                [],
                |row| row.get(0),
            )
            .unwrap();
        (id, self.directory.path().join("arguments.txt"))
    }

    fn launch_and_wait(&self, game: i64, port: i64, iwad: i64, additional: &[&str]) {
        let started = self.call(json!({"op":"launch","options":{"game_file_id":game,"source_port_id":port,"iwad_id":iwad,"additional_files":additional,"remember":true,"save_statistics":false}}));
        let id = &started["session"]["id"];
        let deadline = Instant::now() + Duration::from_secs(8);
        loop {
            let result = self.call(json!({"op":"sessions"}));
            let session = result["sessions"]
                .as_array()
                .unwrap()
                .iter()
                .find(|session| &session["id"] == id)
                .unwrap();
            if session["status"] != "running" {
                assert_eq!(session["status"], "completed", "{session}");
                break;
            }
            assert!(Instant::now() < deadline, "Fixture engine did not finish");
            std::thread::sleep(Duration::from_millis(10));
        }
    }

    fn saved(&self, table: &str, key: &str, id: i64) -> String {
        self.db()
            .query_row(
                &format!("SELECT SettingsFiles FROM {table} WHERE {key}=?1"),
                [id],
                |row| row.get(0),
            )
            .unwrap()
    }
}

fn encoded_names(value: &str) -> Value {
    serde_json::from_str(
        value
            .strip_prefix("@DoomLauncher/SettingsFiles/v1:")
            .expect("ambiguous list must use the versioned encoding"),
    )
    .unwrap()
}

#[test]
fn native_remember_rename_and_rollback_preserve_unicode_semicolons_and_load_order() {
    let library = Library::new();
    let game = library.import("main.wad", false);
    let base = library.import("base.wad", true);
    let first = library.import("first.wad", false);
    let middle = library.import("épisode;日本語.wad", false);
    library.import("last.wad", false);
    let legacy_owner = library.import("legacy-owner.wad", false);
    let iwad = library
        .db()
        .query_row(
            "SELECT IWadID FROM IWads WHERE GameFileID=?1",
            [base],
            |row| row.get(0),
        )
        .unwrap();
    let (port, arguments) = library.engine();

    library.launch_and_wait(game, port, iwad, &["first.wad", "last.wad"]);
    assert_eq!(
        library.saved("GameFiles", "GameFileID", game),
        "first.wad;last.wad"
    );
    let original = ["first.wad", "épisode;日本語.wad", "last.wad"];
    library.launch_and_wait(game, port, iwad, &original);
    let saved = library.saved("GameFiles", "GameFileID", game);
    assert_eq!(encoded_names(&saved), json!(original));
    let defaults = library.call(json!({"op":"play_defaults","id":game}));
    assert_eq!(defaults["options"]["additional_files"], json!(original));
    assert_eq!(defaults["additional_files"], json!(original));
    let argv = fs::read_to_string(arguments).unwrap();
    let positions = original.map(|name| argv.find(name).unwrap());
    assert!(positions[0] < positions[1] && positions[1] < positions[2]);

    let db = library.db();
    db.execute(
        "UPDATE SourcePorts SET SettingsFiles=?2 WHERE SourcePortID=?1",
        params![port, saved],
    )
    .unwrap();
    db.execute("INSERT INTO GameProfiles(GameFileID,SourcePortID,IWadID,Name,SettingsFiles,SettingsSpecificFiles) VALUES(?1,?2,?3,'Encoded profile',?4,'opaque;archive;members')", params![game,port,iwad,saved]).unwrap();
    let profile = db.last_insert_rowid();
    db.execute(
        "UPDATE GameFiles SET SettingsSpecificFiles='opaque;archive;members' WHERE GameFileID=?1",
        [game],
    )
    .unwrap();
    db.execute(
        "UPDATE GameFiles SET SettingsFiles='first.wad;last.wad' WHERE GameFileID=?1",
        [legacy_owner],
    )
    .unwrap();
    db.execute("INSERT INTO GameProfiles(GameFileID,SourcePortID,IWadID,Name,SettingsFiles) VALUES(?1,?2,?3,'Legacy profile','first.wad;last.wad')", params![legacy_owner,port,iwad]).unwrap();
    let legacy_profile = db.last_insert_rowid();
    db.execute("INSERT INTO SourcePorts(Name,Executable,SupportedExtensions,Directory,LaunchType,SettingsFiles) VALUES('Legacy port','engine','wad','','0','first.wad;last.wad')", []).unwrap();
    let legacy_port = db.last_insert_rowid();
    drop(db);

    library.call(json!({"op":"rename_entry","id":middle,"name":"改名;épisode.wad"}));
    let renamed = json!(["first.wad", "改名;épisode.wad", "last.wad"]);
    for (table, key, id) in [
        ("GameFiles", "GameFileID", game),
        ("GameProfiles", "GameProfileID", profile),
        ("SourcePorts", "SourcePortID", port),
    ] {
        assert_eq!(encoded_names(&library.saved(table, key, id)), renamed);
    }

    library.call(json!({"op":"rename_entry","id":first,"name":"first;renamed.wad"}));
    let expected = json!(["first;renamed.wad", "改名;épisode.wad", "last.wad"]);
    let owners = [
        ("GameFiles", "GameFileID", game),
        ("GameProfiles", "GameProfileID", profile),
        ("SourcePorts", "SourcePortID", port),
    ];
    for (table, key, id) in owners {
        assert_eq!(encoded_names(&library.saved(table, key, id)), expected);
    }
    for (table, key, id) in [
        ("GameFiles", "GameFileID", legacy_owner),
        ("GameProfiles", "GameProfileID", legacy_profile),
        ("SourcePorts", "SourcePortID", legacy_port),
    ] {
        assert_eq!(
            encoded_names(&library.saved(table, key, id)),
            json!(["first;renamed.wad", "last.wad"])
        );
    }
    assert_eq!(
        library.call(json!({"op":"play_defaults","id":game}))["options"]["additional_files"],
        expected
    );
    for (table, key, id) in [
        ("GameFiles", "GameFileID", game),
        ("GameProfiles", "GameProfileID", profile),
    ] {
        let specific: String = library
            .db()
            .query_row(
                &format!("SELECT SettingsSpecificFiles FROM {table} WHERE {key}=?1"),
                [id],
                |row| row.get(0),
            )
            .unwrap();
        assert_eq!(specific, "opaque;archive;members");
    }

    let before = owners.map(|(table, key, id)| library.saved(table, key, id));
    library.db().execute_batch("CREATE TRIGGER fail_settings_reference BEFORE UPDATE OF SettingsFiles ON SourcePorts BEGIN SELECT RAISE(ABORT,'fixture reference failure'); END;").unwrap();
    assert!(
        library
            .try_call(json!({"op":"rename_entry","id":middle,"name":"failed;rename.wad"}))
            .is_err()
    );
    assert!(library.root.join("GameFiles/改名;épisode.wad").is_file());
    assert!(!library.root.join("GameFiles/failed;rename.wad").exists());
    for (index, (table, key, id)) in owners.into_iter().enumerate() {
        assert_eq!(library.saved(table, key, id), before[index]);
    }
    assert_eq!(
        library.call(json!({"op":"play_defaults","id":game}))["options"]["additional_files"],
        expected
    );
}

#[test]
fn legacy_lists_remain_readable_without_reinterpreting_json_like_filenames() {
    let library = Library::new();
    let game = library.import("main.wad", false);
    library.db().execute("UPDATE GameFiles SET SettingsFiles=';first.wad;;日本語.wad;[last].wad;' WHERE GameFileID=?1",[game]).unwrap();
    assert_eq!(
        library.call(json!({"op":"play_defaults","id":game}))["options"]["additional_files"],
        json!(["first.wad", "日本語.wad", "[last].wad"])
    );
}

#[test]
fn malformed_versioned_lists_report_corruption_instead_of_splitting_filenames() {
    let library = Library::new();
    let game = library.import("main.wad", false);
    library.db().execute("UPDATE GameFiles SET SettingsFiles='@DoomLauncher/SettingsFiles/v1:[broken;list' WHERE GameFileID=?1",[game]).unwrap();
    let error = library
        .try_call(json!({"op":"play_defaults","id":game}))
        .unwrap_err();
    assert_eq!(error.code, "corrupt_data");
}
