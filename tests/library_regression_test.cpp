#include "archivereader.h"
#include "database.h"
#include "gamelauncher.h"
#include "launcherapp.h"
#include "launcherpaths.h"
#include "librarymodel.h"
#include "libraryops.h"
#include "sourceportmodel.h"

#include <QDir>
#include <QFile>
#include <QSignalSpy>
#include <QTemporaryDir>
#include <QTest>
#include <QUrl>

class LibraryRegressionTest : public QObject
{
    Q_OBJECT
    QTemporaryDir temporary;
    LauncherApp launcher;
    Database *database = nullptr;

    QString write(const QString &relative, const QByteArray &contents)
    {
        const QString path = temporary.filePath(relative);
        QDir().mkpath(QFileInfo(path).absolutePath());
        QFile file(path);
        if (!file.open(QIODevice::WriteOnly) || file.write(contents) != contents.size())
            return {};
        return path;
    }

private Q_SLOTS:
    void initTestCase()
    {
        QVERIFY(temporary.isValid());
        qputenv("XDG_DATA_HOME", temporary.path().toUtf8());
        QVERIFY(launcher.initialize().isEmpty());
        database = launcher.findChild<Database *>();
        QVERIFY(database);
    }

    void promoteAndRenameIwad()
    {
        LibraryOps ops(database);
        const QString path = write(QStringLiteral("imports/base.wad"), QByteArray("IWAD\0\0\0\0\x0c\0\0\0", 12));
        QCOMPARE(ops.addFiles({path}, false).added.size(), 1);
        const int id = database->gameFileByName(QStringLiteral("base.wad")).value(QStringLiteral("GameFileID")).toInt();
        QVERIFY(!database->iwadGameFileIds().contains(id));
        QCOMPARE(ops.addFiles({path}, true).added.size(), 1);
        QVERIFY(database->iwadGameFileIds().contains(id));
        const int modId = database->insertGameFile({{QStringLiteral("FileName"), QStringLiteral("mod.wad")},
            {QStringLiteral("SettingsFiles"), QStringLiteral("base.wad;other.wad")}});
        QVERIFY(ops.renameGameFile(id, QStringLiteral("renamed.wad")).isEmpty());
        QCOMPARE(database->gameFileById(modId).value(QStringLiteral("SettingsFiles")).toString(), QStringLiteral("renamed.wad;other.wad"));
        QCOMPARE(database->iwads().first().toMap().value(QStringLiteral("FileName")).toString(), QStringLiteral("renamed.wad"));
    }

    void onlineIdsDoNotUseLocalArtOrIwadStatus()
    {
        const int id = database->iwadGameFileIds().first();
        const QString image = write(QStringLiteral("art #1.png"), "image fixture");
        database->insertFile(id, image, 1, -1, QStringLiteral("Art"));
        LibraryModel model(database);
        model.load(LibraryModel::IWads, -1, {});
        QVERIFY(model.get(0).value(QStringLiteral("isIwad")).toBool());
        QCOMPARE(QUrl(model.get(0).value(QStringLiteral("imagePath")).toString()).toLocalFile(), image);
        model.setExternalRows({QVariantMap{{QStringLiteral("GameFileID"), id}, {QStringLiteral("Title"), QStringLiteral("Online")}}});
        QVERIFY(!model.get(0).value(QStringLiteral("isIwad")).toBool());
        QVERIFY(model.get(0).value(QStringLiteral("imagePath")).toString().isEmpty());
    }

    void extractedMembersDoNotCollide()
    {
        const QString first = write(QStringLiteral("one/shared.wad"), "FIRST");
        const QString second = write(QStringLiteral("two/shared.wad"), "SECOND");
        const QString zipOne = temporary.filePath(QStringLiteral("one.zip"));
        const QString zipTwo = temporary.filePath(QStringLiteral("two.zip"));
        QVERIFY(ArchiveReader::zipDirectory(QFileInfo(first).absolutePath(), zipOne));
        QVERIFY(ArchiveReader::zipDirectory(QFileInfo(second).absolutePath(), zipTwo));
        const QString extractedOne = ArchiveReader::extract(zipOne, QStringLiteral("shared.wad"), LauncherPaths::tempDir());
        const QString extractedTwo = ArchiveReader::extract(zipTwo, QStringLiteral("shared.wad"), LauncherPaths::tempDir());
        QVERIFY(!extractedOne.isEmpty());
        QVERIFY(!extractedTwo.isEmpty());
        QVERIFY(extractedOne != extractedTwo);
        QFile data(extractedOne);
        QVERIFY(data.open(QIODevice::ReadOnly));
        QCOMPARE(data.readAll(), QByteArray("FIRST"));
    }

    void manualPortsRefreshSetupAndDefaults()
    {
        auto *ports = qobject_cast<SourcePortModel *>(launcher.sourcePorts());
        QSignalSpy changed(&launcher, &LauncherApp::libraryChanged);
        ports->save(-1, {{QStringLiteral("name"), QStringLiteral("Fixture")},
            {QStringLiteral("executable"), QStringLiteral("/bin/true")},
            {QStringLiteral("directory"), QStringLiteral("/tmp")}});
        QVERIFY(changed.count() > 0);
        QVERIFY(!launcher.needsSetup());
        const int id = database->iwadGameFileIds().first();
        const int port = ports->idForRow(0);
        QCOMPARE(launcher.playDefaults(id).value(QStringLiteral("sourcePortId")).toInt(), port);
        QCOMPARE(launcher.playDefaults(id).value(QStringLiteral("iwadId")).toInt(), database->iwads().first().toMap().value(QStringLiteral("IWadID")).toInt());
        ports->remove(port);
        QVERIFY(launcher.needsSetup());
        QCOMPARE(launcher.localFilePath(QUrl::fromLocalFile(QStringLiteral("/tmp/engine #1 ü"))), QStringLiteral("/tmp/engine #1 ü"));
    }

    void missingAdditionalModsAreErrors()
    {
        const int port = database->insertSourcePort({{QStringLiteral("Name"), QStringLiteral("Test")},
            {QStringLiteral("Executable"), QStringLiteral("flatpak:org.zdoom.GZDoom")}});
        LaunchRequest request;
        request.sourcePortId = port;
        request.gameFileId = database->iwadGameFileIds().first();
        request.additionalFiles = {QStringLiteral("missing.wad")};
        request.saveStatistics = false;
        GameLauncher gameLauncher(database);
        QVERIFY(gameLauncher.formatCommand(request).contains(QStringLiteral("no longer in the library")));
    }
};

QTEST_MAIN(LibraryRegressionTest)
#include "library_regression_test.moc"
