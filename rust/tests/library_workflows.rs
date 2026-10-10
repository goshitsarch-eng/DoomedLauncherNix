//! End-to-end command regressions against the frozen schema from the previous Qt
//! implementation. Fixtures enter through the public JSON request contract and
//! verify database relations and disk outcomes independently of API internals.
use doomedlauncher_core::api::{Request, dispatch};
use doomedlauncher_core::error::Result;
use rusqlite::{Connection, params};
use serde_json::{Value, json};
use std::fs;
use std::path::{Path, PathBuf};
#[cfg(unix)]
use std::time::{Duration, Instant};
use tempfile::TempDir;

fn legacy_schema() -> &'static str {
    include_str!("fixtures/legacy-4.0.3.sql")
}

struct Library {
    directory: TempDir,
    root: PathBuf,
}
impl Library {
    fn new() -> Self {
        let directory = TempDir::new().unwrap();
        let root = directory.path().join("library ü #1");
        fs::create_dir_all(root.join("GameFiles")).unwrap();
        let db = Connection::open(root.join("DoomLauncher.sqlite")).unwrap();
        db.execute_batch(legacy_schema()).unwrap();
        Self { directory, root }
    }
    fn db(&self) -> Connection {
        Connection::open(self.root.join("DoomLauncher.sqlite")).unwrap()
    }
    fn try_call(&self, mut operation: Value) -> Result<Value> {
        operation["data_root"] = json!(self.root);
        dispatch(serde_json::from_value::<Request>(operation).unwrap())
    }
    fn call(&self, operation: Value) -> Value {
        self.try_call(operation.clone())
            .unwrap_or_else(|error| panic!("{operation}: {error}"))
    }
    fn snapshot(&self) -> Value {
        self.call(json!({"op":"snapshot"}))
    }
    fn write(&self, relative: &str, data: &[u8]) -> PathBuf {
        let path = self.directory.path().join(relative);
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(&path, data).unwrap();
        path
    }
    fn wad(&self, relative: &str, maps: &[&str]) -> PathBuf {
        self.write(relative, &wad(maps))
    }
    fn import(&self, path: &Path, iwad: bool) -> i64 {
        let result = self.call(json!({"op":"import_files","paths":[path],"as_iwads":iwad}));
        assert_eq!(result["failed"], json!([]), "{result}");
        assert_eq!(result["added"].as_array().unwrap().len(), 1);
        let name = path.file_name().unwrap().to_str().unwrap();
        self.db()
            .query_row(
                "SELECT GameFileID FROM GameFiles WHERE FileName=?1 COLLATE NOCASE",
                [name],
                |row| row.get(0),
            )
            .unwrap()
    }
    fn game(&self, id: i64) -> Value {
        self.snapshot()["games"]
            .as_array()
            .unwrap()
            .iter()
            .find(|game| game["GameFileID"] == id)
            .unwrap()
            .clone()
    }
    fn count(&self, table: &str) -> i64 {
        self.db()
            .query_row(&format!("SELECT count(*) FROM {table}"), [], |row| {
                row.get(0)
            })
            .unwrap()
    }
    #[cfg(unix)]
    fn save_port(&self, name: &str, executable: &Path, launch_type: i64) -> i64 {
        self.call(json!({"op":"save_port","port":{"name":name,"executable":executable,"directory":executable.parent().unwrap(),"supported_extensions":"wad,pk3,zip,deh,bex","launch_type":launch_type}}));
        self.db()
            .query_row(
                "SELECT SourcePortID FROM SourcePorts WHERE Name=?1",
                [name],
                |row| row.get(0),
            )
            .unwrap()
    }
}

fn wad(maps: &[&str]) -> Vec<u8> {
    let lumps = maps
        .iter()
        .flat_map(|name| [*name, "THINGS"])
        .collect::<Vec<_>>();
    let mut bytes = b"IWAD".to_vec();
    bytes.extend_from_slice(&(lumps.len() as u32).to_le_bytes());
    bytes.extend_from_slice(&12_u32.to_le_bytes());
    for name in lumps {
        assert!(name.len() <= 8);
        bytes.extend_from_slice(&12_u32.to_le_bytes());
        bytes.extend_from_slice(&0_u32.to_le_bytes());
        let mut lump = [0_u8; 8];
        lump[..name.len()].copy_from_slice(name.as_bytes());
        bytes.extend_from_slice(&lump);
    }
    bytes
}

#[test]
fn legacy_migration_preserves_unknown_data_ids_relations_and_original_backup() {
    let library = Library::new();
    let original = library.wad("source/original.wad", &["MAP01"]);
    fs::copy(&original, library.root.join("GameFiles/legacy.wad")).unwrap();
    let db = library.db();
    db.execute_batch("ALTER TABLE GameFiles ADD COLUMN UserExtension TEXT; CREATE TABLE ThirdParty (ID INTEGER PRIMARY KEY, Data BLOB); INSERT INTO ThirdParty VALUES(99,X'00FF80'); INSERT INTO GameFiles(GameFileID,FileName,Title,Comments,MinutesPlayed,UserExtension) VALUES(42,'legacy.wad','My title','Private note',81,'keep me'); INSERT INTO Tags(TagID,Name,HasTab,Color) VALUES(17,'Favourite',1,16711935); INSERT INTO TagMapping VALUES(42,17); INSERT INTO IWads(IWadID,Name,FileName,GameFileID) VALUES(9,'Base','legacy.wad',42); INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES('VendorPreference','opaque value','a;b',0); INSERT INTO GameProfiles(GameProfileID,GameFileID,SourcePortID,IWadID,Name,SettingsFiles) VALUES(83,42,11,9,'Legacy profile','a.wad;b.wad');").unwrap();
    drop(db);
    let snapshot = library.snapshot();
    assert_eq!(snapshot["games"][0]["GameFileID"], 42);
    assert_eq!(snapshot["games"][0]["Comments"], "Private note");
    assert_eq!(snapshot["games"][0]["UserExtension"], "keep me");
    assert_eq!(snapshot["games"][0]["MinutesPlayed"], 81);
    assert_eq!(snapshot["games"][0]["tagIds"], json!([17]));
    assert_eq!(snapshot["games"][0]["isIwad"], true);
    assert_eq!(snapshot["iwads"][0]["iwad_id"], 9);
    assert_eq!(snapshot["settings"]["VendorPreference"], "opaque value");
    assert_eq!(
        library
            .db()
            .query_row("SELECT hex(Data) FROM ThirdParty WHERE ID=99", [], |r| {
                r.get::<_, String>(0)
            })
            .unwrap(),
        "00FF80"
    );
    assert_eq!(
        library
            .db()
            .query_row(
                "SELECT Name FROM GameProfiles WHERE GameProfileID=83",
                [],
                |r| r.get::<_, String>(0)
            )
            .unwrap(),
        "Legacy profile"
    );
    let backups: Vec<PathBuf> = fs::read_dir(&library.root)
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .filter(|path| {
            path.file_name()
                .unwrap()
                .to_string_lossy()
                .starts_with("DoomLauncher.pre-flutter-")
        })
        .collect();
    assert_eq!(backups.len(), 1);
    let backup = Connection::open(&backups[0]).unwrap();
    assert_eq!(
        backup
            .query_row(
                "SELECT Comments FROM GameFiles WHERE GameFileID=42",
                [],
                |r| r.get::<_, String>(0)
            )
            .unwrap(),
        "Private note"
    );
    assert_eq!(
        backup
            .query_row(
                "SELECT count(*) FROM sqlite_master WHERE name='CoreMigrations'",
                [],
                |r| r.get::<_, i64>(0)
            )
            .unwrap(),
        0
    );
    assert_eq!(
        backup
            .query_row("PRAGMA integrity_check", [], |r| r.get::<_, String>(0))
            .unwrap(),
        "ok"
    );
    library.snapshot();
    assert_eq!(
        fs::read_dir(&library.root)
            .unwrap()
            .filter_map(std::result::Result::ok)
            .filter(|entry| entry
                .file_name()
                .to_string_lossy()
                .starts_with("DoomLauncher.pre-flutter-"))
            .count(),
        1
    );
    assert_eq!(
        fs::read(library.root.join("GameFiles/legacy.wad")).unwrap(),
        fs::read(original).unwrap()
    );
}

#[test]
fn incomplete_legacy_schema_is_upgraded_without_losing_records() {
    let library = Library::new();
    let db = library.db();
    db.execute_batch("DROP TABLE GameFiles; CREATE TABLE GameFiles (GameFileID INTEGER PRIMARY KEY,FileName TEXT NOT NULL,VendorNote TEXT); INSERT INTO GameFiles VALUES(123,'old.wad','custom');").unwrap();
    drop(db);
    fs::write(library.root.join("GameFiles/old.wad"), wad(&[])).unwrap();
    let entry = library.game(123);
    assert_eq!(entry["FileName"], "old.wad");
    assert_eq!(entry["VendorNote"], "custom");
    assert!(entry.get("SettingsLoadLatestSave").is_some());
    assert_eq!(
        library
            .db()
            .query_row("PRAGMA integrity_check", [], |r| r.get::<_, String>(0))
            .unwrap(),
        "ok"
    );
}

#[test]
fn imports_promote_identical_files_and_reject_case_insensitive_collisions() {
    let library = Library::new();
    let path = library.wad("imports/base.wad", &["MAP01", "MAP02"]);
    let id = library.import(&path, false);
    assert_eq!(library.game(id)["MapCount"], 2);
    assert_eq!(library.game(id)["isIwad"], false);
    assert_eq!(library.import(&path, true), id);
    assert_eq!(library.import(&path, true), id);
    assert_eq!(library.count("GameFiles"), 1);
    assert_eq!(library.count("IWads"), 1);
    assert_eq!(library.game(id)["isIwad"], true);
    let different = library.wad("other/BASE.WAD", &["MAP99"]);
    let result = library.call(json!({"op":"import_files","paths":[different]}));
    assert_eq!(result["failed"].as_array().unwrap().len(), 1);
    assert!(result["added"].as_array().unwrap().is_empty());
    assert_eq!(
        fs::read(library.root.join("GameFiles/base.wad")).unwrap(),
        fs::read(path).unwrap()
    );
    assert_eq!(library.count("GameFiles"), 1);
}

#[test]
fn untracked_collision_and_malformed_content_do_not_create_database_entries() {
    let library = Library::new();
    library.snapshot();
    fs::write(
        library.root.join("GameFiles/existing.wad"),
        b"untracked user content",
    )
    .unwrap();
    let existing = library.wad("source/existing.wad", &["MAP01"]);
    let malformed = library.write("source/broken.wad", b"IWAD\x7f\xff");
    let result = library.call(json!({"op":"import_files","paths":[existing,malformed]}));
    assert_eq!(result["failed"].as_array().unwrap().len(), 2);
    assert_eq!(library.count("GameFiles"), 0);
    assert_eq!(
        fs::read(library.root.join("GameFiles/existing.wad")).unwrap(),
        b"untracked user content"
    );
    assert!(!library.root.join("GameFiles/broken.wad").exists());
}

#[test]
fn import_failure_rolls_back_copy_and_row_even_when_metadata_write_fails() {
    let library = Library::new();
    library.snapshot();
    library.db().execute_batch("CREATE TRIGGER deny_metadata BEFORE UPDATE OF Map ON GameFiles BEGIN SELECT RAISE(ABORT,'fixture metadata write denied'); END;").unwrap();
    let source = library.wad("source/atomic.wad", &["MAP01"]);
    let result = library.call(json!({"op":"import_files","paths":[source],"as_iwads":true}));
    assert_eq!(result["failed"].as_array().unwrap().len(), 1);
    assert_eq!(
        library.count("GameFiles"),
        0,
        "A failed import must not leave a partly initialized entry"
    );
    assert_eq!(library.count("IWads"), 0);
    assert!(!library.root.join("GameFiles/atomic.wad").exists());
}

#[test]
fn rename_updates_iwad_and_remembered_file_order_in_every_legacy_owner() {
    let library = Library::new();
    let base = library.wad("source/base.wad", &["MAP01"]);
    let id = library.import(&base, true);
    let mod_id = library.import(&library.wad("source/mod.wad", &["MAP02"]), false);
    let db = library.db();
    db.execute(
        "UPDATE GameFiles SET SettingsFiles='first.wad;base.wad;last.wad' WHERE GameFileID=?1",
        [mod_id],
    )
    .unwrap();
    db.execute("INSERT INTO SourcePorts(Name,Executable,SupportedExtensions,Directory,SettingsFiles,LaunchType) VALUES('Legacy','engine','wad','','base.wad;other.wad','0')", []).unwrap();
    db.execute("INSERT INTO GameProfiles(GameFileID,SourcePortID,IWadID,Name,SettingsFiles) VALUES(?1,1,1,'Profile','base.wad;other.wad')", [mod_id]).unwrap();
    drop(db);
    library.call(json!({"op":"rename_entry","id":id,"name":"Renamed ü.wad"}));
    assert_eq!(library.game(id)["FileName"], "Renamed ü.wad");
    assert_eq!(
        library.game(mod_id)["SettingsFiles"],
        "first.wad;Renamed ü.wad;last.wad"
    );
    for table in ["IWads", "GameProfiles", "SourcePorts"] {
        let field = if table == "IWads" {
            "FileName"
        } else {
            "SettingsFiles"
        };
        let saved: String = library
            .db()
            .query_row(&format!("SELECT {field} FROM {table} LIMIT 1"), [], |r| {
                r.get(0)
            })
            .unwrap();
        assert!(saved.starts_with("Renamed ü.wad"));
    }
    assert!(!library.root.join("GameFiles/base.wad").exists());
    assert_eq!(
        fs::read(library.root.join("GameFiles/Renamed ü.wad")).unwrap(),
        fs::read(base).unwrap()
    );
    library.call(json!({"op":"rename_entry","id":id,"name":"RENAMED Ü.wad"}));
    assert!(library.root.join("GameFiles/RENAMED Ü.wad").is_file());
}

#[test]
fn failed_rename_restores_disk_and_database_and_rejects_collisions() {
    let library = Library::new();
    let id = library.import(&library.wad("source/first.wad", &["MAP01"]), true);
    fs::write(
        library.root.join("GameFiles/first.txt"),
        "Title: Preserve sidecar",
    )
    .unwrap();
    library.import(&library.wad("source/second.wad", &["MAP02"]), false);
    assert!(
        library
            .try_call(json!({"op":"rename_entry","id":id,"name":"second.wad"}))
            .is_err()
    );
    library.db().execute_batch("CREATE TRIGGER deny_rename BEFORE UPDATE OF FileName ON GameFiles BEGIN SELECT RAISE(ABORT,'fixture rename denied'); END;").unwrap();
    assert!(
        library
            .try_call(json!({"op":"rename_entry","id":id,"name":"new.wad"}))
            .is_err()
    );
    assert!(library.root.join("GameFiles/first.wad").is_file());
    assert!(!library.root.join("GameFiles/new.wad").exists());
    assert_eq!(
        fs::read_to_string(library.root.join("GameFiles/first.txt")).unwrap(),
        "Title: Preserve sidecar"
    );
    assert!(!library.root.join("GameFiles/new.txt").exists());
    assert_eq!(library.game(id)["FileName"], "first.wad");
    assert_eq!(library.snapshot()["iwads"][0]["file_name"], "first.wad");
}

#[test]
fn entry_edits_and_tag_mappings_commit_together_and_cancel_discards_draft() {
    let library = Library::new();
    let id = library.import(&library.wad("source/game.wad", &["MAP01"]), false);
    library.call(json!({"op":"save_tag","name":"One","has_tab":true}));
    library.call(json!({"op":"save_tag","name":"Two","has_tab":false}));
    let tags = library.snapshot()["tags"].clone();
    let one = tags[0]["tag_id"].as_i64().unwrap();
    let two = tags[1]["tag_id"].as_i64().unwrap();
    library.call(json!({"op":"edit_entry","id":id,"fields":{"Title":"Original","Author":"Author","Rating":4,"Comments":"Keep"},"tag_ids":[one]}));
    let mut draft = library.game(id);
    draft["Title"] = json!("Unsaved");
    draft["tagIds"] = json!([two]);
    drop(draft); // Cancelling the UI draft sends no edit command.
    assert_eq!(library.game(id)["Title"], "Original");
    assert_eq!(library.game(id)["tagIds"], json!([one]));
    assert!(library.try_call(json!({"op":"edit_entry","id":id,"fields":{"Title":"Must roll back"},"tag_ids":[two,99999]})).is_err());
    assert_eq!(library.game(id)["Title"], "Original");
    assert_eq!(library.game(id)["tagIds"], json!([one]));
    assert!(library.try_call(json!({"op":"edit_entry","id":id,"fields":{"FileName":"outside.wad"},"tag_ids":[one]})).is_err());
    assert!(
        library
            .try_call(json!({"op":"edit_entry","id":id,"fields":{"Rating":6},"tag_ids":[one]}))
            .is_err()
    );
    library.call(json!({"op":"edit_entry","id":id,"fields":{"Title":"Saved"},"tag_ids":[two,two]}));
    assert_eq!(library.game(id)["Title"], "Saved");
    assert_eq!(library.game(id)["tagIds"], json!([two]));
    library.call(json!({"op":"delete_tag","id":two}));
    assert_eq!(library.game(id)["tagIds"], json!([]));
}

#[test]
fn deleting_entry_cleans_relations_and_preserves_a_recoverable_managed_file() {
    let library = Library::new();
    let id = library.import(&library.wad("source/remove.wad", &["MAP01"]), true);
    library.call(json!({"op":"save_tag","name":"Tag"}));
    let db = library.db();
    db.execute("INSERT INTO TagMapping(FileID,TagID) VALUES(?1,1)", [id])
        .unwrap();
    db.execute(
        "INSERT INTO GameProfiles(GameFileID,SourcePortID,IWadID,Name) VALUES(?1,1,1,'old')",
        [id],
    )
    .unwrap();
    db.execute("INSERT INTO Stats(GameFileID,KillCount,TotalKills,SecretCount,TotalSecrets,LevelTime,ItemCount,TotalItems,SourcePortID,MapName,RecordTime) VALUES(?1,1,2,0,1,10.5,3,4,1,'MAP01','2026-01-01')", [id]).unwrap();
    drop(db);
    let image = library.write("images/shot.png", b"screenshot content");
    library.call(json!({"op":"import_associations","id":id,"paths":[image]}));
    library.call(json!({"op":"delete_entry","id":id,"delete_file":true}));
    for table in [
        "GameFiles",
        "IWads",
        "Files",
        "GameProfiles",
        "Stats",
        "TagMapping",
    ] {
        assert_eq!(library.count(table), 0, "orphan records in {table}");
    }
    assert_eq!(library.count("Tags"), 1);
    assert!(!library.root.join("GameFiles/remove.wad").exists());
    assert!(!library.root.join("GameFiles/Screenshots/shot.png").exists());
    assert_eq!(library.snapshot()["settings"]["DefaultIWad"], "0");
    let trash = walkdir::WalkDir::new(library.root.join("Trash"))
        .into_iter()
        .filter_map(std::result::Result::ok)
        .filter(|e| e.file_type().is_file())
        .map(|e| e.into_path())
        .collect::<Vec<_>>();
    assert!(trash.iter().any(|path| {
        path.file_name()
            .unwrap()
            .to_string_lossy()
            .ends_with("remove.wad")
    }));
    assert!(
        trash
            .iter()
            .any(|path| path.file_name().unwrap() == "recovery.json")
    );
}

#[test]
fn delete_without_files_keeps_managed_content_and_failed_delete_rolls_back() {
    let library = Library::new();
    let id = library.import(&library.wad("source/keep.wad", &["MAP01"]), false);
    fs::write(
        library.root.join("GameFiles/keep.txt"),
        "Do not lose this note",
    )
    .unwrap();
    library.db().execute_batch("CREATE TRIGGER deny_delete BEFORE DELETE ON GameFiles BEGIN SELECT RAISE(ABORT,'fixture delete denied'); END;").unwrap();
    assert!(
        library
            .try_call(json!({"op":"delete_entry","id":id,"delete_file":true}))
            .is_err()
    );
    assert_eq!(library.game(id)["FileName"], "keep.wad");
    assert!(library.root.join("GameFiles/keep.wad").exists());
    assert_eq!(
        fs::read_to_string(library.root.join("GameFiles/keep.txt")).unwrap(),
        "Do not lose this note"
    );
    library
        .db()
        .execute_batch("DROP TRIGGER deny_delete;")
        .unwrap();
    library.call(json!({"op":"delete_entry","id":id,"delete_file":false}));
    assert!(library.root.join("GameFiles/keep.wad").exists());
    assert!(library.root.join("GameFiles/keep.txt").exists());
    assert_eq!(library.count("GameFiles"), 0);
}

#[test]
fn absolute_legacy_game_and_association_paths_are_readable_but_never_deleted() {
    let library = Library::new();
    let game = library.wad("external/unmanaged.wad", &["MAP01"]);
    let screenshot = library.write("external/precious.png", b"external image");
    let db = library.db();
    db.execute(
        "INSERT INTO GameFiles(GameFileID,FileName) VALUES(47,?1)",
        [game.to_str().unwrap()],
    )
    .unwrap();
    db.execute("INSERT INTO Files(FileID,GameFileID,FileName,DateCreated,FileTypeID,SourcePortID) VALUES(88,47,?1,'2026-01-01',1,-1)", [screenshot.to_str().unwrap()]).unwrap();
    drop(db);
    let details = library.call(json!({"op":"entry_details","id":47}));
    assert_eq!(details["game"]["path"], json!(game));
    assert_eq!(details["game"]["imagePath"], json!(screenshot));
    assert_eq!(details["files"][0]["path"], json!(screenshot));
    assert!(
        library
            .try_call(json!({"op":"rename_entry","id":47,"name":"managed.wad"}))
            .is_err()
    );
    library.call(json!({"op":"delete_association","file_id":88}));
    assert!(
        screenshot.exists(),
        "Removing an external reference must preserve the user's file"
    );
    library.call(json!({"op":"delete_entry","id":47,"delete_file":true}));
    assert!(
        game.exists(),
        "Delete managed files must never delete an external game path"
    );
    assert!(screenshot.exists());
}

#[test]
fn screenshot_demo_and_legacy_save_associations_handle_collisions_and_shared_rows() {
    let library = Library::new();
    let first = library.import(&library.wad("source/one.wad", &["MAP01"]), false);
    let second = library.import(&library.wad("source/two.wad", &["MAP02"]), false);
    let png = library.write("source/shot.png", b"image");
    let demo = library.write("source/run.lmp", b"demo");
    let save = library.write("source/latest.save", b"save");
    let result = library
        .call(json!({"op":"import_associations","id":first,"paths":[png.clone(),demo,save]}));
    assert_eq!(result["failed"], json!([]));
    assert_eq!(result["added"].as_array().unwrap().len(), 3);
    library.call(json!({"op":"import_associations","id":second,"paths":[png]}));
    let first_details = library.call(json!({"op":"entry_details","id":first}));
    let second_details = library.call(json!({"op":"entry_details","id":second}));
    let screenshot = first_details["files"]
        .as_array()
        .unwrap()
        .iter()
        .find(|row| row["FileTypeID"] == 1)
        .unwrap();
    assert_ne!(screenshot["path"], second_details["files"][0]["path"]);
    assert!(
        first_details["files"]
            .as_array()
            .unwrap()
            .iter()
            .any(|row| row["FileTypeID"] == 3)
    );
    let id = screenshot["FileID"].as_i64().unwrap();
    let name = screenshot["FileName"].as_str().unwrap();
    library.db().execute("INSERT INTO Files(GameFileID,FileName,DateCreated,FileTypeID,SourcePortID) VALUES(?1,?2,'2026-01-01',1,-1)",params![second,name]).unwrap();
    library.call(json!({"op":"delete_association","file_id":id}));
    assert!(
        Path::new(screenshot["path"].as_str().unwrap()).exists(),
        "Deleting one shared reference must retain the other reference's file"
    );
    let shared: i64 = library
        .db()
        .query_row(
            "SELECT FileID FROM Files WHERE GameFileID=?1 AND FileName=?2",
            params![second, name],
            |r| r.get(0),
        )
        .unwrap();
    library.call(json!({"op":"delete_association","file_id":shared}));
    assert!(!Path::new(screenshot["path"].as_str().unwrap()).exists());
}

#[test]
fn settings_validate_whole_batch_before_writing_and_preserve_unknown_keys() {
    let library = Library::new();
    library.call(json!({"op":"update_settings","values":{"ColorThemeType":"Dark","VendorOpaque":"keep;value","AppWidth":"1200"}}));
    assert!(
        library
            .try_call(
                json!({"op":"update_settings","values":{"ColorThemeType":"Default","AppWidth":"2"}})
            )
            .is_err()
    );
    let settings = library.snapshot()["settings"].clone();
    assert_eq!(settings["ColorThemeType"], "Dark");
    assert_eq!(settings["AppWidth"], "1200");
    assert_eq!(settings["VendorOpaque"], "keep;value");
    for (name, value) in [
        ("MirrorUrl", "ftp://example.com/"),
        ("IdGamesUrl", "https://user:secret@example.com"),
        ("ApiPage", "../escape"),
        ("ShowPlayDialog", "yes"),
        ("DefaultIWad", "-3"),
    ] {
        let mut values = serde_json::Map::new();
        values.insert(name.into(), json!(value));
        assert!(
            library
                .try_call(json!({"op":"update_settings","values":values}))
                .is_err(),
            "{name}={value}"
        );
    }
}

#[test]
fn legacy_text_launch_types_and_existing_play_defaults_survive_migration() {
    let library = Library::new();
    let first = library.import(&library.wad("source/first.wad", &["MAP01"]), true);
    let second = library.import(&library.wad("source/second.wad", &["MAP02"]), true);
    let db = library.db();
    db.execute_batch("INSERT INTO SourcePorts(SourcePortID,Name,Executable,SupportedExtensions,Directory,LaunchType,Archived) VALUES(31,'Normal','engine','wad','','0',0),(32,'Doom64','doom64_x64','wad','','2',0),(33,'Saved normal','other-engine','wad','','0',0); UPDATE Configuration SET Value='31' WHERE Name='DefaultSourcePort'; UPDATE Configuration SET Value='1' WHERE Name='DefaultIWad';").unwrap();
    let second_iwad: i64 = db
        .query_row(
            "SELECT IWadID FROM IWads WHERE GameFileID=?1",
            [second],
            |r| r.get(0),
        )
        .unwrap();
    db.execute("UPDATE GameFiles SET SourcePortID=33,IWadID=?2,SettingsSaved=NULL,SettingsMap='MAP02',SettingsSkill='4',SettingsFiles='one.wad;two.wad',SettingsStat=NULL WHERE GameFileID=?1",params![first,second_iwad]).unwrap();
    drop(db);
    let snapshot = library.snapshot();
    let port = snapshot["ports"]
        .as_array()
        .unwrap()
        .iter()
        .find(|port| port["id"] == 32)
        .unwrap();
    assert_eq!(
        port["launch_type"], 2,
        "LaunchType TEXT must not silently become the normal engine type"
    );
    let defaults = library.call(json!({"op":"play_defaults","id":first}));
    assert_eq!(defaults["options"]["source_port_id"], 33);
    let first_iwad: i64 = library
        .db()
        .query_row(
            "SELECT IWadID FROM IWads WHERE GameFileID=?1",
            [first],
            |r| r.get(0),
        )
        .unwrap();
    assert_eq!(
        defaults["options"]["iwad_id"], first_iwad,
        "Playing an IWAD must select itself"
    );
    assert_eq!(defaults["options"]["skill"], 4);
    assert_eq!(defaults["options"]["save_statistics"], true);
    assert_eq!(
        defaults["options"]["additional_files"],
        json!(["one.wad", "two.wad"])
    );
    library
        .db()
        .execute(
            "UPDATE GameFiles SET SourcePortID=32 WHERE GameFileID=?1",
            [second],
        )
        .unwrap();
    let standalone = library.call(json!({"op":"play_defaults","id":second}));
    assert_eq!(standalone["options"]["extra_params_only"], true);
    assert_eq!(standalone["options"]["iwad_id"], Value::Null);
    assert_eq!(standalone["options"]["save_statistics"], false);
    library.call(json!({"op":"delete_port","id":33}));
    assert_eq!(
        library.call(json!({"op":"play_defaults","id":first}))["options"]["source_port_id"],
        31
    );
}

fn journal(id: &str, game: i64, started: &str, seconds: u64) -> Value {
    json!({"id":id,"game_file_id":game,"source_port_id":7,"process_id":1,"started_at":started,"finished_at":"2026-02-02T12:30:00Z","status":"completed","exit_code":0,"elapsed_seconds":seconds,"error":null,"statistics":[{"map_name":"MAP01","kill_count":3,"total_kills":4,"item_count":2,"total_items":3,"secret_count":1,"total_secrets":1,"level_time":20.0,"skill":3}],"log_path":"fixture.log","demo_path":null})
}

#[test]
fn finished_session_journals_harvest_once_but_identical_distinct_runs_both_count() {
    let library = Library::new();
    let id = library.import(&library.wad("source/session.wad", &["MAP01"]), false);
    let old = journal(
        "11111111-1111-4111-8111-111111111111",
        id,
        "2026-02-01T12:00:00Z",
        125,
    );
    let newer = journal(
        "22222222-2222-4222-8222-222222222222",
        id,
        "2026-02-02T12:00:00Z",
        185,
    );
    for session in [&old, &newer] {
        fs::write(
            library
                .root
                .join("Sessions")
                .join(format!("{}.json", session["id"].as_str().unwrap())),
            serde_json::to_vec(session).unwrap(),
        )
        .unwrap();
    }
    for _ in 0..3 {
        library.call(json!({"op":"sessions"}));
        library.snapshot();
    }
    assert_eq!(
        library.count("Stats"),
        2,
        "Identical statistics from distinct sessions must both be kept"
    );
    assert_eq!(library.count("HarvestedSessions"), 2);
    assert_eq!(library.game(id)["MinutesPlayed"], 5);
    assert_eq!(
        library.game(id)["LastPlayed"],
        "2026-02-02T12:00:00Z",
        "Journal enumeration must not move LastPlayed backwards"
    );
}

#[cfg(unix)]
fn executable(library: &Library, name: &str, script: &str) -> PathBuf {
    use std::os::unix::fs::PermissionsExt;
    let path = library.write(
        &format!("engine/{name}"),
        format!("#!/bin/sh\nset -eu\n{script}\n").as_bytes(),
    );
    fs::set_permissions(&path, fs::Permissions::from_mode(0o700)).unwrap();
    path
}

#[cfg(unix)]
#[test]
fn native_launch_uses_typed_arguments_records_stats_and_remembers_only_after_start() {
    let library = Library::new();
    let game = library.import(&library.wad("source/main.wad", &["MAP01"]), false);
    let base = library.import(&library.wad("source/base.wad", &[]), true);
    let iwad: i64 = library
        .db()
        .query_row(
            "SELECT IWadID FROM IWads WHERE GameFileID=?1",
            [base],
            |r| r.get(0),
        )
        .unwrap();
    let engine = executable(
        &library,
        "dsda-doom-fixture",
        "printf '%s\\n' \"$@\" > arguments.txt\nprintf 'MAP01 - 0:20.00 (0:20) K: 3/4 I: 2/3 S: 1/1\\n' > levelstat.txt\nprintf 'engine output\\n'\nsleep 0.1",
    );
    let port = library.save_port("Fixture", &engine, 0);
    let working_directory = library.directory.path().join("explicit working directory");
    fs::create_dir_all(&working_directory).unwrap();
    library
        .db()
        .execute(
            "UPDATE SourcePorts SET Directory=?2 WHERE SourcePortID=?1",
            params![port, working_directory.to_str().unwrap()],
        )
        .unwrap();
    let options = json!({"game_file_id":game,"source_port_id":port,"iwad_id":iwad,"map":"MAP01","skill":4,"extra_params":"+set player \"Name with spaces\" ;touch unwanted","save_statistics":true,"remember":true});
    let preview = library.call(json!({"op":"launch_preview","options":options}));
    assert_eq!(preview["program"], json!(engine));
    assert_eq!(preview["working_directory"], json!(working_directory));
    assert!(
        preview["arguments"]
            .as_array()
            .unwrap()
            .contains(&json!("Name with spaces"))
    );
    let started = library.call(json!({"op":"launch","options":options}));
    let session_id = started["session"]["id"].as_str().unwrap();
    let start = Instant::now();
    loop {
        let history = library.call(json!({"op":"sessions"}));
        let session = history["sessions"]
            .as_array()
            .unwrap()
            .iter()
            .find(|s| s["id"] == session_id)
            .unwrap();
        if session["status"] != "running" {
            assert_eq!(session["status"], "completed", "{session}");
            break;
        }
        assert!(start.elapsed() < Duration::from_secs(8));
        std::thread::sleep(Duration::from_millis(25));
    }
    library.call(json!({"op":"sessions"}));
    assert_eq!(library.count("Stats"), 1);
    assert_eq!(library.game(game)["SettingsSaved"], 1);
    assert_eq!(library.game(game)["SettingsSkill"], "4");
    assert!(!working_directory.join("unwanted").exists());
    let argv = fs::read_to_string(working_directory.join("arguments.txt")).unwrap();
    assert!(argv.contains("Name with spaces\n;touch\nunwanted\n"));
    let missing = library.directory.path().join("does-not-exist-engine");
    let bad_port = library.save_port("Missing", &missing, 0);
    assert!(library.try_call(json!({"op":"launch","options":{"game_file_id":game,"source_port_id":bad_port,"extra_params_only":true,"extra_params":"changed","remember":true}})).is_err());
    assert_eq!(library.game(game)["SourcePortID"], port);
    assert_eq!(library.game(game)["SettingsSkill"], "4");
}

#[cfg(unix)]
#[test]
fn launch_rejects_missing_addons_and_unassociated_demos_before_starting_engine() {
    let library = Library::new();
    let game = library.import(&library.wad("source/game.wad", &["MAP01"]), true);
    let engine = executable(&library, "fixture-engine", "touch engine-started");
    let port = library.save_port("Fixture", &engine, 0);
    assert!(library.try_call(json!({"op":"launch","options":{"game_file_id":game,"source_port_id":port,"additional_files":["missing.wad"]}})).is_err());
    let demo = library.write("external/unassociated.lmp", b"demo");
    assert!(library.try_call(json!({"op":"launch","options":{"game_file_id":game,"source_port_id":port,"play_demo_file":demo}})).is_err());
    assert!(!engine.parent().unwrap().join("engine-started").exists());
    assert_eq!(library.count("HarvestedSessions"), 0);
}

#[test]
fn zip_import_exposes_metadata_and_text_without_allowing_arbitrary_member_reads() {
    use std::io::Write;
    use zip::write::SimpleFileOptions;
    let library = Library::new();
    let path = library.directory.path().join("package.zip");
    let mut archive = zip::ZipWriter::new(fs::File::create(&path).unwrap());
    archive
        .start_file("levels/main.wad", SimpleFileOptions::default())
        .unwrap();
    archive.write_all(&wad(&["MAP03", "MAP04"])).unwrap();
    archive
        .start_file("docs/package.txt", SimpleFileOptions::default())
        .unwrap();
    archive
        .write_all(
            b"Title : Archive fixture\nAuthor : Test author\nDescription : A complete package\n",
        )
        .unwrap();
    archive.finish().unwrap();
    let id = library.import(&path, false);
    assert_eq!(library.game(id)["Title"], "Archive fixture");
    assert_eq!(library.game(id)["Author"], "Test author");
    assert_eq!(library.game(id)["MapCount"], 2);
    let details = library.call(json!({"op":"entry_details","id":id}));
    assert_eq!(details["text_files"], json!(["docs/package.txt"]));
    let text = library.call(json!({"op":"open_text","id":id,"member":"docs/package.txt"}));
    assert!(
        fs::read_to_string(text["path"].as_str().unwrap())
            .unwrap()
            .contains("Archive fixture")
    );
    assert!(
        library
            .try_call(json!({"op":"open_text","id":id,"member":"../../outside.txt"}))
            .is_err()
    );
    library.call(json!({"op":"edit_entry","id":id,"fields":{"Title":"User title","Comments":"Keep annotation"}}));
    library.call(json!({"op":"resync_metadata","ids":[id]}));
    assert_eq!(library.game(id)["Title"], "User title");
    assert_eq!(library.game(id)["Comments"], "Keep annotation");
    assert_eq!(library.game(id)["MapCount"], 2);
}

#[test]
fn directory_import_reports_bad_files_and_respects_recursion() {
    let library = Library::new();
    let first = library.wad("directory/top.wad", &["MAP01"]);
    library.wad("directory/nested/child.wad", &["MAP02"]);
    library.write("directory/broken.wad", b"broken");
    library.write("directory/readme.txt", b"not an import");
    let result = library
        .call(json!({"op":"import_directory","path":first.parent().unwrap(),"recursive":false}));
    assert_eq!(result["added"], json!(["top.wad"]));
    assert_eq!(result["failed"].as_array().unwrap().len(), 1);
    assert_eq!(library.count("GameFiles"), 1);
    let recursive = library
        .call(json!({"op":"import_directory","path":first.parent().unwrap(),"recursive":true}));
    assert_eq!(recursive["failed"].as_array().unwrap().len(), 1);
    assert_eq!(library.count("GameFiles"), 2);
}

#[test]
fn unavailable_or_foreign_platform_entry_does_not_hide_the_rest_of_the_library() {
    let library = Library::new();
    let id = library.import(&library.wad("source/valid.wad", &["MAP01"]), false);
    let foreign = if cfg!(windows) {
        "/Volumes/Games/old.wad"
    } else {
        r"C:\Games\old.wad"
    };
    library.db().execute("INSERT INTO GameFiles(GameFileID,FileName,Title,Comments) VALUES(999,?1,'Unavailable game','Preserve this note')",[foreign]).unwrap();
    library.db().execute("INSERT INTO Stats(GameFileID,KillCount,TotalKills,SecretCount,TotalSecrets,LevelTime,ItemCount,TotalItems,SourcePortID,MapName,RecordTime) VALUES(999,1,2,0,1,10.5,3,4,1,'MAP01','2026-01-01')", []).unwrap();
    let snapshot = library.snapshot();
    assert_eq!(snapshot["games"].as_array().unwrap().len(), 2);
    let foreign_entry = snapshot["games"]
        .as_array()
        .unwrap()
        .iter()
        .find(|entry| entry["GameFileID"] == 999)
        .unwrap();
    assert_eq!(foreign_entry["FileName"], foreign);
    assert_eq!(foreign_entry["Comments"], "Preserve this note");
    assert!(
        !foreign_entry["path"]
            .as_str()
            .is_some_and(|path| path.starts_with(library.root.to_str().unwrap())),
        "Foreign absolute paths cannot be reinterpreted beneath this data root"
    );
    let foreign_details = library.call(json!({"op":"entry_details","id":999}));
    assert_eq!(foreign_details["game"]["Title"], "Unavailable game");
    assert_eq!(foreign_details["stats"][0]["MapName"], "MAP01");
    assert_eq!(foreign_details["text_files"], json!([]));
    fs::remove_file(library.root.join("GameFiles/valid.wad")).unwrap();
    let missing = library.call(json!({"op":"entry_details","id":id}));
    assert_eq!(missing["game"]["GameFileID"], id);
    assert_eq!(missing["text_files"], json!([]));
    assert!(library.try_call(json!({"op":"open_text","id":id})).is_err());
    library.call(json!({"op":"delete_entry","id":999,"delete_file":false}));
    assert_eq!(library.count("GameFiles"), 1);
}

#[test]
fn unicode_case_collisions_are_rejected_and_identical_case_variants_promote_existing_entry() {
    let library = Library::new();
    let original = library.wad("first/über.wad", &["MAP01"]);
    let id = library.import(&original, false);
    let other = library.wad("second/ÜBER.WAD", &["MAP02"]);
    let rejected = library.call(json!({"op":"import_files","paths":[other],"as_iwads":true}));
    assert_eq!(
        rejected["failed"].as_array().unwrap().len(),
        1,
        "A portable library cannot contain Unicode case variants of different files"
    );
    assert_eq!(library.count("GameFiles"), 1);
    assert_eq!(library.count("IWads"), 0);
    fs::copy(&original, &other).unwrap();
    let promoted = library.call(json!({"op":"import_files","paths":[other],"as_iwads":true}));
    assert_eq!(promoted["failed"], json!([]));
    assert_eq!(promoted["added"], json!(["über.wad"]));
    assert_eq!(library.count("GameFiles"), 1);
    assert_eq!(library.game(id)["isIwad"], true);
}

#[test]
fn deleting_one_legacy_record_keeps_content_referenced_by_another_record() {
    let library = Library::new();
    let first = library.import(&library.wad("source/shared.wad", &["MAP01"]), false);
    library.db().execute("INSERT INTO GameFiles(GameFileID,FileName,Title) VALUES(999,'shared.wad','Surviving legacy record')",[]).unwrap();
    library.call(json!({"op":"delete_entry","id":first,"delete_file":true}));
    let survivor = library.game(999);
    assert_eq!(survivor["Title"], "Surviving legacy record");
    assert!(library.root.join("GameFiles/shared.wad").is_file());
    assert_eq!(
        fs::read(library.root.join("GameFiles/shared.wad")).unwrap(),
        wad(&["MAP01"])
    );
    let details = library.call(json!({"op":"entry_details","id":999}));
    assert_eq!(details["game"]["FileName"], "shared.wad");
    library.call(json!({"op":"delete_entry","id":999,"delete_file":true}));
    assert!(!library.root.join("GameFiles/shared.wad").exists());
}

#[test]
fn clean_temp_shutdown_removes_cache_only_and_preserves_disabled_cache() {
    let library = Library::new();
    let id = library.import(&library.wad("source/keep.wad", &["MAP01"]), false);
    let cache = library.root.join("GameFiles/Temp");
    fs::create_dir_all(cache.join("nested")).unwrap();
    fs::write(cache.join("nested/extracted.wad"), wad(&["MAP02"])).unwrap();
    fs::write(cache.join("temporary.txt"), "cached text").unwrap();
    let external = library.write("outside/keep.txt", b"original user data");
    #[cfg(unix)]
    std::os::unix::fs::symlink(external.parent().unwrap(), cache.join("outside-link")).unwrap();
    library.call(json!({"op":"shutdown"}));
    assert!(cache.is_dir());
    assert_eq!(fs::read_dir(&cache).unwrap().count(), 0);
    assert_eq!(fs::read(external).unwrap(), b"original user data");
    assert_eq!(library.game(id)["FileName"], "keep.wad");
    assert!(library.root.join("GameFiles/keep.wad").is_file());
    library.call(json!({"op":"update_settings","values":{"CleanTemp":"false"}}));
    fs::write(cache.join("retained.txt"), "retain cache").unwrap();
    library.call(json!({"op":"shutdown"}));
    assert_eq!(
        fs::read_to_string(cache.join("retained.txt")).unwrap(),
        "retain cache"
    );
}

#[test]
fn shutdown_preserves_extracted_inputs_for_an_interrupted_engine_journal() {
    let library = Library::new();
    let id = library.import(&library.wad("source/interrupted.wad", &["MAP01"]), false);
    let cache = library.root.join("GameFiles/Temp/extracted.wad");
    fs::write(&cache, wad(&["MAP02"])).unwrap();
    let mut session = journal(
        "77777777-7777-4777-8777-777777777777",
        id,
        "2026-02-02T12:00:00Z",
        5,
    );
    session["status"] = json!("running");
    session["finished_at"] = Value::Null;
    session["exit_code"] = Value::Null;
    session["statistics"] = json!([]);
    fs::write(
        library
            .root
            .join("Sessions")
            .join("77777777-7777-4777-8777-777777777777.json"),
        serde_json::to_vec(&session).unwrap(),
    )
    .unwrap();
    library.call(json!({"op":"shutdown"}));
    assert!(
        cache.is_file(),
        "A recovered journal cannot establish that the old engine no longer uses its extracted files"
    );
    assert_eq!(
        library.call(json!({"op":"sessions"}))["sessions"][0]["status"],
        "interrupted"
    );
    assert_eq!(library.count("HarvestedSessions"), 0);
}

#[test]
fn legacy_artwork_prefers_ordered_screenshots_then_title_picture_then_thumbnail() {
    let library = Library::new();
    let id = library.import(&library.wad("source/artwork.wad", &["MAP01"]), false);
    let fixtures = [
        ("TitlePics", "title.png", 6_i64, 0_i64),
        ("Screenshots", "later.png", 1, 5),
        ("Screenshots", "main.png", 1, 1),
        ("Thumbnails", "fallback.png", 4, 0),
    ];
    for (directory, name, kind, order) in fixtures {
        fs::write(
            library.root.join("GameFiles").join(directory).join(name),
            name.as_bytes(),
        )
        .unwrap();
        library.db().execute("INSERT INTO Files(GameFileID,FileName,DateCreated,FileTypeID,SourcePortID,FileOrder) VALUES(?1,?2,'2026-01-01',?3,-1,?4)",params![id,name,kind,order]).unwrap();
    }
    for relative in [
        "Screenshots/main.png",
        "Screenshots/later.png",
        "TitlePics/title.png",
        "Thumbnails/fallback.png",
    ] {
        let expected = library.root.join("GameFiles").join(relative);
        assert_eq!(library.game(id)["imagePath"], json!(expected));
        fs::remove_file(expected).unwrap();
    }
    assert_eq!(library.game(id)["imagePath"], Value::Null);
}

#[test]
fn metadata_resync_preserves_known_maps_when_resource_has_no_inspectable_maps() {
    let library = Library::new();
    for (name, bytes) in [
        ("opaque.pk7", b"7z\xbc\xafopaque engine resource".as_slice()),
        ("patch.deh", b"Patch File for DeHackEd v3.0\n".as_slice()),
    ] {
        let source = library.write(&format!("source/{name}"), bytes);
        let id = library.import(&source, false);
        library.db().execute("UPDATE GameFiles SET Map='MAP12, MAP13',MapCount=2,Title='Known title',Comments='Keep user notes' WHERE GameFileID=?1", [id]).unwrap();
        library.call(json!({"op":"resync_metadata","ids":[id]}));
        let game = library.game(id);
        assert_eq!(game["Map"], "MAP12, MAP13");
        assert_eq!(game["MapCount"], 2);
        assert_eq!(game["Title"], "Known title");
        assert_eq!(game["Comments"], "Keep user notes");
    }
}

#[test]
fn legacy_tab_selection_follows_tab_identity_across_navigation_changes() {
    // Expected indices are the visible Flutter tabs, independently enumerated
    // from the legacy selections. Hidden tags must not consume a tab position.
    for (visible, old_index, new_index) in [
        ("Recent;Untagged;IWads;Id Games", "0", "1"),
        ("Recent;Untagged;IWads;Id Games", "1", "0"),
        ("Recent;Untagged;IWads;Id Games", "2", "2"),
        ("Recent;Untagged;IWads;Id Games", "3", "3"),
        ("Recent;Untagged;IWads;Id Games", "4", "0"),
        ("Recent;Untagged;IWads;Id Games", "5", "4"),
        ("Recent;Untagged;IWads;Id Games", "6", "5"),
        ("Untagged;IWads;Id Games", "0", "0"),
        ("Untagged;IWads;Id Games", "4", "3"),
        ("Recent;IWads", "3", "3"),
        ("Id Games", "1", "0"),
        ("Id Games", "2", "1"),
        ("", "2", "2"),
        ("", "99", "0"),
        ("Recent", "-1", "0"),
    ] {
        let library = Library::new();
        let db = library.db();
        db.execute_batch("INSERT INTO Tags(TagID,Name,HasTab) VALUES(8,'Zeta',1),(15,'Alpha',1),(22,'Hidden',0); INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES('UnrelatedPreference','retain original','original;other',0)").unwrap();
        db.execute("INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES('VisibleViews',?1,'',1),('LastSelectedTabIndex',?2,'opaque legacy metadata',0)", params![visible,old_index]).unwrap();
        drop(db);
        let settings = library.snapshot()["settings"].clone();
        assert_eq!(
            settings["LastSelectedTabIndex"], new_index,
            "visible={visible}, old_index={old_index}"
        );
        assert_eq!(settings["UnrelatedPreference"], "retain original");
        assert_eq!(settings["VisibleViews"], visible);
        assert_eq!(
            library
                .db()
                .query_row(
                    "SELECT AvailableValues FROM Configuration WHERE Name='LastSelectedTabIndex'",
                    [],
                    |row| row.get::<_, String>(0)
                )
                .unwrap(),
            "opaque legacy metadata"
        );
        // Opening an already migrated database cannot translate the new index
        // again: Local and Recent would otherwise swap on every command.
        assert_eq!(
            library.snapshot()["settings"]["LastSelectedTabIndex"],
            new_index
        );
    }
}

#[test]
fn tab_migration_preserves_new_preferences_and_original_database_backup() {
    let library = Library::new();
    library.db().execute("INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES('LastSelectedTabIndex','0','',0)", []).unwrap();
    assert_eq!(library.snapshot()["settings"]["LastSelectedTabIndex"], "1");
    library.call(json!({"op":"update_settings","values":{"LastSelectedTabIndex":"0"}}));
    assert_eq!(library.snapshot()["settings"]["LastSelectedTabIndex"], "0");
    let backup = fs::read_dir(&library.root)
        .unwrap()
        .map(|entry| entry.unwrap().path())
        .find(|path| {
            path.file_name()
                .unwrap()
                .to_string_lossy()
                .starts_with("DoomLauncher.pre-flutter-")
        })
        .unwrap();
    let original = Connection::open(backup).unwrap();
    assert_eq!(
        original
            .query_row(
                "SELECT Value FROM Configuration WHERE Name='LastSelectedTabIndex'",
                [],
                |row| row.get::<_, String>(0)
            )
            .unwrap(),
        "0"
    );
    // A fresh Flutter library has no old tab order to translate.
    let fresh_root = library.directory.path().join("fresh library");
    let fresh = dispatch(
        serde_json::from_value::<Request>(json!({"op":"snapshot","data_root":fresh_root})).unwrap(),
    )
    .unwrap();
    assert_eq!(fresh["settings"]["LastSelectedTabIndex"], "0");
}

#[test]
fn game_identifiers_never_reuse_deleted_entries_or_their_retained_engine_data() {
    for fresh in [false, true] {
        let library = Library::new();
        if fresh {
            fs::remove_file(library.root.join("DoomLauncher.sqlite")).unwrap();
        }
        let first = library.import(&library.wad("source/first.wad", &["MAP01"]), false);
        let save = library
            .root
            .join(format!("GameFiles/SaveGames/{first}/1/doom0.dsg"));
        fs::create_dir_all(save.parent().unwrap()).unwrap();
        fs::write(&save, "save belonging only to the first game").unwrap();
        library.call(json!({"op":"delete_entry","id":first,"delete_file":false}));
        let second = library.import(&library.wad("source/second.wad", &["MAP02"]), false);
        assert!(second > first);
        assert_eq!(
            fs::read_to_string(&save).unwrap(),
            "save belonging only to the first game"
        );
        assert!(library.root.join("GameFiles/first.wad").is_file());
        library.call(json!({"op":"delete_entry","id":second,"delete_file":true}));
        let third = library.import(&library.wad("source/third.wad", &["MAP03"]), false);
        assert!(third > second);
        assert_eq!(library.game(third)["MinutesPlayed"], 0);
    }
}

#[test]
fn external_legacy_identifier_writes_advance_the_allocator_even_after_deletion() {
    let library = Library::new();
    library.snapshot();
    library.db().execute_batch("INSERT INTO GameFiles(GameFileID,FileName) VALUES(750,'external.wad'); DELETE FROM GameFiles WHERE GameFileID=750;").unwrap();
    let next = library.import(&library.wad("source/next.wad", &["MAP01"]), false);
    assert_eq!(next, 751);
    library
        .db()
        .execute(
            "UPDATE GameFiles SET GameFileID=900 WHERE GameFileID=?1",
            [next],
        )
        .unwrap();
    library.db().execute_batch("DELETE FROM GameFiles WHERE GameFileID=900; BEGIN; INSERT INTO GameFiles(GameFileID,FileName) VALUES(9000,'rolled-back.wad'); ROLLBACK;").unwrap();
    assert_eq!(
        library.import(&library.wad("source/after.wad", &["MAP02"]), false),
        901
    );
}

#[test]
fn rejected_import_rolls_back_identifier_reservation_with_database_and_files() {
    let library = Library::new();
    let first = library.import(&library.wad("source/first.wad", &["MAP01"]), false);
    let high_water = || {
        library
            .db()
            .query_row(
                "SELECT HighWater FROM CoreSequences WHERE Name='GameFiles'",
                [],
                |row| row.get::<_, i64>(0),
            )
            .unwrap()
    };
    assert_eq!(high_water(), first);
    library.db().execute_batch("CREATE TRIGGER deny_metadata BEFORE UPDATE ON GameFiles BEGIN SELECT RAISE(ABORT,'reject fixture metadata'); END;").unwrap();
    let incoming = library.wad("source/rejected.wad", &["MAP02"]);
    let result = library.call(json!({"op":"import_files","paths":[incoming]}));
    assert_eq!(result["failed"].as_array().unwrap().len(), 1);
    assert_eq!(library.count("GameFiles"), 1);
    assert_eq!(high_water(), first);
    assert!(!library.root.join("GameFiles/rejected.wad").exists());
    library
        .db()
        .execute_batch("DROP TRIGGER deny_metadata;")
        .unwrap();
    assert_eq!(library.import(&incoming, false), first + 1);
}

#[test]
fn allocator_migration_reserves_orphaned_save_directories_and_session_identifiers() {
    for (kind, retained_id) in [("SaveGames", 700), ("EngineData", 850), ("Session", 999)] {
        let library = Library::new();
        if kind == "Session" {
            let session = journal(
                "11111111-7777-4777-8777-777777777777",
                retained_id,
                "2026-01-01T12:00:00Z",
                5,
            );
            fs::create_dir_all(library.root.join("Sessions")).unwrap();
            fs::write(
                library
                    .root
                    .join("Sessions/11111111-7777-4777-8777-777777777777.json"),
                serde_json::to_vec(&session).unwrap(),
            )
            .unwrap();
        } else {
            let root = library.root.join("GameFiles").join(kind);
            fs::create_dir_all(root.join(retained_id.to_string()).join("1")).unwrap();
            fs::create_dir_all(root.join("non-numeric-data")).unwrap();
        }
        let imported = library.import(&library.wad("source/new.wad", &["MAP01"]), false);
        assert_eq!(imported, retained_id + 1, "seed source {kind}");
        assert_eq!(library.game(imported)["MinutesPlayed"], 0);
        assert_eq!(library.count("Stats"), 0);
    }
}

#[test]
fn session_harvest_compares_legacy_local_dates_and_rfc3339_instants_chronologically() {
    use chrono::{Local, TimeZone, Utc};
    let library = Library::new();
    let id = library.import(&library.wad("source/timestamps.wad", &["MAP01"]), false);
    let local_timestamp = |hour| {
        Local
            .with_ymd_and_hms(2026, 2, 2, hour, 0, 0)
            .latest()
            .unwrap()
            .with_timezone(&Utc)
            .to_rfc3339()
    };
    let earlier = local_timestamp(10);
    let later = local_timestamp(20);
    library
        .db()
        .execute(
            "UPDATE GameFiles SET LastPlayed='2026-02-02 18:00:00' WHERE GameFileID=?1",
            [id],
        )
        .unwrap();
    for (index, timestamp, expected) in [
        (1, earlier.as_str(), "2026-02-02 18:00:00"),
        (2, later.as_str(), later.as_str()),
    ] {
        let session_id = format!("88888888-0000-4000-8000-{index:012}");
        fs::write(
            library
                .root
                .join("Sessions")
                .join(format!("{session_id}.json")),
            serde_json::to_vec(&journal(&session_id, id, timestamp, 60)).unwrap(),
        )
        .unwrap();
        assert_eq!(library.game(id)["LastPlayed"], expected);
    }
    assert_eq!(library.game(id)["MinutesPlayed"], 2);
    // A larger calendar-date string with an explicit offset can be an earlier
    // instant. UTC normalization must win over either raw date representation.
    library
        .db()
        .execute(
            "UPDATE GameFiles SET LastPlayed='2026-02-02T22:00:00Z' WHERE GameFileID=?1",
            [id],
        )
        .unwrap();
    let session_id = "88888888-0000-4000-8000-000000000003";
    fs::write(
        library
            .root
            .join("Sessions")
            .join(format!("{session_id}.json")),
        serde_json::to_vec(&journal(session_id, id, "2026-02-03T06:30:00+14:00", 60)).unwrap(),
    )
    .unwrap();
    assert_eq!(library.game(id)["LastPlayed"], "2026-02-02T22:00:00Z");
    assert_eq!(library.game(id)["MinutesPlayed"], 3);
}

#[test]
fn session_harvest_preserves_unknown_dates_but_fills_empty_legacy_values() {
    for existing in [None, Some(""), Some("Imported date with unknown format")] {
        let library = Library::new();
        let id = library.import(&library.wad("source/timestamps.wad", &["MAP01"]), false);
        library
            .db()
            .execute(
                "UPDATE GameFiles SET LastPlayed=?2 WHERE GameFileID=?1",
                params![id, existing],
            )
            .unwrap();
        let session_id = "99999999-0000-4000-8000-000000000001";
        let incoming = "2026-02-02T20:00:00Z";
        fs::write(
            library
                .root
                .join("Sessions")
                .join(format!("{session_id}.json")),
            serde_json::to_vec(&journal(session_id, id, incoming, 60)).unwrap(),
        )
        .unwrap();
        let game = library.game(id);
        assert_eq!(
            game["LastPlayed"],
            existing
                .filter(|value| !value.is_empty())
                .unwrap_or(incoming)
        );
        assert_eq!(game["MinutesPlayed"], 1);
        assert_eq!(library.count("HarvestedSessions"), 1);
    }
}
