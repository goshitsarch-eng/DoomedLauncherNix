using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DoomLauncher
{
    public enum LibraryTabKind
    {
        Recent,
        Local,
        Untagged,
        IWads,
        IdGames,
        Tag
    }

    public class LibraryTab
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public LibraryTabKind Kind { get; set; }
        public ITagData Tag { get; set; }
        public bool IsLocal => Kind != LibraryTabKind.IdGames;
        public bool IsEditAllowed => IsLocal;
        public bool IsDeleteAllowed => IsLocal;
        public bool IsPlayAllowed => Kind != LibraryTabKind.IdGames;
        public bool IsSearchAllowed => true;
        public bool FilterIWads => Kind == LibraryTabKind.Local || Kind == LibraryTabKind.Recent || Kind == LibraryTabKind.Untagged;
    }

    public static class LibraryTabService
    {
        public static List<LibraryTab> BuildTabs(IDataSourceAdapter adapter, AppConfiguration config)
        {
            var tabs = new List<LibraryTab>();
            if (config.VisibleViews.Contains(TabKeys.RecentKey))
                tabs.Add(new LibraryTab { Key = TabKeys.RecentKey, Title = StaticTagData.GetFavoriteName(TabKeys.RecentKey), Kind = LibraryTabKind.Recent });

            tabs.Add(new LibraryTab { Key = TabKeys.LocalKey, Title = StaticTagData.GetFavoriteName(TabKeys.LocalKey), Kind = LibraryTabKind.Local });

            if (config.VisibleViews.Contains(TabKeys.UntaggedKey))
                tabs.Add(new LibraryTab { Key = TabKeys.UntaggedKey, Title = StaticTagData.GetFavoriteName(TabKeys.UntaggedKey), Kind = LibraryTabKind.Untagged });
            if (config.VisibleViews.Contains(TabKeys.IWadsKey))
                tabs.Add(new LibraryTab { Key = TabKeys.IWadsKey, Title = StaticTagData.GetFavoriteName(TabKeys.IWadsKey), Kind = LibraryTabKind.IWads });
            if (config.VisibleViews.Contains(TabKeys.IdGamesKey))
                tabs.Add(new LibraryTab { Key = TabKeys.IdGamesKey, Title = StaticTagData.GetFavoriteName(TabKeys.IdGamesKey), Kind = LibraryTabKind.IdGames });

            DataCache.Instance.UpdateTags();
            foreach (var tag in DataCache.Instance.Tags.Where(x => x.HasTab))
            {
                tabs.Add(new LibraryTab
                {
                    Key = tag.TagID.ToString(),
                    Title = tag.FavoriteName,
                    Kind = LibraryTabKind.Tag,
                    Tag = tag
                });
            }

            return tabs;
        }

        public static IEnumerable<IGameFile> LoadFiles(LibraryTab tab, IDataSourceAdapter adapter, IGameFileDataSourceAdapter idGamesAdapter, IEnumerable<GameFileSearchField> searchFields)
        {
            var fields = Util.DefaultGameFileSelectFields;
            switch (tab.Kind)
            {
                case LibraryTabKind.Recent:
                    return LoadRecent(adapter, fields, searchFields);
                case LibraryTabKind.Local:
                    return FilterIWads(adapter, LoadLocal(adapter, fields, searchFields));
                case LibraryTabKind.Untagged:
                    return FilterIWads(adapter, LoadUntagged(adapter, fields, searchFields));
                case LibraryTabKind.IWads:
                    return LoadIWads(adapter, searchFields);
                case LibraryTabKind.IdGames:
                    return LoadIdGames(idGamesAdapter, searchFields);
                case LibraryTabKind.Tag:
                    return LoadTag(adapter, tab.Tag, fields, searchFields);
                default:
                    return Array.Empty<IGameFile>();
            }
        }

        private static IEnumerable<IGameFile> FilterIWads(IDataSourceAdapter adapter, IEnumerable<IGameFile> files)
        {
            var iwads = adapter.GetGameFileIWads().ToList();
            return files.Except(iwads);
        }

        private static IEnumerable<IGameFile> LoadLocal(IDataSourceAdapter adapter, GameFileFieldType[] fields, IEnumerable<GameFileSearchField> searchFields)
        {
            if (searchFields == null || !searchFields.Any())
                return adapter.GetGameFiles(new GameFileGetOptions(fields));

            IEnumerable<IGameFile> items = Enumerable.Empty<IGameFile>();
            foreach (var sf in searchFields)
                items = items.Union(adapter.GetGameFiles(new GameFileGetOptions(fields, sf)));
            return items;
        }

        private static IEnumerable<IGameFile> LoadRecent(IDataSourceAdapter adapter, GameFileFieldType[] fields, IEnumerable<GameFileSearchField> searchFields)
        {
            var options = new GameFileGetOptions
            {
                Limit = 25,
                OrderBy = OrderType.Desc,
                OrderField = GameFileFieldType.Downloaded,
                SelectFields = fields
            };

            if (searchFields != null && searchFields.Any())
            {
                IEnumerable<IGameFile> items = Enumerable.Empty<IGameFile>();
                foreach (var sf in searchFields)
                {
                    options.SearchField = sf;
                    items = items.Union(adapter.GetGameFiles(options));
                }
                return items;
            }

            return adapter.GetGameFiles(options);
        }

        private static IEnumerable<IGameFile> LoadUntagged(IDataSourceAdapter adapter, GameFileFieldType[] fields, IEnumerable<GameFileSearchField> searchFields)
        {
            var untagged = adapter.GetUntaggedGameFiles();
            if (searchFields == null || !searchFields.Any())
                return untagged;

            IEnumerable<IGameFile> items = Enumerable.Empty<IGameFile>();
            foreach (var sf in searchFields)
            {
                var search = adapter.GetGameFiles(new GameFileGetOptions(fields, sf));
                items = items.Union(untagged.Intersect(search));
            }
            return items;
        }

        private static IEnumerable<IGameFile> LoadIWads(IDataSourceAdapter adapter, IEnumerable<GameFileSearchField> searchFields)
        {
            var iwads = adapter.GetGameFileIWads();
            if (searchFields == null || !searchFields.Any())
                return iwads;

            IEnumerable<IGameFile> items = Enumerable.Empty<IGameFile>();
            foreach (var sf in searchFields)
                items = items.Union(adapter.GetGameFiles(new GameFileGetOptions(new[] { GameFileFieldType.GameFileID, GameFileFieldType.Filename }, sf)));
            return iwads.Where(x => items.Any(y => x.GameFileID == y.GameFileID));
        }

        private static IEnumerable<IGameFile> LoadIdGames(IGameFileDataSourceAdapter adapter, IEnumerable<GameFileSearchField> searchFields)
        {
            if (adapter == null)
                return Array.Empty<IGameFile>();
            if (searchFields == null || !searchFields.Any())
                return adapter.GetGameFiles();

            IEnumerable<IGameFile> ret = Enumerable.Empty<IGameFile>();
            foreach (var sf in searchFields)
                ret = ret.Union(adapter.GetGameFiles(new GameFileGetOptions(sf)));
            return ret;
        }

        private static IEnumerable<IGameFile> LoadTag(IDataSourceAdapter adapter, ITagData tag, GameFileFieldType[] fields, IEnumerable<GameFileSearchField> searchFields)
        {
            if (tag == null)
                return Array.Empty<IGameFile>();
            if (searchFields == null || !searchFields.Any())
                return adapter.GetGameFiles(new GameFileGetOptions(fields), tag);

            IEnumerable<IGameFile> items = Enumerable.Empty<IGameFile>();
            foreach (var sf in searchFields)
                items = items.Union(adapter.GetGameFiles(new GameFileGetOptions(fields, sf), tag));
            return items;
        }
    }
}
