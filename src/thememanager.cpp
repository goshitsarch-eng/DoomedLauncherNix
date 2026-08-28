#include "thememanager.h"

#include "database.h"

#include <KColorSchemeManager>

#include <QGuiApplication>
#include <QStyleHints>

ThemeManager::ThemeManager(Database *db, QObject *parent)
    : QObject(parent)
    , m_db(db)
{
    const QString stored = m_db->configValue(QStringLiteral("ColorThemeType"),
                                             QStringLiteral("System"));
    if (stored.compare(QStringLiteral("Dark"), Qt::CaseInsensitive) == 0)
        m_colorScheme = 2;
    else if (stored.compare(QStringLiteral("System"), Qt::CaseInsensitive) == 0)
        m_colorScheme = 0;
    else
        m_colorScheme = 1; // "Default" was the light theme.
}

void ThemeManager::apply()
{
    activate(m_colorScheme);
}

void ThemeManager::setColorScheme(int scheme)
{
    if (m_colorScheme == scheme)
        return;
    m_colorScheme = scheme;
    const char *names[] = {"System", "Default", "Dark"};
    m_db->setConfigValue(QStringLiteral("ColorThemeType"),
                         QString::fromLatin1(names[qBound(0, scheme, 2)]));
    activate(scheme);
    Q_EMIT colorSchemeChanged();
}

void ThemeManager::activate(int scheme)
{
    KColorSchemeManager *manager = KColorSchemeManager::instance();

    switch (scheme) {
    case 1: {
        const QModelIndex index = manager->indexForScheme(QStringLiteral("Breeze Light"));
        if (index.isValid())
            manager->activateScheme(index);
        else
            QGuiApplication::styleHints()->setColorScheme(Qt::ColorScheme::Light);
        break;
    }
    case 2: {
        const QModelIndex index = manager->indexForScheme(QStringLiteral("Breeze Dark"));
        if (index.isValid())
            manager->activateScheme(index);
        else
            QGuiApplication::styleHints()->setColorScheme(Qt::ColorScheme::Dark);
        break;
    }
    default:
        // Follow the system: activate the default (empty) scheme.
        manager->activateScheme(QModelIndex());
        QGuiApplication::styleHints()->unsetColorScheme();
        break;
    }
}
