add_executable(managedpath_test managedpath_test.cpp)
add_executable(hostbridge_fixture hostbridge_fixture.cpp)
set_target_properties(hostbridge_fixture PROPERTIES OUTPUT_NAME flatpak-spawn)
target_link_libraries(hostbridge_fixture PRIVATE Qt6::Core)
add_executable(hostprocess_test hostprocess_test.cpp
    ../src/hostprocess.cpp ../src/sourceportdetector.cpp ../src/gamelauncher.cpp
    ../src/database.cpp ../src/launcherpaths.cpp ../src/archivereader.cpp ../src/statsreader.cpp)
target_link_libraries(hostprocess_test PRIVATE Qt6::Core Qt6::Test Qt6::Sql KF6::Archive)
target_include_directories(hostprocess_test PRIVATE ${CMAKE_SOURCE_DIR}/src)
add_dependencies(hostprocess_test hostbridge_fixture)
add_test(NAME hostprocess_test COMMAND hostprocess_test)
target_link_libraries(managedpath_test PRIVATE Qt6::Core Qt6::Test)
target_include_directories(managedpath_test PRIVATE ${CMAKE_SOURCE_DIR}/src)
add_test(NAME managedpath_test COMMAND managedpath_test)

add_executable(externalurl_test externalurl_test.cpp)
target_link_libraries(externalurl_test PRIVATE Qt6::Core Qt6::Test)
target_include_directories(externalurl_test PRIVATE ${CMAKE_SOURCE_DIR}/src)
add_test(NAME externalurl_test COMMAND externalurl_test)

configure_file(metadata_consistency_test.cmake.in metadata_consistency_test.cmake @ONLY)
add_test(NAME metadata_consistency
    COMMAND ${CMAKE_COMMAND} -P ${CMAKE_CURRENT_BINARY_DIR}/metadata_consistency_test.cmake)

add_test(NAME command_line_version COMMAND $<TARGET_FILE:doomedlauncher> --version)
set_tests_properties(command_line_version PROPERTIES
    ENVIRONMENT "QT_QPA_PLATFORM=offscreen"
    PASS_REGULAR_EXPRESSION "doomedlauncher ${PROJECT_VERSION}")

add_executable(library_regression_test library_regression_test.cpp)
target_link_libraries(library_regression_test PRIVATE doomedlauncher_backend Qt6::Test)
add_test(NAME library_regression_test COMMAND library_regression_test)
set_tests_properties(library_regression_test PROPERTIES
    ENVIRONMENT "QT_QPA_PLATFORM=offscreen;QT_QUICK_BACKEND=software")

add_executable(idgamesclient_test idgamesclient_test.cpp)
target_link_libraries(idgamesclient_test PRIVATE doomedlauncher_backend Qt6::Test)
add_test(NAME idgamesclient_test COMMAND idgamesclient_test)

add_executable(qml_smoke_test qml_smoke_test.cpp)
target_link_libraries(qml_smoke_test PRIVATE doomedlauncher_backend Qt6::Test)
target_compile_definitions(qml_smoke_test PRIVATE QML_SOURCE_DIR="${CMAKE_SOURCE_DIR}/src/qml")
add_test(NAME qml_smoke_test COMMAND qml_smoke_test)
set_tests_properties(qml_smoke_test PROPERTIES
    ENVIRONMENT "QT_QPA_PLATFORM=offscreen;QT_QUICK_BACKEND=software")
