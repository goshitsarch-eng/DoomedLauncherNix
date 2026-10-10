use crate::{
    content, discovery,
    error::{Error, Result},
    runtime,
    storage::{Store, integer, now, string},
};
use rusqlite::params;
use serde::Deserialize;
use serde_json::{Map, Value, json};
use sha2::{Digest, Sha256};
use std::{
    collections::HashMap,
    fs::{self, File, OpenOptions},
    io::Read,
    path::{Path, PathBuf},
};

#[derive(Deserialize)]
pub struct Request {
    pub data_root: PathBuf,
    #[serde(flatten)]
    pub operation: Operation,
}
#[derive(Deserialize)]
#[serde(tag = "op", rename_all = "snake_case")]
pub enum Operation {
    Snapshot,
    ActiveJobs,
    Shutdown,
    ImportFiles {
        paths: Vec<PathBuf>,
        #[serde(default)]
        as_iwads: bool,
    },
    ImportDirectory {
        path: PathBuf,
        #[serde(default = "yes")]
        recursive: bool,
        #[serde(default)]
        as_iwads: bool,
    },
    EditEntry {
        id: i64,
        fields: Map<String, Value>,
        #[serde(default)]
        tag_ids: Vec<i64>,
    },
    RenameEntry {
        id: i64,
        name: String,
    },
    DeleteEntry {
        id: i64,
        #[serde(default)]
        delete_file: bool,
    },
    ResyncMetadata {
        ids: Vec<i64>,
    },
    EntryDetails {
        id: i64,
    },
    OpenText {
        id: i64,
        #[serde(default)]
        member: Option<String>,
    },
    ImportAssociations {
        id: i64,
        paths: Vec<PathBuf>,
    },
    DeleteAssociation {
        file_id: i64,
    },
    SaveTag {
        #[serde(default)]
        id: Option<i64>,
        name: String,
        #[serde(default)]
        has_tab: bool,
    },
    DeleteTag {
        id: i64,
    },
    SavePort {
        port: Value,
    },
    DeletePort {
        id: i64,
    },
    DetectPorts,
    UpdateSettings {
        values: HashMap<String, String>,
    },
    PlayDefaults {
        id: i64,
    },
    LaunchPreview {
        options: runtime::LaunchOptions,
    },
    Launch {
        options: runtime::LaunchOptions,
    },
    Sessions,
    StopSession {
        id: String,
    },
    ScanStores {
        #[serde(default)]
        roots: Vec<PathBuf>,
    },
    SearchIdgames {
        #[serde(default)]
        query: String,
        #[serde(rename = "type", default = "search_type")]
        kind: String,
        #[serde(default)]
        latest: bool,
    },
    StartDownload {
        directory: String,
        file_name: String,
    },
    DownloadStatus {
        id: String,
    },
    CancelDownload {
        id: String,
    },
    DiscardDownload {
        id: String,
    },
}
fn yes() -> bool {
    true
}
fn search_type() -> String {
    "title".into()
}

pub fn dispatch(request: Request) -> Result<Value> {
    // Coordinate filesystem/database edits across Dart worker isolates. SQLite
    // alone cannot protect a file move performed before the SQL transaction.
    static FILE_EDITS: std::sync::Mutex<()> = std::sync::Mutex::new(());
    let _file_edit = if matches!(
        &request.operation,
        Operation::ImportFiles { .. }
            | Operation::ImportDirectory { .. }
            | Operation::RenameEntry { .. }
            | Operation::DeleteEntry { .. }
            | Operation::ImportAssociations { .. }
            | Operation::DeleteAssociation { .. }
            | Operation::ResyncMetadata { .. }
            | Operation::Launch { .. }
            | Operation::LaunchPreview { .. }
            | Operation::Shutdown
    ) {
        Some(FILE_EDITS.lock().map_err(|_| Error::new("internal", "A prior file operation failed unexpectedly. Restart the launcher before editing files."))?)
    } else {
        None
    };
    let mut app = Store::open(&request.data_root)?;
    if matches!(
        &request.operation,
        Operation::RenameEntry { .. }
            | Operation::DeleteEntry { .. }
            | Operation::DeleteAssociation { .. }
    ) && runtime::session_history(&app.root)?
        .iter()
        .any(|s| s.status == runtime::SessionStatus::Running)
    {
        return Err(Error::new(
            "busy",
            "Stop active game sessions before renaming or removing library files.",
        ));
    }
    match request.operation {
        Operation::Snapshot => snapshot(&mut app),
        Operation::ActiveJobs => Ok(
            json!({"sessions":runtime::session_history(&app.root)?.into_iter().filter(|s|s.status==runtime::SessionStatus::Running).collect::<Vec<_>>(),"downloads":discovery::active_downloads()?}),
        ),
        Operation::Shutdown => {
            for session in runtime::session_history(&app.root)?
                .into_iter()
                .filter(|s| s.status == runtime::SessionStatus::Running)
            {
                runtime::stop_session(&session.id)?;
            }
            discovery::shutdown_downloads()?;
            let deadline = std::time::Instant::now() + std::time::Duration::from_secs(10);
            while runtime::session_history(&app.root)?
                .iter()
                .any(|s| s.status == runtime::SessionStatus::Running)
            {
                if std::time::Instant::now() >= deadline {
                    return Err(Error::new(
                        "busy",
                        "Some game sessions are still stopping. Wait a moment and try closing again.",
                    ));
                }
                std::thread::sleep(std::time::Duration::from_millis(50));
            }
            harvest(&mut app)?;
            // A journal interrupted by a previous app exit may still represent a
            // running engine. Preserve extracted inputs until that ambiguity is resolved.
            if app.config_bool("CleanTemp", true)?
                && !runtime::session_history(&app.root)?
                    .iter()
                    .any(|s| s.status == runtime::SessionStatus::Interrupted)
            {
                let cache = app.root.join("GameFiles/Temp");
                for entry in fs::read_dir(&cache)? {
                    let entry = entry?;
                    if entry.file_type()?.is_dir() {
                        fs::remove_dir_all(entry.path())?;
                    } else {
                        fs::remove_file(entry.path())?;
                    }
                }
            }
            Ok(json!({}))
        }
        Operation::ImportFiles { paths, as_iwads } => import_files(&mut app, &paths, as_iwads),
        Operation::ImportDirectory {
            path,
            recursive,
            as_iwads,
        } => {
            if !path.is_dir() {
                return Err(Error::validation("Choose an existing directory."));
            }
            let mut paths = Vec::new();
            let mut failures = Vec::new();
            let iter = walkdir::WalkDir::new(&path)
                .follow_links(false)
                .max_depth(if recursive { usize::MAX } else { 1 });
            for entry in iter {
                match entry {
                    Ok(e) if e.file_type().is_file() && supported(e.path()) => {
                        paths.push(e.into_path())
                    }
                    Ok(_) => (),
                    Err(e) => failures.push(json!({"path":e.path(),"message":e.to_string()})),
                }
                if paths.len() > 100_000 {
                    return Err(Error::validation(
                        "This directory contains more than 100,000 game files. Import smaller subdirectories.",
                    ));
                }
            }
            paths.sort();
            let mut result = import_files(&mut app, &paths, as_iwads)?;
            result["failed"]
                .as_array_mut()
                .ok_or_else(|| Error::new("internal", "Invalid import result"))?
                .extend(failures);
            Ok(result)
        }
        Operation::EditEntry {
            id,
            fields,
            tag_ids,
        } => {
            edit_entry(&mut app, id, fields, tag_ids)?;
            Ok(json!({}))
        }
        Operation::RenameEntry { id, name } => {
            rename(&mut app, id, &name)?;
            Ok(json!({}))
        }
        Operation::DeleteEntry { id, delete_file } => {
            delete_entry(&mut app, id, delete_file)?;
            Ok(json!({}))
        }
        Operation::ResyncMetadata { ids } => {
            for id in ids {
                let row = app.game(id)?;
                apply_metadata(&app, id, &app.game_path(&row)?)?;
            }
            Ok(json!({}))
        }
        Operation::EntryDetails { id } => {
            let game = app
                .snapshot_games()?
                .into_iter()
                .find(|g| integer(g, "GameFileID") == id)
                .ok_or_else(|| Error::new("not_found", "Entry not found"))?;
            let mut files = app.rows(
                "SELECT * FROM Files WHERE GameFileID=?1 ORDER BY DateCreated DESC",
                &[&id],
            )?;
            for file in &mut files {
                let path = app.association_path(file);
                file["pathError"] = json!(path.as_ref().err().map(|e| &e.message));
                file["path"] = json!(path.ok());
            }
            let text_result = app
                .game_path(&game)
                .and_then(|path| content::text_files(&path));
            let text_error = text_result.as_ref().err().map(|e| e.message.clone());
            let text_files = text_result.unwrap_or_default();
            Ok(
                json!({"game":game,"files":files,"stats":app.rows("SELECT * FROM Stats WHERE GameFileID=?1 ORDER BY RecordTime DESC,StatID DESC",&[&id])?,"text_files":text_files,"text_error":text_error}),
            )
        }
        Operation::OpenText { id, member } => {
            let row = app.game(id)?;
            let path = app.game_path(&row)?;
            let members = content::text_files(&path)?;
            let name = member.or_else(|| members.first().cloned()).ok_or_else(|| {
                Error::new("not_found", "No text file is associated with this entry.")
            })?;
            if !members.contains(&name) {
                return Err(Error::validation("Choose a text file from this entry."));
            }
            Ok(json!({"path":content::extract_text(&path,&name,&app.root.join("GameFiles/Temp"))?}))
        }
        Operation::ImportAssociations { id, paths } => import_associations(&app, id, &paths),
        Operation::DeleteAssociation { file_id } => {
            delete_association(&mut app, file_id)?;
            Ok(json!({}))
        }
        Operation::SaveTag { id, name, has_tab } => {
            let name = name.trim();
            if name.is_empty() || name.len() > 128 {
                return Err(Error::validation(
                    "Tag names must contain 1–128 characters.",
                ));
            }
            let duplicate: i64 = app.db.query_row(
                "SELECT count(*) FROM Tags WHERE Name=?1 COLLATE NOCASE AND TagID<>?2",
                params![name, id.unwrap_or(-1)],
                |r| r.get(0),
            )?;
            if duplicate > 0 {
                return Err(Error::validation("A tag with that name already exists."));
            }
            if let Some(id) = id.filter(|v| *v > 0) {
                if app.db.execute(
                    "UPDATE Tags SET Name=?2,HasTab=?3 WHERE TagID=?1",
                    params![id, name, has_tab],
                )? == 0
                {
                    return Err(Error::new("not_found", "Tag not found."));
                }
            } else {
                app.db.execute(
                    "INSERT INTO Tags(Name,HasTab) VALUES(?1,?2)",
                    params![name, has_tab],
                )?;
            }
            Ok(json!({}))
        }
        Operation::DeleteTag { id } => {
            let tx = app.db.transaction()?;
            tx.execute("DELETE FROM TagMapping WHERE TagID=?1", [id])?;
            tx.execute("DELETE FROM Tags WHERE TagID=?1", [id])?;
            tx.commit()?;
            Ok(json!({}))
        }
        Operation::SavePort { port } => {
            save_port(&app, port)?;
            app.ensure_defaults()?;
            Ok(json!({}))
        }
        Operation::DeletePort { id } => {
            app.db.execute(
                "UPDATE SourcePorts SET Archived=1 WHERE SourcePortID=?1",
                [id],
            )?;
            app.ensure_defaults()?;
            Ok(json!({}))
        }
        Operation::DetectPorts => {
            let mut added = 0;
            for port in runtime::detect_ports()? {
                let exists:i64=app.db.query_row("SELECT count(*) FROM SourcePorts WHERE Executable=?1 AND Directory=?2 AND (Archived IS NULL OR Archived=0)",params![port.executable,port.directory],|r|r.get(0))?;
                if exists == 0 {
                    save_port(&app, serde_json::to_value(port)?)?;
                    added += 1;
                }
            }
            app.ensure_defaults()?;
            Ok(json!({"added":added}))
        }
        Operation::UpdateSettings { values } => {
            for (key, value) in &values {
                validate_setting(key, value)?;
            }
            let tx = app.db.transaction()?;
            for (key, value) in values {
                if tx.execute(
                    "UPDATE Configuration SET Value=?2 WHERE Name=?1",
                    params![key, value],
                )? == 0
                {
                    tx.execute("INSERT INTO Configuration(Name,Value,AvailableValues,UserCanModify) VALUES(?1,?2,'',1)",params![key,value])?;
                }
            }
            tx.commit()?;
            Ok(json!({}))
        }
        Operation::PlayDefaults { id } => play_defaults(&app, id),
        Operation::LaunchPreview { options } => {
            let plan = launch_plan(&app, &options)?;
            let mut value = serde_json::to_value(&plan)?;
            value["display"] = json!(plan.display_command());
            Ok(value)
        }
        Operation::Launch { options } => {
            let plan = launch_plan(&app, &options)?;
            let session = runtime::start_session(
                plan,
                options.game_file_id,
                options.source_port_id,
                app.root.clone(),
            )?;
            if options.remember
                && let Err(error) = remember_launch(&app, &options)
            {
                runtime::stop_session(&session.id)?;
                return Err(Error::new(
                    "settings_save",
                    format!(
                        "The game was stopped because its launch settings could not be saved: {}",
                        error.message
                    ),
                ));
            }
            Ok(json!({"session":session}))
        }
        Operation::Sessions => {
            harvest(&mut app)?;
            Ok(json!({"sessions":runtime::session_history(&app.root)?}))
        }
        Operation::StopSession { id } => Ok(json!({"session":runtime::stop_session(&id)?})),
        Operation::ScanStores { roots } => {
            Ok(serde_json::to_value(discovery::scan_stores(&roots)?)?)
        }
        Operation::SearchIdgames {
            query,
            kind,
            latest,
        } => Ok(
            json!({"files":discovery::search_idgames(&app.config("IdGamesUrl","https://www.doomworld.com/idgames/")?,&app.config("ApiPage","api/api.php")?,&query,&kind,latest)?}),
        ),
        Operation::StartDownload {
            directory,
            file_name,
        } => Ok(
            json!({"download":discovery::start_download(&app.config("MirrorUrl","https://www.quaddicted.com/files/idgames/")?,&directory,&file_name,&app.root.join("GameFiles/Temp"))?}),
        ),
        Operation::DownloadStatus { id } => {
            Ok(json!({"download":discovery::download_status(&id)?}))
        }
        Operation::CancelDownload { id } => {
            Ok(json!({"download":discovery::cancel_download(&id)?}))
        }
        Operation::DiscardDownload { id } => {
            discovery::discard_download(&id)?;
            Ok(json!({}))
        }
    }
}
fn snapshot(app: &mut Store) -> Result<Value> {
    harvest(app)?;
    app.ensure_defaults()?;
    let mut settings = Map::new();
    for row in app.rows("SELECT Name,Value FROM Configuration", &[])? {
        settings.insert(string(&row, "Name"), row["Value"].clone());
    }
    let ports=app.rows("SELECT * FROM SourcePorts WHERE Archived IS NULL OR Archived=0 ORDER BY Name COLLATE NOCASE",&[])?.iter().map(port_value).collect::<Vec<_>>();
    let mut tags=app.rows("SELECT TagID AS tag_id,Name AS name,HasTab AS has_tab FROM Tags ORDER BY Name COLLATE NOCASE",&[])?;
    for tag in &mut tags {
        tag["has_tab"] = json!(integer(tag, "has_tab") != 0);
    }
    let iwads=app.rows("SELECT IWadID AS iwad_id,Name AS name,GameFileID AS game_file_id,FileName AS file_name FROM IWads ORDER BY Name COLLATE NOCASE",&[])?;
    Ok(
        json!({"games":app.snapshot_games()?,"ports":ports,"tags":tags,"iwads":iwads,"settings":settings,"sessions":runtime::session_history(&app.root)?,"data_root":app.root,"version":env!("CARGO_PKG_VERSION")}),
    )
}
fn supported(path: &Path) -> bool {
    matches!(
        path.extension()
            .and_then(|x| x.to_str())
            .unwrap_or("")
            .to_ascii_lowercase()
            .as_str(),
        "wad" | "iwad" | "zip" | "pk3" | "pk7" | "ipk3" | "deh" | "bex"
    )
}
fn hash(path: &Path) -> Result<Vec<u8>> {
    let mut file = File::open(path)?;
    let mut hash = Sha256::new();
    let mut buffer = [0u8; 65536];
    loop {
        let n = file.read(&mut buffer)?;
        if n == 0 {
            break;
        }
        hash.update(&buffer[..n]);
    }
    Ok(hash.finalize().to_vec())
}
fn import_files(app: &mut Store, paths: &[PathBuf], as_iwads: bool) -> Result<Value> {
    let mut added = Vec::new();
    let mut failed = Vec::new();
    for path in paths {
        match import_one(app, path, as_iwads) {
            Ok(name) => added.push(name),
            Err(e) => failed.push(json!({"path":path,"message":e.message})),
        }
    }
    app.ensure_defaults()?;
    Ok(json!({"added":added,"failed":failed}))
}
fn import_one(app: &mut Store, path: &Path, as_iwads: bool) -> Result<String> {
    if !path.is_file() || !supported(path) {
        return Err(Error::validation(
            "Choose a WAD, ZIP, PK3, PK7, IPK3, DEH or BEX game file.",
        ));
    }
    let name = path
        .file_name()
        .and_then(|s| s.to_str())
        .ok_or_else(|| Error::validation("The filename is not valid Unicode."))?;
    if !content::safe_filename(name) {
        return Err(Error::validation(
            "Rename the file to a portable filename without reserved characters.",
        ));
    }
    let existing = app.game_by_name(name)?;
    let actual_name = existing
        .as_ref()
        .map(|v| string(v, "FileName"))
        .unwrap_or_else(|| name.to_owned());
    let dest = app.root.join("GameFiles").join(&actual_name);
    if dest.exists() && (existing.is_none() || hash(path)? != hash(&dest)?) {
        return Err(Error::validation(format!(
            "A different file named {name} already exists in the library. Rename the incoming file before importing."
        )));
    }
    let metadata = content::inspect(path, &app.root.join("GameFiles/Temp"))?;
    let mut created = false;
    if !dest.exists() {
        let mut source = File::open(path)?;
        let mut target = OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&dest)?;
        created = true;
        if let Err(e) = std::io::copy(&mut source, &mut target).and_then(|_| target.sync_all()) {
            let _ = fs::remove_file(&dest);
            return Err(e.into());
        }
    }
    let mut created_art = None;
    let mut created_text = Vec::new();
    if let Err(error) = app.db.execute_batch("SAVEPOINT import_file") {
        if created {
            fs::remove_file(&dest)?;
        }
        return Err(error.into());
    }
    let operation = (|| -> Result<()> {
        // Loose idgames text accompanies the imported file, including after the
        // original download directory is removed. Never replace another text.
        if !matches!(
            path.extension()
                .and_then(|e| e.to_str())
                .unwrap_or("")
                .to_ascii_lowercase()
                .as_str(),
            "zip" | "pk3" | "ipk3"
        ) {
            for member in content::text_files(path)? {
                let source =
                    content::extract_text(path, &member, &app.root.join("GameFiles/Temp"))?;
                let target =
                    dest.with_extension(Path::new(&member).extension().unwrap_or_default());
                if target.exists() {
                    if hash(&source)? != hash(&target)? {
                        return Err(Error::validation(
                            "A different text sidecar already exists. Rename the incoming game and its text file before importing.",
                        ));
                    }
                } else {
                    let mut input = File::open(source)?;
                    let mut output = OpenOptions::new()
                        .write(true)
                        .create_new(true)
                        .open(&target)?;
                    created_text.push(target);
                    std::io::copy(&mut input, &mut output)?;
                    output.sync_all()?;
                }
            }
        }
        let id = if let Some(row) = &existing {
            integer(row, "GameFileID")
        } else {
            let id = app.allocate_game_file_id()?;
            app.db.execute("INSERT INTO GameFiles(GameFileID,FileName,Downloaded,SettingsStat,MinutesPlayed,Rating) VALUES(?1,?2,?3,1,0,0)",params![id,actual_name,now()])?;
            id
        };
        if as_iwads {
            app.db.execute("INSERT INTO IWads(Name,FileName,GameFileID) SELECT ?1,?2,?3 WHERE NOT EXISTS(SELECT 1 FROM IWads WHERE GameFileID=?3)",params![content::iwad_display_name(&actual_name),actual_name,id])?;
        }
        created_art = save_metadata(app, id, metadata)?;
        app.db.execute_batch("RELEASE import_file")?;
        Ok(())
    })();
    if let Err(error) = operation {
        app.db
            .execute_batch("ROLLBACK TO import_file; RELEASE import_file")?;
        if created {
            fs::remove_file(&dest)?;
        }
        if let Some(path) = created_art {
            fs::remove_file(path)?;
        }
        for path in created_text {
            fs::remove_file(path)?;
        }
        return Err(error);
    }
    Ok(actual_name)
}
fn apply_metadata(app: &Store, id: i64, path: &Path) -> Result<()> {
    let metadata = content::inspect(path, &app.root.join("GameFiles/Temp"))?;
    app.db.execute_batch("SAVEPOINT refresh_metadata")?;
    let mut created_art = None;
    let operation = (|| -> Result<()> {
        created_art = save_metadata(app, id, metadata)?;
        app.db.execute_batch("RELEASE refresh_metadata")?;
        Ok(())
    })();
    if let Err(error) = operation {
        app.db
            .execute_batch("ROLLBACK TO refresh_metadata; RELEASE refresh_metadata")?;
        if let Some(path) = created_art {
            fs::remove_file(path)?;
        }
        return Err(error);
    }
    Ok(())
}
fn save_metadata(app: &Store, id: i64, metadata: content::Metadata) -> Result<Option<PathBuf>> {
    for (field, value) in [
        ("Title", metadata.title),
        ("Author", metadata.author),
        ("Description", metadata.description),
        ("ReleaseDate", metadata.release_date),
    ] {
        if let Some(value) = value {
            app.db.execute(&format!("UPDATE GameFiles SET {field}=?2 WHERE GameFileID=?1 AND ({field} IS NULL OR {field}='')"),params![id,value])?;
        }
    }
    // Opaque engine formats (PK7, DEH/BEX) cannot prove that old map metadata
    // is obsolete. Match the legacy resync behavior and retain it if unknown.
    if !metadata.maps.is_empty() {
        app.db.execute(
            "UPDATE GameFiles SET Map=?2,MapCount=?3 WHERE GameFileID=?1",
            params![id, metadata.maps.join(", "), metadata.maps.len()],
        )?;
    }
    if app.config_bool("AutomaticallyPullTitlpic", true)?
        && let Some(pic) = metadata.title_picture
    {
        let exists = app
            .rows(
                "SELECT * FROM Files WHERE GameFileID=?1 AND FileTypeID=6",
                &[&id],
            )?
            .iter()
            .any(|r| app.association_path(r).is_ok_and(|p| p.exists()));
        if !exists {
            let name = format!("{}.png", uuid::Uuid::new_v4());
            let dest = app.root.join("GameFiles/TitlePics").join(&name);
            fs::copy(pic, &dest)?;
            if let Err(error) = app.insert_file(id, &name, 6, "TITLEPIC") {
                fs::remove_file(&dest)?;
                return Err(error);
            }
            return Ok(Some(dest));
        }
    }
    Ok(None)
}

fn edit_entry(app: &mut Store, id: i64, fields: Map<String, Value>, tags: Vec<i64>) -> Result<()> {
    app.game(id)?;
    for (key, value) in &fields {
        if ![
            "Title",
            "Author",
            "Description",
            "Rating",
            "Comments",
            "ReleaseDate",
        ]
        .contains(&key.as_str())
        {
            return Err(Error::validation(format!("Field {key} cannot be edited.")));
        }
        if key == "Rating" {
            if !value.as_i64().is_some_and(|v| (0..=5).contains(&v)) {
                return Err(Error::validation("Rating must be between 0 and 5."));
            }
        } else if !value.is_string() || value.as_str().unwrap_or("").len() > 1_048_576 {
            return Err(Error::validation("Text field is invalid or too large."));
        }
    }
    let tx = app.db.transaction()?;
    for (key, value) in fields {
        let v = if key == "Rating" {
            rusqlite::types::Value::Integer(value.as_i64().unwrap_or(0))
        } else {
            rusqlite::types::Value::Text(value.as_str().unwrap_or("").into())
        };
        tx.execute(
            &format!("UPDATE GameFiles SET {key}=?2 WHERE GameFileID=?1"),
            params![id, v],
        )?;
    }
    tx.execute("DELETE FROM TagMapping WHERE FileID=?1", [id])?;
    for tag in tags {
        let exists: bool = tx.query_row(
            "SELECT EXISTS(SELECT 1 FROM Tags WHERE TagID=?1)",
            [tag],
            |r| r.get(0),
        )?;
        if !exists {
            return Err(Error::validation("A selected tag no longer exists."));
        }
        tx.execute(
            "INSERT OR IGNORE INTO TagMapping(FileID,TagID) VALUES(?1,?2)",
            params![id, tag],
        )?;
    }
    tx.commit()?;
    Ok(())
}
fn rename(app: &mut Store, id: i64, name: &str) -> Result<()> {
    let row = app.game(id)?;
    let old_name = string(&row, "FileName");
    if !crate::storage::managed_basename(&old_name) || !content::safe_filename(name) {
        return Err(Error::validation(
            "Only managed files can be renamed. Use a portable filename without directory components.",
        ));
    }
    if name == old_name {
        return Ok(());
    }
    if let Some(other) = app.game_by_name(name)?
        && integer(&other, "GameFileID") != id
    {
        return Err(Error::validation(
            "A library entry with this name already exists.",
        ));
    }
    let old = app.game_path(&row)?;
    let dest = app.root.join("GameFiles").join(name);
    if dest.exists() && fs::canonicalize(&dest)? != fs::canonicalize(&old)? {
        return Err(Error::validation("A file with that name already exists."));
    }
    let loose = !matches!(
        old.extension()
            .and_then(|e| e.to_str())
            .unwrap_or("")
            .to_ascii_lowercase()
            .as_str(),
        "zip" | "pk3" | "ipk3"
    );
    let old_stem = old
        .file_stem()
        .unwrap_or_default()
        .to_string_lossy()
        .to_lowercase();
    let shared_text = app
        .rows(
            "SELECT FileName FROM GameFiles WHERE GameFileID<>?1",
            &[&id],
        )?
        .iter()
        .any(|row| {
            let name = string(row, "FileName");
            crate::storage::managed_basename(&name)
                && Path::new(&name)
                    .file_stem()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_lowercase()
                    == old_stem
        });
    let mut sidecars = Vec::new();
    if loose {
        for member in content::text_files(&old)? {
            let source = content::extract_text(&old, &member, &app.root.join("GameFiles/Temp"))?;
            let target = dest.with_extension(Path::new(&member).extension().unwrap_or_default());
            if target.exists() {
                if fs::canonicalize(&target)? == source {
                    continue;
                }
                return Err(Error::validation(
                    "A text file for the new name already exists. Choose another name.",
                ));
            }
            if target == dest {
                return Err(Error::validation(
                    "The new game filename conflicts with its text sidecar.",
                ));
            }
            sidecars.push((source, target));
        }
    }
    let mut copied_text = Vec::new();
    let mut staged_text = Vec::new();
    let mut game_renamed = false;
    let temporary = app
        .root
        .join("GameFiles")
        .join(format!(".rename-{}", uuid::Uuid::new_v4()));
    let result = (|| -> Result<()> {
        for (source, target) in &sidecars {
            let mut input = File::open(source)?;
            let mut output = OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(target)?;
            copied_text.push(target.clone());
            std::io::copy(&mut input, &mut output)?;
            output.sync_all()?;
            drop(output);
            if !shared_text {
                let staged = app
                    .root
                    .join("GameFiles")
                    .join(format!(".rename-text-{}", uuid::Uuid::new_v4()));
                fs::rename(source, &staged)?;
                staged_text.push((source.clone(), staged));
            }
        }
        fs::rename(&old, &temporary)?;
        if let Err(e) = fs::rename(&temporary, &dest) {
            fs::rename(&temporary, &old).map_err(|rollback| Error::new("recovery",format!("Rename failed ({e}) and recovery failed ({rollback}); the game is preserved at {}.",temporary.display())))?;
            return Err(e.into());
        }
        game_renamed = true;
        let tx = app.db.transaction()?;
        tx.execute(
            "UPDATE GameFiles SET FileName=?2 WHERE GameFileID=?1",
            params![id, name],
        )?;
        tx.execute(
            "UPDATE IWads SET FileName=?2 WHERE GameFileID=?1",
            params![id, name],
        )?;
        for (table, key) in [
            ("GameFiles", "GameFileID"),
            ("GameProfiles", "GameProfileID"),
            ("SourcePorts", "SourcePortID"),
        ] {
            let rows = crate::storage::rows(
                &tx,
                &format!("SELECT {key},SettingsFiles FROM {table}"),
                &[],
            )?;
            for row in rows {
                let value = string(&row, "SettingsFiles");
                let mut parts = parse_settings_files(&value)?;
                let mut changed = false;
                for part in &mut parts {
                    if *part == old_name {
                        *part = name.to_owned();
                        changed = true;
                    }
                }
                if changed {
                    let new = encode_settings_files(&parts)?;
                    tx.execute(
                        &format!("UPDATE {table} SET SettingsFiles=?2 WHERE {key}=?1"),
                        params![integer(&row, key), new],
                    )?;
                }
            }
        }
        tx.commit()?;
        Ok(())
    })();
    if let Err(error) = result {
        let mut recovery_errors = Vec::new();
        if game_renamed && let Err(e) = fs::rename(&dest, &old) {
            recovery_errors.push(e.to_string());
        }
        for path in copied_text.iter().rev() {
            if let Err(e) = fs::remove_file(path) {
                recovery_errors.push(e.to_string());
            }
        }
        for (source, staged) in staged_text.iter().rev() {
            if let Err(e) = fs::rename(staged, source) {
                recovery_errors.push(e.to_string());
            }
        }
        if !recovery_errors.is_empty() {
            return Err(Error::new(
                "recovery",
                format!(
                    "Rename failed ({error}); some files could not be restored: {}. Retained text backups: {:?}",
                    recovery_errors.join("; "),
                    staged_text
                ),
            ));
        }
        return Err(error);
    }
    for (_, staged) in staged_text {
        fs::remove_file(staged)?;
    }
    Ok(())
}
fn stage_file(path: &Path, trash: &Path, moved: &mut Vec<(PathBuf, PathBuf)>) -> Result<()> {
    if !path.exists() {
        return Ok(());
    }
    fs::create_dir_all(trash)?;
    let target = trash.join(format!(
        "{}-{}",
        uuid::Uuid::new_v4(),
        path.file_name().unwrap_or_default().to_string_lossy()
    ));
    fs::rename(path, &target)?;
    moved.push((path.into(), target));
    Ok(())
}
fn restore_files(moved: &[(PathBuf, PathBuf)]) -> Result<()> {
    for (source, trash) in moved.iter().rev() {
        fs::rename(trash, source)?;
    }
    Ok(())
}
fn delete_entry(app: &mut Store, id: i64, delete_file: bool) -> Result<()> {
    let row = app.game(id)?;
    let associations = app.rows("SELECT * FROM Files WHERE GameFileID=?1", &[&id])?;
    let trash = app
        .root
        .join("Trash")
        .join(uuid::Uuid::new_v4().to_string());
    let mut moved = Vec::new();
    if delete_file {
        let prepare = (|| -> Result<()> {
            let name = string(&row, "FileName");
            let others = app.rows("SELECT * FROM GameFiles WHERE GameFileID<>?1", &[&id])?;
            let shared = others
                .iter()
                .any(|other| string(other, "FileName").to_lowercase() == name.to_lowercase());
            if !shared && crate::storage::managed_basename(&name) {
                let game_path = app.game_path(&row)?;
                let stem = game_path
                    .file_stem()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_lowercase();
                let shared_text = others.iter().any(|other| {
                    let name = string(other, "FileName");
                    crate::storage::managed_basename(&name)
                        && Path::new(&name)
                            .file_stem()
                            .unwrap_or_default()
                            .to_string_lossy()
                            .to_lowercase()
                            == stem
                });
                let loose = !matches!(
                    game_path
                        .extension()
                        .and_then(|e| e.to_str())
                        .unwrap_or("")
                        .to_ascii_lowercase()
                        .as_str(),
                    "zip" | "pk3" | "ipk3"
                );
                if !shared_text && loose && game_path.is_file() {
                    for member in content::text_files(&game_path)? {
                        let text = content::extract_text(
                            &game_path,
                            &member,
                            &app.root.join("GameFiles/Temp"),
                        )?;
                        stage_file(&text, &trash, &mut moved)?;
                    }
                }
                stage_file(&game_path, &trash, &mut moved)?;
            }
            for item in &associations {
                let other:i64=app.db.query_row("SELECT count(*) FROM Files WHERE FileName=?1 AND FileTypeID=?2 AND GameFileID<>?3",params![string(item,"FileName"),integer(item,"FileTypeID"),id],|r|r.get(0))?;
                if other == 0 && crate::storage::managed_basename(&string(item, "FileName")) {
                    stage_file(&app.association_path(item)?, &trash, &mut moved)?;
                }
            }
            Ok(())
        })();
        if let Err(e) = prepare {
            restore_files(&moved)?;
            return Err(e);
        }
    }
    if !moved.is_empty() {
        let write_result = fs::write(
            trash.join("recovery.json"),
            serde_json::to_vec_pretty(
                &json!({"game":row,"associations":associations,"moved":moved}),
            )?,
        );
        if let Err(error) = write_result {
            restore_files(&moved)?;
            return Err(error.into());
        }
    }
    let result = (|| -> Result<()> {
        let tx = app.db.transaction()?;
        for (table, key) in [
            ("TagMapping", "FileID"),
            ("Files", "GameFileID"),
            ("Stats", "GameFileID"),
            ("IWads", "GameFileID"),
            ("GameProfiles", "GameFileID"),
            ("GameFiles", "GameFileID"),
        ] {
            tx.execute(&format!("DELETE FROM {table} WHERE {key}=?1"), [id])?;
        }
        tx.commit()?;
        Ok(())
    })();
    if let Err(e) = result {
        restore_files(&moved)?;
        return Err(e);
    }
    app.ensure_defaults()?;
    Ok(())
}
fn import_associations(app: &Store, id: i64, paths: &[PathBuf]) -> Result<Value> {
    app.game(id)?;
    let mut added = Vec::new();
    let mut failed = Vec::new();
    for path in paths {
        let result = (|| -> Result<String> {
            if !path.is_file() {
                return Err(Error::validation("Choose an existing association file."));
            }
            let original = path
                .file_name()
                .and_then(|s| s.to_str())
                .ok_or_else(|| Error::validation("Invalid filename."))?;
            if !content::safe_filename(original) {
                return Err(Error::validation(
                    "Rename the association to a portable filename.",
                ));
            }
            let ext = path
                .extension()
                .and_then(|s| s.to_str())
                .unwrap_or("")
                .to_lowercase();
            let (kind, dir) = match ext.as_str() {
                "png" | "jpg" | "jpeg" | "bmp" | "gif" | "webp" => (1, "Screenshots"),
                "lmp" | "demo" => (2, "Demos"),
                "zds" | "dsg" | "esg" | "hsg" | "sav" | "save" => (3, "SaveGames"),
                _ => {
                    return Err(Error::validation(
                        "Choose a screenshot, demo (.lmp), or game save.",
                    ));
                }
            };
            let directory = app.root.join("GameFiles").join(dir);
            let mut name = original.to_owned();
            if directory.join(&name).exists() {
                name = format!("{}-{}", uuid::Uuid::new_v4(), original)
            }
            let dest = directory.join(&name);
            let mut source = File::open(path)?;
            let mut output = OpenOptions::new()
                .create_new(true)
                .write(true)
                .open(&dest)?;
            if let Err(e) = std::io::copy(&mut source, &mut output).and_then(|_| output.sync_all())
            {
                let _ = fs::remove_file(&dest);
                return Err(e.into());
            }
            if let Err(e) = app.insert_file(id, &name, kind, original) {
                let _ = fs::remove_file(&dest);
                return Err(e);
            }
            Ok(name)
        })();
        match result {
            Ok(name) => added.push(name),
            Err(e) => failed.push(json!({"path":path,"message":e.message})),
        }
    }
    Ok(json!({"added":added,"failed":failed}))
}
fn delete_association(app: &mut Store, id: i64) -> Result<()> {
    let row = app
        .rows("SELECT * FROM Files WHERE FileID=?1", &[&id])?
        .into_iter()
        .next()
        .ok_or_else(|| Error::new("not_found", "Association no longer exists."))?;
    let shared: i64 = app.db.query_row(
        "SELECT count(*) FROM Files WHERE FileID<>?1 AND FileName=?2 AND FileTypeID=?3",
        params![id, string(&row, "FileName"), integer(&row, "FileTypeID")],
        |r| r.get(0),
    )?;
    let trash = app
        .root
        .join("Trash")
        .join(uuid::Uuid::new_v4().to_string());
    let mut moved = Vec::new();
    if shared == 0 && crate::storage::managed_basename(&string(&row, "FileName")) {
        stage_file(&app.association_path(&row)?, &trash, &mut moved)?;
    }
    if let Err(e) = app.db.execute("DELETE FROM Files WHERE FileID=?1", [id]) {
        restore_files(&moved)?;
        return Err(e.into());
    }
    Ok(())
}
fn port_value(row: &Value) -> Value {
    json!({"id":integer(row,"SourcePortID"),"name":string(row,"Name"),"executable":string(row,"Executable"),"directory":string(row,"Directory"),"supported_extensions":string(row,"SupportedExtensions"),"file_option":if row["FileOption"].is_null(){"-file".into()}else{string(row,"FileOption")},"extra_parameters":string(row,"ExtraParameters"),"alt_save_directory":string(row,"AltSaveDirectory"),"launch_type":integer(row,"LaunchType")})
}
fn save_port(app: &Store, port: Value) -> Result<()> {
    let port: runtime::Port = serde_json::from_value(port)?;
    if port.name.trim().is_empty() || port.executable.trim().is_empty() {
        return Err(Error::validation(
            "A source port needs a name and executable.",
        ));
    }
    if port.name.len() > 256
        || port.executable.len() > 32768
        || !(0..=2).contains(&port.launch_type)
    {
        return Err(Error::validation("Invalid source port fields."));
    }
    for value in [
        &port.name,
        &port.executable,
        &port.directory,
        &port.supported_extensions,
        &port.file_option,
        &port.extra_parameters,
        &port.alt_save_directory,
    ] {
        if value.contains('\0') {
            return Err(Error::validation(
                "Source port fields cannot contain NUL characters.",
            ));
        }
    }
    if !port.directory.is_empty() && !Path::new(&port.directory).is_absolute() {
        return Err(Error::validation(
            "The source port directory must be an absolute path.",
        ));
    }
    if !port.alt_save_directory.is_empty() && !Path::new(&port.alt_save_directory).is_absolute() {
        return Err(Error::validation(
            "The save directory must be an absolute path.",
        ));
    }
    if port.id > 0 {
        if app.db.execute("UPDATE SourcePorts SET Name=?2,Executable=?3,Directory=?4,SupportedExtensions=?5,FileOption=?6,ExtraParameters=?7,AltSaveDirectory=?8,LaunchType=?9,Archived=0 WHERE SourcePortID=?1",params![port.id,port.name.trim(),port.executable.trim(),port.directory,port.supported_extensions,port.file_option,port.extra_parameters,port.alt_save_directory,port.launch_type])?==0{return Err(Error::new("not_found","Source port no longer exists."))}
    } else {
        app.db.execute("INSERT INTO SourcePorts(Name,Executable,Directory,SupportedExtensions,FileOption,ExtraParameters,AltSaveDirectory,LaunchType,Archived) VALUES(?1,?2,?3,?4,?5,?6,?7,?8,0)",params![port.name.trim(),port.executable.trim(),port.directory,port.supported_extensions,port.file_option,port.extra_parameters,port.alt_save_directory,port.launch_type])?;
    }
    Ok(())
}
fn validate_setting(key: &str, value: &str) -> Result<()> {
    if key.is_empty()
        || key.len() > 128
        || value.len() > 65536
        || key.contains('\0')
        || value.contains('\0')
    {
        return Err(Error::validation("Invalid preference name or value."));
    }
    match key {
        "ColorThemeType" if !["System", "Default", "Dark"].contains(&value) => {
            return Err(Error::validation("Choose System, Light or Dark."));
        }
        "GameFileViewType" if !["GridView", "TileView"].contains(&value) => {
            return Err(Error::validation("Choose list or tile view."));
        }
        "ShowPlayDialog" | "CleanTemp" | "AutomaticallyPullTitlpic"
            if !["true", "false"].contains(&value) =>
        {
            return Err(Error::validation("This preference must be true or false."));
        }
        "AppWidth" | "AppHeight"
            if !value
                .parse::<u32>()
                .is_ok_and(|v| (300..=16384).contains(&v)) =>
        {
            return Err(Error::validation(
                "Window size is outside supported bounds.",
            ));
        }
        "DefaultSourcePort" | "DefaultIWad" | "LastSelectedTabIndex"
            if value.parse::<u32>().is_err() =>
        {
            return Err(Error::validation(
                "This preference must be a nonnegative ID.",
            ));
        }
        "IdGamesUrl" | "MirrorUrl" => {
            let u = url::Url::parse(value)
                .map_err(|_| Error::validation("Enter a valid HTTP or HTTPS URL."))?;
            if !["http", "https"].contains(&u.scheme())
                || u.host_str().is_none()
                || !u.username().is_empty()
                || u.password().is_some()
            {
                return Err(Error::validation(
                    "Use an HTTP or HTTPS URL without embedded credentials.",
                ));
            }
        }
        "ApiPage"
            if value.starts_with('/')
                || value.contains("://")
                || value.split('/').any(|s| s == "..") =>
        {
            return Err(Error::validation(
                "API path must be relative to the archive URL.",
            ));
        }
        _ => (),
    }
    Ok(())
}
// A slash cannot occur in a managed basename, so this versioned marker cannot
// be confused with a legacy additional-file name. Keep simple lists in their
// original format and use JSON only when a filename makes `;` ambiguous.
const SETTINGS_FILES_JSON_PREFIX: &str = "@DoomLauncher/SettingsFiles/v1:";

fn parse_settings_files(value: &str) -> Result<Vec<String>> {
    if let Some(encoded) = value.strip_prefix(SETTINGS_FILES_JSON_PREFIX) {
        serde_json::from_str(encoded)
            .map_err(|_| Error::corrupt("The remembered additional-file list is invalid."))
    } else {
        Ok(value
            .split(';')
            .filter(|name| !name.is_empty())
            .map(str::to_owned)
            .collect())
    }
}

fn encode_settings_files(files: &[String]) -> Result<String> {
    let legacy = files.join(";");
    if files
        .iter()
        .any(|name| name.is_empty() || name.contains(';'))
        || legacy.starts_with(SETTINGS_FILES_JSON_PREFIX)
    {
        Ok(format!(
            "{SETTINGS_FILES_JSON_PREFIX}{}",
            serde_json::to_string(files)?
        ))
    } else {
        Ok(legacy)
    }
}

fn play_defaults(app: &Store, id: i64) -> Result<Value> {
    let row = app.game(id)?;
    app.ensure_defaults()?;
    let stored_port = integer(&row, "SourcePortID");
    let valid_port:bool=app.db.query_row("SELECT EXISTS(SELECT 1 FROM SourcePorts WHERE SourcePortID=?1 AND (Archived IS NULL OR Archived=0))",[stored_port],|r|r.get(0))?;
    let port = if stored_port > 0 && valid_port {
        stored_port
    } else {
        app.config("DefaultSourcePort", "0")?.parse().unwrap_or(0)
    };
    let stored_iwad = integer(&row, "IWadID");
    let valid_iwad: bool = app.db.query_row(
        "SELECT EXISTS(SELECT 1 FROM IWads WHERE IWadID=?1)",
        [stored_iwad],
        |r| r.get(0),
    )?;
    let mut iwad = if stored_iwad > 0 && valid_iwad {
        stored_iwad
    } else {
        app.config("DefaultIWad", "0")?.parse().unwrap_or(0)
    };
    if let Some(own) = app
        .rows("SELECT IWadID FROM IWads WHERE GameFileID=?1", &[&id])?
        .first()
    {
        iwad = integer(own, "IWadID");
    }
    let mut options = runtime::LaunchOptions {
        game_file_id: id,
        source_port_id: port,
        iwad_id: if iwad > 0 { Some(iwad) } else { None },
        map: string(&row, "SettingsMap"),
        skill: string(&row, "SettingsSkill").parse().ok(),
        extra_params: string(&row, "SettingsExtraParams"),
        extra_params_only: integer(&row, "SettingsExtraParamsOnly") != 0,
        load_latest_save: integer(&row, "SettingsLoadLatestSave") != 0,
        save_statistics: row["SettingsStat"].is_null() || integer(&row, "SettingsStat") != 0,
        record_demo: false,
        play_demo_file: None,
        additional_files: parse_settings_files(&string(&row, "SettingsFiles"))?,
        remember: true,
    };
    let standalone = app
        .rows(
            "SELECT LaunchType FROM SourcePorts WHERE SourcePortID=?1",
            &[&port],
        )?
        .first()
        .is_some_and(|row| matches!(integer(row, "LaunchType"), 1 | 2));
    if standalone {
        options.extra_params_only = true;
        options.iwad_id = None;
        options.map.clear();
        options.skill = None;
        options.load_latest_save = false;
        options.save_statistics = false;
        options.additional_files.clear();
    }
    Ok(
        json!({"maps":string(&row,"Map").split(',').map(str::trim).filter(|s|!s.is_empty()).collect::<Vec<_>>(),"additional_files":options.additional_files,"options":options}),
    )
}
fn launch_plan(app: &Store, options: &runtime::LaunchOptions) -> Result<runtime::LaunchPlan> {
    let game = app.game(options.game_file_id)?;
    let port = app
        .rows(
            "SELECT * FROM SourcePorts WHERE SourcePortID=?1 AND (Archived IS NULL OR Archived=0)",
            &[&options.source_port_id],
        )?
        .into_iter()
        .next()
        .ok_or_else(|| Error::validation("Select an available source port."))?;
    let mut port: runtime::Port = serde_json::from_value(port_value(&port))?;
    // Previous libraries store some source-port directories relative to data root.
    for path in [&mut port.directory, &mut port.alt_save_directory] {
        if !path.is_empty() && !Path::new(path).is_absolute() {
            *path = app.root.join(&*path).to_string_lossy().into_owned();
        }
    }
    if options.extra_params_only || port.launch_type == 2 {
        return runtime::build_plan_paths(&port, &[], None, &[], options, &app.root);
    }
    let extensions = port
        .supported_extensions
        .split(',')
        .map(|s| s.trim().trim_start_matches('.').to_lowercase())
        .filter(|s| !s.is_empty())
        .collect::<Vec<_>>();
    let specific = string(&game, "SettingsSpecificFiles")
        .split(';')
        .filter(|s| !s.is_empty())
        .map(str::to_owned)
        .collect::<Vec<_>>();
    let cache = app.root.join("GameFiles/Temp");
    let game_path = app.game_path(&game)?;
    let selected = if options.extra_params_only || port.launch_type == 2 {
        vec![game_path.clone()]
    } else {
        content::prepare_files(&game_path, &extensions, &specific, &cache)?
    };
    let self_iwad: bool = app.db.query_row(
        "SELECT EXISTS(SELECT 1 FROM IWads WHERE GameFileID=?1)",
        [options.game_file_id],
        |r| r.get(0),
    )?;
    let iwad_path = if self_iwad {
        selected.first().cloned()
    } else if let Some(id) = options.iwad_id.filter(|id| *id > 0) {
        let row=app.rows("SELECT GameFiles.* FROM GameFiles JOIN IWads ON IWads.GameFileID=GameFiles.GameFileID WHERE IWads.IWadID=?1",&[&id])?.into_iter().next().ok_or_else(||Error::validation("Selected IWAD no longer exists."))?;
        let path = app.game_path(&row)?;
        content::prepare_files(
            &path,
            &["wad".into(), "iwad".into(), "ipk3".into()],
            &[],
            &cache,
        )?
        .first()
        .cloned()
    } else {
        None
    };
    let mut additional = Vec::new();
    for name in &options.additional_files {
        let row = app.game_by_name(name)?.ok_or_else(|| {
            Error::new(
                "not_found",
                format!("Additional file {name} is missing from the library."),
            )
        })?;
        additional.extend(content::prepare_files(
            &app.game_path(&row)?,
            &extensions,
            &[],
            &cache,
        )?);
    }
    if let Some(demo) = &options.play_demo_file {
        let candidate = fs::canonicalize(demo)?;
        let files = app.rows(
            "SELECT * FROM Files WHERE GameFileID=?1 AND FileTypeID=2",
            &[&options.game_file_id],
        )?;
        if !files.iter().any(|f| {
            app.association_path(f)
                .and_then(|p| fs::canonicalize(p).map_err(Into::into))
                .is_ok_and(|p| p == candidate)
        }) {
            return Err(Error::validation(
                "This demo is not associated with the selected entry.",
            ));
        }
    }
    runtime::build_plan_paths(
        &port,
        &selected,
        iwad_path.as_deref(),
        &additional,
        options,
        &app.root,
    )
}
fn remember_launch(app: &Store, o: &runtime::LaunchOptions) -> Result<()> {
    let files = encode_settings_files(&o.additional_files)?;
    app.db.execute("UPDATE GameFiles SET SourcePortID=?2,IWadID=?3,SettingsMap=?4,SettingsSkill=?5,SettingsExtraParams=?6,SettingsExtraParamsOnly=?7,SettingsLoadLatestSave=?8,SettingsStat=?9,SettingsFiles=?10,SettingsSaved=1 WHERE GameFileID=?1",params![o.game_file_id,o.source_port_id,o.iwad_id,o.map,o.skill.map(|v|v.to_string()).unwrap_or_default(),o.extra_params,o.extra_params_only,o.load_latest_save,o.save_statistics,files])?;
    Ok(())
}

fn played_timestamp(value: &str) -> Option<chrono::DateTime<chrono::Utc>> {
    use chrono::{DateTime, Local, NaiveDate, NaiveDateTime, Utc};
    let value = value.trim();
    if let Ok(date) = DateTime::parse_from_rfc3339(value) {
        return Some(date.with_timezone(&Utc));
    }
    let local = ["%Y-%m-%d %H:%M:%S%.f", "%Y-%m-%dT%H:%M:%S%.f"]
        .iter()
        .find_map(|format| NaiveDateTime::parse_from_str(value, format).ok())
        .or_else(|| {
            NaiveDate::parse_from_str(value, "%Y-%m-%d")
                .ok()?
                .and_hms_opt(0, 0, 0)
        })?;
    // Legacy timestamps had no offset and represented local wall time. For a
    // repeated DST hour, use its later occurrence to avoid moving backwards.
    local
        .and_local_timezone(Local)
        .latest()
        .map(|date| date.with_timezone(&Utc))
}

fn should_update_last_played(existing: Option<&str>, incoming: &str) -> bool {
    let Some(incoming) = played_timestamp(incoming) else {
        return false;
    };
    match existing.map(str::trim).filter(|value| !value.is_empty()) {
        None => true,
        // Preserve an unparseable nonempty legacy value rather than replacing
        // user data based on a guessed order. The new journal remains available.
        Some(existing) => played_timestamp(existing).is_some_and(|old| incoming > old),
    }
}

fn harvest(app: &mut Store) -> Result<()> {
    for session in runtime::session_history(&app.root)? {
        if session.finished_at.is_none() {
            continue;
        }
        let id = &session.id;
        let tx = app.db.transaction()?;
        if tx.execute(
            "INSERT OR IGNORE INTO HarvestedSessions(SessionID,RecordedAt) VALUES(?1,?2)",
            params![id, now()],
        )? == 0
        {
            continue;
        }
        let exists: bool = tx.query_row(
            "SELECT EXISTS(SELECT 1 FROM GameFiles WHERE GameFileID=?1)",
            [session.game_file_id],
            |r| r.get(0),
        )?;
        if exists {
            let minutes = (session.elapsed_seconds / 60) as i64;
            let old: Option<String> = tx.query_row(
                "SELECT LastPlayed FROM GameFiles WHERE GameFileID=?1",
                [session.game_file_id],
                |row| row.get(0),
            )?;
            let update_date = should_update_last_played(old.as_deref(), &session.started_at);
            tx.execute("UPDATE GameFiles SET LastPlayed=CASE WHEN ?2 THEN ?3 ELSE LastPlayed END,MinutesPlayed=COALESCE(MinutesPlayed,0)+?4 WHERE GameFileID=?1",params![session.game_file_id,update_date,session.started_at,minutes])?;
            if matches!(session.status, runtime::SessionStatus::Completed) {
                for stat in session.statistics {
                    tx.execute("INSERT INTO Stats(GameFileID,KillCount,TotalKills,SecretCount,TotalSecrets,LevelTime,ItemCount,TotalItems,SourcePortID,MapName,RecordTime,Skill) VALUES(?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12)",params![session.game_file_id,stat.kill_count,stat.total_kills,stat.secret_count,stat.total_secrets,stat.level_time,stat.item_count,stat.total_items,session.source_port_id,stat.map_name,session.finished_at,stat.skill])?;
                }
            }
            if let Some(demo) = session.demo_path {
                let path = Path::new(&demo);
                if path.is_file()
                    && let Some(name) = path.file_name().and_then(|s| s.to_str())
                {
                    tx.execute("INSERT INTO Files(GameFileID,FileName,DateCreated,FileTypeID,SourcePortID,Description) VALUES(?1,?2,?3,2,?4,'Recorded demo')",params![session.game_file_id,name,now(),session.source_port_id])?;
                }
            }
        }
        tx.commit()?;
    }
    Ok(())
}
