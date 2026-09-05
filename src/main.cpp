#include "launcherapp.h"
#include "version.h"

#include <KAboutData>
#include <KIconTheme>
#include <KLocalizedContext>
#include <KLocalizedString>

#include <QApplication>
#include <QCommandLineParser>
#include <QFileInfo>
#include <QIcon>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include <QQuickStyle>

int main(int argc, char *argv[])
{
    // Breeze icons for environments without a Plasma icon theme.
    KIconTheme::initTheme();

    QApplication app(argc, argv);
    KLocalizedString::setApplicationDomain("doomedlauncher");
    QApplication::setApplicationName(QStringLiteral("doomedlauncher"));
    QApplication::setOrganizationName(QStringLiteral("Gosh"));
    QApplication::setOrganizationDomain(QStringLiteral("goshapps.com"));
    QApplication::setDesktopFileName(QStringLiteral("com.goshapps.DoomLauncher"));
    QApplication::setWindowIcon(QIcon::fromTheme(QStringLiteral("com.goshapps.DoomLauncher")));

    KAboutData about(
        QStringLiteral("doomedlauncher"),
        i18n("Doom Launcher"),
        QStringLiteral(DOOMEDLAUNCHER_VERSION),
        i18n("Doom frontend and WAD library for KDE"),
        KAboutLicense::GPL_V3,
        i18n("© DoomLauncher / DoomedLauncherNix contributors"));
    about.addAuthor(i18n("Gosh"));
    about.setHomepage(QStringLiteral("https://github.com/GoshitsArch-eng/DoomedLauncherNix"));
    about.setBugAddress("https://github.com/GoshitsArch-eng/DoomedLauncherNix/issues");
    KAboutData::setApplicationData(about);

    QCommandLineParser parser;
    about.setupCommandLine(&parser);
    parser.addPositionalArgument(i18n("files"), i18n("Files to import into the library."));
    parser.process(app);
    about.processCommandLine(&parser);

    // The KDE Qt Quick Controls style everywhere; fall back to a
    // Kirigami-compatible style outside Plasma sessions.
    if (qEnvironmentVariableIsEmpty("QT_QUICK_CONTROLS_STYLE"))
        QQuickStyle::setStyle(QStringLiteral("org.kde.desktop"));

    LauncherApp launcher;
    const QString error = launcher.initialize();
    if (!error.isEmpty()) {
        qCritical("%s", qUtf8Printable(error));
        return 1;
    }

    QQmlApplicationEngine engine;
    engine.rootContext()->setContextProperty(QStringLiteral("Launcher"), &launcher);
    engine.rootContext()->setContextProperty(QStringLiteral("AboutData"),
                                             QVariant::fromValue(KAboutData::applicationData()));
    engine.rootContext()->setContextObject(new KLocalizedContext(&engine));
    engine.loadFromModule("com.goshapps.doomlauncher", "Main");
    if (engine.rootObjects().isEmpty())
        return 1;

    // Files passed on the command line (desktop file %F / file manager
    // "Open with") are imported into the library on startup.
    QList<QUrl> filesToOpen;
    const QStringList arguments = parser.positionalArguments();
    for (const QString &argument : arguments) {
        if (!argument.startsWith(QLatin1Char('-')) && QFileInfo::exists(argument))
            filesToOpen.append(QUrl::fromLocalFile(QFileInfo(argument).absoluteFilePath()));
    }
    if (!filesToOpen.isEmpty())
        launcher.addFiles(filesToOpen, false);

    return app.exec();
}
