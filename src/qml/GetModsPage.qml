import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

// Search the idgames archive and download mods straight into the library.
Kirigami.ScrollablePage {
    id: page

    title: i18n("Get Mods")

    property var results: []

    Connections {
        target: Launcher.idGames
        function onSearchFinished(list) {
            if (applicationWindow().pageStack.currentItem === page)
                page.results = list
        }
        function onSearchFailed(message) {
            applicationWindow().showPassiveNotification(i18n("Search failed: %1", message))
        }
        function onDownloadProgress(fileName, received, total) {
            downloadBar.indeterminate = total <= 0
            if (total > 0)
                downloadBar.value = received / total
        }
        function onDownloadFinished(fileName, localPath) {
        }
        function onDownloadFailed(fileName, message) {
        }
    }

    header: ColumnLayout {
        spacing: 0

        RowLayout {
            Layout.margins: Kirigami.Units.largeSpacing
            spacing: Kirigami.Units.smallSpacing

            QQC2.ComboBox {
                id: searchTypeCombo
                model: [i18n("Title"), i18n("Author"), i18n("Filename"), i18n("Description")]
                property var apiTypes: ["title", "author", "filename", "descrption"]
            }

            Kirigami.SearchField {
                id: searchField
                Layout.fillWidth: true
                placeholderText: i18n("Search the idgames archive…")
                onAccepted: Launcher.idGames.search(
                    searchTypeCombo.apiTypes[searchTypeCombo.currentIndex], text)
            }

            QQC2.BusyIndicator {
                running: Launcher.idGames.busy
                visible: running
            }
        }

        QQC2.ProgressBar {
            id: downloadBar
            Layout.fillWidth: true
            Layout.leftMargin: Kirigami.Units.largeSpacing
            Layout.rightMargin: Kirigami.Units.largeSpacing
            visible: Launcher.idGames.downloading
            from: 0
            to: 1
        }
    }

    ListView {
        id: resultsList
        model: page.results

        delegate: QQC2.ItemDelegate {
            required property var modelData

            width: ListView.view.width

            contentItem: RowLayout {
                spacing: Kirigami.Units.largeSpacing

                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 0
                    QQC2.Label {
                        Layout.fillWidth: true
                        text: modelData.Title !== "" ? modelData.Title : modelData.FileName
                        elide: Text.ElideRight
                        font.weight: Font.DemiBold
                    }
                    QQC2.Label {
                        Layout.fillWidth: true
                        text: modelData.FileName
                            + (modelData.Author !== "" ? "  ·  " + modelData.Author : "")
                            + (modelData.ReleaseDate !== "" ? "  ·  " + modelData.ReleaseDate : "")
                        elide: Text.ElideRight
                        opacity: 0.7
                        font: Kirigami.Theme.smallFont
                    }
                }
                QQC2.Label {
                    visible: modelData.Rating !== undefined && modelData.Rating > 0
                    text: "★ " + Number(modelData.Rating).toFixed(1)
                }
                QQC2.ToolButton {
                    icon.name: "download"
                    enabled: !Launcher.idGames.downloading
                    QQC2.ToolTip.text: i18n("Download and import")
                    QQC2.ToolTip.visible: hovered
                    onClicked: Launcher.downloadIdGamesFile(modelData, false)
                }
                QQC2.ToolButton {
                    icon.name: "media-playback-start"
                    enabled: !Launcher.idGames.downloading
                    QQC2.ToolTip.text: i18n("Download and play")
                    QQC2.ToolTip.visible: hovered
                    onClicked: Launcher.downloadIdGamesFile(modelData, true)
                }
                QQC2.ToolButton {
                    icon.name: "internet-web-browser"
                    visible: modelData.idgamesUrl !== undefined && modelData.idgamesUrl !== ""
                    QQC2.ToolTip.text: i18n("View web page")
                    QQC2.ToolTip.visible: hovered
                    onClicked: Launcher.openUrl(modelData.idgamesUrl)
                }
            }
        }

        Kirigami.PlaceholderMessage {
            anchors.centerIn: parent
            width: parent.width - Kirigami.Units.gridUnit * 4
            visible: resultsList.count === 0 && !Launcher.idGames.busy
            icon.name: "download"
            text: i18n("Search the idgames archive")
            explanation: i18n("Downloads are fetched from the configured mirror and imported into your library automatically.")
        }
    }

    Component.onCompleted: Launcher.idGames.loadLatest()
}
