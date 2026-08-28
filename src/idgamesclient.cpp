#include "idgamesclient.h"

#include "database.h"
#include "launcherpaths.h"

#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QNetworkReply>
#include <QUrl>

IdGamesClient::IdGamesClient(Database *db, QObject *parent)
    : QObject(parent)
    , m_db(db)
{
}

void IdGamesClient::setBusy(bool busy)
{
    if (m_busy == busy)
        return;
    m_busy = busy;
    Q_EMIT busyChanged();
}

void IdGamesClient::search(const QString &type, const QString &query)
{
    request(QStringLiteral("action=search&type=%1&query=%2&sort=rating&dir=desc")
                .arg(type, QString::fromUtf8(QUrl::toPercentEncoding(query))));
}

void IdGamesClient::loadLatest()
{
    request(QStringLiteral("action=latestfiles&limit=50"));
}

void IdGamesClient::request(const QString &query)
{
    const QString base = m_db->configValue(QStringLiteral("IdGamesUrl"),
                                           QStringLiteral("https://www.doomworld.com/idgames/"));
    const QString apiPage = m_db->configValue(QStringLiteral("ApiPage"), QStringLiteral("api/api.php"));
    const QUrl url(base + apiPage + QStringLiteral("?") + query + QStringLiteral("&out=json"));

    setBusy(true);
    QNetworkReply *reply = m_network.get(QNetworkRequest(url));
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        reply->deleteLater();
        setBusy(false);
        if (reply->error() != QNetworkReply::NoError) {
            Q_EMIT searchFailed(reply->errorString());
            return;
        }

        const QJsonDocument doc = QJsonDocument::fromJson(reply->readAll());
        const QJsonObject content = doc.object().value(QStringLiteral("content")).toObject();
        QJsonArray fileArray;
        const QJsonValue fileValue = content.value(QStringLiteral("file"));
        if (fileValue.isArray())
            fileArray = fileValue.toArray();
        else if (fileValue.isObject())
            fileArray.append(fileValue);

        QVariantList results;
        for (const QJsonValue &value : std::as_const(fileArray)) {
            const QJsonObject object = value.toObject();
            QVariantMap row;
            // Mapped onto the GameFiles column names so the same library
            // model/delegates render idgames rows unchanged.
            row.insert(QStringLiteral("GameFileID"), object.value(QStringLiteral("id")).toInt());
            row.insert(QStringLiteral("FileName"), object.value(QStringLiteral("filename")).toString());
            row.insert(QStringLiteral("Title"), object.value(QStringLiteral("title")).toString());
            row.insert(QStringLiteral("Author"), object.value(QStringLiteral("author")).toString());
            row.insert(QStringLiteral("Description"),
                       object.value(QStringLiteral("description")).toString()
                           .replace(QStringLiteral("<br>"), QStringLiteral("\n")));
            row.insert(QStringLiteral("Rating"), object.value(QStringLiteral("rating")).toDouble());
            row.insert(QStringLiteral("ReleaseDate"), object.value(QStringLiteral("date")).toString());
            row.insert(QStringLiteral("dir"), object.value(QStringLiteral("dir")).toString());
            row.insert(QStringLiteral("idgamesUrl"), object.value(QStringLiteral("url")).toString());
            row.insert(QStringLiteral("size"), object.value(QStringLiteral("size")).toInt());
            results.append(row);
        }
        Q_EMIT searchFinished(results);
    });
}

void IdGamesClient::download(const QString &dir, const QString &fileName)
{
    QString mirror = m_db->configValue(QStringLiteral("MirrorUrl"),
                                       QStringLiteral("https://www.quaddicted.com/files/idgames/"));
    // Databases from old releases may still carry an ftp:// mirror;
    // Qt 6 has no FTP backend, so fall back to an https mirror.
    if (mirror.startsWith(QStringLiteral("ftp://"), Qt::CaseInsensitive))
        mirror = QStringLiteral("https://www.quaddicted.com/files/idgames/");
    if (!mirror.endsWith(QLatin1Char('/')))
        mirror += QLatin1Char('/');
    QString path = dir;
    if (!path.isEmpty() && !path.endsWith(QLatin1Char('/')))
        path += QLatin1Char('/');

    const QUrl url(mirror + path + fileName);
    QNetworkRequest networkRequest(url);
    networkRequest.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                                QNetworkRequest::NoLessSafeRedirectPolicy);
    QNetworkReply *reply = m_network.get(networkRequest);

    connect(reply, &QNetworkReply::downloadProgress, this,
            [this, fileName](qint64 received, qint64 total) {
                Q_EMIT downloadProgress(fileName, received, total);
            });
    connect(reply, &QNetworkReply::finished, this, [this, reply, fileName]() {
        reply->deleteLater();
        if (reply->error() != QNetworkReply::NoError) {
            Q_EMIT downloadFailed(fileName, reply->errorString());
            return;
        }
        LauncherPaths::ensureLayout();
        const QString localPath = LauncherPaths::tempDir() + QLatin1Char('/') + fileName;
        QFile file(localPath);
        if (!file.open(QIODevice::WriteOnly)) {
            Q_EMIT downloadFailed(fileName, tr("Cannot write %1").arg(localPath));
            return;
        }
        file.write(reply->readAll());
        file.close();
        Q_EMIT downloadFinished(fileName, localPath);
    });
}

QString IdGamesClient::webUrl(const QString &idgamesUrl) const
{
    return idgamesUrl;
}
