import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

// The launch options dialog: source port, IWAD, warp map, skill, extra
// parameters and additional files, with a live command preview.
Kirigami.Dialog {
    id: dialog

    title: i18n("Play")
    preferredWidth: Kirigami.Units.gridUnit * 28
    standardButtons: Kirigami.Dialog.Cancel

    property int gameFileId: -1
    property var maps: []
    property var iwads: []
    property var additionalFiles: []

    customFooterActions: [
        Kirigami.Action {
            text: i18n("Launch")
            icon.name: "media-playback-start"
            onTriggered: {
                const error = Launcher.play(dialog.collect())
                if (error !== "")
                    applicationWindow().showPassiveNotification(error)
                else
                    dialog.close()
            }
        }
    ]

    function open(fileId) {
        gameFileId = fileId
        const defaults = Launcher.playDefaults(fileId)
        iwads = Launcher.iwadEntries()
        maps = defaults.maps !== undefined ? defaults.maps : []

        const ports = Launcher.sourcePorts
        portCombo.currentIndex = Math.max(0, ports.rowForId(defaults.sourcePortId))

        let iwadIndex = 0
        for (let i = 0; i < iwads.length; ++i) {
            if (iwads[i].iwadId === defaults.iwadId)
                iwadIndex = i
        }
        iwadCombo.currentIndex = iwadIndex

        warpCheck.checked = defaults.map !== ""
        mapCombo.editText = defaults.map
        const parsedSkill = parseInt(defaults.skill)
        skillCombo.currentIndex = isNaN(parsedSkill) ? 3 : Math.min(4, Math.max(0, parsedSkill - 1))
        extraParamsField.text = defaults.extraParams
        extraOnlyCheck.checked = defaults.extraParamsOnly
        loadSaveCheck.checked = defaults.loadLatestSave
        statsCheck.checked = defaults.saveStatistics !== undefined ? defaults.saveStatistics : true
        recordCheck.checked = false
        rememberCheck.checked = true
        additionalFiles = defaults.additionalFiles !== undefined ? defaults.additionalFiles : []
        visible = true
    }

    function collect() {
        return {
            gameFileId: gameFileId,
            sourcePortId: Launcher.sourcePorts.idForRow(portCombo.currentIndex),
            iwadId: iwads.length > iwadCombo.currentIndex && iwadCombo.currentIndex >= 0
                    ? iwads[iwadCombo.currentIndex].iwadId : -1,
            map: warpCheck.checked ? mapCombo.editText : "",
            skill: warpCheck.checked ? String(skillCombo.currentIndex + 1) : "",
            extraParams: extraParamsField.text,
            extraParamsOnly: extraOnlyCheck.checked,
            loadLatestSave: loadSaveCheck.checked,
            saveStatistics: statsCheck.checked,
            recordDemo: recordCheck.checked,
            additionalFiles: additionalFiles,
            remember: rememberCheck.checked
        }
    }

    Kirigami.FormLayout {
        QQC2.ComboBox {
            id: portCombo
            Kirigami.FormData.label: i18n("Source port:")
            model: Launcher.sourcePorts
            textRole: "name"
            Layout.fillWidth: true
        }

        QQC2.ComboBox {
            id: iwadCombo
            Kirigami.FormData.label: i18n("IWAD:")
            model: dialog.iwads.map(entry => entry.name)
            Layout.fillWidth: true
        }

        QQC2.CheckBox {
            id: warpCheck
            Kirigami.FormData.label: i18n("Warp to map:")
        }

        QQC2.ComboBox {
            id: mapCombo
            enabled: warpCheck.checked
            editable: true
            model: dialog.maps
            Layout.fillWidth: true
        }

        QQC2.ComboBox {
            id: skillCombo
            Kirigami.FormData.label: i18n("Skill:")
            enabled: warpCheck.checked
            model: [
                i18n("1 – I'm too young to die"),
                i18n("2 – Hey, not too rough"),
                i18n("3 – Hurt me plenty"),
                i18n("4 – Ultra-Violence"),
                i18n("5 – Nightmare!")
            ]
            currentIndex: 3
            Layout.fillWidth: true
        }

        QQC2.TextField {
            id: extraParamsField
            Kirigami.FormData.label: i18n("Extra parameters:")
            Layout.fillWidth: true
        }

        QQC2.CheckBox {
            id: extraOnlyCheck
            text: i18n("Extra parameters only")
        }

        QQC2.CheckBox {
            id: loadSaveCheck
            text: i18n("Load latest save")
        }

        QQC2.CheckBox {
            id: statsCheck
            text: i18n("Save statistics")
            checked: true
        }

        QQC2.CheckBox {
            id: recordCheck
            text: i18n("Record demo")
        }

        QQC2.CheckBox {
            id: rememberCheck
            text: i18n("Remember these settings")
            checked: true
        }

        QQC2.Label {
            Kirigami.FormData.label: i18n("Additional files:")
            visible: dialog.additionalFiles.length > 0
            text: dialog.additionalFiles.join("\n")
            wrapMode: Text.Wrap
            Layout.fillWidth: true
        }

        QQC2.Label {
            Kirigami.FormData.label: i18n("Command:")
            text: dialog.visible ? Launcher.commandPreview(dialog.collect()) : ""
            wrapMode: Text.Wrap
            font: Kirigami.Theme.smallFont
            opacity: 0.7
            Layout.fillWidth: true
        }
    }
}
