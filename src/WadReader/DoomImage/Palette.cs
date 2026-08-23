using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;

namespace WadReader
{
    public class Palette
    {
        public static readonly int NumColors = 256;
        public static readonly int ColorComponents = 3;
        public static readonly int BytesPerLayer = NumColors * ColorComponents;
        private static Palette DefaultPalette;

        private readonly List<Rgba32[]> layers;

        public int Count => layers.Count;
        public Rgba32[] DefaultLayer => layers[0];

        private Palette(List<Rgba32[]> paletteLayers)
        {
            layers = paletteLayers;
        }

        public static Palette From(byte[] data)
        {
            if (data.Length == 0 || data.Length % BytesPerLayer != 0)
                return null;

            List<Rgba32[]> paletteLayers = new List<Rgba32[]>();
            for (int layer = 0; layer < data.Length / BytesPerLayer; layer++)
            {
                int offset = layer * BytesPerLayer;
                Span<byte> layerSpan = new Span<byte>(data, offset, BytesPerLayer);
                paletteLayers.Add(PaletteLayerFrom(layerSpan));
            }

            return new Palette(paletteLayers);
        }

        private static Rgba32[] PaletteLayerFrom(Span<byte> data)
        {
            Rgba32[] paletteColors = new Rgba32[NumColors];

            int offset = 0;
            for (int i = 0; i < BytesPerLayer; i += ColorComponents)
                paletteColors[offset++] = new Rgba32(data[i], data[i + 1], data[i + 2], 255);

            return paletteColors;
        }

        public Rgba32[] Layer(int index) => layers[index];

        public static Palette GetDefaultPalette()
        {
            if (DefaultPalette != null)
                return DefaultPalette;

            byte[] data = new byte[NumColors * ColorComponents];

            for (int i = 0; i < NumColors; i++)
            {
                data[i * ColorComponents] = (byte)i;
                data[i * ColorComponents + 1] = (byte)i;
                data[i * ColorComponents + 2] = (byte)i;
            }

            Palette palette = From(data);
            if (palette == null)
                throw new NullReferenceException("Failed to create the default palette, shouldn't be possible");

            DefaultPalette = palette;
            return palette;
        }
    }
}
