use crate::error::{Error, Result};
use chrono::Local;
use rusqlite::{Connection, OptionalExtension, params, types::ValueRef};
use serde_json::{Map, Value, json};
use std::{
    collections::HashSet,
    fs,
    path::{Path, PathBuf},
    time::Duration,
};

pub struct Store {
    pub root: PathBuf,
    pub db: Connection,
}
pub fn now() -> String {
    Local::now().format("%Y-%m-%d %H:%M:%S").to_string()
}
pub fn string(row: &Value, key: &str) -> String {
    row.get(key)
        .and_then(Value::as_str)
        .unwrap_or_default()
        .to_owned()
}
pub fn integer(row: &Value, key: &str) -> i64 {
    row.get(key)
        .and_then(|v| {
            v.as_i64()
                .or_else(|| v.as_str().and_then(|s| s.parse().ok()))
        })
        .unwrap_or_default()
}

// Existing Linux libraries may contain names that are not portable to Windows.
// Preserve access to those files; enforce portable names only on new writes.
pub fn managed_basename(name: &str) -> bool {
    !name.is_empty()
        && !matches!(name, "." | "..")
        && !name.contains(['/', '\\', '\0'])
        && !Path::new(name).is_absolute()
        && !(name.as_bytes().get(1) == Some(&b':') && name.as_bytes()[0].is_ascii_alphabetic())
}

impl Store {
    pub fn open(root: &Path) -> Result<Self> {
        static INITIALIZATION: std::sync::Mutex<()> = std::sync::Mutex::new(());
        let _initialization = INITIALIZATION.lock().map_err(|_| Error::new("internal", "Database initialization failed unexpectedly. Restart the launcher before retrying."))?;
        if !root.is_absolute() {
            return Err(Error::validation(
                "The library location must be an absolute path.",
            ));
        }
        fs::create_dir_all(root)?;
        for dir in [
            "GameFiles",
            "GameFiles/Screenshots",
            "GameFiles/Temp",
            "GameFiles/Demos",
            "GameFiles/SaveGames",
            "GameFiles/Thumbnails",
            "GameFiles/TitlePics",
            "Sessions",
            "Trash",
        ] {
            fs::create_dir_all(root.join(dir))?;
        }
        let path = root.join("DoomLauncher.sqlite");
        let existed = path.exists();
        let mut db = Connection::open(&path)?;
        db.busy_timeout(Duration::from_secs(10))?;
        let migrated:bool=db.query_row("SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='CoreMigrations')",[],|r|r.get(0))?;
        if !migrated {
            let check: String = db.query_row("PRAGMA quick_check", [], |r| r.get(0))?;
            if check != "ok" {
                return Err(Error::corrupt(format!(
                    "The library database needs repair: {check}. The original was not changed."
                )));
            }
            if existed {
                let backup = root.join(format!(
                    "DoomLauncher.pre-flutter-{}.sqlite",
                    uuid::Uuid::new_v4()
                ));
                let mut dest = Connection::open(backup)?;
                rusqlite::backup::Backup::new(&db, &mut dest)?.run_to_completion(
                    128,
                    Duration::from_millis(5),
                    None,
                )?;
            }
            let reference = Connection::open_in_memory()?;
            reference.execute_batch(include_str!("schema.sql"))?;
            let tx = db.transaction()?;
            tx.execute_batch(include_str!("schema.sql"))?;
            for table in [
                "GameFiles",
                "Files",
                "SourcePorts",
                "IWads",
                "Configuration",
                "Tags",
                "TagMapping",
                "Stats",
                "GameProfiles",
                "CleanupFiles",
            ] {
                let existing = columns(&tx, table)?;
                let mut query = reference.prepare(&format!("PRAGMA table_info({table})"))?;
                let desired = query.query_map([], |r| {
                    Ok((
                        r.get::<_, String>(1)?,
                        r.get::<_, String>(2)?,
                        r.get::<_, i64>(3)?,
                        r.get::<_, Option<String>>(4)?,
                        r.get::<_, i64>(5)?,
                    ))
                })?;
                for column in desired {
                    let (name, kind, not_null, default, primary) = column?;
                    if !existing.contains(&name) {
                        if primary > 0 {
                            return Err(Error::corrupt(format!(
                                "Legacy table {table} is missing its primary key {name}. Restore the database backup."
                            )));
                        }
                        let default = default.unwrap_or_else(|| {
                            if kind.to_lowercase().contains("int")
                                || kind.eq_ignore_ascii_case("real")
                            {
                                "0".into()
                            } else {
                                "''".into()
                            }
                        });
                        tx.execute_batch(&format!(
                            "ALTER TABLE {table} ADD COLUMN {name} {kind} {} DEFAULT {default}",
                            if not_null != 0 { "NOT NULL" } else { "" }
                        ))?;
                    }
                }
            }
            if existed {
                migrate_legacy_tab_selection(&tx)?;
            }
            tx.execute_batch("CREATE TABLE CoreMigrations (Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL); CREATE TABLE IF NOT EXISTS HarvestedSessions (SessionID TEXT PRIMARY KEY, RecordedAt TEXT NOT NULL); INSERT INTO CoreMigrations VALUES(1,datetime('now'));")?;
            tx.commit()?;
        }
        db.pragma_update(None, "journal_mode", "WAL")?;
        db.execute_batch("CREATE TABLE IF NOT EXISTS HarvestedSessions (SessionID TEXT PRIMARY KEY, RecordedAt TEXT NOT NULL); CREATE INDEX IF NOT EXISTS CoreGameFileName ON GameFiles(FileName COLLATE NOCASE);")?;
        initialize_game_file_sequence(&mut db, root)?;
        let store = Self {
            root: root.to_path_buf(),
            db,
        };
        store.seed()?;
        Ok(store)
    }
    fn seed(&self) -> Result<()> {
        for (name, value, available, modifiable) in [
            ("IdGamesUrl", "https://www.doomworld.com/idgames/", "", 1),
            ("ApiPage", "api/api.php", "", 1),
            (
                "MirrorUrl",
                "https://www.quaddicted.com/files/idgames/",
                "",
                1,
            ),
            ("DefaultSourcePort", "0", "", 0),
            ("DefaultIWad", "0", "", 0),
            ("ShowPlayDialog", "true", "true;false", 1),
            ("ColorThemeType", "System", "System;Default;Dark", 1),
            ("GameFileViewType", "GridView", "GridView;TileView", 1),
            ("VisibleViews", "Recent;Untagged;IWads;Id Games", "", 1),
            ("CleanTemp", "true", "", 0),
            ("LastSelectedTabIndex", "0", "", 0),
            ("AppWidth", "1100", "", 0),
            ("AppHeight", "720", "", 0),
        ] {
            self.db.execute("INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) SELECT ?1,?2,?3,?4 WHERE NOT EXISTS (SELECT 1 FROM Configuration WHERE Name=?1)",params![name,value,available,modifiable])?;
        }
        Ok(())
    }
    pub fn config(&self, key: &str, default: &str) -> Result<String> {
        Ok(self
            .db
            .query_row(
                "SELECT Value FROM Configuration WHERE Name=?1",
                [key],
                |r| r.get(0),
            )
            .optional()?
            .unwrap_or_else(|| default.into()))
    }
    pub fn config_bool(&self, key: &str, default: bool) -> Result<bool> {
        Ok(matches!(
            self.config(key, if default { "true" } else { "false" })?
                .to_lowercase()
                .as_str(),
            "true" | "1"
        ))
    }
    pub fn set_config(&self, key: &str, value: &str) -> Result<()> {
        if self.db.execute(
            "UPDATE Configuration SET Value=?2 WHERE Name=?1",
            params![key, value],
        )? == 0
        {
            self.db.execute("INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES(?1,?2,'',1)",params![key,value])?;
        }
        Ok(())
    }
    pub fn game(&self, id: i64) -> Result<Value> {
        self.rows("SELECT * FROM GameFiles WHERE GameFileID=?1", &[&id])?
            .into_iter()
            .next()
            .ok_or_else(|| Error::new("not_found", "This library entry no longer exists."))
    }
    // Call inside the same transaction/savepoint as the new game row. SQLite
    // INTEGER PRIMARY KEY alone reuses a deleted maximum ID, which would attach
    // retained saves and session journals to an unrelated subsequent import.
    pub fn allocate_game_file_id(&self) -> Result<i64> {
        self.db.query_row(
            "UPDATE CoreSequences SET HighWater=MAX(HighWater,COALESCE((SELECT MAX(GameFileID) FROM GameFiles),0))+1 WHERE Name='GameFiles' AND MAX(HighWater,COALESCE((SELECT MAX(GameFileID) FROM GameFiles),0))<9223372036854775807 RETURNING HighWater",
            [], |row| row.get(0),
        ).optional()?.ok_or_else(|| Error::new("capacity", "This library has exhausted its available game identifiers."))
    }
    pub fn game_by_name(&self, name: &str) -> Result<Option<Value>> {
        if let Some(row) = self.rows("SELECT * FROM GameFiles WHERE FileName=?1 COLLATE NOCASE ORDER BY GameFileID LIMIT 1", &[&name])?.into_iter().next() {
            return Ok(Some(row));
        }
        let folded = name.to_lowercase();
        let mut query = self
            .db
            .prepare("SELECT GameFileID,FileName FROM GameFiles ORDER BY GameFileID")?;
        let mut rows = query.query([])?;
        while let Some(row) = rows.next()? {
            let stored: String = row.get(1)?;
            if stored.to_lowercase() == folded {
                return Ok(Some(self.game(row.get(0)?)?));
            }
        }
        Ok(None)
    }
    pub fn game_path(&self, row: &Value) -> Result<PathBuf> {
        let name = string(row, "FileName");
        let path = PathBuf::from(&name);
        if path.is_absolute() {
            return Ok(path);
        }
        if !managed_basename(&name) {
            return Err(Error::validation(format!(
                "Unsupported managed filename: {name}"
            )));
        }
        Ok(self.root.join("GameFiles").join(path))
    }
    pub fn association_path(&self, row: &Value) -> Result<PathBuf> {
        let name = string(row, "FileName");
        if Path::new(&name).is_absolute() {
            return Ok(PathBuf::from(name));
        }
        if !managed_basename(&name) {
            return Err(Error::validation("Unsafe association filename."));
        }
        let dir = match integer(row, "FileTypeID") {
            1 => "Screenshots",
            2 => "Demos",
            3 => "SaveGames",
            4 => "Thumbnails",
            6 => "TitlePics",
            _ => "Screenshots",
        };
        Ok(self.root.join("GameFiles").join(dir).join(name))
    }
    pub fn rows(&self, sql: &str, values: &[&dyn rusqlite::ToSql]) -> Result<Vec<Value>> {
        rows(&self.db, sql, values)
    }
    pub fn insert_file(
        &self,
        game_id: i64,
        name: &str,
        kind: i64,
        description: &str,
    ) -> Result<i64> {
        self.db.execute("INSERT INTO Files(GameFileID,FileName,DateCreated,FileTypeID,SourcePortID,Description) VALUES(?1,?2,?3,?4,-1,?5)",params![game_id,name,now(),kind,description])?;
        Ok(self.db.last_insert_rowid())
    }
    pub fn snapshot_games(&self) -> Result<Vec<Value>> {
        let mut games = self.rows("SELECT * FROM GameFiles ORDER BY GameFileID", &[])?;
        let iwads: HashSet<i64> = self
            .rows("SELECT GameFileID FROM IWads", &[])?
            .iter()
            .map(|r| integer(r, "GameFileID"))
            .collect();
        let mut tags_by_game = std::collections::HashMap::<i64, Vec<i64>>::new();
        for mapping in self.rows("SELECT FileID,TagID FROM TagMapping", &[])? {
            tags_by_game
                .entry(integer(&mapping, "FileID"))
                .or_default()
                .push(integer(&mapping, "TagID"));
        }
        let mut images_by_game = std::collections::HashMap::<i64, Vec<PathBuf>>::new();
        // Preserve the existing screenshot/title/thumbnail priority and each
        // entry's FileOrder while visiting every association only once.
        for image in self.rows("SELECT * FROM Files WHERE FileTypeID IN(1,4,6) ORDER BY CASE FileTypeID WHEN 1 THEN 0 WHEN 6 THEN 1 ELSE 2 END,FileOrder,FileID",&[])? {
            if let Ok(path) = self.association_path(&image) {
                images_by_game
                    .entry(integer(&image, "GameFileID"))
                    .or_default()
                    .push(path);
            }
        }
        for row in &mut games {
            let id = integer(row, "GameFileID");
            let path = self.game_path(row);
            let path_error = match &path {
                Err(error) => Some(error.message.clone()),
                Ok(path) if !path.is_file() => {
                    Some("The original game file is unavailable on this computer.".to_string())
                }
                _ => None,
            };
            let image = images_by_game
                .remove(&id)
                .unwrap_or_default()
                .into_iter()
                .find(|p| p.is_file());
            let obj = row
                .as_object_mut()
                .ok_or_else(|| Error::corrupt("Invalid library record."))?;
            obj.insert("path".into(), json!(path.ok()));
            obj.insert("pathError".into(), json!(path_error));
            obj.insert("isIwad".into(), json!(iwads.contains(&id)));
            obj.insert("imagePath".into(), json!(image));
            obj.insert(
                "tagIds".into(),
                json!(tags_by_game.remove(&id).unwrap_or_default()),
            );
        }
        Ok(games)
    }
    pub fn ensure_defaults(&self) -> Result<()> {
        for (config, table, id_col, filter) in [
            (
                "DefaultSourcePort",
                "SourcePorts",
                "SourcePortID",
                " AND (Archived IS NULL OR Archived=0) AND (LaunchType IS NULL OR LaunchType='' OR LaunchType IN(0,2))",
            ),
            ("DefaultIWad", "IWads", "IWadID", ""),
        ] {
            let current = self.config(config, "0")?.parse::<i64>().unwrap_or(0);
            let count: i64 = self.db.query_row(
                &format!("SELECT count(*) FROM {table} WHERE {id_col}=?1{filter}"),
                [current],
                |r| r.get(0),
            )?;
            if count == 0 {
                let id:Option<i64>=self.db.query_row(&format!("SELECT {id_col} FROM {table} WHERE 1=1{filter} ORDER BY {id_col} LIMIT 1"),[],|r|r.get(0)).optional()?;
                self.set_config(config, &id.unwrap_or(0).to_string())?;
            }
        }
        Ok(())
    }
}

fn initialize_game_file_sequence(db: &mut Connection, root: &Path) -> Result<()> {
    let tx = db.transaction()?;
    tx.execute_batch("CREATE TABLE IF NOT EXISTS CoreSequences (Name TEXT PRIMARY KEY, HighWater INTEGER NOT NULL CHECK(typeof(HighWater)='integer' AND HighWater>=0));")?;
    let initialized: bool = tx.query_row(
        "SELECT EXISTS(SELECT 1 FROM CoreSequences WHERE Name='GameFiles')",
        [],
        |row| row.get(0),
    )?;
    if !initialized {
        let mut high_water = tx.query_row(
            "SELECT MAX(0,COALESCE(MAX(GameFileID),0)) FROM (SELECT GameFileID FROM GameFiles UNION ALL SELECT GameFileID FROM Files UNION ALL SELECT GameFileID FROM IWads UNION ALL SELECT GameFileID FROM Stats UNION ALL SELECT GameFileID FROM GameProfiles) WHERE typeof(GameFileID)='integer'",
            [], |row| row.get::<_, i64>(0),
        )?;
        // Earlier alpha builds already used game IDs for retained engine data.
        // Reserve these IDs even when their original game row has been deleted.
        for relative in ["GameFiles/SaveGames", "GameFiles/EngineData"] {
            let entries = match fs::read_dir(root.join(relative)) {
                Ok(entries) => entries,
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
                Err(error) => return Err(error.into()),
            };
            for entry in entries {
                if let Some(id) = entry?
                    .file_name()
                    .to_str()
                    .and_then(|name| name.parse::<i64>().ok())
                {
                    high_water = high_water.max(id);
                }
            }
        }
        for session in crate::runtime::session_history(root)? {
            high_water = high_water.max(session.game_file_id);
        }
        tx.execute(
            "INSERT INTO CoreSequences(Name,HighWater) VALUES('GameFiles',?1)",
            [high_water],
        )?;
    }
    // Track inserts made by legacy/external SQLite clients as well as this core.
    // The trigger and explicit allocator update participate in their caller's
    // transaction, so rejected imports cannot leave a partial reservation.
    tx.execute_batch("CREATE TRIGGER IF NOT EXISTS CoreGameFileInserted AFTER INSERT ON GameFiles BEGIN UPDATE CoreSequences SET HighWater=NEW.GameFileID WHERE Name='GameFiles' AND HighWater<NEW.GameFileID; END; CREATE TRIGGER IF NOT EXISTS CoreGameFileIDChanged AFTER UPDATE OF GameFileID ON GameFiles BEGIN UPDATE CoreSequences SET HighWater=NEW.GameFileID WHERE Name='GameFiles' AND HighWater<NEW.GameFileID; END; UPDATE CoreSequences SET HighWater=(SELECT MAX(GameFileID) FROM GameFiles) WHERE Name='GameFiles' AND HighWater<COALESCE((SELECT MAX(GameFileID) FROM GameFiles),0);")?;
    tx.commit()?;
    Ok(())
}

// Qt placed Recent before Local and included idgames among the library tabs.
// Translate by tab identity during the original-library migration only. Idgames
// is now a separate navigation destination, so its old selection returns Local.
fn migrate_legacy_tab_selection(db: &Connection) -> Result<()> {
    let configuration = |name: &str| -> Result<Option<String>> {
        Ok(db
            .query_row(
                "SELECT Value FROM Configuration WHERE Name=?1",
                [name],
                |r| r.get::<_, Option<String>>(0),
            )
            .optional()?
            .flatten())
    };
    let visible =
        configuration("VisibleViews")?.unwrap_or_else(|| "Recent;Untagged;IWads;Id Games".into());
    let enabled = |name| visible.split(';').any(|view| view == name);
    let mut old_tabs = Vec::new();
    let mut new_tabs = vec!["local".to_string()];
    if enabled("Recent") {
        old_tabs.push("recent".to_string());
        new_tabs.push("recent".to_string());
    }
    old_tabs.push("local".to_string());
    for (name, id) in [("Untagged", "untagged"), ("IWads", "iwads")] {
        if enabled(name) {
            old_tabs.push(id.to_string());
            new_tabs.push(id.to_string());
        }
    }
    if enabled("Id Games") {
        old_tabs.push("idgames".to_string());
    }
    let mut query =
        db.prepare("SELECT TagID FROM Tags WHERE HasTab<>0 ORDER BY Name COLLATE NOCASE")?;
    for id in query.query_map([], |row| row.get::<_, i64>(0))? {
        let key = format!("tag-{}", id?);
        old_tabs.push(key.clone());
        new_tabs.push(key);
    }
    let old_index = configuration("LastSelectedTabIndex")?
        .unwrap_or_else(|| "0".into())
        .parse::<usize>()
        .ok();
    let new_index = old_index
        .and_then(|index| old_tabs.get(index))
        .and_then(|selected| new_tabs.iter().position(|tab| tab == selected))
        .unwrap_or(0)
        .to_string();
    if db.execute(
        "UPDATE Configuration SET Value=?1 WHERE Name='LastSelectedTabIndex'",
        [&new_index],
    )? == 0
    {
        db.execute("INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES('LastSelectedTabIndex',?1,'',0)", [&new_index])?;
    }
    Ok(())
}
fn columns(db: &Connection, table: &str) -> Result<HashSet<String>> {
    let mut s = db.prepare(&format!("PRAGMA table_info({table})"))?;
    Ok(s.query_map([], |r| r.get(1))?
        .collect::<std::result::Result<_, _>>()?)
}
pub fn rows(db: &Connection, sql: &str, values: &[&dyn rusqlite::ToSql]) -> Result<Vec<Value>> {
    let mut statement = db.prepare(sql)?;
    let names = statement
        .column_names()
        .iter()
        .map(|s| s.to_string())
        .collect::<Vec<_>>();
    let mut cursor = statement.query(values)?;
    let mut result = Vec::new();
    while let Some(row) = cursor.next()? {
        let mut obj = Map::new();
        for (i, name) in names.iter().enumerate() {
            let value = match row.get_ref(i)? {
                ValueRef::Null => Value::Null,
                ValueRef::Integer(v) => json!(v),
                ValueRef::Real(v) => json!(v),
                ValueRef::Text(v) => json!(String::from_utf8_lossy(v)),
                ValueRef::Blob(_) => Value::Null,
            };
            obj.insert(name.clone(), value);
        }
        result.push(Value::Object(obj));
    }
    Ok(result)
}
