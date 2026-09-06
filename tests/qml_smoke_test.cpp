#include "database.h"
#include "launcherapp.h"
#include "librarymodel.h"
#include <KAboutData>
#include <KLocalizedContext>
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
        QObject *root = engine.rootObjects().first();
        auto evaluate = [&](QObject *scope, const QString &code) {
            QQmlExpression expression(QQmlEngine::contextForObject(scope), scope, code);
            QVariant value = expression.evaluate();
            if (expression.hasError())
                warnings.append(expression.error().toString());
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
        evaluate(page, QStringLiteral("playDialog.openFor(%1)").arg(mod));
        evaluate(page, QStringLiteral("playDialog.additionalFiles = ['extra.wad']"));
        QCOMPARE(evaluate(page, QStringLiteral("playDialog.collect().additionalFiles[0]")).toString(), QStringLiteral("extra.wad"));
        evaluate(page, QStringLiteral("playDialog.close()"));
        evaluate(page, QStringLiteral("editDialog.openFor(%1)").arg(mod));
        evaluate(page, QStringLiteral("editDialog.fileTags = [1]; editDialog.reject()"));
        QVERIFY(database->tagsForGameFile(mod).isEmpty());
        model->load(LibraryModel::Local, -1, QStringLiteral("no-match"));
        QCOMPARE(page->property("selectedGameFileId").toInt(), -1);
        for (const QString &component : {QStringLiteral("sourcePortsPageComponent"), QStringLiteral("tagsPageComponent"),
             QStringLiteral("settingsPageComponent"), QStringLiteral("setupPageComponent"), QStringLiteral("getModsPageComponent")}) {
            evaluate(root, QStringLiteral("pushUnique(%1)").arg(component));
            QTest::qWait(30);
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
