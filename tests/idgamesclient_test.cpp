#include "database.h"
#include "idgamesclient.h"
#include <QFile>
#include <QSignalSpy>
#include <QTcpServer>
#include <QTcpSocket>
#include <QTemporaryDir>
#include <QTest>

class IdGamesClientTest : public QObject
{
    Q_OBJECT
    QTemporaryDir temporary;
    Database database;
    QTcpServer server;
    QList<QTcpSocket *> requests;

    void respond(QTcpSocket *socket, const QByteArray &body)
    {
        socket->write("HTTP/1.1 200 OK\r\nContent-Length: " + QByteArray::number(body.size())
                      + "\r\nConnection: close\r\n\r\n" + body);
        socket->disconnectFromHost();
    }

private Q_SLOTS:
    void initTestCase()
    {
        QVERIFY(temporary.isValid());
        qputenv("XDG_DATA_HOME", temporary.path().toUtf8());
        QVERIFY(database.open());
        QVERIFY(server.listen(QHostAddress::LocalHost));
        const QString base = QStringLiteral("http://127.0.0.1:%1/").arg(server.serverPort());
        database.setConfigValue(QStringLiteral("IdGamesUrl"), base);
        database.setConfigValue(QStringLiteral("MirrorUrl"), base);
        connect(&server, &QTcpServer::newConnection, this, [this]() {
            auto *socket = server.nextPendingConnection();
            connect(socket, &QTcpSocket::readyRead, this, [this, socket]() {
                socket->readAll();
                if (!requests.contains(socket))
                    requests.append(socket);
            });
        });
    }

    void init() { requests.clear(); }

    void latestSearchWins()
    {
        IdGamesClient client(&database);
        QSignalSpy finished(&client, &IdGamesClient::searchFinished);
        QSignalSpy failed(&client, &IdGamesClient::searchFailed);
        client.search(QStringLiteral("title"), QStringLiteral("old"));
        QTRY_COMPARE(requests.size(), 1);
        client.search(QStringLiteral("title"), QStringLiteral("new"));
        QTRY_COMPARE(requests.size(), 2);
        respond(requests[1], R"({"content":{"file":{"id":2,"filename":"new.zip"}}})");
        QTRY_COMPARE(finished.count(), 1);
        QCOMPARE(failed.count(), 0);
        QVERIFY(!client.busy());
        QCOMPARE(finished.first().first().toList().first().toMap().value(QStringLiteral("FileName")).toString(), QStringLiteral("new.zip"));
    }

    void invalidJsonIsReported()
    {
        IdGamesClient client(&database);
        QSignalSpy failed(&client, &IdGamesClient::searchFailed);
        client.loadLatest();
        QTRY_COMPARE(requests.size(), 1);
        respond(requests.first(), "<html>Service unavailable</html>");
        QTRY_COMPARE(failed.count(), 1);
        QVERIFY(!client.busy());
    }

    void unsafePathsAreRejected()
    {
        IdGamesClient client(&database);
        QSignalSpy failed(&client, &IdGamesClient::downloadFailed);
        client.download({}, QStringLiteral("../escape.zip"));
        client.download(QStringLiteral("../outside"), QStringLiteral("file.zip"));
        QCOMPARE(failed.count(), 2);
        QVERIFY(!client.downloading());
        QCOMPARE(requests.size(), 0);
    }

    void streamsToIsolatedFileAndCleansUp()
    {
        IdGamesClient client(&database);
        QSignalSpy finished(&client, &IdGamesClient::downloadFinished);
        QString downloadedPath;
        QByteArray contents;
        connect(&client, &IdGamesClient::downloadFinished, this, [&](const QString &, const QString &path) {
            downloadedPath = path;
            QFile file(path);
            if (file.open(QIODevice::ReadOnly))
                contents = file.readAll();
        });
        client.download(QStringLiteral("levels"), QStringLiteral("test.zip"));
        QVERIFY(client.downloading());
        client.download(QStringLiteral("levels"), QStringLiteral("ignored.zip"));
        QTRY_COMPARE(requests.size(), 1);
        respond(requests.first(), "archive payload");
        QTRY_COMPARE(finished.count(), 1);
        QCOMPARE(contents, QByteArray("archive payload"));
        QVERIFY(downloadedPath.endsWith(QStringLiteral("/test.zip")));
        QVERIFY(!client.downloading());
        QTRY_VERIFY(!QFile::exists(downloadedPath));
    }
};
QTEST_GUILESS_MAIN(IdGamesClientTest)
#include "idgamesclient_test.moc"
