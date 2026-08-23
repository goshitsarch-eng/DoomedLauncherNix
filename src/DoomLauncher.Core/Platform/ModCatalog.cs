using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DoomLauncher
{
    public enum RemoteModKind
    {
        IdGames,
        GitHubRelease,
        GitHubArchive,
        DirectUrl,
        BrowserPage
    }

    public class RemoteMod
    {
        public string Title { get; set; }
        public string Summary { get; set; }
        public string Group { get; set; }
        public string SourceName { get; set; }
        public string PageUrl { get; set; }
        public RemoteModKind Kind { get; set; }
        public bool CanAutoDownload { get; set; }

        public string IdGamesQuery { get; set; }
        public GameFileFieldType IdGamesField { get; set; } = GameFileFieldType.Title;

        public string DirectUrl { get; set; }
        public string SuggestedFileName { get; set; }

        public string GitHubOwner { get; set; }
        public string GitHubRepo { get; set; }
        public string GitHubBranch { get; set; }
        public string GitHubAssetPattern { get; set; }
    }

    public class RemoteModResolveResult
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Error { get; set; }
        public bool Succeeded => !string.IsNullOrEmpty(Url) && string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// Featured mods from idgames, GitHub, Romero, and other community hubs.
    /// Auto-download uses official/direct URLs; storefront pages that need a browser stay as links.
    /// </summary>
    public static class ModCatalog
    {
        public static readonly RemoteMod[] Featured =
        {
            new RemoteMod
            {
                Title = "Beautiful Doom",
                Summary = "Popular GZDoom visual and weapon overhaul. Official builds are on GitHub.",
                Group = "Gameplay",
                SourceName = "GitHub",
                PageUrl = "https://github.com/jekyllgrim/Beautiful-Doom/releases",
                Kind = RemoteModKind.GitHubRelease,
                CanAutoDownload = true,
                GitHubOwner = "jekyllgrim",
                GitHubRepo = "Beautiful-Doom",
                GitHubAssetPattern = ".pk3",
                DirectUrl = "https://github.com/jekyllgrim/Beautiful-Doom/releases/download/7.1.6/Beautiful_Doom_716.pk3",
                SuggestedFileName = "Beautiful_Doom.pk3"
            },
            new RemoteMod
            {
                Title = "Project Brutality",
                Summary = "Large GZDoom gameplay overhaul. Staging zip from the official GitHub repo.",
                Group = "Gameplay",
                SourceName = "GitHub",
                PageUrl = "https://github.com/pa1nki113r/Project_Brutality/tree/PB_Staging",
                Kind = RemoteModKind.GitHubArchive,
                CanAutoDownload = true,
                GitHubOwner = "pa1nki113r",
                GitHubRepo = "Project_Brutality",
                GitHubBranch = "PB_Staging",
                SuggestedFileName = "Project_Brutality-PB_Staging.zip"
            },
            new RemoteMod
            {
                Title = "Brutal Doom",
                Summary = "The best-known GZDoom gore/gameplay pack. ModDB hosts the official downloads.",
                Group = "Gameplay",
                SourceName = "ModDB",
                PageUrl = "https://www.moddb.com/mods/brutal-doom",
                Kind = RemoteModKind.BrowserPage,
                CanAutoDownload = false
            },
            new RemoteMod
            {
                Title = "Freedoom",
                Summary = "Free IWAD replacement so you can play without commercial Doom.",
                Group = "IWADs",
                SourceName = "GitHub",
                PageUrl = "https://github.com/freedoom/freedoom/releases",
                Kind = RemoteModKind.GitHubRelease,
                CanAutoDownload = true,
                GitHubOwner = "freedoom",
                GitHubRepo = "freedoom",
                GitHubAssetPattern = "freedoom-",
                SuggestedFileName = "freedoom.zip"
            },
            new RemoteMod
            {
                Title = "SIGIL",
                Summary = "John Romero's fifth Doom episode. Official free zip from Romero Games.",
                Group = "Mapsets",
                SourceName = "Romero Games",
                PageUrl = "https://romero.com/sigil",
                Kind = RemoteModKind.DirectUrl,
                CanAutoDownload = true,
                DirectUrl = "https://www.romerogames.ie/s/SIGIL_v1_21.zip",
                SuggestedFileName = "SIGIL_v1_21.zip"
            },
            new RemoteMod
            {
                Title = "SIGIL II",
                Summary = "Romero's unofficial sixth Doom episode. Official free zip.",
                Group = "Mapsets",
                SourceName = "Romero Games",
                PageUrl = "https://romero.com/sigil",
                Kind = RemoteModKind.DirectUrl,
                CanAutoDownload = true,
                DirectUrl = "https://romero.com/s/SIGIL_II_V1_0.zip",
                SuggestedFileName = "SIGIL_II_V1_0.zip"
            },
            new RemoteMod
            {
                Title = "Gossip",
                Summary = "2025 Cacoward winner: huge Boom-compatible Doom II maps.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/cacowards/2025",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "Gossip",
                IdGamesField = GameFileFieldType.Title
            },
            new RemoteMod
            {
                Title = "Sunless Empire",
                Summary = "2025 Cacoward megawad with a celebrated soundtrack.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/cacowards/2025",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "Sunless Empire",
                IdGamesField = GameFileFieldType.Title
            },
            new RemoteMod
            {
                Title = "Valiant",
                Summary = "Modern Boom megawad that plays great in GZDoom.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "valiant",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Ancient Aliens",
                Summary = "Colorful, fast Doom II maps with a sci-fi theme.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "aaliens",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Eviternity",
                Summary = "Highly polished 32-map megawad.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "eviternity",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Sunlust",
                Summary = "Tough, beautiful slaughter-leaning megawad.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "sunlust",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Scythe 2",
                Summary = "Classic compact maps with a brutal finale.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "scythe2",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Back to Saturn X",
                Summary = "Vanilla-friendly episode with a distinct look.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "btsx",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Going Down",
                Summary = "Witty vertical office-building campaign.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "Going Down",
                IdGamesField = GameFileFieldType.Title
            },
            new RemoteMod
            {
                Title = "Alien Vendetta",
                Summary = "Landmark Doom II megawad.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "Alien Vendetta",
                IdGamesField = GameFileFieldType.Title
            },
            new RemoteMod
            {
                Title = "NOVA IV",
                Summary = "2025 Cacoward honorable mention community megawad.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/cacowards/2025",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "nova",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Doom High-Res Texture Pack",
                Summary = "Replacement textures for GZDoom (idgames).",
                Group = "Gameplay",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "dhtp",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Eviternity II",
                Summary = "2024 megawad follow-up. idgames filename eviternityii.zip.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/levels/doom2/Ports/megawads/eviternityii",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "eviternityii",
                IdGamesField = GameFileFieldType.Filename
            },
            new RemoteMod
            {
                Title = "Heartland",
                Summary = "Polished Boom-compatible episode.",
                Group = "Mapsets",
                SourceName = "idgames",
                PageUrl = "https://www.doomworld.com/idgames/",
                Kind = RemoteModKind.IdGames,
                CanAutoDownload = true,
                IdGamesQuery = "Heartland",
                IdGamesField = GameFileFieldType.Title
            },
            new RemoteMod
            {
                Title = "Ashes 2063",
                Summary = "Post-apocalyptic GZDoom total conversion. Official files are on ModDB.",
                Group = "Gameplay",
                SourceName = "ModDB",
                PageUrl = "https://www.moddb.com/mods/ashes-2063",
                Kind = RemoteModKind.BrowserPage,
                CanAutoDownload = false
            },
            new RemoteMod
            {
                Title = "Hedon",
                Summary = "Standalone GZDoom action-RPG. ModDB hosts the official downloads.",
                Group = "Gameplay",
                SourceName = "ModDB",
                PageUrl = "https://www.moddb.com/games/hedon",
                Kind = RemoteModKind.BrowserPage,
                CanAutoDownload = false
            },
            new RemoteMod
            {
                Title = "Hideous Destructor",
                Summary = "Simulation-leaning GZDoom/UZDoom gameplay overhaul. Official repo is on Codeberg.",
                Group = "Gameplay",
                SourceName = "Codeberg",
                PageUrl = "https://codeberg.org/mc776/hideousdestructor",
                Kind = RemoteModKind.BrowserPage,
                CanAutoDownload = false
            }
        };

        public static readonly (string Title, string Url, string Summary)[] CommunitySites =
        {
            ("Doomworld /idgames", "https://www.doomworld.com/idgames/", "The classic wad archive. This launcher can search and download it directly."),
            ("Cacowards", "https://www.doomworld.com/cacowards/2025", "Doomworld's annual best-of. 2025 winners include Gossip and Sunless Empire."),
            ("ModDB — Doom mods", "https://www.moddb.com/games/doom/mods", "Gameplay packs such as Brutal Doom, Beautiful Doom, Ashes 2063, and total conversions."),
            ("Romero Games — SIGIL", "https://romero.com/sigil", "Official free SIGIL and SIGIL II downloads from John Romero."),
            ("GitHub — Beautiful Doom", "https://github.com/jekyllgrim/Beautiful-Doom", "Source and PK3 releases for a popular GZDoom overhaul."),
            ("GitHub — Project Brutality", "https://github.com/pa1nki113r/Project_Brutality", "Active GZDoom gameplay overhaul. Use the PB_Staging branch zip."),
            ("Realm667", "https://www.realm667.com/", "Beastiary and weapon resources for GZDoom projects."),
            ("DSDA / wad archive", "https://dsdarchive.com/", "Speedrun wad mirror with many classic and modern mapsets."),
            ("ZDoom forums", "https://forum.zdoom.org/", "GZDoom-specific releases and support threads."),
            ("Codeberg — Hideous Destructor", "https://codeberg.org/mc776/hideousdestructor", "Simulation-style GZDoom gameplay mod and related UZDoom ports."),
            ("DoomWiki — notable WADs", "https://doomwiki.org/wiki/List_of_WADs", "Encyclopedia of mapsets with idgames and homepage links.")
        };

        public static async Task<RemoteModResolveResult> ResolveAsync(RemoteMod mod, CancellationToken cancellationToken)
        {
            var result = new RemoteModResolveResult();
            if (mod == null)
            {
                result.Error = "No mod selected.";
                return result;
            }

            try
            {
                if (mod.Kind == RemoteModKind.DirectUrl)
                {
                    result.Url = mod.DirectUrl;
                    result.FileName = FileNameFrom(mod.SuggestedFileName, mod.DirectUrl);
                    if (string.IsNullOrEmpty(result.Url))
                        result.Error = "No download URL.";
                    return result;
                }

                if (mod.Kind == RemoteModKind.GitHubArchive)
                {
                    string branch = string.IsNullOrEmpty(mod.GitHubBranch) ? "master" : mod.GitHubBranch;
                    result.Url = $"https://github.com/{mod.GitHubOwner}/{mod.GitHubRepo}/archive/refs/heads/{branch}.zip";
                    result.FileName = FileNameFrom(mod.SuggestedFileName, result.Url);
                    return result;
                }

                if (mod.Kind == RemoteModKind.GitHubRelease)
                {
                    var resolved = await ResolveGitHubRelease(mod, cancellationToken).ConfigureAwait(false);
                    if (resolved.Succeeded)
                        return resolved;
                    if (!string.IsNullOrEmpty(mod.DirectUrl))
                    {
                        result.Url = mod.DirectUrl;
                        result.FileName = FileNameFrom(mod.SuggestedFileName, mod.DirectUrl);
                        return result;
                    }
                    return resolved;
                }

                result.Error = "This entry is not a direct download.";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        public static CuratedMod ToIdGamesQuery(RemoteMod mod)
        {
            if (mod == null || mod.Kind != RemoteModKind.IdGames)
                return null;
            return new CuratedMod
            {
                Title = mod.Title,
                Summary = mod.Summary,
                Group = mod.Group,
                SearchQuery = mod.IdGamesQuery,
                SearchField = mod.IdGamesField
            };
        }

        private static async Task<RemoteModResolveResult> ResolveGitHubRelease(RemoteMod mod, CancellationToken cancellationToken)
        {
            var result = new RemoteModResolveResult();
            try
            {
                var client = new GitHubClient(new ProductHeaderValue("DoomLauncher"));
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                IReadOnlyList<Release> releases = await client.Repository.Release.GetAll(mod.GitHubOwner, mod.GitHubRepo)
                    .WaitAsync(timeout.Token).ConfigureAwait(false);
                foreach (var release in releases)
                {
                    var asset = release.Assets?.FirstOrDefault(a => AssetMatches(a.Name, mod.GitHubAssetPattern));
                    if (asset == null)
                        continue;
                    result.Url = asset.BrowserDownloadUrl;
                    result.FileName = FileNameFrom(mod.SuggestedFileName, asset.Name);
                    return result;
                }

                result.Error = "No matching GitHub release asset.";
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex is OperationCanceledException ? "GitHub lookup timed out." : ex.Message;
                return result;
            }
        }

        private static bool AssetMatches(string name, string pattern)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (string.IsNullOrEmpty(pattern))
            {
                return name.EndsWith(".pk3", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".pk7", StringComparison.OrdinalIgnoreCase);
            }
            if (pattern.StartsWith(".", StringComparison.Ordinal))
                return name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
            return name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string FileNameFrom(string suggested, string urlOrName)
        {
            if (!string.IsNullOrEmpty(suggested))
                return suggested;
            try
            {
                if (Uri.TryCreate(urlOrName, UriKind.Absolute, out var uri))
                    return Path.GetFileName(uri.LocalPath);
            }
            catch
            {
            }
            return Path.GetFileName(urlOrName);
        }
    }
}
