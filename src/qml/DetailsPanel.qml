import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Dialogs
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

// Right-hand summary of the selected file: image, metadata, description
// and the screenshot/save/demo association lists.
QQC2.ScrollView {
    id: panel

    property var currentFile: null
    property bool isIdGames: false
    readonly property int gameFileId: currentFile && currentFile.GameFileID !== undefined
                                      ? currentFile.GameFileID : -1

    function refreshAssociations() {
        if (gameFileId < 0 || isIdGames) {
            screenshotsModel.clear()
            savesModel.clear()
            demosModel.clear()
            return
        }
        loadInto(screenshotsModel, 1)
        loadInto(demosModel, 2)
        loadInto(savesModel, 3)
    }

    function loadInto(listModel, fileType) {
        listModel.clear()
        const files = Launcher.associationFiles(gameFileId, fileType)
        for (let i = 0; i < files.length; ++i) {
            listModel.append({
                fileId: files[i].FileID,
                fileName: files[i].FileName,
                description: files[i].Description !== undefined ? String(files[i].Description) : "",
                fileType: fileType
            })
        }
    }

    onCurrentFileChanged: refreshAssociations()

    Connections {
        target: Launcher
        function onLibraryChanged() { panel.refreshAssociations() }
    }

    ListModel { id: screenshotsModel }
    ListModel { id: savesModel }
    ListModel { id: demosModel }

    ColumnLayout {
        width: panel.availableWidth
        spacing: Kirigami.Units.largeSpacing

        Item {
            Layout.fillWidth: true
            Layout.margins: Kirigami.Units.largeSpacing
            Layout.preferredHeight: Kirigami.Units.gridUnit * 10
            visible: panel.currentFile !== null

            Image {
                anchors.fill: parent
                source: panel.currentFile && panel.currentFile.imagePath !== undefined
                        ? panel.currentFile.imagePath : ""
                fillMode: Image.PreserveAspectFit
                asynchronous: true
                visible: source !== ""
            }
            Kirigami.Icon {
                anchors.centerIn: parent
                width: Kirigami.Units.iconSizes.enormous
                height: width
                source: "folder-games"
                visible: !panel.currentFile || panel.currentFile.imagePath === undefined
                         || panel.currentFile.imagePath === ""
            }
        }

        Kirigami.Heading {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing
            level: 2
            wrapMode: Text.Wrap
            text: panel.currentFile
                  ? (panel.currentFile.displayTitle !== undefined && panel.currentFile.displayTitle !== ""
                     ? panel.currentFile.displayTitle
                     : (panel.currentFile.Title !== undefined && panel.currentFile.Title !== ""
                        ? panel.currentFile.Title : panel.currentFile.FileName))
                  : i18n("Select a file")
        }

        QQC2.Label {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing
            visible: panel.currentFile !== null
            wrapMode: Text.Wrap
            opacity: 0.75
            text: {
                if (!panel.currentFile)
                    return ""
                let lines = []
                if (panel.currentFile.Author !== undefined && panel.currentFile.Author !== "")
                    lines.push(i18n("By %1", panel.currentFile.Author))
                if (panel.currentFile.FileName !== undefined)
                    lines.push(String(panel.currentFile.FileName))
                if (panel.currentFile.Map !== undefined && panel.currentFile.Map !== "")
                    lines.push(i18n("Maps: %1", panel.currentFile.Map))
                if (panel.currentFile.LastPlayed !== undefined && String(panel.currentFile.LastPlayed) !== "")
                    lines.push(i18n("Last played: %1", String(panel.currentFile.LastPlayed).substring(0, 10)))
                if (panel.currentFile.MinutesPlayed !== undefined && panel.currentFile.MinutesPlayed > 0)
                    lines.push(i18n("Time played: %1 min", panel.currentFile.MinutesPlayed))
                return lines.join("\n")
            }
        }

        QQC2.Label {
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing
            visible: panel.currentFile !== null && panel.currentFile.Description !== undefined
                     && panel.currentFile.Description !== ""
            wrapMode: Text.Wrap
            text: panel.currentFile && panel.currentFile.Description !== undefined
                  ? panel.currentFile.Description : ""
        }

        Repeater {
            model: [
                { title: i18n("Screenshots"), files: screenshotsModel },
                { title: i18n("Saves"), files: savesModel },
                { title: i18n("Demos"), files: demosModel }
            ]

            delegate: ColumnLayout {
                id: assocSection
                required property var modelData
                Layout.fillWidth: true
                Layout.leftMargin: Kirigami.Units.largeSpacing
                Layout.rightMargin: Kirigami.Units.largeSpacing
                spacing: Kirigami.Units.smallSpacing
                visible: !panel.isIdGames && panel.currentFile !== null && modelData.files.count > 0

                Kirigami.Heading {
                    level: 4
                    text: assocSection.modelData.title
                }

                Repeater {
                    model: assocSection.modelData.files
                    delegate: RowLayout {
                        required property string fileName
                        required property int fileId
                        required property int fileType
                        Layout.fillWidth: true

                        QQC2.Label {
                            Layout.fillWidth: true
                            text: fileName
                            elide: Text.ElideMiddle
                        }
                        QQC2.ToolButton {
                            icon.name: "document-open"
                            QQC2.ToolTip.text: i18n("Open")
                            QQC2.ToolTip.visible: hovered
                            onClicked: Launcher.openAssociationFile(fileName, fileType)
                        }
                        QQC2.ToolButton {
                            icon.name: "edit-delete"
                            QQC2.ToolTip.text: i18n("Remove from library")
                            QQC2.ToolTip.visible: hovered
                            onClicked: Launcher.deleteAssociationFile(fileId)
                        }
                    }
                }
            }
        }

        QQC2.Button {
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.bottomMargin: Kirigami.Units.largeSpacing
            visible: !panel.isIdGames && panel.currentFile !== null
            text: i18n("Import Screenshot/Save/Demo…")
            icon.name: "document-import"
            onClicked: importDialog.open()
        }
    }

    FileDialog {
        id: importDialog
        title: i18n("Import files")
        fileMode: FileDialog.OpenFiles
        onAccepted: Launcher.importAssociationFiles(panel.gameFileId, selectedFiles)
    }
}
