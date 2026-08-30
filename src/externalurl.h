#pragma once

#include <QString>
#include <QUrl>

namespace ExternalUrl
{
inline QUrl fromHttpInput(const QString &input)
{
    const QUrl target = QUrl::fromUserInput(input);
    if (!target.isValid()
        || (target.scheme() != QLatin1String("https") && target.scheme() != QLatin1String("http")))
        return {};
    return target;
}
}