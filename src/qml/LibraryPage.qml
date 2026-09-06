import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

Kirigami.Page {
    id: page

    title: i18n("Library")
    padding: 0

    property int currentTabIndex: Math.max(0, Math.min(Launcher.lastTabIndex, Launcher.tabs.length - 1))
    property var currentTab: currentTabIndex >= 0 && Launcher.tabs.length > currentTabIndex ? Launcher.tabs[currentTabIndex] : null
    property bool isIdGamesTab: currentTab !== null && currentTab.kind === 4
    property int selectedGameFileId: -1
    property var selectedIds: []
    property string searchText: ""
    property bool showingDetails: false
    readonly property bool activePage: applicationWindow().pageStack.currentItem === page
    onActivePageChanged: {
        if (activePage && isIdGamesTab)
            reload()
    }

    function clearSelection() {
        fileList.currentIndex = -1
        selectedGameFileId = -1
        selectedIds = []
        detailsPanel.currentFile = null
    }

    function reload() {
        if (!currentTab)
            return
        if (isIdGamesTab) {
            Launcher.library.load(currentTab.kind, currentTab.tagId, searchText)
            if (searchText.length >= 3)
                Launcher.idGames.search("title", searchText)
            else
                Launcher.idGames.loadLatest()
        } else {
            Launcher.library.load(currentTab.kind, currentTab.tagId, searchText)
        }
    }

    function selectRow(row) {
        fileList.currentIndex = row
        const item = Launcher.library.get(row)
        selectedGameFileId = item.GameFileID !== undefined ? item.GameFileID : -1
        selectedIds = [selectedGameFileId]
        detailsPanel.currentFile = selectedGameFileId >= 0 ? item : null
    }

    function playSelected(forceDialog) {
        if (selectedGameFileId < 0) {
            applicationWindow().showPassiveNotification(i18n("Select a file to play."))
            return
        }
        if (isIdGamesTab) {
            Launcher.downloadIdGamesFile(Launcher.library.get(fileList.currentIndex), false)
            return
        }
        if (forceDialog || Launcher.showPlayDialog) {
            playDialog.openFor(selectedGameFileId)
        } else {
            const error = Launcher.playWithDefaults(selectedGameFileId)
            if (error !== "")
                applicationWindow().showPassiveNotification(error)
        }
    }

    Connections {
        target: Launcher
        function onLibraryChanged() { if (!page.isIdGamesTab) page.reload() }
        function onTabsChanged() {
            page.currentTabIndex = Math.max(0, Math.min(page.currentTabIndex, Launcher.tabs.length - 1))
            page.clearSelection()
            page.reload()
        }
    }

    Connections {
        target: Launcher.library
        function onModelReset() {
            const row = Launcher.library.rowForGameFileId(page.selectedGameFileId)
            page.selectRow(row)
        }
    }

    Connections {
        target: Launcher.idGames
        function onSearchFinished(results) {
            if (page.isIdGamesTab && page.activePage)
                Launcher.library.setExternalRows(results)
        }
        function onSearchFailed(message) {
            applicationWindow().showPassiveNotification(i18n("idgames search failed: %1", message))
        }
    }

    Timer {
        id: searchDebounce
        interval: 250
        onTriggered: page.reload()
    }

    titleDelegate: RowLayout {
        Layout.fillWidth: true
        spacing: Kirigami.Units.smallSpacing

        Kirigami.SearchField {
            Layout.fillWidth: true
            Layout.minimumWidth: Kirigami.Units.gridUnit * 12
            placeholderText: i18n("Search title, author, filename…")
            onTextChanged: {
                page.searchText = text
                searchDebounce.restart()
            }
            onAccepted: page.reload()
        }
    }

    actions: [
        Kirigami.Action {
            text: i18n("Play")
            icon.name: "media-playback-start"
            displayHint: Kirigami.DisplayHint.KeepVisible
            onTriggered: page.playSelected(false)
        },
        Kirigami.Action {
            text: i18n("Play with Options…")
            icon.name: "configure"
            onTriggered: page.playSelected(true)
        },
        Kirigami.Action {
            text: page.showingDetails ? i18n("Hide Details") : i18n("Show Details")
            icon.name: "document-properties"
            visible: !applicationWindow().wideScreen
            onTriggered: page.showingDetails = !page.showingDetails
        },
        Kirigami.Action {
            id: viewToggle
            text: Launcher.tileView ? i18n("List View") : i18n("Tile View")
            icon.name: Launcher.tileView ? "view-list-details" : "view-preview"
            onTriggered: Launcher.tileView = !Launcher.tileView
        },
        Kirigami.Action {
            text: i18n("Sort")
            icon.name: "view-sort"
            Kirigami.Action {
                text: i18n("Title")
                onTriggered: { Launcher.library.sortField = "Title"; Launcher.library.sortDescending = false }
            }
            Kirigami.Action {
                text: i18n("Author")
                onTriggered: { Launcher.library.sortField = "Author"; Launcher.library.sortDescending = false }
            }
            Kirigami.Action {
                text: i18n("Filename")
                onTriggered: { Launcher.library.sortField = "FileName"; Launcher.library.sortDescending = false }
            }
            Kirigami.Action {
                text: i18n("Rating")
                onTriggered: { Launcher.library.sortField = "Rating"; Launcher.library.sortDescending = true }
            }
            Kirigami.Action {
                text: i18n("Last Played")
                onTriggered: { Launcher.library.sortField = "LastPlayed"; Launcher.library.sortDescending = true }
            }
            Kirigami.Action {
                text: i18n("Recently Added")
                onTriggered: { Launcher.library.sortField = "Downloaded"; Launcher.library.sortDescending = true }
            }
        }
    ]

    // Tab strip + content
    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        QQC2.TabBar {
            id: tabBar
            Layout.fillWidth: true
            currentIndex: page.currentTabIndex
            onCurrentIndexChanged: {
                page.clearSelection()
                page.currentTabIndex = currentIndex
                Launcher.lastTabIndex = currentIndex
                page.reload()
            }

            Repeater {
                id: tabRepeater
                model: Launcher.tabs
                QQC2.TabButton {
                    required property var modelData
                    text: modelData.title
                    width: implicitWidth
                }
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: 0

            // Main file view (list or tiles)
            Item {
                visible: applicationWindow().wideScreen || !page.showingDetails
                Layout.fillWidth: true
                Layout.fillHeight: true

                QQC2.ScrollView {
                    anchors.fill: parent
                    visible: !Launcher.tileView

                    ListView {
                        id: fileList
                        clip: true
                        model: Launcher.library
                        currentIndex: -1

                        delegate: QQC2.ItemDelegate {
                            required property int index
                            required property string title
                            required property string fileName
                            required property string author
                            required property string lastPlayed
                            required property var rating
                            required property bool isIwad

                            width: ListView.view.width
                            highlighted: ListView.isCurrentItem

                            contentItem: RowLayout {
                                spacing: Kirigami.Units.largeSpacing
                                ColumnLayout {
                                    Layout.fillWidth: true
                                    spacing: 0
                                    QQC2.Label {
                                        Layout.fillWidth: true
                                        text: title
                                        elide: Text.ElideRight
                                        font.weight: Font.DemiBold
                                    }
                                    QQC2.Label {
                                        Layout.fillWidth: true
                                        text: fileName
                                            + (author !== "" ? "  ·  " + author : "")
                                            + (lastPlayed !== "" ? "  ·  " + i18n("played %1", lastPlayed) : "")
                                        elide: Text.ElideRight
                                        opacity: 0.7
                                        font: Kirigami.Theme.smallFont
                                    }
                                }
                                QQC2.Label {
                                    visible: isIwad
                                    text: i18n("IWAD")
                                    opacity: 0.7
                                }
                                QQC2.Label {
                                    visible: rating !== undefined && rating !== null && rating > 0
                                    text: "★ " + Number(rating).toFixed(1)
                                }
                            }

                            onClicked: page.selectRow(index)
                            onDoubleClicked: { page.selectRow(index); page.playSelected(false) }
                            onPressAndHold: { page.selectRow(index); contextMenu.popup() }

                            TapHandler {
                                acceptedButtons: Qt.RightButton
                                onTapped: { page.selectRow(index); contextMenu.popup() }
                            }
                        }

                        Kirigami.PlaceholderMessage {
                            anchors.centerIn: parent
                            width: parent.width - Kirigami.Units.gridUnit * 4
                            visible: fileList.count === 0
                            icon.name: page.isIdGamesTab ? "download" : "folder-games"
                            text: page.isIdGamesTab
                                  ? i18n("Search the idgames archive")
                                  : i18n("No files here yet")
                            explanation: page.isIdGamesTab
                                  ? i18n("Type at least three characters to search, or wait for the latest uploads to load.")
                                  : i18n("Add WADs and mods from the sidebar menu.")
                        }
                    }
                }

                QQC2.ScrollView {
                    anchors.fill: parent
                    visible: Launcher.tileView

                    GridView {
                        id: tileGrid
                        clip: true
                        model: Launcher.library
                        cellWidth: Kirigami.Units.gridUnit * 12
                        cellHeight: Kirigami.Units.gridUnit * 10
                        currentIndex: fileList.currentIndex

                        delegate: QQC2.ItemDelegate {
                            required property int index
                            required property string title
                            required property string imagePath

                            width: tileGrid.cellWidth - Kirigami.Units.smallSpacing
                            height: tileGrid.cellHeight - Kirigami.Units.smallSpacing
                            highlighted: GridView.isCurrentItem

                            contentItem: ColumnLayout {
                                spacing: Kirigami.Units.smallSpacing
                                Item {
                                    Layout.fillWidth: true
                                    Layout.fillHeight: true
                                    Image {
                                        anchors.fill: parent
                                        source: imagePath
                                        fillMode: Image.PreserveAspectCrop
                                        asynchronous: true
                                        visible: imagePath !== ""
                                    }
                                    Kirigami.Icon {
                                        anchors.centerIn: parent
                                        width: Kirigami.Units.iconSizes.huge
                                        height: width
                                        source: "folder-games"
                                        visible: imagePath === ""
                                    }
                                }
                                QQC2.Label {
                                    Layout.fillWidth: true
                                    text: title
                                    elide: Text.ElideRight
                                    horizontalAlignment: Text.AlignHCenter
                                }
                            }

                            onClicked: { fileList.currentIndex = index; page.selectRow(index) }
                            onDoubleClicked: { page.selectRow(index); page.playSelected(false) }
                            TapHandler {
                                acceptedButtons: Qt.RightButton
                                onTapped: { page.selectRow(index); contextMenu.popup() }
                            }
                        }
                    }
                }
            }

            Kirigami.Separator {
                Layout.fillHeight: true
                visible: applicationWindow().wideScreen
            }

            DetailsPanel {
                id: detailsPanel
                Layout.preferredWidth: Kirigami.Units.gridUnit * 20
                Layout.fillHeight: true
                Layout.fillWidth: !applicationWindow().wideScreen
                visible: applicationWindow().wideScreen || page.showingDetails
                isIdGames: page.isIdGamesTab
            }
        }
    }

    QQC2.Menu {
        id: contextMenu

        QQC2.MenuItem {
            text: i18n("Play…")
            icon.name: "media-playback-start"
            enabled: !page.isIdGamesTab
            onTriggered: page.playSelected(true)
        }
        QQC2.MenuItem {
            text: i18n("Download")
            icon.name: "download"
            visible: page.isIdGamesTab
            onTriggered: Launcher.downloadIdGamesFile(Launcher.library.get(fileList.currentIndex), false)
        }
        QQC2.MenuItem {
            text: i18n("View Text File")
            icon.name: "text-x-generic"
            enabled: !page.isIdGamesTab
            onTriggered: Launcher.openTextFile(page.selectedGameFileId)
        }
        QQC2.MenuItem {
            text: i18n("Open Containing Archive")
            icon.name: "document-open"
            enabled: !page.isIdGamesTab
            onTriggered: Launcher.openGameFile(page.selectedGameFileId)
        }
        QQC2.MenuItem {
            text: i18n("Edit…")
            icon.name: "document-edit"
            enabled: !page.isIdGamesTab
            onTriggered: editDialog.openFor(page.selectedGameFileId)
        }
        QQC2.MenuItem {
            text: i18n("Resync")
            icon.name: "view-refresh"
            enabled: !page.isIdGamesTab
            onTriggered: Launcher.resyncGameFiles([page.selectedGameFileId])
        }
        QQC2.MenuItem {
            text: i18n("Rename…")
            icon.name: "edit-rename"
            enabled: !page.isIdGamesTab
            onTriggered: {
                renameField.text = Launcher.library.get(fileList.currentIndex).FileName
                renamePrompt.open()
            }
        }
        QQC2.MenuItem {
            text: i18n("Delete…")
            icon.name: "edit-delete"
            enabled: !page.isIdGamesTab
            onTriggered: deletePrompt.open()
        }
        QQC2.MenuItem {
            text: i18n("View Web Page")
            icon.name: "internet-web-browser"
            visible: page.isIdGamesTab
            onTriggered: {
                const row = Launcher.library.get(fileList.currentIndex)
                if (row.idgamesUrl !== undefined && row.idgamesUrl !== "")
                    Launcher.openUrl(row.idgamesUrl)
            }
        }
    }

    PlayDialog {
        id: playDialog
    }

    EditGameFileDialog {
        id: editDialog
    }

    Kirigami.PromptDialog {
        id: deletePrompt
        title: i18n("Delete file")
        subtitle: i18n("Delete the selected file and its library data?")
        standardButtons: Kirigami.Dialog.Ok | Kirigami.Dialog.Cancel
        onAccepted: Launcher.deleteGameFiles([page.selectedGameFileId], true)
    }

    Kirigami.PromptDialog {
        id: renamePrompt
        title: i18n("Rename file")
        standardButtons: Kirigami.Dialog.Ok | Kirigami.Dialog.Cancel

        QQC2.TextField {
            id: renameField
            placeholderText: i18n("New file name")
        }

        onAccepted: {
            const error = Launcher.renameGameFile(page.selectedGameFileId, renameField.text)
            if (error !== "")
                applicationWindow().showPassiveNotification(error)
        }
    }

    Component.onCompleted: reload()
}
