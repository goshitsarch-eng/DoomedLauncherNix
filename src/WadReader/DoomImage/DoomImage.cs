using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;

namespace WadReader
{
    public enum ImageType
    {
        Palette,
        Argb
    }

    public class DoomImage
    {
        public const ushort TransparentIndex = 0xFF00;

        public readonly Image<Rgba32> Bitmap;
        public readonly ImageType ImageType;
        public readonly int OffsetX;
        public readonly int OffsetY;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public DoomImage(Image<Rgba32> bitmap, ImageType imageType, int offsetX, int offsetY)
        {
            Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
            ImageType = imageType;
            Width = Bitmap.Width;
            Height = Bitmap.Height;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public DoomImage(int width, int height, ImageType imageType, int offsetX, int offsetY, Rgba32? fillColor = null)
        {
            Width = Math.Max(width, 1);
            Height = Math.Max(height, 1);
            Bitmap = new Image<Rgba32>(Width, Height);
            ImageType = imageType;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Fill(fillColor ?? new Rgba32(0, 0, 0, 0));
        }

        public static DoomImage FromArgbBytes(int w, int h, byte[] argb, int offsetX = 0, int offsetY = 0)
        {
            int numBytes = w * h * 4;

            if (argb.Length != numBytes || w <= 0 || h <= 0)
                return null;

            Image<Rgba32> bitmap = new Image<Rgba32>(w, h);
            bitmap.ProcessPixelRows(accessor =>
            {
                int i = 0;
                for (int y = 0; y < h; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        byte b = argb[i];
                        byte g = argb[i + 1];
                        byte r = argb[i + 2];
                        byte a = argb[i + 3];
                        row[x] = new Rgba32(r, g, b, a);
                        i += 4;
                    }
                }
            });

            return new DoomImage(bitmap, ImageType.Argb, offsetX, offsetY);
        }

        public static DoomImage FromPaletteIndices(int width, int height, ushort[] indices, int offsetX, int offsetY)
        {
            if (width <= 0 || height <= 0 || indices.Length != width * height)
                return null;

            Image<Rgba32> bitmap = new Image<Rgba32>(width, height);
            bitmap.ProcessPixelRows(accessor =>
            {
                int i = 0;
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        ushort index = indices[i++];
                        byte alpha = (byte)~(index >> 8);
                        byte palIndex = (byte)index;
                        row[x] = new Rgba32(0, 0, palIndex, alpha);
                    }
                }
            });

            return new DoomImage(bitmap, ImageType.Palette, offsetX, offsetY);
        }

        public void Fill(Rgba32 color)
        {
            Bitmap.Mutate(ctx => ctx.BackgroundColor(color));
            Bitmap.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    row.Fill(color);
                }
            });
        }

        public DoomImage PaletteToArgb(Palette palette)
        {
            if (ImageType == ImageType.Argb)
                return this;

            Rgba32[] colors = palette.DefaultLayer;
            byte[] argbBytes = new byte[Width * Height * 4];
            int offset = 0;

            Bitmap.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < Width; x++)
                    {
                        Rgba32 px = row[x];
                        if (px.A != 0)
                        {
                            int index = px.B;
                            if (index >= 0 && index < colors.Length)
                            {
                                Rgba32 color = colors[index];
                                argbBytes[offset] = color.B;
                                argbBytes[offset + 1] = color.G;
                                argbBytes[offset + 2] = color.R;
                                argbBytes[offset + 3] = 255;
                            }
                        }
                        offset += 4;
                    }
                }
            });

            DoomImage image = FromArgbBytes(Width, Height, argbBytes, OffsetX, OffsetY);
            if (image != null)
                return image;

            return new DoomImage(1, 1, ImageType.Argb, 0, 0);
        }
    }
}
