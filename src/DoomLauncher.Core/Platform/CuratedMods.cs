using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DoomLauncher
{
    public class CuratedMod
    {
        public string Title { get; set; }
        public string Summary { get; set; }
        public string SearchQuery { get; set; }
        public GameFileFieldType SearchField { get; set; }
        public string Group { get; set; }
    }

    public static class CuratedMods
    {
        public static readonly CuratedMod[] All =
        {
            new CuratedMod
            {
                Title = "Valiant",
                Summary = "A modern Boom megawad that plays great in GZDoom.",
                SearchQuery = "valiant",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Ancient Aliens",
                Summary = "Colorful, fast Doom II maps with a sci-fi theme.",
                SearchQuery = "aaliens",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Eviternity",
                Summary = "A highly polished 32-map megawad.",
                SearchQuery = "eviternity",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Sunlust",
                Summary = "A tough, beautiful slaughter-leaning megawad.",
                SearchQuery = "sunlust",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Scythe 2",
                Summary = "Classic compact maps with a brutal finale.",
                SearchQuery = "scythe2",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "SIGIL",
                Summary = "John Romero's fifth Doom episode.",
                SearchQuery = "sigil",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Back to Saturn X",
                Summary = "A vanilla-friendly episode with a distinct look.",
                SearchQuery = "btsx",
                SearchField = GameFileFieldType.Filename,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Going Down",
                Summary = "A witty, vertical office-building campaign.",
                SearchQuery = "Going Down",
                SearchField = GameFileFieldType.Title,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Alien Vendetta",
                Summary = "A landmark Doom II megawad.",
                SearchQuery = "Alien Vendetta",
                SearchField = GameFileFieldType.Title,
                Group = "Mapsets"
            },
            new CuratedMod
            {
                Title = "Beautiful Doom",
                Summary = "A popular GZDoom visual and gameplay overhaul.",
                SearchQuery = "Beautiful Doom",
                SearchField = GameFileFieldType.Title,
                Group = "Gameplay"
            },
            new CuratedMod
            {
                Title = "Doom High-Res Texture Pack",
                Summary = "Replacement textures for GZDoom.",
                SearchQuery = "dhtp",
                SearchField = GameFileFieldType.Filename,
                Group = "Gameplay"
            }
        };

        public static IEnumerable<IGameFile> Search(IGameFileDataSourceAdapter adapter, CuratedMod mod)
        {
            if (adapter == null || mod == null || string.IsNullOrWhiteSpace(mod.SearchQuery))
                return Array.Empty<IGameFile>();
            return Search(adapter, mod.SearchField, mod.SearchQuery);
        }

        public static IEnumerable<IGameFile> Search(IGameFileDataSourceAdapter adapter, GameFileFieldType field, string query)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(query))
                return Array.Empty<IGameFile>();

            var options = new GameFileGetOptions(new GameFileSearchField(field, query.Trim()));
            try
            {
                return adapter.GetGameFiles(options).ToList();
            }
            catch
            {
                return Array.Empty<IGameFile>();
            }
        }

        public static IEnumerable<IGameFile> Latest(IGameFileDataSourceAdapter adapter)
        {
            if (adapter == null)
                return Array.Empty<IGameFile>();
            try
            {
                return adapter.GetGameFiles().Take(40).ToList();
            }
            catch
            {
                return Array.Empty<IGameFile>();
            }
        }
    }
}
