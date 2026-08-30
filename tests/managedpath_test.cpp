#include "managedpath.h"

#include <QTest>

class ManagedPathTest : public QObject
{
    Q_OBJECT

private Q_SLOTS:
    void acceptsBaseNames()
    {
        QVERIFY(ManagedPath::isSafeFileName(QStringLiteral("doom2.wad")));
        QVERIFY(ManagedPath::isSafeFileName(QStringLiteral("my mod.pk3")));
    }

    void rejectsDirectoryTraversal()
    {
        QVERIFY(!ManagedPath::isSafeFileName(QString()));
        QVERIFY(!ManagedPath::isSafeFileName(QStringLiteral(".")));
        QVERIFY(!ManagedPath::isSafeFileName(QStringLiteral("..")));
        QVERIFY(!ManagedPath::isSafeFileName(QStringLiteral("../outside.wad")));
        QVERIFY(!ManagedPath::isSafeFileName(QStringLiteral("subdir/mod.wad")));
        QVERIFY(!ManagedPath::isSafeFileName(QStringLiteral("subdir\\mod.wad")));
        QVERIFY(!ManagedPath::isSafeFileName(QStringLiteral("/tmp/mod.wad")));
    }
};

QTEST_MAIN(ManagedPathTest)
#include "managedpath_test.moc"
