import QtQuick
import QtQuick.Controls as QQC2
import QtQuick.Layouts
import org.kde.kirigami as Kirigami

// Edits the library metadata of one game file, including its tags.
Kirigami.Dialog {
    id: dialog

    title: i18n("Edit File")
    preferredWidth: Kirigami.Units.gridUnit * 26
    standardButtons: Kirigami.Dialog.Save | Kirigami.Dialog.Cancel

    property int gameFileId: -1
    property var fileTags: []
    property var allTags: []

    function openFor(fileId) {
        gameFileId = fileId
        const file = Launcher.gameFile(fileId)
        titleField.text = file.Title ? String(file.Title) : ""
        authorField.text = file.Author ? String(file.Author) : ""
        ratingSpin.value = file.Rating !== undefined && file.Rating !== null ? Number(file.Rating) : 0
        commentsArea.text = file.Comments ? String(file.Comments) : ""
        allTags = Launcher.tagEntries()
        fileTags = Launcher.tagsOfGameFile(fileId)
        open()
    }

    onAccepted: {
        for (const tag of allTags)
            Launcher.setGameFileTag(gameFileId, tag.tagId, fileTags.indexOf(tag.tagId) !== -1)
        Launcher.updateGameFile(gameFileId, {
            Title: titleField.text,
            Author: authorField.text,
            Rating: ratingSpin.value,
            Comments: commentsArea.text
        })
    }

    Kirigami.FormLayout {
        QQC2.TextField {
            id: titleField
            Kirigami.FormData.label: i18n("Title:")
            Layout.fillWidth: true
        }
        QQC2.TextField {
            id: authorField
            Kirigami.FormData.label: i18n("Author:")
            Layout.fillWidth: true
        }
        QQC2.SpinBox {
            id: ratingSpin
            Kirigami.FormData.label: i18n("Rating (0–5):")
            from: 0
            to: 5
        }
        QQC2.TextArea {
            id: commentsArea
            Kirigami.FormData.label: i18n("Comments:")
            Layout.fillWidth: true
            Layout.preferredHeight: Kirigami.Units.gridUnit * 4
            wrapMode: TextEdit.Wrap
        }

        Kirigami.Separator {
            Kirigami.FormData.isSection: true
            Kirigami.FormData.label: i18n("Tags")
        }

        Repeater {
            model: dialog.allTags
            delegate: QQC2.CheckBox {
                required property var modelData
                text: modelData.name
                checked: dialog.fileTags.indexOf(modelData.tagId) !== -1
                onToggled: {
                    const tags = dialog.fileTags.filter(id => id !== modelData.tagId)
                    if (checked)
                        tags.push(modelData.tagId)
                    dialog.fileTags = tags
                }
            }
        }
    }
}
