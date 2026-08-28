#include "statsreader.h"

#include <KZip>
#include <KArchiveDirectory>
#include <KArchiveFile>

#include <QFileInfo>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QRegularExpression>

namespace
{
double parseClockTime(const QString &value)
{
    // "m:ss.ss" or "h:mm:ss(.ss)".
    const QStringList parts = value.split(QLatin1Char(':'), Qt::SkipEmptyParts);
    double seconds = 0.0;
    for (const QString &part : parts)
        seconds = seconds * 60.0 + part.toDouble();
    return seconds;
}

// Revived monsters can push the kill count past the total; clamp like the
// previous releases did.
StatsReader::Stats clamped(StatsReader::Stats stats)
{
    if (stats.killCount > stats.totalKills)
        stats.killCount = stats.totalKills;
    return stats;
}
}

namespace StatsReader
{
QList<Stats> parseLevelstat(const QString &text)
{
    QList<Stats> result;
    // e.g. "E1M1 - 0:56.63 (0:56)  K: 4/6  I: 4/37  S: 0/3"
    static const QRegularExpression lineRe(QStringLiteral(
        "^\\s*(\\S+)\\s*-\\s*((?:\\d+:)?\\d+:\\d+(?:\\.\\d+)?)\\s*\\([^)]*\\)\\s*"
        "K:\\s*(\\d+)/(\\d+)[^I]*I:\\s*(\\d+)/(\\d+)[^S]*S:\\s*(\\d+)/(\\d+)"));

    const QStringList lines = text.split(QLatin1Char('\n'), Qt::SkipEmptyParts);
    for (const QString &line : lines) {
        const auto match = lineRe.match(line);
        if (!match.hasMatch())
            continue;
        Stats stats;
        stats.mapName = match.captured(1);
        stats.levelTime = parseClockTime(match.captured(2));
        stats.killCount = match.captured(3).toInt();
        stats.totalKills = match.captured(4).toInt();
        stats.itemCount = match.captured(5).toInt();
        stats.totalItems = match.captured(6).toInt();
        stats.secretCount = match.captured(7).toInt();
        stats.totalSecrets = match.captured(8).toInt();
        result.append(clamped(stats));
    }
    return result;
}

QList<Stats> parseStatdump(const QString &text)
{
    QList<Stats> result;
    // Matching strategy from the previous releases: strip all whitespace,
    // then scan the blob per level block.
    QString stripped = text;
    stripped.remove(QLatin1Char(' '));
    stripped.remove(QLatin1Char('\t'));
    stripped.remove(QLatin1Char('\r'));
    stripped.remove(QLatin1Char('\n'));

    static const QRegularExpression blockRe(QStringLiteral(
        "=+(\\w+(?:/\\w+)?)=+Time:(\\d+):(\\d+)\\(par:\\d+:\\d+\\)\\w+\\(\\w+\\):"
        "Kills:(\\d+)(?:/(\\d+)(?:\\(-?\\d+%\\))?)?"
        "Items:(\\d+)(?:/(\\d+)(?:\\(-?\\d+%\\))?)?"
        "Secrets:(\\d+)(?:/(\\d+))?"));

    auto it = blockRe.globalMatch(stripped);
    while (it.hasNext()) {
        const auto match = it.next();
        Stats stats;
        stats.mapName = match.captured(1);
        stats.levelTime = match.captured(2).toInt() * 60 + match.captured(3).toInt();
        stats.killCount = match.captured(4).toInt();
        stats.totalKills = match.captured(5).isEmpty() ? stats.killCount : match.captured(5).toInt();
        stats.itemCount = match.captured(6).toInt();
        stats.totalItems = match.captured(7).isEmpty() ? stats.itemCount : match.captured(7).toInt();
        stats.secretCount = match.captured(8).toInt();
        stats.totalSecrets =
            match.captured(9).isEmpty() ? stats.secretCount : match.captured(9).toInt();
        result.append(clamped(stats));
    }
    return result;
}

QList<Stats> parseZDoomSave(const QString &savePath)
{
    QList<Stats> result;

    KZip zip(savePath);
    if (!zip.open(QIODevice::ReadOnly))
        return result;
    const KArchiveEntry *entry = zip.directory()->entry(QStringLiteral("globals.json"));
    if (!entry || !entry->isFile()) {
        zip.close();
        return result;
    }
    const QByteArray json = static_cast<const KArchiveFile *>(entry)->data();
    zip.close();

    const QJsonObject root = QJsonDocument::fromJson(json).object();

    // Skill lives in the cvar blobs, off by one from the 1-5 UI value.
    int skill = -1;
    const QString importantCvars = root.value(QStringLiteral("importantcvars")).toString();
    if (!importantCvars.isEmpty()) {
        const QStringList cvars = importantCvars.split(QLatin1Char('\\'));
        const int index = cvars.indexOf(QStringLiteral("skill"));
        if (index >= 0 && index + 1 < cvars.size()) {
            bool ok = false;
            const int value = cvars.at(index + 1).toInt(&ok);
            if (ok)
                skill = value + 1;
        }
    } else if (const QJsonObject serverCvars =
                   root.value(QStringLiteral("servercvars")).toObject();
               !serverCvars.isEmpty()) {
        const QJsonValue value = serverCvars.value(QStringLiteral("skill"));
        if (!value.isUndefined())
            skill = value.toString(QString::number(value.toInt(-1))).toInt() + 1;
    }

    const QJsonArray levels = root.value(QStringLiteral("statistics")).toObject()
                                  .value(QStringLiteral("levels")).toArray();
    for (const QJsonValue &levelValue : levels) {
        const QJsonObject level = levelValue.toObject();
        Stats stats;
        stats.mapName = level.value(QStringLiteral("levelname")).toString();
        stats.totalKills = level.value(QStringLiteral("totalkills")).toInt();
        stats.killCount = level.value(QStringLiteral("killcount")).toInt();
        stats.totalItems = level.value(QStringLiteral("totalitems")).toInt();
        stats.itemCount = level.value(QStringLiteral("itemcount")).toInt();
        stats.totalSecrets = level.value(QStringLiteral("totalsecrets")).toInt();
        stats.secretCount = level.value(QStringLiteral("secretcount")).toInt();
        stats.levelTime = level.value(QStringLiteral("leveltime")).toDouble() / 35.0; // tics
        stats.skill = skill;
        result.append(clamped(stats));
    }
    return result;
}

Kind kindForExecutable(const QString &executable)
{
    const QString name = QFileInfo(executable.trimmed()).fileName().toLower();

    for (const char *token : {"gzdoom", "uzdoom", "vkdoom", "lzdoom", "zandronum", "zdoom",
                              "org.zdoom"}) {
        if (executable.contains(QLatin1String(token), Qt::CaseInsensitive))
            return Kind::ZDoomSave;
    }
    for (const char *token : {"crispy-doom", "crispy-heretic", "so-doom", "inter-heretic",
                              "boom-plus", "prboom-plus", "dsda-doom", "nyan-doom", "fdwl",
                              "woof", "nugget-doom", "cherry-doom"}) {
        if (name.contains(QLatin1String(token)))
            return Kind::Levelstat;
    }
    for (const char *token : {"chocolate-doom", "cndoom", "crl-doom", "inter-doom"}) {
        if (name.contains(QLatin1String(token)))
            return Kind::Statdump;
    }
    return Kind::None;
}
}
