#include "database.h"
#include "launcherapp.h"
#include "librarymodel.h"
#include "sourceportmodel.h"
#include <KAboutData>
#include <KLocalizedContext>
#include <KLocalizedString>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include <QQmlExpression>
#include <QQuickStyle>
#include <QTemporaryDir>
#include <QTest>

class QmlSmokeTest : public QObject
{
    Q_OBJECT
private Q_SLOTS:
    void pagesAndDialogs()
    {
        QTemporaryDir temporary;
        QVERIFY(temporary.isValid());
        qputenv("XDG_DATA_HOME", temporary.path().toUtf8());
        KLocalizedString::setApplicationDomain("doomedlauncher");
        QQuickStyle::setStyle(QStringLiteral("Basic"));
        LauncherApp launcher;
        QVERIFY(launcher.initialize().isEmpty());
        auto *database = launcher.findChild<Database *>();
        QVERIFY(database);
        const int iwad = database->insertGameFile({{QStringLiteral("FileName"), QStringLiteral("base.wad")}});
        database->insertIWad(QStringLiteral("Base"), QStringLiteral("base.wad"), iwad);
        database->insertSourcePort({{QStringLiteral("Name"), QStringLiteral("Test")},
            {QStringLiteral("Executable"), QStringLiteral("flatpak:org.zdoom.GZDoom")}});
        const int mod = database->insertGameFile({{QStringLiteral("FileName"), QStringLiteral("mod.wad")}});
        database->insertGameFile({{QStringLiteral("FileName"), QStringLiteral("extra.wad")}});
        database->insertTag(QStringLiteral("Test tag"), true);
        qobject_cast<SourcePortModel *>(launcher.sourcePorts())->reload();
        QVERIFY(!launcher.needsSetup());

        QQmlApplicationEngine engine;
        QStringList warnings;
        connect(&engine, &QQmlEngine::warnings, this, [&](const QList<QQmlError> &errors) {
            for (const auto &error : errors)
                warnings.append(error.toString());
        });
        engine.rootContext()->setContextProperty(QStringLiteral("Launcher"), &launcher);
        engine.rootContext()->setContextProperty(QStringLiteral("AboutData"), QVariant::fromValue(KAboutData::applicationData()));
        engine.rootContext()->setContextObject(new KLocalizedContext(&engine));
        engine.load(QUrl::fromLocalFile(QStringLiteral(QML_SOURCE_DIR "/Main.qml")));
        QVERIFY2(!engine.rootObjects().isEmpty(), qPrintable(warnings.join(QLatin1Char('\n'))));
        QTest::qWait(100);
        QObject *root = engine.rootObjects().first();
        auto evaluate = [&](QObject *scope, const QString &code) {
            QQmlExpression expression(QQmlEngine::contextForObject(scope), scope, code);
            QVariant value = expression.evaluate();
            if (expression.hasError()) {
                warnings.append(expression.error().toString());
                qWarning() << expression.error();
            }
            return value;
        };
        QObject *page = evaluate(root, QStringLiteral("pageStack.currentItem")).value<QObject *>();
        QVERIFY(page);
        auto *model = qobject_cast<LibraryModel *>(launcher.library());
        QVERIFY(model);
        model->load(LibraryModel::Local, -1, {});
        evaluate(page, QStringLiteral("selectRow(%1)").arg(model->rowForGameFileId(mod)));
        QCOMPARE(page->property("selectedGameFileId").toInt(), mod);
        model->setSortDescending(true);
        QCOMPARE(page->property("selectedGameFileId").toInt(), mod);
        QObject *playDialog = page->findChild<QObject *>(QStringLiteral("playDialog"));
        QObject *editDialog = page->findChild<QObject *>(QStringLiteral("editDialog"));
        QVERIFY(playDialog);
        QVERIFY(editDialog);
        evaluate(playDialog, QStringLiteral("openFor(%1)").arg(mod));
        evaluate(playDialog, QStringLiteral("additionalFiles = ['extra.wad']"));
        QCOMPARE(evaluate(playDialog, QStringLiteral("collect().additionalFiles[0]")).toString(), QStringLiteral("extra.wad"));
        evaluate(playDialog, QStringLiteral("close()"));
        evaluate(editDialog, QStringLiteral("openFor(%1)").arg(mod));
        evaluate(editDialog, QStringLiteral("fileTags = [1]; close()"));
        QVERIFY(database->tagsForGameFile(mod).isEmpty());
        model->load(LibraryModel::Local, -1, QStringLiteral("no-match"));
        QCOMPARE(page->property("selectedGameFileId").toInt(), -1);
        for (const QString &file : {QStringLiteral("SourcePortsPage.qml"), QStringLiteral("TagsPage.qml"),
             QStringLiteral("SettingsPage.qml"), QStringLiteral("SetupPage.qml"), QStringLiteral("GetModsPage.qml")}) {
            const QString url = QUrl::fromLocalFile(QStringLiteral(QML_SOURCE_DIR "/") + file).toString();
            evaluate(root, QStringLiteral("pushUnique('%1')").arg(url));
            QTest::qWait(100);
            if (file == QStringLiteral("SourcePortsPage.qml")) {
                QObject *portsPage = evaluate(root, QStringLiteral("pageStack.currentItem")).value<QObject *>();
                QVERIFY(portsPage);
                QObject *dialog = portsPage->findChild<QObject *>(QStringLiteral("sourcePortEditDialog"));
                QVERIFY(dialog);
                evaluate(dialog, QStringLiteral("openFor(1)"));
                QObject *extra = dialog->findChild<QObject *>(QStringLiteral("extraField"));
                QObject *saveDir = dialog->findChild<QObject *>(QStringLiteral("saveDirField"));
                QVERIFY(extra);
                QVERIFY(saveDir);
                QCOMPARE(extra->property("text").toString(), QString());
                QCOMPARE(saveDir->property("text").toString(), QString());
                evaluate(dialog, QStringLiteral("close()"));
            }
        }
        QStringList failures;
        for (const QString &warning : warnings) {
            if (warning.contains(QStringLiteral("ReferenceError")) || warning.contains(QStringLiteral("TypeError"))
                || warning.contains(QStringLiteral("Unable to assign")) || warning.contains(QStringLiteral("Cannot assign"))
                || warning.contains(QStringLiteral("is not a type")) || warning.contains(QStringLiteral("Cannot override")))
                failures.append(warning);
        }
        QVERIFY2(failures.isEmpty(), qPrintable(failures.join(QLatin1Char('\n'))));
    }
};
QTEST_MAIN(QmlSmokeTest)
#include "qml_smoke_test.moc"
