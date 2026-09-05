import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Dialogs
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

// First-run assistant: detect source ports, add IWADs, ready to play.
Kirigami.ScrollablePage {
    id: page

    title: i18n("Setup Assistant")

    Connections {
        target: Launcher
        function onDetectFinished(added) {
            detectResult.text = added > 0
                ? i18n("Added %1 source port(s).", added)
                : i18n("No new source ports found. Install GZDoom (native, Flatpak or snap) and detect again, or add one manually.")
        }
    }

    ColumnLayout {
        spacing: Kirigami.Units.largeSpacing

        Kirigami.Heading {
            Layout.fillWidth: true
            Layout.margins: Kirigami.Units.largeSpacing
            level: 1
            text: i18n("Welcome to Doom Launcher")
            wrapMode: Text.Wrap
        }

        QQC2.Label {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing
            wrapMode: Text.Wrap
            text: i18n("Two things are needed before playing: a source port (the engine, e.g. GZDoom) and at least one IWAD (the game data, e.g. DOOM2.WAD or Freedoom).")
        }

        Kirigami.Card {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing

            header: Kirigami.Heading {
                level: 2
                text: i18n("1. Source port")
            }

            contentItem: ColumnLayout {
                QQC2.Label {
                    Layout.fillWidth: true
                    wrapMode: Text.Wrap
                    text: Launcher.sourcePorts.count > 0
                          ? i18np("%1 source port is configured.", "%1 source ports are configured.", Launcher.sourcePorts.count)
                          : i18n("No source port configured yet.")
                }
                QQC2.Label {
                    id: detectResult
                    Layout.fillWidth: true
                    wrapMode: Text.Wrap
                    visible: text !== ""
                    opacity: 0.8
                    text: ""
                }
                RowLayout {
                    QQC2.Button {
                        text: i18n("Detect Installed Ports")
                        icon.name: "system-search"
                        enabled: !Launcher.detecting
                        onClicked: Launcher.detectSourcePorts()
                    }
                    QQC2.BusyIndicator {
                        id: detectBusy
                        running: Launcher.detecting
                        visible: running
                    }
                }
            }
        }

        Kirigami.Card {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing

            header: Kirigami.Heading {
                level: 2
                text: i18n("2. IWADs")
            }

            contentItem: ColumnLayout {
                QQC2.Label {
                    Layout.fillWidth: true
                    wrapMode: Text.Wrap
                    text: i18n("Add your game WADs (doom2.wad, doom.wad, heretic.wad, …). Freedoom works too and is freely available.")
                }
                RowLayout {
                    QQC2.Button {
                        text: i18n("Add IWAD Files…")
                        icon.name: "list-add"
                        onClicked: iwadDialog.open()
                    }
                    QQC2.Button {
                        text: i18n("Load from Steam/GOG…")
                        icon.name: "folder-download"
                        onClicked: Launcher.scanGameStores()
                    }
                }
            }
        }

        Kirigami.Card {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing

            header: Kirigami.Heading {
                level: 2
                text: i18n("3. Play")
            }

            contentItem: ColumnLayout {
                QQC2.Label {
                    Layout.fillWidth: true
                    wrapMode: Text.Wrap
                    text: Launcher.needsSetup
                          ? i18n("Finish the steps above, then head to the library.")
                          : i18n("You are ready. Head back to the library and double-click a file to play, or grab mods from the idgames archive.")
                }
                QQC2.Button {
                    text: i18n("Go to Library")
                    icon.name: "go-previous"
                    enabled: !Launcher.needsSetup
                    onClicked: {
                        while (applicationWindow().pageStack.depth > 1)
                            applicationWindow().pageStack.pop()
                    }
                }
            }
        }
    }

    FileDialog {
        id: iwadDialog
        title: i18n("Select IWADs")
        fileMode: FileDialog.OpenFiles
        nameFilters: [i18n("IWADs (*.wad *.iwad *.ipk3 *.zip)"), i18n("All files (*)")]
        onAccepted: Launcher.addFiles(selectedFiles, true)
    }
}
