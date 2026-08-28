import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

Kirigami.ScrollablePage {
    id: page

    title: i18n("Tags")

    property var tagList: Launcher.tagEntries()

    function refresh() {
        tagList = Launcher.tagEntries()
    }

    actions: [
        Kirigami.Action {
            text: i18n("New Tag…")
            icon.name: "list-add"
            onTriggered: {
                newTagField.text = ""
                newTagTabCheck.checked = false
                newTagDialog.open()
            }
        }
    ]

    Connections {
        target: Launcher
        function onTabsChanged() { page.refresh() }
    }

    ListView {
        id: tagListView
        model: page.tagList

        delegate: QQC2.ItemDelegate {
            required property var modelData

            width: ListView.view.width

            contentItem: RowLayout {
                spacing: Kirigami.Units.largeSpacing
                Kirigami.Icon {
                    source: "tag"
                    Layout.preferredWidth: Kirigami.Units.iconSizes.smallMedium
                    Layout.preferredHeight: Kirigami.Units.iconSizes.smallMedium
                }
                QQC2.Label {
                    Layout.fillWidth: true
                    text: modelData.name
                }
                QQC2.CheckBox {
                    text: i18n("Show as tab")
                    checked: modelData.hasTab
                    onToggled: Launcher.updateTag(modelData.tagId, modelData.name, checked)
                }
                QQC2.ToolButton {
                    icon.name: "edit-delete"
                    QQC2.ToolTip.text: i18n("Delete tag")
                    QQC2.ToolTip.visible: hovered
                    onClicked: {
                        deletePrompt.tagId = modelData.tagId
                        deletePrompt.open()
                    }
                }
            }
        }

        Kirigami.PlaceholderMessage {
            anchors.centerIn: parent
            width: parent.width - Kirigami.Units.gridUnit * 4
            visible: tagListView.count === 0
            icon.name: "tag"
            text: i18n("No tags yet")
            explanation: i18n("Tags group your files; a tag can also appear as its own library tab. Assign tags from a file's Edit dialog.")
        }
    }

    Kirigami.PromptDialog {
        id: newTagDialog
        title: i18n("New tag")
        standardButtons: Kirigami.Dialog.Ok | Kirigami.Dialog.Cancel

        ColumnLayout {
            QQC2.TextField {
                id: newTagField
                Layout.fillWidth: true
                placeholderText: i18n("Tag name")
            }
            QQC2.CheckBox {
                id: newTagTabCheck
                text: i18n("Show as tab")
            }
        }

        onAccepted: {
            if (newTagField.text.trim() !== "")
                Launcher.createTag(newTagField.text.trim(), newTagTabCheck.checked)
        }
    }

    Kirigami.PromptDialog {
        id: deletePrompt
        property int tagId: -1
        title: i18n("Delete tag")
        subtitle: i18n("Delete this tag? Files keep their other tags.")
        standardButtons: Kirigami.Dialog.Ok | Kirigami.Dialog.Cancel
        onAccepted: Launcher.deleteTag(tagId)
    }
}
