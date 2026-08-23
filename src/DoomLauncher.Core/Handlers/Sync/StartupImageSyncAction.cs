using DoomLauncher.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.IO;
using System.Linq;

namespace DoomLauncher.Handlers.Sync {


    /// <summary>
    /// https://zdoom.org/wiki/Startup_lumps
    /// Planar image reading code adapted from https://github.com/ZDoom/gzdoom/blob/5e35ebc8fe698f86c8b0c4c98774bc397f30e7d4/src/common/textures/formats/startuptexture.cpp
    /// </summary>
    public class StartupImageSyncAction : ISyncAction
    {
        private static readonly int WIDTH = 640;
        private static readonly int HEIGHT = 480;
        private static readonly int PALETTE_LENGTH = 48;
        private static readonly int PIXELS_LENGTH = WIDTH * HEIGHT / 2;
        private static readonly int STARTUP_LENGTH = PALETTE_LENGTH + PIXELS_LENGTH;

        public SyncResult ApplyToGameFile(IGameFile gameFile, IArchiveReader reader, string[] mapInfoData)
        {
            var entry = reader.Entries.FirstOrDefault(e => 
                e.Name.ToLower().StartsWith("startup")
                && !e.Name.ToLower().EndsWith(".wad")
                && !e.Name.ToLower().StartsWith("startup0"));

            if (entry != null)
            {
                byte[] entryBytes = entry.ReadEntry();

                try 
                { 
                    Image pngImage = Image.Load(new MemoryStream(entryBytes));
                    return SyncResult.TitlePic(gameFile, pngImage);
                }
                catch (System.Exception)
                {
                }

                if (entry.Length != STARTUP_LENGTH)
                {
                    return SyncResult.FailedTitlePicFile(gameFile);
                }

                Rgba32[] palette = ReadPalette(entryBytes);
                Image image = ReadPlanarPalettedImage(palette, entryBytes);
                return SyncResult.TitlePic(gameFile, image);
            }

            return SyncResult.EMPTY;
        }

        private Image ReadPlanarPalettedImage(Rgba32[] palette, byte[] entryBytes)
        {
            Image<Rgba32> image = new Image<Rgba32>(640, 480);
            int planeSize = WIDTH * HEIGHT / 8;

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < HEIGHT; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < WIDTH / 8; x++)
                    {
                        int offset = PALETTE_LENGTH + y * (WIDTH / 8) + x;
                        byte p0 = entryBytes[offset];
                        byte p1 = entryBytes[offset + planeSize];
                        byte p2 = entryBytes[offset + 2 * planeSize];
                        byte p3 = entryBytes[offset + 3 * planeSize];

                        for (int bit = 0; bit < 8; bit++)
                        {
                            int mask = 1 << (7 - bit);
                            int colorIndex =
                                ((p0 & mask) != 0 ? 1 : 0) |
                                ((p1 & mask) != 0 ? 2 : 0) |
                                ((p2 & mask) != 0 ? 4 : 0) |
                                ((p3 & mask) != 0 ? 8 : 0);

                            row[x * 8 + bit] = palette[colorIndex];
                        }
                    }
                }
            });
            return image;
        }

        private Rgba32[] ReadPalette(byte[] entryBytes)
        {
            Rgba32[] palette = new Rgba32[16];
            for (int i = 0; i < 16; i++)
            {
                byte r = (byte)(entryBytes[i * 3 + 0] * 255 / 63);
                byte g = (byte)(entryBytes[i * 3 + 1] * 255 / 63);
                byte b = (byte)(entryBytes[i * 3 + 2] * 255 / 63);
                palette[i] = new Rgba32(r, g, b, 255);
            }
            return palette;
        }
    }
}
