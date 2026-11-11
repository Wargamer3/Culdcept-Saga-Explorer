using CuldceptSagaExplorer.AssetManager.RSE;
using NUnit.Framework;
using SharpGLTF.Schema2;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CuldceptSagaExplorer.ModelManager.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class EffectConversionTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string EffectDirectory => Environment.GetEnvironmentVariable("CULDCEPT_EFFECT_DIRECTORY")
            ?? @"D:\CuldceptSaga\Culdcept Saga Extracted\data\EFFECT";

        [Test]
        public void MainGlbConversionIncludesModelsAndEffectsAndBrowsesSelfContainedOutputs()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-batch-effects-{Guid.NewGuid():N}");
            string Source = Path.Combine(Folder, "Culdcept Saga Extracted", "data", "EFFECT");
            string Models = Path.Combine(Folder, "RSP_unpacked");
            string Output = Path.Combine(Folder, "glTF");
            string Fallback = Path.Combine(Folder, "fallback");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Path.Combine(Models, "DATA00"));
            Directory.CreateDirectory(Fallback);
            try
            {
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Source, "EB_401" + Extension));
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Models, "DATA00", "model" + Extension));
                }
                File.Copy(Path.Combine(EffectDirectory, "EB_401.RSE"), Path.Combine(Source, "EB_401.RSE"));
                File.WriteAllText(Path.Combine(Source, "missing.RSE"),
                    "[Version]\nVERSION=2\n"); // Deterministic rejection: absent dependencies are now preserved, not fatal.
                File.WriteAllText(Path.Combine(Source, "fallback.RSE"),
                    File.ReadAllText(Path.Combine(Source, "EB_401.RSE")).Replace("EB_401.RSM", "fallback.RSM"));
                foreach (string Extension in new[] { ".RSM", ".RST" })
                {
                    File.Copy(Path.Combine(EffectDirectory, "EB_401" + Extension), Path.Combine(Fallback, "fallback" + Extension));
                }

                foreach (string Path in Directory.GetFiles(EffectDirectory, "EE_735*")
                    .Where(Path => new[] { ".rse", ".rsm", ".rst", ".rsa" }.Contains(System.IO.Path.GetExtension(Path).ToLowerInvariant())))
                {
                    File.Copy(Path, System.IO.Path.Combine(Source, System.IO.Path.GetFileName(Path)));
                }

                using (ExportProgressForm Progress = new global::CuldceptSagaExplorer.ExportProgressForm(Models, Output, false, Source, Fallback, 30))
                {
                    Task Work = (Task)Progress.GetType().GetMethod("RunExtractionAsync", PrivateInstance)!.Invoke(Progress, null)!;
                    DateTime Deadline = DateTime.UtcNow.AddSeconds(90);
                    while (!Work.IsCompleted && DateTime.UtcNow < Deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Assert.That(Work.IsCompleted, Is.True, "Batch conversion must finish.");
                    Work.GetAwaiter().GetResult();
                    Assert.That(Progress.EffectResults.Count, Is.EqualTo(4));
                    Assert.That(Progress.EffectResults.Count(Result => Result.Exported), Is.EqualTo(3),
                        string.Join("\n", Progress.EffectResults.Where(Result => !Result.Exported).Select(Result => Result.Error)));
                    Assert.That(Progress.EffectResults.Single(Result => !Result.Exported).Error, Does.Contain("missing"));
                }
                Assert.That(File.Exists(Path.Combine(Output, "DATA00", "model.glb")), Is.True);
                Assert.That(Directory.GetFiles(Output, "*.dae", SearchOption.AllDirectories), Is.Empty,
                    "The GLB button must not accidentally call the Collada exporter.");
                string EffectsOutput = Path.Combine(Output, "EFFECT");
                RseBatchResult[] Results = JsonSerializer.Deserialize<RseBatchResult[]>(File.ReadAllText(Path.Combine(EffectsOutput, RseBatchExporter.ReportFileName)))!;
                Assert.That(Results.Count(Result => !Result.Exported), Is.EqualTo(1));
                Assert.That(ModelRoot.Load(Path.Combine(EffectsOutput, "EE_735.glb")).LogicalMaterials
                    .Any(Material =>
                    {
                        if (Material.Extras == null)
                        {
                            return false;
                        }

                        JsonNode? Rendering = Material.Extras["rendering"];
                        if (Rendering == null)
                        {
                            return false;
                        }

                        return Rendering["opacityTrack"] != null;
                    }), Is.True);
                Assert.That(ModelRoot.Load(Path.Combine(EffectsOutput, "EB_401.glb")).Extras!["rendering"]!["effect"]!["framesPerSecond"]!.GetValue<float>(), Is.EqualTo(30));

                // A failed conversion must never advertise an older GLB as a current success.
                File.Copy(Path.Combine(EffectsOutput, "EB_401.glb"), Path.Combine(EffectsOutput, "missing.glb"));
                using CuldceptSagaExplorer Window = new global::CuldceptSagaExplorer.CuldceptSagaExplorer { ShowInTaskbar = false };
                Window.Controls.Find("txtWorkingFolder", true).OfType<TextBox>().Single().Text = Folder;
                Window.GetType().GetMethod("OpenEffectFolder", PrivateInstance)!.Invoke(Window, null);
                ListView List = (ListView)Window.GetType().GetField("EffectScenes", PrivateInstance)!.GetValue(Window)!;
                TabControl Tabs = Window.Controls.OfType<TabControl>().Single();
                Assert.That(Tabs.TabPages.Cast<TabPage>().Count(Page => Page.Text == "Effects"), Is.EqualTo(1));
                Assert.That(List.Name, Is.EqualTo("lsEffectsModels"));
                Assert.That(List.View, Is.EqualTo(View.List));
                Assert.That(List.Items.Count, Is.EqualTo(4));
                Assert.That(List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "missing").SubItems[1].Text, Is.EqualTo("Not supported"));
                Assert.That(List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "EB_401").Tag,
                    Is.EqualTo(Path.Combine(EffectsOutput, "EB_401.glb")));
                Assert.That(List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "EE_735").ToolTipText,
                    Does.Contain("raw opacity is preserved"));
                CheckBox Toggle = (CheckBox)Window.GetType().GetField("UseConvertedEffects", PrivateInstance)!.GetValue(Window)!;
                Toggle.Checked = false;
                Assert.That(List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "EB_401").Tag,
                    Is.EqualTo(Path.Combine(Source, "EB_401.RSE")));
                Toggle.Checked = true;

                Directory.Move(Source, Source + ".unavailable");
                Directory.Move(Fallback, Fallback + ".unavailable");
                Window.GetType().GetMethod("RefreshEffectScenes", PrivateInstance)!.Invoke(Window, null);
                Assert.That(List.Items.Count, Is.EqualTo(3), "Converted browsing works without any source RSE/resources.");
                global::GraphicsManager.Model3DViewer Viewer = Window.Controls.OfType<global::GraphicsManager.Model3DViewer>().Single();
                _ = Window.Handle;
                _ = Viewer.Handle;
                _ = List.Handle;
                ListView Textures = Window.Controls.Find("lsEffectModelTextures", true).OfType<ListView>().Single();
                ListView Animations = Window.Controls.Find("lsEffectModelAnimations", true).OfType<ListView>().Single();
                _ = Textures.Handle;
                _ = Animations.Handle;
                string Converted = Path.Combine(EffectsOutput, "fallback.glb");
                ModelRoot Legacy = ModelRoot.Load(Converted);
                Assert.That(Legacy.LogicalTextures.All(Texture => !string.IsNullOrWhiteSpace(Texture.Name)
                    && !string.Equals(Texture.Name, "texture", StringComparison.OrdinalIgnoreCase)), Is.True,
                    "New exports preserve source material names on textures.");
                string[] ExpectedTextureNames = Legacy.LogicalTextures.Select(Texture =>
                    Legacy.LogicalMaterials.First(Material =>
                    {
                        MaterialChannel? BaseColor = Material.FindChannel("BaseColor");
                        if (!BaseColor.HasValue)
                        {
                            return false;
                        }

                        return BaseColor.Value.Texture == Texture;
                    }).Name).ToArray();
                foreach (Texture? Texture in Legacy.LogicalTextures)
                {
                    Texture.Name = "texture";
                    Texture.PrimaryImage.Name = "image";
                }
                Legacy.SaveGLB(Converted);
                List.Items.Cast<ListViewItem>().Single(Item => (string)Item.Tag == Converted).Selected = true;
                Assert.That(Viewer.EffectMetadata!["framesPerSecond"]!.GetValue<float>(), Is.EqualTo(30));
                Assert.That(Viewer.EffectFallbackDependencies.Count, Is.EqualTo(2));
                Assert.That(Viewer.Textures.Count, Is.GreaterThan(0));
                Assert.That(Textures.Items.Count, Is.EqualTo(Viewer.Textures.Count));
                Assert.That(Textures.Items.Cast<ListViewItem>().Select(Item => Item.Text), Is.EqualTo(ExpectedTextureNames),
                    "Already-converted effects recover names from material references without reconverting.");
                Assert.That(Animations.Items.Cast<ListViewItem>().Select(Item => Item.Text),
                    Is.EqualTo(Viewer.GetAnimationNames()));
                Assert.That(Textures.SelectedItems.Count, Is.EqualTo(1));
                Assert.That(Animations.SelectedIndices[0], Is.EqualTo(0));
                Label Status = (Label)Window.GetType().GetField("EffectStatus", PrivateInstance)!.GetValue(Window)!;
                Assert.That(Status.Text, Does.Contain("stored 30 FPS").And.Contain("no source RSE"));
                Viewer.SetAnimationPaused(true);
                Viewer.RestartAnimation();
                Viewer.SetAnimationPaused(false);
                Animated3DModel Active = (Animated3DModel)Viewer.GetType().GetField("DisplayedModel", PrivateInstance)!.GetValue(Viewer)!;
                Active.Update(new Microsoft.Xna.Framework.GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(.25)));
                Assert.That(Active.AnimationTime, Is.EqualTo(.25).Within(1e-6));
                Animations.Items[0].Selected = false;
                Animations.Items[0].Selected = true;
                Assert.That(Active.AnimationTime, Is.EqualTo(.25).Within(1e-6), "Reselecting the active animation preserves its playback position.");
                string Copy = Path.Combine(Folder, "reexport.glb");
                Viewer.ExportEffect(Copy);
                Assert.That(ModelRoot.Load(Copy).Extras!["rendering"]!["effect"]!["framesPerSecond"]!.GetValue<float>(), Is.EqualTo(30));
                List.SelectedItems[0].Selected = false;
                List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "EE_735").Selected = true;
                Assert.That(Status.Text, Does.Contain("raw opacity is preserved").And.Contain("bounded approximation"));
                List.SelectedItems[0].Selected = false;
                List.Items.Cast<ListViewItem>().Single(Item => Item.Text == "EB_401").Selected = true;
                Assert.That(Status.Text, Does.Contain("Playing converted EB_401.glb").And.Not.Contain("raw opacity is preserved"));
                Assert.That(Textures.Items.Count, Is.EqualTo(Viewer.Textures.Count));
                Assert.That(Animations.Items.Count, Is.EqualTo(Viewer.GetAnimationNames().Count));
                Window.GetType().GetMethod("ClearModelDetails", PrivateInstance)!.Invoke(Window, null);
                Assert.That(Textures.Items.Count, Is.Zero);
                Assert.That(Animations.Items.Count, Is.Zero);
                File.WriteAllText(Path.Combine(EffectsOutput, RseBatchExporter.ReportFileName), "{invalid");
                Window.GetType().GetMethod("RefreshEffectScenes", PrivateInstance)!.Invoke(Window, null);
                Assert.That(List.Items.Count, Is.Zero, "A corrupt conversion report must not expose stale GLBs as current.");
                Assert.That(Status.Text, Does.Contain("Cannot read effect conversion report"));
            }
            finally { Directory.Delete(Folder, true); }
        }

        [Test]
        public void BatchReportsEveryRejectedSourceWithoutHidingOtherSuccessesAndGuardsInvalidConfiguration()
        {
            string Folder = Path.Combine(Path.GetTempPath(), $"culdcept-batch-errors-{Guid.NewGuid():N}");
            string Source = Path.Combine(Folder, "source"), Output = Path.Combine(Folder, "output");
            Directory.CreateDirectory(Source);
            try
            {
                File.WriteAllText(Path.Combine(Source, "empty.RSE"), "[Version]\nVERSION=1\nAUTORST=1\n[SceneInformation]\nTotalFrame=3\n");
                File.WriteAllText(Path.Combine(Source, "malformed.RSE"), "not an RSE");
                File.WriteAllText(Path.Combine(Source, "unsupported.RSE"), "[Version]\nVERSION=2\n");
                System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
                System.Collections.Generic.IReadOnlyList<RseBatchResult> Results = RseBatchExporter.ExportAll(Source, Output, Progress: Messages.Add);
                Assert.That(Results.Count, Is.EqualTo(3));
                Assert.That(Results.Single(Result => Result.Exported).Source, Is.EqualTo("empty.RSE"));
                Assert.That(Results.Where(Result => !Result.Exported).All(Result => !string.IsNullOrEmpty(Result.Error)), Is.True);
                Assert.That(Messages.Count(Message => Message.StartsWith("Effect unavailable:")), Is.EqualTo(2));
                Assert.That(ModelRoot.Load(Path.Combine(Output, "empty.glb")).Extras!["rendering"]!["effect"]!["empty"]!.GetValue<bool>(), Is.True);
                Assert.Throws<DirectoryNotFoundException>(() => RseBatchExporter.ExportAll(Source + ".absent", Output));
                Assert.Throws<DirectoryNotFoundException>(() => RseBatchExporter.ExportAll(Source, Output, DependencyDirectory: Folder + ".absent"));
                Assert.Throws<ArgumentOutOfRangeException>(() => RseBatchExporter.ExportAll(Source, Output, float.NaN));
                File.Delete(Path.Combine(Output, "empty.glb"));
                Directory.CreateDirectory(Path.Combine(Output, "empty.glb"));
                Assert.That(() => RseBatchExporter.ExportAll(Source, Output),
                    Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>(),
                    "Output/storage failures abort rather than being reported as unavailable source content.");
                RseBatchResult[] Interrupted = JsonSerializer.Deserialize<RseBatchResult[]>(
                    File.ReadAllText(Path.Combine(Output, RseBatchExporter.ReportFileName)))!;
                Assert.That(Interrupted.All(Result => !Result.Exported), Is.True);
                Assert.That(Interrupted.All(Result => Result.Error.Contains("has not completed")), Is.True);
            }
            finally { Directory.Delete(Folder, true); }
        }
    }
}
