using DoomLauncher.Interfaces;
using SixLabors.ImageSharp;
using System.IO;
using System.Linq;

namespace DoomLauncher.Handlers
{
    public static class LegacyTitlePicMigration
    {
        public static bool DeleteScreenshotThatIsReallyATitlePic(IFileHandler fileHandler, IGameFile gameFile, Image image)
        {
            var screenshots = fileHandler.GetFiles(gameFile, FileType.Screenshot);

            if (!screenshots.Any())
                return false;

            image = LegacyScaleImage(image);

            long fileSize = 0;
            try
            {
                using (var imageStream = new MemoryStream())
                {
                    image.SaveAsPng(imageStream);
                    fileSize = imageStream.Length;
                }
            }
            catch { }

            foreach (IFileData screenshot in screenshots)
            {
                try
                {
                    FileInfo fi = new FileInfo(screenshot.FullFileName);
                    if (fi.Length == fileSize)
                    {
                        fileHandler.DeleteFile(screenshot);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        public static Image LegacyScaleImage(Image image)
        {
            if (!(image.Width / (float)image.Height).ApproxEquals(1.6f))
                return image;

            int multiplier = image.Width / 320;
            return image.ResizeNearest(640 * multiplier, 480 * multiplier);
        }
    }
}
