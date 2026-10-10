# Migration bridge contract (v1)

Native C ABI is a small synchronous UTF-8 JSON request/response boundary called only from a Dart worker isolate. `dl_invoke(const uint8_t*, size_t) -> char*`, `dl_free(char*)`, `dl_abi_version() -> uint32_t`. Dart owns input via malloc; Rust owns returned CString until exactly one dl_free. Panics contained; max request 8 MiB. No Dart pointers/callbacks enter Rust. ABI version 1. Request `{op, data_root, ...args}`; response `{ok:true,result:{...}}` or `{ok:false,error:{code,message}}`.

`lib/bridge/backend.dart` provides `Backend({String? dataRoot})`, `Future<void> initialize()`, `String get dataRoot`, `Future<Map<String,dynamic>> call(String op,[Map<String,dynamic> args={}])`. Initialization resolves native platform app data, reuses Linux legacy root, supports DOOMEDLAUNCHER_DATA_DIR for test isolation. `BackendException` has code/message. UI never sends SQL.

`AppController` (`lib/state/app_controller.dart`) owns backend, `snapshot`, `games`, `ports`, `tags`, `iwads`, `settings`, `sessions`, `busy`, `error`; `initialize()`, `refresh()`, `execute(op,args)` catches BackendException and returns nullable result with error available to UI. `call` operations that need custom polling can use backend directly with errors handled by controller. Controller clears stale errors on new action.

Operations and results:
- snapshot -> {games,ports,iwads,tags,settings,sessions,data_root,version}; games are legacy column keys (GameFileID,FileName,Title,Author,Description,Rating,Comments,Map,MapCount,Downloaded,LastPlayed,MinutesPlayed,Settings*,SourcePortID,IWadID) plus path,imagePath,isIwad,tagIds. All games included; presentation filters use snapshot IDs/strings. ports use runtime Port snake_case: id,name,executable,directory,supported_extensions,file_option,extra_parameters,alt_save_directory,launch_type. tags tag_id,name,has_tab. iwads iwad_id,name,game_file_id,file_name. settings map uses legacy names.
- import_files {paths:[string],as_iwads:bool} -> {added:[string],failed:[{path,message}]}; import_directory {path,recursive:true,as_iwads:false} same.
- edit_entry {id,fields:{Title,Author,Description,Rating,Comments,ReleaseDate},tag_ids:[int]} -> {}; validation and all edits atomic.
- rename_entry {id,name}; delete_entry {id,delete_file:bool}; resync_metadata {ids:[int]} -> {}.
- entry_details {id} -> {game,files,stats,text_files}; files legacy keys plus path; stats legacy PascalCase SQLite columns; text_files string members.
- open_text {id,member?:string} -> {path}; frontend opens path through url_launcher; game path already in snapshot.
- import_associations {id,paths:[string]} -> {added,failed}; delete_association {file_id} -> {}. association type auto detected by suffix. use path from details for opening.
- save_tag {id?:int,name,has_tab:bool}; delete_tag {id} -> {}.
- save_port {port:{id?:int,name,executable,directory,supported_extensions,file_option,extra_parameters,alt_save_directory,launch_type:0}}; delete_port {id}; detect_ports -> {added:int}.
- update_settings {values:{string:string}} -> {}; theme ColorThemeType=System/Default/Dark, ShowPlayDialog=true/false, GameFileViewType=GridView/TileView, AppWidth/AppHeight, LastSelectedTabIndex, DefaultSourcePort,DefaultIWad,MirrorUrl,IdGamesUrl,ApiPage,VisibleViews.
- play_defaults {id} -> {options, maps:[string],additional_files:[string]}; options same below.
- launch_preview / launch {options:{game_file_id,source_port_id,iwad_id:int?,map,skill:int?,extra_params,extra_params_only,load_latest_save,save_statistics,record_demo,play_demo_file:string?,additional_files:[managed filenames],remember:bool}} -> preview {program,arguments,working_directory,display}; launch {session:{...}}.
- sessions -> {sessions}; stop_session {id} -> {session}. session id,status,game_file_id,source_port_id,elapsed_seconds,exit_code,error,log_path.
- active_jobs -> {sessions,downloads}; shutdown -> {}. Shutdown stops owned processes, drains cancelled downloads, harvests finished sessions, and safely clears temporary files when enabled. A failed shutdown returns an error and the UI keeps its window open.
- scan_stores {roots?:[string]} -> {iwads:[paths],pwads:[paths],doom64_exe?:string}; scanning doesn't import until user invokes import_files. For Doom64 save_port launch_type=2 supported by runtime.
- search_idgames {query,type:filename|title|author|description,latest:bool} -> {files:[{id,file_name,title,author,description,release_date,directory,url,rating,size}]}.
- start_download {directory,file_name} -> {download:{id,status,received,total,error,local_path}}; download_status {id}; cancel_download {id} same. status pending/downloading/completed/cancelled/failed. UI import_files once completed, then explicit Play dialog if requested. Never auto-launch unrelated completed download.

Game and association `path` values may be null for a path unavailable on the current platform. `pathError` describes unavailable content; entries remain visible and editable. Details also return `text_error` when text cannot be inspected. `discard_download {id}` removes an explicitly discarded completed or failed download and its owned temporary data.

The transport deliberately carries batched domain operations; JSON isn't used for image pixel transfers. Image paths refer to bounded decoded cache files. UI widgets should use typed DTO/view models where meaningful and keep domain validation in Rust. Work remains prerelease until parity matrix checks pass.
