#include "externalurl.h"

#include <QTest>

class ExternalUrlTest : public QObject
{
    Q_OBJECT

private Q_SLOTS:
    void acceptsWebUrls()
    {
        QCOMPARE(ExternalUrl::fromHttpInput(QStringLiteral("https://example.com/mod.wad")),
                 QUrl(QStringLiteral("https://example.com/mod.wad")));
        QCOMPARE(ExternalUrl::fromHttpInput(QStringLiteral("http://example.com")),
                 QUrl(QStringLiteral("http://example.com")));
    }

    void rejectsOtherSchemes()
    {
        QVERIFY(ExternalUrl::fromHttpInput(QString()).isEmpty());
        QVERIFY(ExternalUrl::fromHttpInput(QStringLiteral("file:///tmp/mod.wad")).isEmpty());
        QVERIFY(ExternalUrl::fromHttpInput(QStringLiteral("mailto:test@example.com")).isEmpty());
        QVERIFY(ExternalUrl::fromHttpInput(QStringLiteral("javascript:alert(1)")).isEmpty());
    }
};

QTEST_MAIN(ExternalUrlTest)
#include "externalurl_test.moc"