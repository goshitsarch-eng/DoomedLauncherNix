use super::*;
use std::io::{BufRead, BufReader};
use std::net::{TcpListener, TcpStream};
use std::sync::mpsc;
use std::thread;
use tempfile::TempDir;

fn fixture(body: &[u8], status: &str, headers: &str) -> (String, mpsc::Receiver<String>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let address = format!("http://{}/", listener.local_addr().unwrap());
    let body = body.to_vec();
    let status = status.to_owned();
    let headers = headers.to_owned();
    let (sender, receiver) = mpsc::channel();
    thread::spawn(move || {
        let (mut stream, _) = listener.accept().unwrap();
        stream
            .set_read_timeout(Some(Duration::from_secs(5)))
            .unwrap();
        let request = read_request(&mut stream);
        sender.send(request).ok();
        write!(
            stream,
            "HTTP/1.1 {status}\r\nContent-Length: {}\r\nConnection: close\r\n{headers}\r\n",
            body.len()
        )
        .ok();
        stream.write_all(&body).ok();
    });
    (address, receiver)
}

fn read_request(stream: &mut TcpStream) -> String {
    let mut result = String::new();
    let mut reader = BufReader::new(stream);
    loop {
        let mut line = String::new();
        if reader.read_line(&mut line).unwrap() == 0 || line == "\r\n" {
            break;
        }
        result.push_str(&line);
    }
    result
}

fn terminal(id: &str) -> DownloadStatus {
    let start = Instant::now();
    loop {
        let state = download_status(id).unwrap();
        if !active(&state.status) {
            return state;
        }
        assert!(
            start.elapsed() < Duration::from_secs(8),
            "Download timed out: {state:?}"
        );
        thread::sleep(Duration::from_millis(10));
    }
}

fn touch(root: &Path, relative: &str) -> PathBuf {
    let path = root.join(relative);
    fs::create_dir_all(path.parent().unwrap()).unwrap();
    fs::write(&path, b"IWAD\0\0\0\0\x0c\0\0\0").unwrap();
    fs::canonicalize(path).unwrap()
}

#[test]
fn vdf_preserves_escaped_quotes_backslashes_and_legacy_libraries() {
    let paths = parse_library_paths(
        r#"
        // path tokens inside comments are ignored: "path" "bad"
        "libraryfolders" {
            "0" { "path" "C:\\Program Files (x86)\\Steam" "apps" { "2280" "12345" } }
            "1" { "path" "/games/John\"s Steam" }
            "2" "/legacy/Steam"
            "3" { "path" "/legacy/Steam" }
        }
    "#,
    )
    .unwrap();
    assert_eq!(
        paths,
        vec![
            PathBuf::from(r"C:\Program Files (x86)\Steam"),
            PathBuf::from("/games/John\"s Steam"),
            PathBuf::from("/legacy/Steam")
        ]
    );
    assert_eq!(
        parse_install_dir(r#""AppState" { "installdir" "DOOM + DOOM II" }"#).unwrap(),
        Some("DOOM + DOOM II".into())
    );
    assert!(parse_install_dir(r#""installdir" "../outside""#).is_err());
    assert!(parse_library_paths(r#""libraryfolders" { "path" "unfinished"#).is_err());
}

#[test]
fn steam_default_library_needs_no_libraryfolders_file() {
    let root = TempDir::new().unwrap();
    let expected = touch(root.path(), "steamapps/common/Doom/base/DOOM.WAD");
    fs::write(
        root.path().join("steamapps/appmanifest_2280.acf"),
        r#""AppState" { "installdir" "Doom" }"#,
    )
    .unwrap();
    let found = scan_roots(&[root.path().into()], &[]).unwrap();
    assert_eq!(found.iwads, vec![expected]);
}

#[test]
fn steam_configured_library_and_rerelease_expansions_are_discovered() {
    let steam = TempDir::new().unwrap();
    let secondary = TempDir::new().unwrap();
    let iwad = touch(
        secondary.path(),
        "steamapps/common/DOOM + DOOM II/RERELEASE/DOOM2.WAD",
    );
    let expansion = touch(
        secondary.path(),
        "steamapps/common/DOOM + DOOM II/RERELEASE/sigil.wad",
    );
    fs::create_dir_all(steam.path().join("steamapps")).unwrap();
    fs::write(
        secondary.path().join("steamapps/appmanifest_2280.acf"),
        r#""AppState" { "installdir" "DOOM + DOOM II" }"#,
    )
    .unwrap();
    fs::write(
        steam.path().join("steamapps/libraryfolders.vdf"),
        format!(
            r#""libraryfolders" {{ "0" {{ "path" {:?} }} }}"#,
            secondary.path().to_string_lossy()
        ),
    )
    .unwrap();
    let found = scan_roots(&[steam.path().into()], &[]).unwrap();
    assert_eq!(found.iwads, vec![iwad]);
    assert_eq!(found.pwads, vec![expansion]);
}

#[test]
fn gog_prefixes_and_doom64_and_duplicate_roots() {
    let root = TempDir::new().unwrap();
    let expected = touch(
        root.path(),
        "Prefixes/heretic/drive_c/GOG Games/Heretic/HERETIC.WAD",
    );
    let doom = touch(root.path(), "Doom 64/doom64.wad");
    let executable = touch(root.path(), "Doom 64/doom64_x64.exe");
    let expansion = touch(root.path(), "Hexen/HEXDD.WAD");
    let found = scan_roots(&[], &[root.path().into(), root.path().into()]).unwrap();
    assert!(found.iwads.contains(&expected));
    assert!(found.iwads.contains(&doom));
    assert_eq!(found.iwads.len(), 2);
    assert_eq!(found.pwads, vec![expansion]);
    assert_eq!(found.doom64_exe, Some(executable));
}

#[test]
fn heroic_and_lutris_configured_install_roots() {
    let root = TempDir::new().unwrap();
    fs::write(root.path().join("installed.json"), r#"{"installed":[{"install_path":"/games/custom/Doom"},{"install":{"installPath":"/games/Hexen"}}]}"#).unwrap();
    fs::write(
        root.path().join("doom.yml"),
        "game:\n  exe: '/games/lutris/Doom/doom.exe'\n  prefix: /games/prefix\n",
    )
    .unwrap();
    let found = configured_game_roots(&[root.path().into()]);
    for path in [
        "/games/custom/Doom",
        "/games/Hexen",
        "/games/lutris/Doom",
        "/games/prefix",
    ] {
        assert!(found.contains(&PathBuf::from(path)));
    }
}

#[cfg(unix)]
#[test]
fn recursive_discovery_does_not_follow_directory_symlinks() {
    let root = TempDir::new().unwrap();
    let outside = TempDir::new().unwrap();
    touch(outside.path(), "doom2.wad");
    std::os::unix::fs::symlink(outside.path(), root.path().join("outside")).unwrap();
    assert!(
        scan_roots(&[], &[root.path().into()])
            .unwrap()
            .iwads
            .is_empty()
    );
}

#[test]
fn api_single_object_numeric_strings_and_encoded_search() {
    let (base, requests) = fixture(br#"{"content":{"file":{"id":"14","filename":"cool.zip","title":"Cool","description":"first<br />second","size":"42","rating":"4.25","dir":"levels/doom2/","date":"2026-01-02"}}}"#, "200 OK", "");
    let result = search_idgames(&base, "api/api.php", "doom & +?", "title", false).unwrap();
    assert_eq!(result.len(), 1);
    assert_eq!(result[0].id, 14);
    assert_eq!(result[0].size, 42);
    assert_eq!(result[0].rating, 4.25);
    assert_eq!(result[0].description, "first\nsecond");
    let request = requests.recv_timeout(Duration::from_secs(2)).unwrap();
    let target = request
        .lines()
        .next()
        .unwrap()
        .split_whitespace()
        .nth(1)
        .unwrap();
    let url = Url::parse(&format!("{base}{}", target.trim_start_matches('/'))).unwrap();
    assert_eq!(url.path(), "/api/api.php");
    assert_eq!(
        url.query_pairs().find(|(key, _)| key == "query").unwrap().1,
        "doom & +?"
    );
}

#[test]
fn api_latest_array_empty_and_error_responses() {
    let (base, request) = fixture(br#"{"content":{"file":[{"id":1},{"id":2}]}}"#, "200 OK", "");
    assert_eq!(
        search_idgames(&base, "api.php", "", "title", true)
            .unwrap()
            .len(),
        2
    );
    assert!(
        request
            .recv_timeout(Duration::from_secs(2))
            .unwrap()
            .contains("action=latestfiles")
    );
    assert!(parse_remote_files(br#"{"content":{}}"#).unwrap().is_empty());
    assert_eq!(
        parse_remote_files(br#"{"error":{"message":"No results"}}"#)
            .unwrap_err()
            .message,
        "No results"
    );
    for invalid in [
        b"<html>offline".as_slice(),
        b"[]",
        br#"{"content":{"file":42}}"#,
        br#"{"content":{"file":[42]}}"#,
    ] {
        assert!(parse_remote_files(invalid).is_err());
    }
}

#[test]
fn api_http_failure_is_reported_and_url_credentials_rejected() {
    let (base, _) = fixture(b"offline", "503 Service Unavailable", "");
    let error = search_idgames(&base, "api.php", "doom", "title", false).unwrap_err();
    assert_eq!(error.code, "network");
    assert!(!error.message.contains(&base));
    assert!(
        search_idgames(
            "https://user:secret@example.com",
            "api.php",
            "",
            "title",
            true
        )
        .is_err()
    );
    assert!(search_idgames("https://example.com", "../api.php", "", "title", true).is_err());
}

#[test]
fn unsafe_download_names_rejected_without_touching_disk() {
    let root = TempDir::new().unwrap();
    for (directory, name) in [
        ("../outside", "a.zip"),
        ("/outside", "a.zip"),
        ("levels", "../outside.zip"),
        ("levels", "C:\\evil.zip"),
        ("levels", "CON.zip"),
        ("levels\\evil", "a.zip"),
        ("levels", "a.zip."),
        ("", ""),
    ] {
        assert!(
            start_download("https://example.com/idgames", directory, name, root.path()).is_err(),
            "{directory}/{name}"
        );
    }
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
    let url = append_url_path(
        valid_url("https://example.com/idgames/").unwrap(),
        "levels/doom2",
        "file #1.zip",
    )
    .unwrap();
    assert_eq!(
        url.as_str(),
        "https://example.com/idgames/levels/doom2/file%20%231.zip"
    );
}

#[test]
fn downloaded_body_is_retained_until_importer_discards() {
    let root = TempDir::new().unwrap();
    let data = vec![0x5a; 180_000];
    let (base, requests) = fixture(&data, "200 OK", "");
    let initial = start_download(&base, "levels/doom2/", "fixture.zip", root.path()).unwrap();
    let state = terminal(&initial.id);
    assert_eq!(state.status, "completed", "{state:?}");
    assert_eq!(state.received, data.len() as u64);
    assert_eq!(state.total, Some(data.len() as u64));
    let path = state.local_path.unwrap();
    assert!(path.ends_with("fixture.zip"));
    assert_eq!(fs::read(&path).unwrap(), data);
    assert!(
        requests
            .recv_timeout(Duration::from_secs(2))
            .unwrap()
            .starts_with("GET /levels/doom2/fixture.zip ")
    );
    assert_eq!(cancel_download(&initial.id).unwrap().status, "completed");
    discard_download(&initial.id).unwrap();
    assert!(!path.exists());
    assert!(download_status(&initial.id).is_err());
}

#[test]
fn failed_download_removes_partial_files() {
    let root = TempDir::new().unwrap();
    let (base, _) = fixture(b"missing", "404 Not Found", "");
    let initial = start_download(&base, "", "no.zip", root.path()).unwrap();
    let state = terminal(&initial.id);
    assert_eq!(state.status, "failed");
    assert!(state.error.is_some());
    assert!(state.local_path.is_none());
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
    discard_download(&initial.id).unwrap();
}

#[test]
fn cancelled_transfer_never_becomes_a_successful_download() {
    let root = TempDir::new().unwrap();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let base = format!("http://{}/", listener.local_addr().unwrap());
    let (started_send, started_receive) = mpsc::channel();
    let (finish_send, finish_receive) = mpsc::channel();
    let server = thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        read_request(&mut socket);
        write!(
            socket,
            "HTTP/1.1 200 OK\r\nContent-Length: 12\r\nConnection: close\r\n\r\nfirst"
        )
        .unwrap();
        socket.flush().unwrap();
        started_send.send(()).unwrap();
        finish_receive.recv_timeout(Duration::from_secs(5)).unwrap();
        socket.write_all(b" final!").ok();
    });
    let initial = start_download(&base, "", "cancel.zip", root.path()).unwrap();
    started_receive
        .recv_timeout(Duration::from_secs(5))
        .unwrap();
    assert!(
        active_downloads()
            .unwrap()
            .iter()
            .any(|job| job.id == initial.id)
    );
    assert_eq!(cancel_download(&initial.id).unwrap().status, "cancelled");
    finish_send.send(()).unwrap();
    shutdown_jobs(&[find_download(&initial.id).unwrap()]).unwrap();
    server.join().unwrap();
    let start = Instant::now();
    while fs::read_dir(root.path()).unwrap().count() != 0 {
        assert!(start.elapsed() < Duration::from_secs(5));
        thread::sleep(Duration::from_millis(10));
    }
    let state = download_status(&initial.id).unwrap();
    assert_eq!(state.status, "cancelled");
    assert!(state.local_path.is_none());
    discard_download(&initial.id).unwrap();
}

#[test]
fn oversized_and_truncated_responses_are_rejected() {
    for advertised in [MAX_DOWNLOAD + 1, 100] {
        let root = TempDir::new().unwrap();
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        let base = format!("http://{}/", listener.local_addr().unwrap());
        thread::spawn(move || {
            let (mut socket, _) = listener.accept().unwrap();
            read_request(&mut socket);
            write!(
                socket,
                "HTTP/1.1 200 OK\r\nContent-Length: {advertised}\r\nConnection: close\r\n\r\nshort"
            )
            .unwrap();
        });
        let initial = start_download(&base, "", "limit.zip", root.path()).unwrap();
        let state = terminal(&initial.id);
        assert_eq!(state.status, "failed", "{state:?}");
        assert!(state.local_path.is_none());
        assert_eq!(fs::read_dir(root.path()).unwrap().count(), 0);
        discard_download(&initial.id).unwrap();
    }
}

#[test]
fn redirect_to_non_http_transport_is_rejected() {
    let root = TempDir::new().unwrap();
    let (base, _) = fixture(
        b"",
        "302 Found",
        "Location: ftp://example.com/fixture.zip\r\n",
    );
    let initial = start_download(&base, "", "redirect.zip", root.path()).unwrap();
    assert_eq!(terminal(&initial.id).status, "failed");
    discard_download(&initial.id).unwrap();
}

#[test]
fn redirects_never_downgrade_tls_or_accept_credentials() {
    let https = Url::parse("https://example.com/first").unwrap();
    let http = Url::parse("http://example.com/second").unwrap();
    assert!(redirect_error(std::slice::from_ref(&https), &http).is_some());
    assert!(redirect_error(std::slice::from_ref(&http), &https).is_none());
    assert!(redirect_error(std::slice::from_ref(&https), &https).is_none());
    assert!(redirect_error(&vec![https; 10], &http).is_some());
    assert!(
        redirect_error(
            &[],
            &Url::parse("https://user:secret@example.com/").unwrap()
        )
        .is_some()
    );
}
