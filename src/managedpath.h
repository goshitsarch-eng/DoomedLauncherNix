#pragma once

#include <QDir>
#include <QFileInfo>
#include <QString>

namespace ManagedPath
{
inline bool isSafeFileName(const QString &name)
{
    return !name.isEmpty() && name != QLatin1String(".") && name != QLatin1String("..")
        && !name.contains(QLatin1Char('/')) && !name.contains(QLatin1Char('\\'))
        && !QDir::isAbsolutePath(name) && QFileInfo(name).fileName() == name;
}
}
