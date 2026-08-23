using System.IO;
using System.Reflection;

namespace DoomLauncher
{
    public static class PaletteResources
    {
        public static byte[] DoomPalette => Load("PLAYPAL.LMP");
        public static byte[] HereticPalette => Load("HereticPalette.pal");
        public static byte[] HexenPalette => Load("HexenPalette.pal");

        private static byte[] Load(string name)
        {
            var assembly = typeof(PaletteResources).Assembly;
            using var stream = assembly.GetManifestResourceStream($"DoomLauncher.Resources.{name}");
            if (stream == null)
            {
                string path = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? ".", "Resources", name);
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
                path = Path.Combine(Directory.GetCurrentDirectory(), "Resources", name);
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
                return System.Array.Empty<byte>();
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
