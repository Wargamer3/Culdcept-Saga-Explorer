using System.IO;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager
{
    public class TextureExporter
    {
        /// <summary>
        /// Dumps the texture payloads from the CuldceptModel to the target export directory.
        /// </summary>
        /// <param name="TextureMap">The Dictionary containing material names and their PNG byte arrays.</param>
        /// <param name="ExportDirectory">The folder where the .dae file is being saved.</param>
        public static void ExportTexturesToDisk(Dictionary<string, List<byte[]>> TextureMap, string ExportDirectory)
        {
            if (TextureMap == null || TextureMap.Count == 0)
            {
                return;
            }

            // Ensure the destination folder exists
            if (!Directory.Exists(ExportDirectory))
            {
                Directory.CreateDirectory(ExportDirectory);
            }

            foreach (KeyValuePair<string, List<byte[]>> TextureEntry in TextureMap)
            {
                string MaterialName = TextureEntry.Key;
                List<byte[]> Frames = TextureEntry.Value;
                string CleanName = Path.GetFileName(MaterialName);

                if (Frames == null || Frames.Count == 0)
                {
                    continue;
                }

                for (int FrameIndex = 0; FrameIndex < Frames.Count; FrameIndex++)
                {
                    // Ensure we don't double up on extension if cleanName already ends with .png
                    string Extension = CleanName.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) ? "" : ".png";

                    string FileName = Frames.Count > 1
                        ? $"{Path.GetFileNameWithoutExtension(CleanName)}_{FrameIndex}{Extension}"
                        : $"{CleanName}{Extension}";

                    string FullPath = Path.Combine(ExportDirectory, FileName);

                    if (!File.Exists(FullPath))
                    {
                        File.WriteAllBytes(FullPath, Frames[FrameIndex]);
                    }
                }
            }
        }
    }
}
