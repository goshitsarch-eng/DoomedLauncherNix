cmake_minimum_required(VERSION 3.20)

project(DoomedLauncher VERSION 4.0.0 LANGUAGES CXX)

set(QT_MIN_VERSION "6.8.0")
set(KF_MIN_VERSION "6.6.0")

find_package(ECM ${KF_MIN_VERSION} REQUIRED NO_MODULE)
list(APPEND CMAKE_MODULE_PATH ${ECM_MODULE_PATH})

include(KDEInstallDirs)
include(KDECMakeSettings)
include(KDECompilerSettings NO_POLICY_SCOPE)
include(ECMInstallIcons)
include(FeatureSummary)

find_package(Qt6 ${QT_MIN_VERSION} REQUIRED COMPONENTS
    Core
    Gui
    Qml
    Quick
    QuickControls2
    Sql
    Network
    Widgets
    Concurrent
)

find_package(KF6 ${KF_MIN_VERSION} REQUIRED COMPONENTS
    Kirigami
    CoreAddons
    I18n
    ColorScheme
    Archive
    IconThemes
)

qt_policy(SET QTP0001 NEW)

add_subdirectory(src)

install(PROGRAMS data/com.goshapps.DoomLauncher.desktop DESTINATION ${KDE_INSTALL_APPDIR})
install(FILES data/com.goshapps.DoomLauncher.metainfo.xml DESTINATION ${KDE_INSTALL_METAINFODIR})
install(FILES data/com.goshapps.DoomLauncher.png DESTINATION ${KDE_INSTALL_ICONDIR}/hicolor/256x256/apps)

feature_summary(WHAT ALL INCLUDE_QUIET_PACKAGES FATAL_ON_MISSING_REQUIRED_PACKAGES)
