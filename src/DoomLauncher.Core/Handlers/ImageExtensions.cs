using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;

namespace DoomLauncher
{
    internal static class ImageExtensions
    {
        public static bool TryFromFile(string path, out Image image)
        {
            image = null;
            try
            {
                if (!File.Exists(path))
                    return false;

                image = Image.Load(path);
                return true;
            }
            catch
            {
            }

            return false;
        }

        public static Image FromFileOrDefault(string path)
        {
            if (TryFromFile(path, out var image))
                return image;

            return new Image<Rgba32>(1, 1);
        }

        public static Image StretchTo(this Image imgPhoto, int width, int height)
        {
            var dest = imgPhoto.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Bicubic
            }));
            return dest;
        }

        public static Image FixedSize(this Image imgPhoto, int width, int height, Color backColor)
        {
            var dest = new Image<Rgba32>(width, height, backColor);
            dest.Mutate(ctx =>
            {
                ctx.DrawImage(imgPhoto.Clone(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
                    Mode = ResizeMode.Pad,
                    PadColor = backColor,
                    Sampler = KnownResamplers.Bicubic
                })), 1f);
            });
            return dest;
        }

        public static Image ResizeNearest(this Image image, int width, int height)
        {
            return image.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(width, height),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.NearestNeighbor
            }));
        }
    }
}
