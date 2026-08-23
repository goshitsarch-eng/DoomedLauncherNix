using DoomLauncher.Handlers;
using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DoomLauncher.GameStores
{
    public class AutomaticGameStoreCheck
    {
        private IDataSourceAdapter m_dataSourceAdapter;
        private Func<GameStoreFiles> m_loadGameStoreFiles;
        private List<string> m_installedGameFiles;

        public AutomaticGameStoreCheck(Func<GameStoreFiles> loadGameStoreFiles, IDataSourceAdapter dataSourceAdapter)
        {
            m_dataSourceAdapter = dataSourceAdapter;
            m_loadGameStoreFiles = loadGameStoreFiles;
        }

        public delegate Task AsyncWadInstaller(List<string> wads);
        public delegate Task AsyncDoom64Installer(string doom64Exe);

        public async Task LoadGamesFromGameStores(AsyncWadInstaller addIwadToGame, AsyncWadInstaller addPwadToGame, AsyncDoom64Installer addDoom64ExeToGame)
        {
            var gameStoreFiles = m_loadGameStoreFiles();

            m_installedGameFiles = m_dataSourceAdapter.GetGameFileNames().Select(PathExtensions.GetFileName).ToList();

            var iwadsFromStore = gameStoreFiles.InstalledIWads.Where(NotInCollection).ToList();
            if (iwadsFromStore.Count > 0)
            {
                await addIwadToGame(iwadsFromStore);
            }

            var pwadsFromStore = gameStoreFiles.InstalledPWads.Where(NotInCollection).ToList();
            if (pwadsFromStore.Count > 0)
            {
                await addPwadToGame(pwadsFromStore);
            }

            var doom64ExeFromStore = gameStoreFiles.InstalledDoom64Exe;
            if (doom64ExeFromStore != null)
            {
                await addDoom64ExeToGame(doom64ExeFromStore);
            }
        }

        private bool NotInCollection(string file)
        {
            var justTheFileName = PathExtensions.GetFileName(file);
            var justTheFileNameZip = PathExtensions.GetFileNameWithoutExtension(file) + ".zip";

            return !m_installedGameFiles.Exists(x => x.Equals(justTheFileName) || x.Equals(justTheFileNameZip));
        }
    }
}
