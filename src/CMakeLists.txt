add_executable(doomedlauncher
    main.cpp
    launcherpaths.cpp
    database.cpp
    archivereader.cpp
    wadparser.cpp
    sourceportdetector.cpp
    gamelauncher.cpp
    librarymodel.cpp
    sourceportmodel.cpp
    idgamesclient.cpp
    thememanager.cpp
    libraryops.cpp
    launcherapp.cpp
    doomimage.cpp
    titlepicextractor.cpp
    statsreader.cpp
    storescanner.cpp
)

qt_add_resources(doomedlauncher "palettes"
    PREFIX "/palettes"
    BASE resources
    FILES
        resources/PLAYPAL.LMP
        resources/HereticPalette.pal
        resources/HexenPalette.pal
)

set(APP_QML_FILES
    qml/Main.qml
    qml/LibraryPage.qml
    qml/DetailsPanel.qml
    qml/PlayDialog.qml
    qml/EditGameFileDialog.qml
    qml/SourcePortsPage.qml
    qml/SourcePortEditDialog.qml
    qml/TagsPage.qml
    qml/SettingsPage.qml
    qml/GetModsPage.qml
    qml/SetupPage.qml
)

# Alias the files into the module root so each one registers as a QML type
# of the module (subdirectory paths would otherwise stay out of the module
# namespace and break loadFromModule).
foreach(qml_file IN LISTS APP_QML_FILES)
    get_filename_component(qml_alias ${qml_file} NAME)
    set_source_files_properties(${qml_file} PROPERTIES QT_RESOURCE_ALIAS ${qml_alias})
endforeach()

qt_add_qml_module(doomedlauncher
    URI com.goshapps.doomlauncher
    VERSION 1.0
    QML_FILES ${APP_QML_FILES}
)

target_link_libraries(doomedlauncher PRIVATE
    Qt6::Core
    Qt6::Gui
    Qt6::Qml
    Qt6::Quick
    Qt6::QuickControls2
    Qt6::Sql
    Qt6::Network
    Qt6::Widgets
    Qt6::Concurrent
    KF6::Kirigami
    KF6::CoreAddons
    KF6::I18n
    KF6::ColorScheme
    KF6::Archive
    KF6::IconThemes
)

install(TARGETS doomedlauncher ${KDE_INSTALL_TARGETS_DEFAULT_ARGS})
