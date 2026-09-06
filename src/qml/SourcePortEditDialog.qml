import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Dialogs
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

Kirigami.Dialog {
    id: dialog
    objectName: "sourcePortEditDialog"

    title: editingId > 0 ? i18n("Edit Source Port") : i18n("Add Source Port")
    preferredWidth: Kirigami.Units.gridUnit * 26
    standardButtons: Kirigami.Dialog.Save | Kirigami.Dialog.Cancel

    property int editingId: -1

    function openFor(sourcePortId) {
        editingId = sourcePortId
        if (sourcePortId > 0) {
            const row = Launcher.sourcePorts.get(Launcher.sourcePorts.rowForId(sourcePortId))
            nameField.text = row.Name ? String(row.Name) : ""
            executableField.text = row.Executable ? String(row.Executable) : ""
            directoryField.text = row.Directory ? String(row.Directory) : ""
            extensionsField.text = row.SupportedExtensions ? String(row.SupportedExtensions) : ""
            fileOptionField.text = row.FileOption ? String(row.FileOption) : "-file"
            extraField.text = row.ExtraParameters ? String(row.ExtraParameters) : ""
            saveDirField.text = row.AltSaveDirectory ? String(row.AltSaveDirectory) : ""
        } else {
            nameField.text = ""
            executableField.text = ""
            directoryField.text = ""
            extensionsField.text = ".wad,.deh,.bex,.pk3,.pk7,.ipk3,.zip"
            fileOptionField.text = "-file"
            extraField.text = ""
            saveDirField.text = ""
        }
        visible = true
    }

    onAccepted: {
        Launcher.sourcePorts.save(editingId, {
            name: nameField.text,
            executable: executableField.text,
            directory: directoryField.text,
            supportedExtensions: extensionsField.text,
            fileOption: fileOptionField.text,
            extraParameters: extraField.text,
            altSaveDirectory: saveDirField.text
        })
    }

    Kirigami.FormLayout {
        QQC2.TextField {
            id: nameField
            Kirigami.FormData.label: i18n("Name:")
            Layout.fillWidth: true
        }

        RowLayout {
            Kirigami.FormData.label: i18n("Executable:")
            Layout.fillWidth: true
            QQC2.TextField {
                id: executableField
                Layout.fillWidth: true
                placeholderText: i18n("gzdoom, flatpak:org.zdoom.GZDoom, or a full path")
            }
            QQC2.ToolButton {
                icon.name: "document-open"
                onClicked: executablePicker.open()
            }
        }

        QQC2.TextField {
            id: directoryField
            Kirigami.FormData.label: i18n("Directory:")
            Layout.fillWidth: true
        }

        QQC2.TextField {
            id: extensionsField
            Kirigami.FormData.label: i18n("Supported extensions:")
            Layout.fillWidth: true
        }

        QQC2.TextField {
            id: fileOptionField
            Kirigami.FormData.label: i18n("File parameter:")
            Layout.fillWidth: true
        }

        QQC2.TextField {
            id: extraField
            objectName: "extraField"
            Kirigami.FormData.label: i18n("Extra parameters:")
            Layout.fillWidth: true
        }

        QQC2.TextField {
            id: saveDirField
            objectName: "saveDirField"
            Kirigami.FormData.label: i18n("Save game directory:")
            Layout.fillWidth: true
        }
    }

    FileDialog {
        id: executablePicker
        title: i18n("Select Executable")
        onAccepted: {
            const path = Launcher.localFilePath(selectedFile)
            executableField.text = path
            const slash = path.lastIndexOf("/")
            if (slash > 0)
                directoryField.text = path.substring(0, slash)
        }
    }
}
