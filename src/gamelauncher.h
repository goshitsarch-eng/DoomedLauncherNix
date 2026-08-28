#pragma once

#include <QDateTime>
#include <QObject>
#include <QProcess>
#include <QStringList>
#include <QVariantMap>

class Database;

// Everything the play dialog collects for one launch.
struct LaunchRequest {
    int gameFileId = -1;
    int sourcePortId = -1;
    int iwadId = -1;            // IWads.IWadID
    QString map;                // empty = don't warp
    QString skill;              // empty = default
    QString extraParams;
    bool extraParamsOnly = false;
    bool loadLatestSave = false;
    bool saveStatistics = true; // record per-level stats when the port supports it
    QString recordDemoFile;     // path to write, empty = no recording
    QString playDemoFile;       // path to play back, empty = none
    QStringList additionalFiles; // FileName values of extra mods (matches SettingsFiles)
    bool remember = true;       // persist the choices on the GameFiles row
    bool isIwadLaunch = false;  // the selected file is itself the IWAD
};

// Builds source port command lines the same way the previous frontend
// did (zip contents are extracted to the Temp directory, .deh/.bex files
// go behind -deh, ZDoom-family ports get +map warps) and tracks the
// running process to record play time when it exits.
class GameLauncher : public QObject
{
    Q_OBJECT

public:
    explicit GameLauncher(Database *db, QObject *parent = nullptr);

    // Returns an empty string on success, otherwise the error message.
    QString launch(const LaunchRequest &request);

    // Preview of the command line for the play dialog.
    QString formatCommand(const LaunchRequest &request);

    bool hasActiveSessions() const { return m_activeSessions > 0; }

signals:
    void processExited(int gameFileId, int minutesPlayed);
    void launchFailed(const QString &message);

signals:
    void statisticsRecorded(int gameFileId, int levelCount);

private:
    // How a session's statistics get collected, decided at launch time.
    struct StatSession {
        int kind = 0;            // StatsReader::Kind as int
        QString statFile;        // levelstat.txt / statdump output path
        QStringList watchDirs;   // save dirs scanned for new .zds files
        QDateTime launchTime;
    };

    struct BuiltCommand {
        QString program;
        QStringList arguments;
        QString workingDirectory;
        QString error;
        StatSession statSession;
    };

    BuiltCommand build(const LaunchRequest &request);
    void prepareStatistics(BuiltCommand &command, const QVariantMap &sourcePort);
    void collectStatistics(const StatSession &session, int gameFileId, int sourcePortId);
    QStringList gameFileLaunchPaths(const QVariantMap &gameFile, const QVariantMap &sourcePort,
                                    const QStringList &specificFiles, QString *error);
    QString extractIwad(const QVariantMap &iwadGameFile, const QVariantMap &sourcePort, QString *error);
    QString latestSaveFile(const QVariantMap &sourcePort) const;

    static bool isZDoomFamily(const QString &executable);
    static QStringList warpArguments(const QString &map, bool zdoomFamily);

    Database *m_db;
    int m_activeSessions = 0;
};
