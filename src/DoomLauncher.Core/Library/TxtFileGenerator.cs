using DoomLauncher.DataSources;
using DoomLauncher.Interfaces;
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DoomLauncher
{
    public class TxtFileRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; } = DateTime.Now;
        public string Author { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string OtherFiles { get; set; } = string.Empty;
        public string MiscAuthor { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string AdditionalCredits { get; set; } = string.Empty;
        public int NewLevels { get; set; }
        public bool Sounds { get; set; }
        public bool Music { get; set; }
        public bool Graphics { get; set; }
        public bool Dehacked { get; set; }
        public bool Demos { get; set; }
        public bool Other { get; set; }
        public string OtherRequired { get; set; } = string.Empty;
        public string Game { get; set; } = "N/A";
        public string Maps { get; set; } = string.Empty;
        public string SinglePlayer { get; set; } = "Designed for";
        public string Coop { get; set; } = "No";
        public string Deathmatch { get; set; } = "No";
        public string OtherGameStyles { get; set; } = string.Empty;
        public string Difficulty { get; set; } = "Yes";
        public string Base { get; set; } = "New from scratch";
        public string BuildTime { get; set; } = string.Empty;
        public string Editors { get; set; } = string.Empty;
        public string KnownBugs { get; set; } = string.Empty;
        public string MayNotRunWith { get; set; } = string.Empty;
        public string TestedWith { get; set; } = string.Empty;
        public bool MayModify { get; set; }
        public bool MayDistribute { get; set; } = true;
        public string WebSites { get; set; } = string.Empty;
        public string FtpSites { get; set; } = string.Empty;
        public string Engine { get; set; } = "N/A";
        public string PrimaryPurpose { get; set; } = "Single player";
    }

    public static class TxtFileGenerator
    {
        public static TxtFileRequest FromGameFile(IGameFile gameFile, IDataSourceAdapter adapter)
        {
            var request = new TxtFileRequest();
            if (gameFile == null)
                return request;

            request.Title = gameFile.Title ?? string.Empty;
            request.Filename = gameFile.FileName ?? string.Empty;
            request.ReleaseDate = gameFile.ReleaseDate ?? DateTime.Now;
            request.Author = gameFile.Author ?? string.Empty;
            request.Description = gameFile.Description ?? string.Empty;
            request.NewLevels = gameFile.MapCount ?? 0;
            request.Maps = gameFile.Map ?? string.Empty;
            if (gameFile.SourcePortID.HasValue)
            {
                var port = adapter.GetSourcePort(gameFile.SourcePortID.Value);
                if (port != null)
                    request.Engine = port.Name;
            }
            if (gameFile.IWadID.HasValue)
            {
                var iwad = adapter.GetIWads().FirstOrDefault(x => x.IWadID == gameFile.IWadID.Value);
                if (iwad != null)
                    request.Game = iwad.Name;
            }
            return request;
        }

        public static string Generate(TxtFileRequest r)
        {
            string included(bool v) => v ? "Yes" : "No";
            string may = @"You MAY not distribute this file in any format.";
            string maynot = @"You MAY distribute this file, provided you include this text file, with
no modifications.  You may distribute this file in any electronic
format (BBS, Diskette, CD, etc) as long as you include this file 
intact.  I have received permission from the original authors of any
modified or included content in this file to allow further distribution.";

            var sb = new StringBuilder();
            sb.AppendLine("===========================================================================");
            sb.AppendLine($"Advanced engine needed  : {r.Engine}");
            sb.AppendLine($"Primary purpose         : {r.PrimaryPurpose}");
            sb.AppendLine("===========================================================================");
            sb.AppendLine($"Title                   : {r.Title}");
            sb.AppendLine($"Filename                : {r.Filename}");
            sb.AppendLine($"Release date            : {r.ReleaseDate.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern)}");
            sb.AppendLine($"Author                  : {r.Author}");
            sb.AppendLine($"Email Address           : {r.Email}");
            sb.AppendLine($"Other Files By Author   : {r.OtherFiles}");
            sb.AppendLine($"Misc. Author Info       : {r.MiscAuthor}");
            sb.AppendLine();
            sb.AppendLine($"Description             : {r.Description}");
            sb.AppendLine();
            sb.AppendLine($"Additional Credits to   : {r.AdditionalCredits}");
            sb.AppendLine("===========================================================================");
            sb.AppendLine("* What is included *");
            sb.AppendLine();
            sb.AppendLine($"New levels              : {r.NewLevels}");
            sb.AppendLine($"Sounds                  : {included(r.Sounds)}");
            sb.AppendLine($"Music                   : {included(r.Music)}");
            sb.AppendLine($"Graphics                : {included(r.Graphics)}");
            sb.AppendLine($"Dehacked/BEX Patch      : {included(r.Dehacked)}");
            sb.AppendLine($"Demos                   : {included(r.Demos)}");
            sb.AppendLine($"Other                   : {included(r.Other)}");
            sb.AppendLine($"Other files required    : {r.OtherRequired}");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("* Play Information *");
            sb.AppendLine();
            sb.AppendLine($"Game                    : {r.Game}");
            sb.AppendLine($"Map #                   : {r.Maps}");
            sb.AppendLine($"Single Player           : {r.SinglePlayer}");
            sb.AppendLine($"Cooperative 2-4 Player  : {r.Coop}");
            sb.AppendLine($"Deathmatch 2-4 Player   : {r.Deathmatch}");
            sb.AppendLine($"Other game styles       : {r.OtherGameStyles}");
            sb.AppendLine($"Difficulty Settings     : {r.Difficulty}");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("* Construction *");
            sb.AppendLine();
            sb.AppendLine($"Base                    : {r.Base}");
            sb.AppendLine($"Build Time              : {r.BuildTime}");
            sb.AppendLine($"Editor(s) used          : {r.Editors}");
            sb.AppendLine($"Known Bugs              : {r.KnownBugs}");
            sb.AppendLine($"May Not Run With        : {r.MayNotRunWith}");
            sb.AppendLine($"Tested With             : {r.TestedWith}");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("* Copyright / Permissions *");
            sb.AppendLine();
            sb.AppendLine($"Authors {(r.MayModify ? "MAY" : "MAY NOT")} use the contents of this file as a base for");
            sb.AppendLine("modification or reuse.  Permissions have been obtained from original");
            sb.AppendLine("authors for any of their resources modified or included in this file.");
            sb.AppendLine();
            sb.AppendLine(r.MayDistribute ? maynot : may);
            sb.AppendLine();
            sb.AppendLine("* Where to get the file that this text file describes *");
            sb.AppendLine();
            sb.AppendLine("The Usual: ftp://archives.3dgamers.com/pub/idgames/ and mirrors");
            sb.AppendLine($"Web sites: {r.WebSites}");
            sb.AppendLine($"FTP sites: {r.FtpSites}");
            return sb.ToString();
        }
    }
}
