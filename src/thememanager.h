#pragma once

#include <QObject>

class Database;

// Light/dark/system color scheme handling. On KDE the app follows the
// global Breeze color scheme by default ("System"); the user can force
// Breeze Light or Breeze Dark from the settings page. The choice is
// stored in the Configuration table (ColorThemeType: System, Default =
// light, Dark) so it round-trips with the previous releases' setting.
class ThemeManager : public QObject
{
    Q_OBJECT
    // 0 = System, 1 = Light, 2 = Dark
    Q_PROPERTY(int colorScheme READ colorScheme WRITE setColorScheme NOTIFY colorSchemeChanged)

public:
    explicit ThemeManager(Database *db, QObject *parent = nullptr);

    int colorScheme() const { return m_colorScheme; }
    void setColorScheme(int scheme);

    // Applies the stored preference; call once at startup.
    void apply();

signals:
    void colorSchemeChanged();

private:
    void activate(int scheme);

    Database *m_db;
    int m_colorScheme = 0;
};
