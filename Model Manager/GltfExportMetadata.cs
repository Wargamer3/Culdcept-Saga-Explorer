using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace CuldceptSagaExplorer.ModelManager
{
    /// <summary>
    /// Explorer builds a model with its own extra names. SaveGlb renames those into the shared
    /// glTF language (rendering and source.culdceptSaga). PrepareForNativeReaders renames them
    /// back so the preview and the importer can keep reading the Explorer names.
    /// The tables in this class are the whole rename list.
    /// </summary>
    public static class GltfExportMetadata
    {
        public const int Version = 1;

        private const string RenderingBlockName = "rendering";
        private const string SourceBlockName = "source";
        private const string SagaSourceName = "culdceptSaga";

        // Copied whole into source.culdceptSaga. Save removes the Explorer name.
        // These blocks are reconstruction data. A viewer does not need them to draw the file.
        private static readonly ExportedName[] SourceBlocks = new ExportedName[]
        {
            new ExportedName("rsm", "modelData"),
            new ExportedName("rsmNodeIndex", "nodeIndex"),
            new ExportedName("rsmMesh", "meshSnapshot"),
            new ExportedName("rsmGroup", "primitiveGroup"),
            new ExportedName("rsmMaterials", "materialTable"),
            new ExportedName("rse", "effectScene"),
            new ExportedName("rseObjectId", "objectId")
        };

        // Copied whole into rendering. Save removes the Explorer name.
        private static readonly ExportedName[] RenderingBlocks = new ExportedName[]
        {
            new ExportedName("rseBillboard", "billboard"),
            new ExportedName("rseRawOpacity", "opacityTrack")
        };

        // ifl is renamed to rendering.textureSequence, and these fields are renamed with it.
        private static readonly ExportedName[] TextureSequenceFields = new ExportedName[]
        {
            new ExportedName("frames", "textures"),
            new ExportedName("framesPerSecond", "framesPerSecond"),
            new ExportedName("rseFrameIndices", "frameIndices"),
            new ExportedName("rseFramesPerSecond", "frameIndicesPerSecond")
        };

        // These rse fields are also copied to rendering.effect, under the same names.
        // The full rse object still moves to source.culdceptSaga.effectScene.
        private static readonly string[] EffectFields = new string[]
        {
            "animationIndex",
            "totalFrames",
            "framesPerSecond",
            "previewLoops",
            "empty",
            "renderless",
            "particleCount"
        };

        public static void SaveGlb(ModelRoot Model, string Path)
        {
            CreateExportModel(Model).SaveGLB(Path);
        }

        public static ModelRoot CreateExportModel(ModelRoot Model)
        {
            // Export must not change the live preview, including images, tracks or extras.
            ModelRoot Copy = Model.DeepClone();
            foreach (ExtraProperties Object in Objects(Copy))
            {
                if (Object.Extras is JsonObject Extras)
                {
                    Object.Extras = RenameExtrasForExport(Extras);
                }
            }

            return Copy;
        }

        /// <summary>Rename a saved file back to the Explorer extra names.</summary>
        public static void PrepareForNativeReaders(ModelRoot Model)
        {
            foreach (ExtraProperties Object in Objects(Model))
            {
                if (Object.Extras is JsonObject Extras)
                {
                    Object.Extras = RenameExtrasForExplorer(Extras);
                }
            }
        }

        private static IEnumerable<ExtraProperties> Objects(ModelRoot Model)
        {
            yield return Model;
            foreach (Scene Item in Model.LogicalScenes)
            {
                yield return Item;
            }

            foreach (Node Item in Model.LogicalNodes)
            {
                yield return Item;
            }

            foreach (Material Item in Model.LogicalMaterials)
            {
                yield return Item;
            }

            foreach (Mesh Mesh in Model.LogicalMeshes)
            {
                yield return Mesh;
                foreach (MeshPrimitive Primitive in Mesh.Primitives)
                {
                    yield return Primitive;
                }
            }
        }

        private static JsonNode Clone(JsonNode Value)
        {
            if (Value == null)
            {
                return null;
            }

            return JsonNode.Parse(Value.ToJsonString());
        }

        private static JsonObject RenameExtrasForExport(JsonObject Original)
        {
            CheckVersion(Original[RenderingBlockName] as JsonObject);
            CheckVersion(SagaSource(Original));

            JsonObject Extras = (JsonObject)Clone(Original);
            JsonObject Rendering = Block(Extras, RenderingBlockName);
            JsonObject Sources = Block(Extras, SourceBlockName);
            JsonObject Saga = Block(Sources, SagaSourceName);

            MoveSourceBlocksToExport(Extras, Saga);
            RenameMaterialForExport(Original, Rendering);
            RenameTextureSequenceForExport(Original, Extras, Rendering);
            MoveRenderingBlocksToExport(Extras, Rendering);
            CopyEffectFieldsForExport(Original, Rendering);
            AttachExportBlocks(Extras, Rendering, Sources, Saga);
            return Extras;
        }

        private static JsonObject RenameExtrasForExplorer(JsonObject Original)
        {
            JsonObject Saga = SagaSource(Original);
            if (Original[RenderingBlockName] == null && Saga == null)
            {
                return Original;
            }

            JsonObject Extras = (JsonObject)Clone(Original);
            JsonObject Rendering = Extras[RenderingBlockName] as JsonObject;
            Saga = SagaSource(Extras);
            CheckVersion(Rendering);
            CheckVersion(Saga);

            if (Saga != null)
            {
                MoveBlocksToExplorer(Extras, Saga, SourceBlocks);
            }

            if (Rendering == null)
            {
                return Extras;
            }

            RenameMaterialForExplorer(Extras, Rendering);
            RenameTextureSequenceForExplorer(Extras, Rendering);
            MoveBlocksToExplorer(Extras, Rendering, RenderingBlocks);
            CopyEffectFieldsForExplorer(Extras, Rendering);
            return Extras;
        }

        private static void MoveSourceBlocksToExport(JsonObject Extras, JsonObject SagaSource)
        {
            foreach (ExportedName BlockName in SourceBlocks)
            {
                if (!Extras.ContainsKey(BlockName.ExplorerName))
                {
                    continue;
                }

                SagaSource[BlockName.ExportName] = Clone(Extras[BlockName.ExplorerName]);
                Extras.Remove(BlockName.ExplorerName);
            }
        }

        private static void MoveRenderingBlocksToExport(JsonObject Extras, JsonObject Rendering)
        {
            foreach (ExportedName BlockName in RenderingBlocks)
            {
                JsonNode Value = Extras[BlockName.ExplorerName];
                if (Value == null)
                {
                    continue;
                }

                Rendering[BlockName.ExportName] = Clone(Value);
                Extras.Remove(BlockName.ExplorerName);
            }
        }

        private static void MoveBlocksToExplorer(JsonObject Extras, JsonObject Source, ExportedName[] Blocks)
        {
            foreach (ExportedName BlockName in Blocks)
            {
                if (Source.ContainsKey(BlockName.ExportName))
                {
                    Extras[BlockName.ExplorerName] = Clone(Source[BlockName.ExportName]);
                }
            }
        }

        // renderMode bit 0 is forceBlend. Bit 1 is additive. UV scroll stays on the rsm block,
        // which moves to source.modelData; the scroll animation is already a glTF texture track.
        private static void RenameMaterialForExport(JsonObject Original, JsonObject Rendering)
        {
            if (!(Original["rsm"] is JsonObject Material) || Material["renderMode"] == null)
            {
                return;
            }

            uint Mode = Material["renderMode"].GetValue<uint>();
            string BlendMode = "alpha";
            if ((Mode & 2) != 0)
            {
                BlendMode = "additive";
            }

            Rendering["material"] = new JsonObject
            {
                ["blendMode"] = BlendMode,
                ["forceBlend"] = (Mode & 1) != 0,
                ["opacity"] = Clone(Material["opacity"]),
                ["selfIllumination"] = Clone(Material["selfIllumination"])
            };
        }

        private static void RenameMaterialForExplorer(JsonObject Extras, JsonObject Rendering)
        {
            if (!(Rendering["material"] is JsonObject Material))
            {
                return;
            }

            JsonObject Native = Extras["rsm"] as JsonObject;
            if (Native == null)
            {
                Native = new JsonObject();
            }

            string Blend = null;
            JsonNode BlendNode = Material["blendMode"];
            if (BlendNode != null)
            {
                Blend = BlendNode.GetValue<string>();
            }
            if (Blend != "alpha" && Blend != "additive")
            {
                throw new InvalidDataException("Unknown material blend mode.");
            }

            uint NativeFlags = 0;
            JsonNode RenderModeNode = Native["renderMode"];
            if (RenderModeNode != null)
            {
                NativeFlags = RenderModeNode.GetValue<uint>();
            }

            uint BlendFlags = 0;
            if (Blend == "additive")
            {
                BlendFlags = 2u;
            }

            bool ForceBlend = false;
            JsonNode ForceBlendNode = Material["forceBlend"];
            if (ForceBlendNode != null)
            {
                ForceBlend = ForceBlendNode.GetValue<bool>();
            }

            uint ForceBlendFlag = 0;
            if (ForceBlend)
            {
                ForceBlendFlag = 1u;
            }

            Native["renderMode"] = (NativeFlags & ~3u) | BlendFlags | ForceBlendFlag;
            Native["opacity"] = Clone(Material["opacity"]);
            Native["selfIllumination"] = Clone(Material["selfIllumination"]);
            Extras["rsm"] = Native;
        }

        private static void RenameTextureSequenceForExport(JsonObject Original, JsonObject Extras, JsonObject Rendering)
        {
            if (!(Original["ifl"] is JsonObject Playlist))
            {
                return;
            }

            JsonObject Sequence = new JsonObject();
            foreach (ExportedName Field in TextureSequenceFields)
            {
                Sequence[Field.ExportName] = Clone(Playlist[Field.ExplorerName]);
            }

            Rendering["textureSequence"] = Sequence;
            Extras.Remove("ifl");
        }

        private static void RenameTextureSequenceForExplorer(JsonObject Extras, JsonObject Rendering)
        {
            if (!(Rendering["textureSequence"] is JsonObject Sequence))
            {
                return;
            }

            JsonObject Playlist = new JsonObject();
            foreach (ExportedName Field in TextureSequenceFields)
            {
                Playlist[Field.ExplorerName] = Clone(Sequence[Field.ExportName]);
            }

            Extras["ifl"] = Playlist;
        }

        private static void CopyEffectFieldsForExport(JsonObject Original, JsonObject Rendering)
        {
            if (!(Original["rse"] is JsonObject Scene))
            {
                return;
            }

            JsonObject Effect = new JsonObject();
            foreach (string Field in EffectFields)
            {
                if (Scene.ContainsKey(Field))
                {
                    Effect[Field] = Clone(Scene[Field]);
                }
            }

            Rendering["effect"] = Effect;
        }

        // Import copies every field on rendering.effect. Export only writes EffectFields.
        private static void CopyEffectFieldsForExplorer(JsonObject Extras, JsonObject Rendering)
        {
            if (!(Rendering["effect"] is JsonObject Effect))
            {
                return;
            }

            JsonObject Scene = Extras["rse"] as JsonObject;
            if (Scene == null)
            {
                Scene = new JsonObject();
            }

            foreach (KeyValuePair<string, JsonNode> Entry in Effect)
            {
                Scene[Entry.Key] = Clone(Entry.Value);
            }

            Extras["rse"] = Scene;
        }

        private static void AttachExportBlocks(JsonObject Extras, JsonObject Rendering, JsonObject Sources, JsonObject Saga)
        {
            if (Rendering.Count > 0)
            {
                Rendering["version"] = Version;
                Extras[RenderingBlockName] = Rendering;
            }

            if (Saga.Count > 0)
            {
                Saga["version"] = Version;
                Sources[SagaSourceName] = Saga;
                Extras[SourceBlockName] = Sources;
            }
        }

        private static JsonObject SagaSource(JsonObject Extras)
        {
            JsonNode SourceNode = Extras[SourceBlockName];
            if (SourceNode == null)
            {
                return null;
            }

            return SourceNode[SagaSourceName] as JsonObject;
        }

        private static JsonObject Block(JsonObject Parent, string Name)
        {
            JsonObject Existing = Parent[Name] as JsonObject;
            if (Existing == null)
            {
                return new JsonObject();
            }

            return Existing;
        }

        private static void CheckVersion(JsonObject Block)
        {
            bool VersionMismatch = true;
            if (Block != null)
            {
                JsonNode VersionNode = Block["version"];
                if (VersionNode != null)
                {
                    VersionMismatch = VersionNode.GetValue<int>() != Version;
                }
            }

            if (Block != null && VersionMismatch)
            {
                throw new InvalidDataException("Unsupported glTF export metadata version.");
            }
        }

        private readonly struct ExportedName
        {
            public readonly string ExplorerName;
            public readonly string ExportName;

            public ExportedName(string ExplorerName, string ExportName)
            {
                this.ExplorerName = ExplorerName;
                this.ExportName = ExportName;
            }
        }
    }
}
