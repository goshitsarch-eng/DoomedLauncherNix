import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Dialogs
import org.kde.kirigami as Kirigami

Kirigami.ApplicationWindow {
    id: root

    title: i18n("Doom Launcher")
    visible: true
    width: 1100
    height: 720
    minimumWidth: 720
    minimumHeight: 480

    pageStack.initialPage: libraryPageComponent

    Component {
        id: libraryPageComponent
        LibraryPage {}
    }

    Component {
        id: sourcePortsPageComponent
        SourcePortsPage {}
    }

    Component {
        id: tagsPageComponent
        TagsPage {}
    }

    Component {
        id: settingsPageComponent
        SettingsPage {}
    }

    Component {
        id: getModsPageComponent
        GetModsPage {}
    }

    Component {
        id: setupPageComponent
        SetupPage {}
    }

    function pushUnique(component) {
        // Avoid stacking the same page twice.
        while (pageStack.depth > 1)
            pageStack.pop()
        pageStack.push(component)
    }

    globalDrawer: Kirigami.GlobalDrawer {
        isMenu: !root.wideScreen

        actions: [
            Kirigami.Action {
                text: i18n("Library")
                icon.name: "view-list-details"
                onTriggered: {
                    while (root.pageStack.depth > 1)
                        root.pageStack.pop()
                }
            },
            Kirigami.Action {
                text: i18n("Add Files…")
                icon.name: "list-add"
                onTriggered: addFilesDialog.open()
            },
            Kirigami.Action {
                text: i18n("Add IWADs…")
                icon.name: "list-add"
                onTriggered: addIwadsDialog.open()
            },
            Kirigami.Action {
                text: i18n("Add Directory…")
                icon.name: "folder-add"
                onTriggered: addFolderDialog.open()
            },
            Kirigami.Action {
                text: i18n("Load WADs from Steam/GOG…")
                icon.name: "folder-download"
                onTriggered: Launcher.scanGameStores()
            },
            Kirigami.Action {
                text: i18n("Get Mods (idgames)…")
                icon.name: "download"
                onTriggered: root.pushUnique(getModsPageComponent)
            },
            Kirigami.Action {
                separator: true
            },
            Kirigami.Action {
                text: i18n("Source Ports…")
                icon.name: "applications-games"
                onTriggered: root.pushUnique(sourcePortsPageComponent)
            },
            Kirigami.Action {
                text: i18n("Tags…")
                icon.name: "tag"
                onTriggered: root.pushUnique(tagsPageComponent)
            },
            Kirigami.Action {
                text: i18n("Setup Assistant…")
                icon.name: "tools-wizard"
                onTriggered: root.pushUnique(setupPageComponent)
            },
            Kirigami.Action {
                separator: true
            },
            Kirigami.Action {
                text: i18n("Settings…")
                icon.name: "settings-configure"
                onTriggered: root.pushUnique(settingsPageComponent)
            },
            Kirigami.Action {
                text: i18n("About")
                icon.name: "help-about"
                onTriggered: root.pageStack.pushDialogLayer(aboutPage)
            }
        ]
    }

    Component {
        id: aboutPage
        Kirigami.AboutPage {
            aboutData: AboutData
        }
    }

    FileDialog {
        id: addFilesDialog
        title: i18n("Select Game Files")
        fileMode: FileDialog.OpenFiles
        nameFilters: [
            i18n("Game files (*.zip *.wad *.pk3 *.pk7 *.ipk3 *.deh *.bex)"),
            i18n("All files (*)")
        ]
        onAccepted: Launcher.addFiles(selectedFiles, false)
    }

    FileDialog {
        id: addIwadsDialog
        title: i18n("Select IWADs")
        fileMode: FileDialog.OpenFiles
        nameFilters: [i18n("IWADs (*.wad *.iwad *.ipk3 *.zip)"), i18n("All files (*)")]
        onAccepted: Launcher.addFiles(selectedFiles, true)
    }

    FolderDialog {
        id: addFolderDialog
        title: i18n("Select Folder to Import")
        onAccepted: Launcher.addDirectory(selectedFolder, true)
    }

    Connections {
        target: Launcher
        function onToast(message) {
            root.showPassiveNotification(message)
        }
    }

    Component.onCompleted: {
        if (Launcher.needsSetup) {
            Launcher.detectSourcePorts()
            Launcher.scanGameStores()
            root.pageStack.push(setupPageComponent)
        }
    }
}
