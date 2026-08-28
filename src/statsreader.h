#pragma once

#include <QDateTime>
#include <QString>
#include <QStringList>

// Parses per-level statistics written by source ports, matching what the
// previous releases stored in the Stats table:
//  - levelstat.txt   (dsda-doom, PrBoom+, Crispy, Woof! and friends)
//  - statdump output (Chocolate Doom family, -statdump)
//  - ZDoom-family save games (.zds zips carrying globals.json)
namespace StatsReader
{
struct Stats {
    QString mapName;
    int killCount = 0;
    int totalKills = 0;
    int itemCount = 0;
    int totalItems = 0;
    int secretCount = 0;
    int totalSecrets = 0;
    double levelTime = 0.0; // seconds
    int skill = -1;         // 1-5, -1 when unknown
};

QList<Stats> parseLevelstat(const QString &text);
QList<Stats> parseStatdump(const QString &text);
// Reads globals.json from a ZDoom .zds save (a zip archive).
QList<Stats> parseZDoomSave(const QString &savePath);

// Which stats mechanism a source port executable supports.
enum class Kind { None, Levelstat, Statdump, ZDoomSave };
Kind kindForExecutable(const QString &executable);
}
