use super::*;
use image::{DynamicImage, ImageFormat, Rgba, RgbaImage};
use std::io::{Cursor, Write};
use tempfile::TempDir;
use zip::{CompressionMethod, ZipWriter, write::SimpleFileOptions};

fn wad_bytes(lumps: &[(&str, &[u8])]) -> Vec<u8> {
    let mut bytes = b"PWAD".to_vec();
    bytes.extend_from_slice(&(lumps.len() as u32).to_le_bytes());
    bytes.extend_from_slice(&[0; 4]);
    let mut records = Vec::new();
    for (name, data) in lumps {
        records.extend_from_slice(&(bytes.len() as u32).to_le_bytes());
        records.extend_from_slice(&(data.len() as u32).to_le_bytes());
        let mut raw = [0; 8];
        raw[..name.len()].copy_from_slice(name.as_bytes());
        records.extend_from_slice(&raw);
        bytes.extend_from_slice(data);
    }
    let offset = bytes.len() as u32;
    bytes[8..12].copy_from_slice(&offset.to_le_bytes());
    bytes.extend_from_slice(&records);
    bytes
}

fn write_wad(path: &Path, lumps: &[(&str, &[u8])]) {
    fs::write(path, wad_bytes(lumps)).unwrap();
}

fn write_zip(path: &Path, members: &[(&str, &[u8])]) {
    let mut writer = ZipWriter::new(fs::File::create(path).unwrap());
    for (name, data) in members {
        writer
            .start_file(
                *name,
                SimpleFileOptions::default().compression_method(CompressionMethod::Stored),
            )
            .unwrap();
        writer.write_all(data).unwrap();
    }
    writer.finish().unwrap();
}

fn patch() -> Vec<u8> {
    let mut bytes = Vec::new();
    bytes.extend_from_slice(&1_u16.to_le_bytes());
    bytes.extend_from_slice(&300_u16.to_le_bytes());
    bytes.extend_from_slice(&[0; 4]);
    bytes.extend_from_slice(&12_u32.to_le_bytes());
    bytes.extend_from_slice(&[250, 1, 0, 7, 0, 10, 1, 0, 8, 0, 255]);
    bytes
}

fn png(width: u32, height: u32) -> Vec<u8> {
    let mut bytes = Cursor::new(Vec::new());
    DynamicImage::ImageRgba8(RgbaImage::from_pixel(
        width,
        height,
        Rgba([12, 34, 56, 255]),
    ))
    .write_to(&mut bytes, ImageFormat::Png)
    .unwrap();
    bytes.into_inner()
}

#[test]
fn maps_include_custom_and_udmf_markers_in_order_without_duplicates() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("custom.wad");
    write_wad(
        &path,
        &[
            ("MAP01", b""),
            ("THINGS", b"things"),
            ("LINEDEFS", b""),
            ("CUSTOM", b""),
            ("TEXTMAP", b"namespace=\"ZDoom\";"),
            ("ENDMAP", b""),
            ("MAP01", b""),
            ("THINGS", b""),
        ],
    );
    assert_eq!(map_names(&path).unwrap(), ["MAP01", "CUSTOM"]);
    assert!(!is_iwad(&path).unwrap());
    assert_eq!(lump_names(&path).unwrap().len(), 8);
}

#[test]
fn wad_rejects_truncated_directories_and_invalid_offsets() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("bad.wad");
    let good = wad_bytes(&[("MAP01", b""), ("THINGS", b"thing")]);
    let mut examples = vec![good[..good.len() - 1].to_vec(), b"IWAD".to_vec()];
    let mut negative = good.clone();
    negative[4..8].copy_from_slice(&(-1_i32).to_le_bytes());
    examples.push(negative);
    let mut out_of_bounds = good.clone();
    let directory = u32::from_le_bytes(good[8..12].try_into().unwrap()) as usize;
    out_of_bounds[directory..directory + 4].copy_from_slice(&i32::MAX.to_le_bytes());
    examples.push(out_of_bounds);
    let mut huge_lump = good.clone();
    huge_lump[directory + 4..directory + 8].copy_from_slice(&i32::MAX.to_le_bytes());
    examples.push(huge_lump);
    for bad in examples {
        fs::write(&path, bad).unwrap();
        assert!(map_names(&path).is_err());
    }
}

#[test]
fn wad_last_definition_matches_engine_resolution() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("test.wad");
    write_wad(&path, &[("TEST", b"old"), ("TEST", b"new")]);
    assert_eq!(lump_data(&path, "test").unwrap().unwrap(), b"new");
}

#[test]
fn filenames_are_safe_on_windows_and_unix() {
    for valid in [
        "doom.wad",
        "Téléporteur.pk3",
        "MAP01",
        "com0.wad",
        ".hidden",
        "a b.txt",
    ] {
        assert!(safe_filename(valid), "{valid}");
    }
    for invalid in [
        "",
        ".",
        "..",
        "../a",
        "a/b",
        "a\\b",
        "C:doom.wad",
        "CON.wad",
        "nul",
        "LPT9.txt",
        "COM¹",
        "a.",
        "a ",
        "a\0x",
        "a\nx",
        "a?b",
        "a|b",
        "a*b",
        "a<b",
    ] {
        assert!(!safe_filename(invalid), "{invalid}");
    }
}

#[test]
fn extraction_preserves_order_and_isolates_same_basename_members_and_archives() {
    let root = TempDir::new().unwrap();
    let first = root.path().join("first.zip");
    let second = root.path().join("second.zip");
    let cache = root.path().join("cache");
    write_zip(
        &first,
        &[
            ("one/doom.wad", b"one"),
            ("two/doom.wad", b"two"),
            ("readme.txt", b"hi"),
        ],
    );
    write_zip(&second, &[("one/doom.wad", b"other")]);
    let paths = prepare_files(
        &first,
        &[".wad".into()],
        &["two/doom.wad".into(), "one/doom.wad".into()],
        &cache,
    )
    .unwrap();
    let other = prepare_files(&second, &["wad".into()], &[], &cache).unwrap();
    assert_eq!(paths.len(), 2);
    assert_ne!(paths[0], paths[1]);
    assert_ne!(paths[1], other[0]);
    assert_eq!(fs::read(&paths[0]).unwrap(), b"two");
    assert_eq!(fs::read(&paths[1]).unwrap(), b"one");
    assert_eq!(fs::read(&other[0]).unwrap(), b"other");
    assert!(prepare_files(&first, &["wad".into()], &["readme.txt".into()], &cache).is_err());
    assert!(prepare_files(&first, &["wad".into()], &["missing.wad".into()], &cache).is_err());
}

#[test]
fn archive_update_invalidates_cache_and_corrupted_cache_is_rejected() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("mod.zip");
    let cache = root.path().join("cache");
    write_zip(&path, &[("mod.wad", b"before")]);
    let old = prepare_files(&path, &[], &[], &cache).unwrap().remove(0);
    write_zip(&path, &[("mod.wad", b"after!")]);
    let new = prepare_files(&path, &[], &[], &cache).unwrap().remove(0);
    assert_ne!(old, new);
    assert_eq!(fs::read(&old).unwrap(), b"before");
    assert_eq!(
        prepare_files(&path, &[], &[], &cache).unwrap(),
        vec![new.clone()]
    );
    fs::write(&new, b"wrong!").unwrap();
    assert!(prepare_files(&path, &[], &[], &cache).is_err());
}

#[test]
fn archive_traversal_and_symbolic_links_are_rejected() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("bad.zip");
    for member in [
        "../outside.wad",
        "/absolute.wad",
        "dir/../../escape.wad",
        "C:/escape.wad",
        "dir\\escape.wad",
        "CON.wad",
        "dir/./file.wad",
    ] {
        write_zip(&path, &[(member, b"evil")]);
        assert!(
            prepare_files(&path, &[], &[], &root.path().join("cache")).is_err(),
            "{member}"
        );
    }
    let mut writer = ZipWriter::new(fs::File::create(&path).unwrap());
    writer
        .add_symlink("link.wad", "../target", SimpleFileOptions::default())
        .unwrap();
    writer.finish().unwrap();
    assert!(entries(&path).is_err());
}

#[test]
fn corrupt_zip_crc_never_publishes_partial_output() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("bad.zip");
    let cache = root.path().join("cache");
    write_zip(&path, &[("mod.wad", b"unique payload")]);
    let mut bytes = fs::read(&path).unwrap();
    let offset = bytes
        .windows(14)
        .position(|w| w == b"unique payload")
        .unwrap();
    bytes[offset] ^= 1;
    fs::write(&path, bytes).unwrap();
    assert!(prepare_files(&path, &[], &[], &cache).is_err());
    let files = walkdir::WalkDir::new(&cache)
        .into_iter()
        .filter_map(std::result::Result::ok)
        .filter(|e| e.file_type().is_file())
        .count();
    assert_eq!(files, 0);
}

#[test]
fn declared_expansion_limit_is_checked_before_reading() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("large.zip");
    write_zip(&path, &[("map.wad", b"small")]);
    let mut data = fs::read(&path).unwrap();
    let central = data.windows(4).position(|w| w == b"PK\x01\x02").unwrap();
    data[central + 24..central + 28].copy_from_slice(&u32::MAX.to_le_bytes());
    fs::write(&path, data).unwrap();
    assert!(prepare_files(&path, &[], &[], &root.path().join("cache")).is_err());
}

#[test]
fn pk3_is_passed_to_the_engine_without_expanding_it() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("game.pk3");
    write_zip(&path, &[("graphics/titlepic.png", &png(2, 2))]);
    assert_eq!(
        prepare_files(&path, &["pk3".into()], &[], &root.path().join("cache")).unwrap(),
        vec![fs::canonicalize(&path).unwrap()]
    );
}

#[test]
fn metadata_description_handles_continuations_latin1_and_template_boundaries() {
    let metadata = parse_metadata_text(
        "Title : The Library\r\nAuthor(s): A. Mapper\r\nRelease date: 2026-01-02\r\nDescription: First line\r\n             : Second line\r\n  https://example.com/info\r\n\r\nAdditional Credits to : Someone\r\nEmail: ignored",
    );
    assert_eq!(metadata.title.as_deref(), Some("The Library"));
    assert_eq!(metadata.author.as_deref(), Some("A. Mapper"));
    assert_eq!(metadata.release_date.as_deref(), Some("2026-01-02"));
    assert_eq!(
        metadata.description.as_deref(),
        Some("First line\nSecond line\nhttps://example.com/info")
    );
    assert_eq!(decode_text(b"caf\xe9"), "café");
}

#[test]
fn matching_sidecars_can_be_read_but_arbitrary_files_cannot() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("MyMod.wad");
    let cache = root.path().join("cache");
    write_wad(&path, &[("MAP01", b""), ("THINGS", b"")]);
    fs::write(
        root.path().join("mymod.TXT"),
        "Title: My Mod\nAuthor: Mapper",
    )
    .unwrap();
    fs::write(root.path().join("secret.txt"), "unrelated").unwrap();
    assert_eq!(text_files(&path).unwrap(), vec!["mymod.TXT"]);
    assert!(extract_text(&path, "secret.txt", &cache).is_err());
    assert!(extract_text(&path, "../secret.txt", &cache).is_err());
    let metadata = inspect(&path, &cache).unwrap();
    assert_eq!(metadata.title.as_deref(), Some("My Mod"));
    assert_eq!(metadata.maps, vec!["MAP01"]);
}

#[test]
fn archive_metadata_prefers_matching_readme_and_reports_maps() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("pack.zip");
    let wad = wad_bytes(&[("E1M1", b""), ("THINGS", b"")]);
    write_zip(
        &path,
        &[
            ("aaa.txt", b"Title: Wrong"),
            ("docs/PACK.txt", b"Title: Right\nDescription: A map"),
            ("maps/map.wad", &wad),
        ],
    );
    let metadata = inspect(&path, &root.path().join("cache")).unwrap();
    assert_eq!(metadata.title.as_deref(), Some("Right"));
    assert_eq!(metadata.maps, vec!["E1M1"]);
    let extracted = extract_text(&path, "docs/PACK.txt", &root.path().join("cache")).unwrap();
    assert_eq!(
        fs::read_to_string(extracted).unwrap(),
        "Title: Right\nDescription: A map"
    );
}

#[test]
fn patch_only_zip_import_reads_metadata_and_prepares_both_patch_formats() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("patches.zip");
    let cache = root.path().join("cache");
    write_zip(
        &path,
        &[
            (
                "patches.txt",
                b"Title: Patch-only mod\nAuthor: DeHackEd author",
            ),
            (
                "classic.deh",
                b"Patch File for DeHackEd v3.0\nDoom version = 19\n",
            ),
            ("extended.bex", b"[STRINGS]\nHUSTR_1 = Renamed map\n"),
        ],
    );
    let metadata = inspect(&path, &cache).unwrap();
    assert_eq!(metadata.title.as_deref(), Some("Patch-only mod"));
    assert_eq!(metadata.author.as_deref(), Some("DeHackEd author"));
    assert!(metadata.maps.is_empty());
    let files = prepare_files(
        &path,
        &["wad".into(), "deh".into(), "bex".into()],
        &[],
        &cache,
    )
    .unwrap();
    assert_eq!(
        files
            .iter()
            .map(|p| p.file_name().unwrap().to_string_lossy())
            .collect::<Vec<_>>(),
        ["classic.deh", "extended.bex"]
    );
    assert!(
        fs::read_to_string(&files[0])
            .unwrap()
            .starts_with("Patch File for DeHackEd")
    );
    assert!(
        fs::read_to_string(&files[1])
            .unwrap()
            .starts_with("[STRINGS]")
    );
}

#[test]
fn original_palettes_are_preserved_and_flats_decode() {
    for palette in [Palette::Doom, Palette::Heretic, Palette::Hexen] {
        let colors = default_palette(palette);
        assert_eq!(colors.len(), 768);
        let image = decode_picture(&vec![13; 320 * 200], colors).unwrap();
        assert_eq!(image.dimensions(), (320, 200));
        assert_eq!(
            image.get_pixel(0, 0).0,
            [colors[39], colors[40], colors[41], 255]
        );
    }
    assert_ne!(
        default_palette(Palette::Doom),
        default_palette(Palette::Heretic)
    );
}

#[test]
fn tall_patches_preserve_transparency_and_reject_unterminated_columns() {
    let palette = default_palette(Palette::Doom);
    let data = patch();
    let image = decode_picture(&data, palette).unwrap();
    assert_eq!(image.get_pixel(0, 249).0, [0, 0, 0, 0]);
    assert_eq!(
        image.get_pixel(0, 250).0,
        [palette[21], palette[22], palette[23], 255]
    );
    assert_eq!(
        image.get_pixel(0, 260).0,
        [palette[24], palette[25], palette[26], 255]
    );
    assert!(decode_picture(&data[..data.len() - 1], palette).is_err());
    let mut bad = data.clone();
    bad[8..12].copy_from_slice(&u32::MAX.to_le_bytes());
    assert!(decode_picture(&bad, palette).is_err());
}

#[test]
fn encoded_images_do_not_need_palettes_and_enforce_dimensions() {
    assert_eq!(
        decode_picture(&png(3, 2), &[]).unwrap().dimensions(),
        (3, 2)
    );
    assert!(decode_picture(&png(8193, 1), &[]).is_err());
    assert!(decode_picture(b"\x89PNG\r\n\x1a\n", &[]).is_err());
    let mut bmp = Cursor::new(Vec::new());
    DynamicImage::ImageRgba8(RgbaImage::from_pixel(4, 3, Rgba([1, 2, 3, 255])))
        .write_to(&mut bmp, ImageFormat::Bmp)
        .unwrap();
    assert_eq!(
        decode_picture(&bmp.into_inner(), &[])
            .unwrap()
            .get_pixel(1, 1)
            .0,
        [1, 2, 3, 255]
    );
}

#[test]
fn zip64_directory_count_is_bounded_before_allocation() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("zip64.zip");
    let mut data = b"PK\x06\x06".to_vec();
    data.extend_from_slice(&44_u64.to_le_bytes()); // size of ZIP64 end record
    data.extend_from_slice(&[45, 0, 45, 0]); // creator/reader versions
    data.extend_from_slice(&[0; 8]); // disk numbers
    data.extend_from_slice(&u64::MAX.to_le_bytes());
    data.extend_from_slice(&u64::MAX.to_le_bytes());
    data.extend_from_slice(&[0; 16]); // directory size and offset
    data.extend_from_slice(b"PK\x06\x07");
    data.extend_from_slice(&[0; 12]); // disk and ZIP64 record offset
    data.extend_from_slice(&1_u32.to_le_bytes());
    data.extend_from_slice(b"PK\x05\x06");
    data.extend_from_slice(&[0; 4]);
    data.extend_from_slice(&[255; 4]); // ZIP32 sentinel entry counts
    data.extend_from_slice(&[0; 10]); // size, offset, comment length
    fs::write(&path, data).unwrap();
    let error = entries(&path).unwrap_err();
    assert!(error.message.contains("resource limits"));
}

#[test]
fn wad_custom_titlepage_and_playpal_override_defaults() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("art.wad");
    let palette = vec![87; 768];
    let picture = patch();
    write_wad(
        &path,
        &[
            (
                "MAPINFO",
                b"// titlepage = \"IGNORED\"\ngameinfo { titlepage = \"CUSTOM\" }",
            ),
            ("CUSTOM", &picture),
            ("PLAYPAL", &palette),
        ],
    );
    let metadata = inspect(&path, &root.path().join("cache")).unwrap();
    let picture = image::open(metadata.title_picture.unwrap())
        .unwrap()
        .to_rgba8();
    assert_eq!(picture.get_pixel(0, 250).0, [87, 87, 87, 255]);
}

#[test]
fn pk3_inspection_reads_namespace_maps_mapinfo_and_title_art() {
    let root = TempDir::new().unwrap();
    let path = root.path().join("game.pk3");
    let map = wad_bytes(&[("THINGS", b""), ("LINEDEFS", b"")]);
    write_zip(
        &path,
        &[
            ("maps/MAP07.wad", &map),
            (
                "ZMAPINFO",
                b"gameinfo { titlepage = \"graphics/start.png\" }",
            ),
            ("graphics/start.png", &png(5, 3)),
        ],
    );
    let metadata = inspect(&path, &root.path().join("cache")).unwrap();
    assert_eq!(metadata.maps, vec!["MAP07"]);
    assert_eq!(
        image::open(metadata.title_picture.unwrap())
            .unwrap()
            .width(),
        5
    );
}

#[test]
fn malformed_fuzz_like_images_and_wads_do_not_panic() {
    let mut state = 7_u64;
    for length in 0..1024 {
        let data = (0..length)
            .map(|_| {
                state ^= state << 13;
                state ^= state >> 7;
                state ^= state << 17;
                state as u8
            })
            .collect::<Vec<_>>();
        let _ = decode_picture(&data, default_palette(Palette::Doom));
        let _ = wad::Wad::read(Cursor::new(data));
    }
}

#[cfg(unix)]
#[test]
fn cache_symlinks_are_rejected_without_modifying_targets() {
    use std::os::unix::fs::symlink;
    let root = TempDir::new().unwrap();
    let path = root.path().join("mod.zip");
    let outside = root.path().join("outside");
    fs::create_dir(&outside).unwrap();
    let cache = root.path().join("cache");
    symlink(&outside, &cache).unwrap();
    write_zip(&path, &[("mod.wad", b"safe")]);
    assert!(prepare_files(&path, &[], &[], &cache).is_err());
    assert_eq!(fs::read_dir(&outside).unwrap().count(), 0);
}

#[cfg(unix)]
#[test]
fn cache_resolves_system_ancestor_aliases_but_rejects_member_directory_links() {
    use std::os::unix::fs::symlink;
    let root = TempDir::new().unwrap();
    let real = root.path().join("real");
    fs::create_dir(&real).unwrap();
    let alias = root.path().join("alias");
    symlink(&real, &alias).unwrap();
    let archive = root.path().join("mod.zip");
    write_zip(&archive, &[("map.wad", b"a map")]);
    let files = prepare_files(&archive, &[], &[], &alias.join("cache")).unwrap();
    assert!(files[0].starts_with(fs::canonicalize(&real).unwrap()));
    let bad_cache = real.join("bad-cache");
    fs::create_dir(&bad_cache).unwrap();
    let outside = real.join("outside");
    fs::create_dir(&outside).unwrap();
    symlink(&outside, bad_cache.join("members")).unwrap();
    assert!(prepare_files(&archive, &[], &[], &bad_cache).is_err());
    assert_eq!(fs::read_dir(outside).unwrap().count(), 0);
}
