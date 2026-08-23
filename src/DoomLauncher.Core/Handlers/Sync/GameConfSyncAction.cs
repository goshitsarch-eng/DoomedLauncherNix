using DoomLauncher.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace DoomLauncher.Handlers.Sync
{
    public class GameConfSyncAction : ISyncAction
    {
        public SyncResult ApplyToGameFile(IGameFile gameFile, IArchiveReader reader, string[] mapInfoData)
        {
            var entry = reader.Entries.FirstOrDefault(e => e.Name.ToLower().Equals("gameconf"));
            if (entry != null)
            {
                var json = entry.ReadString(Encoding.UTF8);

                try
                {
                    var root = JObject.Parse(json);
                    var data = root["data"] as JObject;
                    var title = data?["title"]?.ToString();
                    var author = data?["author"]?.ToString();
                    var description = data?["description"]?.ToString();
                    var iwad = data?["iwad"]?.ToString();

                    if (!string.IsNullOrEmpty(title))
                        gameFile.Title = title;

                    if (!string.IsNullOrEmpty(author))
                        gameFile.Author = author;

                    if (!string.IsNullOrEmpty(description))
                        gameFile.Description = description;

                    if (!string.IsNullOrEmpty(iwad))
                    {
                        IWadInfo iwadInfo = IWadInfo.FromFileName(iwad);
                        if (iwadInfo != null)
                        {
                            gameFile.IntendedGame = iwadInfo;
                        }
                    }
                }
                catch
                {

                }
            }

            return SyncResult.EMPTY;
        }
    }
}
