using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public sealed record RseBatchResult(string Source, string Output, bool Exported, string Error);

    public static class RseBatchExporter
    {
        public const string ReportFileName = "conversion-report.json";

        public static IReadOnlyList<RseBatchResult> ExportAll(string SourceDirectory, string OutputDirectory,
            float FramesPerSecond = RseExporter.DefaultPreviewFramesPerSecond, string DependencyDirectory = null,
            Action<string> Progress = null)
        {
            if (!Directory.Exists(SourceDirectory))
            {
                throw new DirectoryNotFoundException(SourceDirectory);
            }

            if (!float.IsFinite(FramesPerSecond) || FramesPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(FramesPerSecond));
            }

            if (DependencyDirectory != null && !Directory.Exists(DependencyDirectory))
            {
                throw new DirectoryNotFoundException(DependencyDirectory);
            }

            Directory.CreateDirectory(OutputDirectory);
            List<RseBatchResult> Results = new List<RseBatchResult>();
            string[] Sources = Directory.EnumerateFiles(SourceDirectory, "*.RSE").OrderBy(Path => Path, StringComparer.OrdinalIgnoreCase).ToArray();
            string Report = Path.Combine(OutputDirectory, ReportFileName);
            File.WriteAllText(Report, JsonSerializer.Serialize(Sources.Select(Source =>
                new RseBatchResult(Path.GetFileName(Source), Path.GetFileNameWithoutExtension(Source) + ".glb", false,
                    "Conversion has not completed; any existing GLB is from an earlier run.")),
                new JsonSerializerOptions { WriteIndented = true }));
            foreach (string Source in Sources)
            {
                string Output = Path.Combine(OutputDirectory, Path.GetFileNameWithoutExtension(Source) + ".glb");
                if (Progress != null)
                {
                    Progress.Invoke($"Converting effect {Path.GetFileName(Source)}");
                }
                SharpGLTF.Schema2.ModelRoot Model;
                try
                {
                    Model = RseExporter.Load(Source, FramesPerSecond, DependencyDirectory);
                }
                catch (Exception Error) when (Error is NotSupportedException || Error is InvalidDataException
                    || Error is FileNotFoundException || Error is DirectoryNotFoundException)
                {
                    Results.Add(new RseBatchResult(Path.GetFileName(Source), Path.GetFileName(Output), false, Error.Message));
                    if (Progress != null)
                    {
                        Progress.Invoke($"Effect unavailable: {Path.GetFileName(Source)}: {Error.Message}");
                    }
                    continue;
                }
                CuldceptSagaExplorer.ModelManager.GltfExportMetadata.SaveGlb(Model, Output);
                Results.Add(new RseBatchResult(Path.GetFileName(Source), Path.GetFileName(Output), true, null));
            }
            File.WriteAllText(Report,
                JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            return Results;
        }
    }
}
