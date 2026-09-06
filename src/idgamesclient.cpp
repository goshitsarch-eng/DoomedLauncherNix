#include "idgamesclient.h"

#include "database.h"
#include "launcherpaths.h"
#include "managedpath.h"

#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QJsonObject>
#include <QNetworkReply>
#include <QUrl>
#include <QTemporaryDir>
#include <memory>

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

    if (m_searchReply) {
        auto previous = m_searchReply;
        m_searchReply = nullptr;
        previous->abort();
    }
    setBusy(true);
    QNetworkRequest networkRequest(url);
    networkRequest.setTransferTimeout(30000);
    QNetworkReply *reply = m_network.get(networkRequest);
    m_searchReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        reply->deleteLater();
        if (m_searchReply != reply)
            return;
        m_searchReply = nullptr;
        setBusy(false);
        if (reply->error() != QNetworkReply::NoError) {
            Q_EMIT searchFailed(reply->errorString());
            return;
        }

        QJsonParseError parseError;
        const QJsonDocument doc = QJsonDocument::fromJson(reply->readAll(), &parseError);
        if (parseError.error != QJsonParseError::NoError || !doc.isObject()) {
            Q_EMIT searchFailed(tr("The archive returned an invalid JSON response."));
            return;
        }
        if (doc.object().contains(QStringLiteral("error"))) {
            Q_EMIT searchFailed(doc.object().value(QStringLiteral("error")).toObject()
                                    .value(QStringLiteral("message")).toString(tr("Archive request failed.")));
            return;
        }
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
    if (m_downloading)
        return;
    if (!ManagedPath::isSafeFileName(fileName)) {
        Q_EMIT downloadFailed(fileName, tr("Invalid download filename."));
        return;
    }
    for (const QString &part : dir.split(QLatin1Char('/'), Qt::SkipEmptyParts)) {
        if (!ManagedPath::isSafeFileName(part)) {
            Q_EMIT downloadFailed(fileName, tr("Invalid archive directory."));
            return;
        }
    }
    if (dir.startsWith(QLatin1Char('/'))) {
        Q_EMIT downloadFailed(fileName, tr("Invalid archive directory."));
        return;
    }
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

    QUrl url(mirror);
    if (!url.isValid() || url.host().isEmpty()
        || (url.scheme() != QStringLiteral("https") && url.scheme() != QStringLiteral("http"))) {
        Q_EMIT downloadFailed(fileName, tr("The mirror must be an HTTP or HTTPS URL."));
        return;
    }
    url.setPath(url.path() + path + fileName);
    LauncherPaths::ensureLayout();
    auto temporary = std::make_shared<QTemporaryDir>(LauncherPaths::tempDir() + QStringLiteral("/download-XXXXXX"));
    auto file = std::make_shared<QFile>(temporary->filePath(fileName));
    if (!temporary->isValid() || !file->open(QIODevice::WriteOnly)) {
        Q_EMIT downloadFailed(fileName, tr("Cannot create the download file."));
        return;
    }
    QNetworkRequest networkRequest(url);
    networkRequest.setTransferTimeout(30000);
    networkRequest.setAttribute(QNetworkRequest::RedirectPolicyAttribute,
                                QNetworkRequest::NoLessSafeRedirectPolicy);
    QNetworkReply *reply = m_network.get(networkRequest);
    m_downloading = true;
    Q_EMIT downloadingChanged();
    auto writeFailed = std::make_shared<bool>(false);
    connect(reply, &QIODevice::readyRead, this, [reply, file, writeFailed]() {
        const QByteArray data = reply->readAll();
        if (file->write(data) != data.size()) {
            *writeFailed = true;
            reply->abort();
        }
    });

    connect(reply, &QNetworkReply::downloadProgress, this,
            [this, fileName](qint64 received, qint64 total) {
                Q_EMIT downloadProgress(fileName, received, total);
            });
    connect(reply, &QNetworkReply::finished, this, [this, reply, fileName, file, temporary, writeFailed]() {
        reply->deleteLater();
        m_downloading = false;
        Q_EMIT downloadingChanged();
        if (*writeFailed) {
            Q_EMIT downloadFailed(fileName, tr("Cannot write the download: %1").arg(file->errorString()));
            return;
        }
        if (reply->error() != QNetworkReply::NoError) {
            Q_EMIT downloadFailed(fileName, reply->errorString());
            return;
        }
        const QByteArray remaining = reply->readAll();
        if (file->write(remaining) != remaining.size() || !file->flush()) {
            Q_EMIT downloadFailed(fileName, tr("Cannot finish writing the download."));
            return;
        }
        file->close();
        Q_EMIT downloadFinished(fileName, file->fileName());
    });
}

QString IdGamesClient::webUrl(const QString &idgamesUrl) const
{
    return idgamesUrl;
}
