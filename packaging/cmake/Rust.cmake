# Shared by the Flutter Linux and Windows runners. Cargo owns dependency
# tracking, so this cheap target runs on every build (including `flutter run`).
find_program(CARGO_EXECUTABLE cargo REQUIRED)
get_filename_component(DOOM_ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
file(STRINGS "${DOOM_ROOT}/pubspec.yaml" DOOM_VERSION_LINE REGEX "^version:")
string(REGEX REPLACE "^version:[ ]*([^+ ]+).*" "\\1" DOOM_APP_VERSION "${DOOM_VERSION_LINE}")
target_compile_definitions(${BINARY_NAME} PRIVATE "DOOM_APP_VERSION=\"${DOOM_APP_VERSION}\"")
set(DOOM_RUST_TARGET_DIR "${DOOM_ROOT}/build/native")
if(WIN32)
  if(CMAKE_GENERATOR_PLATFORM STREQUAL "ARM64" OR FLUTTER_TARGET_PLATFORM STREQUAL "windows-arm64")
    set(DOOM_RUST_TARGET "aarch64-pc-windows-msvc")
  else()
    set(DOOM_RUST_TARGET "x86_64-pc-windows-msvc")
  endif()
  set(DOOM_RUST_LIBRARY_NAME "doomedlauncher_core.dll")
else()
  if(FLUTTER_TARGET_PLATFORM STREQUAL "linux-arm64" OR CMAKE_SYSTEM_PROCESSOR MATCHES "^(aarch64|arm64)$")
    set(DOOM_RUST_TARGET "aarch64-unknown-linux-gnu")
  else()
    set(DOOM_RUST_TARGET "x86_64-unknown-linux-gnu")
  endif()
  set(DOOM_RUST_LIBRARY_NAME "libdoomedlauncher_core.so")
endif()
set(DOOM_RUST_PROFILE "$<IF:$<CONFIG:Debug>,debug,release>")
set(DOOM_RUST_LIBRARY "${DOOM_RUST_TARGET_DIR}/${DOOM_RUST_TARGET}/${DOOM_RUST_PROFILE}/${DOOM_RUST_LIBRARY_NAME}")
add_custom_target(doom_rust ALL
  COMMAND "${CARGO_EXECUTABLE}" build --locked
    --manifest-path "${DOOM_ROOT}/rust/Cargo.toml"
    --target "${DOOM_RUST_TARGET}"
    --target-dir "${DOOM_RUST_TARGET_DIR}"
    "$<$<NOT:$<CONFIG:Debug>>:--release>"
  WORKING_DIRECTORY "${DOOM_ROOT}"
  COMMAND_EXPAND_LISTS VERBATIM
  COMMENT "Building Rust core (${DOOM_RUST_TARGET})")
add_dependencies(${BINARY_NAME} doom_rust)
