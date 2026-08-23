using DoomLauncher;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTest.Tests
{
    [TestClass]
    public class TestModCatalog
    {
        [TestMethod]
        public void FeaturedCoversMoreThanIdGames()
        {
            Assert.IsTrue(ModCatalog.Featured.Length >= 15);
            Assert.IsTrue(ModCatalog.Featured.Any(x => x.Kind == RemoteModKind.IdGames));
            Assert.IsTrue(ModCatalog.Featured.Any(x => x.Kind == RemoteModKind.GitHubRelease && x.CanAutoDownload));
            Assert.IsTrue(ModCatalog.Featured.Any(x => x.Kind == RemoteModKind.GitHubArchive && x.CanAutoDownload));
            Assert.IsTrue(ModCatalog.Featured.Any(x => x.Kind == RemoteModKind.DirectUrl && x.CanAutoDownload));
            Assert.IsTrue(ModCatalog.Featured.Any(x => x.Kind == RemoteModKind.BrowserPage && !x.CanAutoDownload));
            Assert.IsTrue(ModCatalog.CommunitySites.Length >= 6);
            Assert.IsTrue(ModCatalog.CommunitySites.Any(x => x.Url.Contains("moddb.com")));
            Assert.IsTrue(ModCatalog.CommunitySites.Any(x => x.Url.Contains("idgames")));
        }

        [TestMethod]
        public async Task DirectUrlAndGitHubArchiveResolveWithoutNetwork()
        {
            var sigil = ModCatalog.Featured.First(x => x.Title == "SIGIL");
            var resolved = await ModCatalog.ResolveAsync(sigil, CancellationToken.None);
            Assert.IsTrue(resolved.Succeeded);
            Assert.AreEqual("https://www.romerogames.ie/s/SIGIL_v1_21.zip", resolved.Url);
            Assert.AreEqual("SIGIL_v1_21.zip", resolved.FileName);

            var brutality = ModCatalog.Featured.First(x => x.Title == "Project Brutality");
            var zip = await ModCatalog.ResolveAsync(brutality, CancellationToken.None);
            Assert.IsTrue(zip.Succeeded);
            Assert.IsTrue(zip.Url.Contains("Project_Brutality/archive/refs/heads/PB_Staging.zip"));
            Assert.AreEqual("Project_Brutality-PB_Staging.zip", zip.FileName);
        }

        [TestMethod]
        public void FileNameFromUsesSuggestedOrUrl()
        {
            Assert.AreEqual("Beautiful_Doom.pk3", ModCatalog.FileNameFrom("Beautiful_Doom.pk3", "https://example.com/x.pk3"));
            Assert.AreEqual("SIGIL_v1_21.zip", ModCatalog.FileNameFrom(null, "https://www.romerogames.ie/s/SIGIL_v1_21.zip"));
        }

        [TestMethod]
        public void UrlDownloadableKeepsFileName()
        {
            var item = new UrlDownloadable("https://example.com/mod.pk3", "Beautiful_Doom.pk3");
            Assert.AreEqual("Beautiful_Doom.pk3", item.FileName);
            Assert.AreEqual("https://example.com/mod.pk3", item.Url);
        }

        [TestMethod]
        public void IdGamesFeaturedMapsToCuratedQuery()
        {
            var gossip = ModCatalog.Featured.First(x => x.Title == "Gossip");
            var curated = ModCatalog.ToIdGamesQuery(gossip);
            Assert.IsNotNull(curated);
            Assert.AreEqual("Gossip", curated.SearchQuery);
            Assert.IsNull(ModCatalog.ToIdGamesQuery(ModCatalog.Featured.First(x => x.Kind == RemoteModKind.DirectUrl)));
        }
    }
}
