using System.Numerics;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSM
{
    /// <summary>
    /// Serializes the RSM material list into a glTF "rsmMaterials" extras block and reads it back.
    /// The game allows duplicate material names (same texture, different render params) which
    /// SharpGLTF collapses into a single material, so the full list is preserved verbatim.
    /// </summary>
    public static class RsmMaterialExtras
    {
        public const string Key = "rsmMaterials";

        public static JsonNode ToJson(List<RsmMaterial> ListMaterial)
        {
            JsonArray MaterialArray = new JsonArray();
            foreach (RsmMaterial Material in ListMaterial)
            {
                MaterialArray.Add(new JsonObject
                {
                    ["name"] = Material.Name,
                    ["width"] = Material.TextureWidth,
                    ["height"] = Material.TextureHeight,
                    ["renderMode"] = Material.RenderMode,
                    ["opacity"] = Material.Opacity,
                    ["selfIllumination"] = Material.SelfIllumination,
                    ["uvScrollX"] = Material.UvScroll.X,
                    ["uvScrollY"] = Material.UvScroll.Y
                });
            }

            return MaterialArray;
        }

        public static List<RsmMaterial> FromJson(JsonNode SourceNode)
        {
            List<RsmMaterial> ListMaterial = new List<RsmMaterial>();
            if (!(SourceNode is JsonArray MaterialArray))
            {
                return ListMaterial;
            }

            foreach (JsonNode MaterialItem in MaterialArray)
            {
                if (!(MaterialItem is JsonObject MaterialObject))
                {
                    continue;
                }

                string Name = string.Empty;
                JsonNode NameNode = MaterialObject["name"];
                if (NameNode != null)
                {
                    Name = NameNode.GetValue<string>();
                }

                int Width = 0;
                JsonNode WidthNode = MaterialObject["width"];
                if (WidthNode != null)
                {
                    Width = WidthNode.GetValue<int>();
                }

                int Height = 0;
                JsonNode HeightNode = MaterialObject["height"];
                if (HeightNode != null)
                {
                    Height = HeightNode.GetValue<int>();
                }

                int RenderMode = 0;
                JsonNode RenderModeNode = MaterialObject["renderMode"];
                if (RenderModeNode != null)
                {
                    RenderMode = RenderModeNode.GetValue<int>();
                }

                float Opacity = 1f;
                JsonNode OpacityNode = MaterialObject["opacity"];
                if (OpacityNode != null)
                {
                    Opacity = OpacityNode.GetValue<float>();
                }

                float SelfIllumination = 0f;
                JsonNode SelfIlluminationNode = MaterialObject["selfIllumination"];
                if (SelfIlluminationNode != null)
                {
                    SelfIllumination = SelfIlluminationNode.GetValue<float>();
                }

                float UvScrollX = 0f;
                JsonNode UvScrollXNode = MaterialObject["uvScrollX"];
                if (UvScrollXNode != null)
                {
                    UvScrollX = UvScrollXNode.GetValue<float>();
                }

                float UvScrollY = 0f;
                JsonNode UvScrollYNode = MaterialObject["uvScrollY"];
                if (UvScrollYNode != null)
                {
                    UvScrollY = UvScrollYNode.GetValue<float>();
                }

                ListMaterial.Add(new RsmMaterial
                {
                    Name = Name,
                    TextureWidth = (ushort)Width,
                    TextureHeight = (ushort)Height,
                    RenderMode = (uint)RenderMode,
                    Opacity = Opacity,
                    SelfIllumination = SelfIllumination,
                    UvScroll = new Vector2(UvScrollX, UvScrollY)
                });
            }

            return ListMaterial;
        }
    }
}
