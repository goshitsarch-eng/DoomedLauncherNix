import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

Kirigami.ScrollablePage {
    id: page

    title: i18n("Source Ports")

    actions: [
        Kirigami.Action {
            text: i18n("Detect Installed Ports")
            icon.name: "system-search"
            enabled: !Launcher.detecting
            onTriggered: Launcher.detectSourcePorts()
        },
        Kirigami.Action {
            text: i18n("Add Manually…")
            icon.name: "list-add"
            onTriggered: editDialog.openFor(-1)
        }
    ]

    Connections {
        target: Launcher
        function onDetectFinished(added) {
            applicationWindow().showPassiveNotification(
                added > 0 ? i18n("Added %1 detected source port(s)", added)
                          : i18n("No new source ports found"))
        }
    }

    ListView {
        id: portsList
        model: Launcher.sourcePorts

        header: RowLayout {
            width: portsList.width
            QQC2.BusyIndicator {
                id: detectBusy
                running: Launcher.detecting
                visible: running
                Layout.alignment: Qt.AlignHCenter
            }
        }

        delegate: QQC2.ItemDelegate {
            required property int index
            required property int sourcePortId
            required property string name
            required property string executable

            width: ListView.view.width

            contentItem: RowLayout {
                spacing: Kirigami.Units.largeSpacing
                Kirigami.Icon {
                    source: "applications-games"
                    Layout.preferredWidth: Kirigami.Units.iconSizes.medium
                    Layout.preferredHeight: Kirigami.Units.iconSizes.medium
                }
                ColumnLayout {
                    Layout.fillWidth: true
                    spacing: 0
                    QQC2.Label {
                        text: name
                        font.weight: Font.DemiBold
                    }
                    QQC2.Label {
                        text: executable
                        opacity: 0.7
                        font: Kirigami.Theme.smallFont
                    }
                }
                QQC2.ToolButton {
                    icon.name: "document-edit"
                    QQC2.ToolTip.text: i18n("Edit")
                    QQC2.ToolTip.visible: hovered
                    onClicked: editDialog.openFor(sourcePortId)
                }
                QQC2.ToolButton {
                    icon.name: "edit-delete"
                    QQC2.ToolTip.text: i18n("Delete")
                    QQC2.ToolTip.visible: hovered
                    onClicked: {
                        deletePrompt.portId = sourcePortId
                        deletePrompt.open()
                    }
                }
            }
        }

        Kirigami.PlaceholderMessage {
            anchors.centerIn: parent
            width: parent.width - Kirigami.Units.gridUnit * 4
            visible: portsList.count === 0
            icon.name: "applications-games"
            text: i18n("No source ports configured")
            explanation: i18n("Use “Detect Installed Ports” to find GZDoom and friends (native, Flatpak or snap), or add one manually.")
        }
    }

    SourcePortEditDialog {
        id: editDialog
    }

    Kirigami.PromptDialog {
        id: deletePrompt
        property int portId: -1
        title: i18n("Delete source port")
        subtitle: i18n("Remove this source port from the launcher?")
        standardButtons: Kirigami.Dialog.Ok | Kirigami.Dialog.Cancel
        onAccepted: Launcher.sourcePorts.remove(portId)
    }
}
