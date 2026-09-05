#pragma once

#include <QNetworkAccessManager>
#include <QObject>
#include <QPointer>
#include <QVariantList>

class Database;

// Doomworld idgames archive client: searches through the JSON API and
// downloads files from the configured mirror.
class IdGamesClient : public QObject
{
    Q_OBJECT
    Q_PROPERTY(bool busy READ busy NOTIFY busyChanged)
    Q_PROPERTY(bool downloading READ downloading NOTIFY downloadingChanged)

public:
    explicit IdGamesClient(Database *db, QObject *parent = nullptr);

    bool busy() const { return m_busy; }
    bool downloading() const { return m_downloading; }

    // type: "title", "author", "filename" or "descrption" (sic — the API's
    // own spelling).
    Q_INVOKABLE void search(const QString &type, const QString &query);

    // Latest uploads, used to fill the Id Games tab before any search.
    Q_INVOKABLE void loadLatest();

    // Downloads dir/filename from the mirror into the temp directory.
    // downloadFinished carries the local path on success.
    Q_INVOKABLE void download(const QString &dir, const QString &fileName);

    Q_INVOKABLE QString webUrl(const QString &idgamesUrl) const;

Q_SIGNALS:
    void busyChanged();
    void downloadingChanged();
    void searchFinished(const QVariantList &results);
    void searchFailed(const QString &message);
    void downloadProgress(const QString &fileName, qint64 received, qint64 total);
    void downloadFinished(const QString &fileName, const QString &localPath);
    void downloadFailed(const QString &fileName, const QString &message);

private:
    void request(const QString &query);
    void setBusy(bool busy);

    Database *m_db;
    QNetworkAccessManager m_network;
    bool m_busy = false;
    bool m_downloading = false;
    QPointer<QNetworkReply> m_searchReply;
};
