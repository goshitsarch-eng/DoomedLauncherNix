import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

Kirigami.ScrollablePage {
    id: page

    title: i18n("Settings")

    ColumnLayout {
        spacing: Kirigami.Units.largeSpacing

        Kirigami.FormLayout {
            Layout.fillWidth: true

            QQC2.ComboBox {
                id: themeCombo
                Kirigami.FormData.label: i18n("Color scheme:")
                model: [i18n("Follow system"), i18n("Light"), i18n("Dark")]
                currentIndex: Launcher.theme.colorScheme
                onActivated: Launcher.theme.colorScheme = currentIndex
            }

            QQC2.CheckBox {
                Kirigami.FormData.label: i18n("Playing:")
                text: i18n("Show the play dialog before launching")
                checked: Launcher.showPlayDialog
                onToggled: Launcher.showPlayDialog = checked
            }

            QQC2.CheckBox {
                Kirigami.FormData.label: i18n("Library:")
                text: i18n("Use tile view")
                checked: Launcher.tileView
                onToggled: Launcher.tileView = checked
            }

            Kirigami.Separator {
                Kirigami.FormData.isSection: true
                Kirigami.FormData.label: i18n("Advanced")
            }

            Repeater {
                model: Launcher.configEntries()

                delegate: RowLayout {
                    id: configRow
                    required property var modelData
                    // The theme row above already covers ColorThemeType.
                    visible: modelData.Name !== "ColorThemeType"
                    Kirigami.FormData.label: modelData.Name + ":"
                    Layout.fillWidth: true

                    QQC2.ComboBox {
                        visible: String(configRow.modelData.AvailableValues) !== ""
                        Layout.fillWidth: true
                        model: String(configRow.modelData.AvailableValues)
                            .split(";").filter(value => value !== "")
                        Component.onCompleted: currentIndex =
                            Math.max(0, model.indexOf(String(configRow.modelData.Value)))
                        onActivated: Launcher.setConfigValue(configRow.modelData.Name, currentText)
                    }

                    QQC2.TextField {
                        visible: String(configRow.modelData.AvailableValues) === ""
                        Layout.fillWidth: true
                        text: String(configRow.modelData.Value)
                        onEditingFinished: Launcher.setConfigValue(configRow.modelData.Name, text)
                    }
                }
            }
        }
    }
}
