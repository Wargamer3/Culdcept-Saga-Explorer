using CuldceptSagaExplorer.AssetManager.RSE;
using CuldceptSagaExplorer.ModelManager;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace CuldceptSagaExplorer
{
    public partial class CuldceptSagaExplorer
    {
        private ListView EffectScenes;
        private Label EffectStatus;
        private NumericUpDown EffectFramesPerSecond;
        private TextBox EffectDependencyFolder;
        private Button ExportEffectButton;
        private Button RestartEffectButton;
        private Button PauseEffectButton;
        private TabPage EffectsTab;
        private bool EffectPaused;
        private CheckBox UseConvertedEffects;

        private void InitializeEffectsTab()
        {
            EffectsTab = tabEffects;
            EffectScenes = lsEffectsModels;
            TableLayoutPanel Layout = new TableLayoutPanel
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(620, 0),
                ColumnCount = 1,
                RowCount = 2
            };
            Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FlowLayoutPanel ControlsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            Button OpenButton = new Button { Text = "Open RSE/GLB...", AutoSize = true };
            Button LoadButton = new Button { Text = "Play selected", AutoSize = true };
            RestartEffectButton = new Button { Text = "Restart", AutoSize = true, Enabled = false };
            PauseEffectButton = new Button { Text = "Pause", AutoSize = true, Enabled = false };
            ExportEffectButton = new Button { Text = "Export effect GLB...", AutoSize = true, Enabled = false };
            EffectFramesPerSecond = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 240,
                DecimalPlaces = 2,
                Value = (decimal)RseExporter.DefaultPreviewFramesPerSecond,
                Width = 70
            };
            ControlsRow.Controls.AddRange(new Control[]
            {
                OpenButton, LoadButton, RestartEffectButton, PauseEffectButton, ExportEffectButton,
                new Label { Text = "FPS (assumed; reload to apply)", AutoSize = true }, EffectFramesPerSecond
            });
            EffectDependencyFolder = new TextBox { ReadOnly = true, Width = 300 };
            Button DependencyButton = new Button { Text = "Fallback folder...", AutoSize = true };
            Button ClearDependencyButton = new Button { Text = "Clear fallback", AutoSize = true };
            ControlsRow.Controls.AddRange(new Control[]
            {
                new Label { Text = "Optional dependencies (reload to apply)", AutoSize = true },
                EffectDependencyFolder, DependencyButton, ClearDependencyButton
            });
            UseConvertedEffects = new CheckBox { Text = "Use converted GLBs", Checked = true, AutoSize = true };
            ControlsRow.Controls.Add(UseConvertedEffects);
            UseConvertedEffects.CheckedChanged += (Sender, Arguments) => RefreshEffectScenes();
            DependencyButton.Click += (Sender, Arguments) =>
            {
                using FolderBrowserDialog Dialog = new FolderBrowserDialog
                {
                    Description = "Choose an optional folder for exact-name RSE dependencies. Scene-local files take priority.",
                    SelectedPath = EffectDependencyFolder.Text,
                    ShowNewFolderButton = false
                };
                if (Dialog.ShowDialog(this) == DialogResult.OK)
                {
                    EffectDependencyFolder.Text = Dialog.SelectedPath;
                }
            };
            ClearDependencyButton.Click += (Sender, Arguments) => EffectDependencyFolder.Clear();
            EffectScenes.MultiSelect = false;
            EffectScenes.HideSelection = false;
            EffectScenes.ShowItemToolTips = true;
            lsEffectModelTextures.MultiSelect = false;
            lsEffectModelTextures.HideSelection = false;
            lsEffectModelAnimations.HideSelection = false;
            lsEffectModelTextures.SelectedIndexChanged += (Sender, Arguments) => LoadSelectedTexture(lsEffectModelTextures);
            lsEffectModelAnimations.SelectedIndexChanged += (Sender, Arguments) => PlaySelectedAnimation(lsEffectModelAnimations);
            EffectScenes.Columns.Add("Scene", 140);
            EffectScenes.Columns.Add("Preview", 120);
            EffectScenes.Columns.Add("File", 430);
            EffectStatus = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(600, 0),
                Dock = DockStyle.Fill,
                Padding = new Padding(4),
                Text = "RSM scenes, OPACITY/MOPA fades, single LOOP/NOLOOP RSA motions, UVANIM and baked KEY2/KEY3 particles with texture playlists (SPIN=0 through 5, including three-axis billboards).\r\nSilent asset-only preview/export; SceneNo text-bank metadata is preserved. GLB billboards/texture playlists require extras-aware playback; filename particle models are an Explorer extension. Timing/end behavior remains unverified."
            };
            OpenButton.Click += (Sender, Arguments) =>
            {
                using OpenFileDialog Dialog = new OpenFileDialog { Filter = "Effect scenes (*.RSE;*.glb;*.gltf)|*.rse;*.glb;*.gltf", Title = "Open an effect scene" };
                if (Dialog.ShowDialog(this) == DialogResult.OK)
                {
                    LoadEffect(Dialog.FileName);
                }
            };
            LoadButton.Click += (Sender, Arguments) => LoadSelectedEffect();
            EffectScenes.ItemSelectionChanged += (Sender, Arguments) =>
            {
                if (Arguments.IsSelected)
                {
                    LoadSelectedEffect();
                }
            };
            RestartEffectButton.Click += (Sender, Arguments) => view3DModel.RestartAnimation();
            PauseEffectButton.Click += (Sender, Arguments) =>
            {
                EffectPaused = !EffectPaused;
                view3DModel.SetAnimationPaused(EffectPaused);
                PauseEffectButton.Text = EffectPaused ? "Resume" : "Pause";
            };
            ExportEffectButton.Click += (Sender, Arguments) =>
            {
                using SaveFileDialog Dialog = new SaveFileDialog { Filter = "glTF binary (*.glb)|*.glb", DefaultExt = "glb", AddExtension = true };
                if (Dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    view3DModel.ExportEffect(Dialog.FileName);
                    EffectStatus.Text = $"Exported {Dialog.FileName}\r\nIncludes RSA/node motion and KHR_animation_pointer opacity/UV animation. Billboards use node extras.rendering.billboard; other viewers use fixed orientation. Raw-opacity materials require extras.rendering.opacityTrack playback; ordinary viewers use a bounded approximation. Audio is not exported.";
                }
                catch (Exception Error)
                {
                    MessageBox.Show(this, Error.Message, "Effect export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            Layout.Controls.Add(ControlsRow, 0, 0);
            Layout.Controls.Add(EffectStatus, 0, 1);
            if (components == null)
            {
                components = new System.ComponentModel.Container();
            }

            ContextMenuStrip Options = new ContextMenuStrip(components);
            Options.Items.Add(new ToolStripControlHost(Layout) { AutoSize = true });
            EffectsTab.ContextMenuStrip = Options;
            EffectScenes.ContextMenuStrip = Options;
            tabControl1.SelectedIndexChanged += (Sender, Arguments) =>
            {
                btnReplaceSelectedModel.Enabled = tabControl1.SelectedTab != EffectsTab;
                btnReplaceSelectedTexture.Enabled = tabControl1.SelectedTab != EffectsTab;
            };
        }

        private void OpenEffectFolder()
        {
            EffectScenes.Items.Clear();
            string PreviewFolder = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted", "eeprev");
            EffectDependencyFolder.Text = Directory.Exists(PreviewFolder) ? PreviewFolder : "";
            RefreshEffectScenes();
        }

        private void RefreshEffectScenes()
        {
            EffectScenes.Items.Clear();
            string Folder = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted", "data", "EFFECT");
            string ConvertedFolder = Path.Combine(txtWorkingFolder.Text, "glTF", "EFFECT");
            Dictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(Folder))
            {
                foreach (string Path in Directory.EnumerateFiles(Folder, "*.RSE"))
                {
                    Paths.Add(System.IO.Path.GetFileNameWithoutExtension(Path), Path);
                }
            }

            Dictionary<string, string> FailedConversions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Report = Path.Combine(ConvertedFolder, RseBatchExporter.ReportFileName);
            if (UseConvertedEffects.Checked && File.Exists(Report))
            {
                try
                {
                    List<RseBatchResult> Results = JsonSerializer.Deserialize<List<RseBatchResult>>(File.ReadAllText(Report))
                        ?? throw new InvalidDataException("The effect conversion report is empty.");
                    foreach (RseBatchResult Result in Results)
                    {
                        if (Result == null || string.IsNullOrEmpty(Result.Source)
                            || Path.GetFileName(Result.Source) != Result.Source || !Result.Source.EndsWith(".rse", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("The effect conversion report contains an invalid source.");
                        }

                        if (!Result.Exported)
                        {
                            FailedConversions.Add(Path.GetFileNameWithoutExtension(Result.Source), Result.Error);
                        }
                    }
                }
                catch (Exception Error) when (Error is JsonException || Error is InvalidDataException || Error is IOException || Error is ArgumentException)
                {
                    EffectStatus.Text = $"Cannot read effect conversion report: {Error.Message}. Converted browsing is disabled until the report is corrected or conversion is rerun.";
                    return;
                }
            }
            if (UseConvertedEffects.Checked && Directory.Exists(ConvertedFolder))
            {
                foreach (string Path in Directory.EnumerateFiles(ConvertedFolder, "*.glb"))
                {
                    string Name = System.IO.Path.GetFileNameWithoutExtension(Path);
                    if (!FailedConversions.ContainsKey(Name))
                    {
                        Paths[Name] = Path;
                    }
                }
            }

            foreach (string ScenePath in Paths.OrderBy(Entry => Entry.Key, StringComparer.OrdinalIgnoreCase).Select(Entry => Entry.Value))
            {
                ListViewItem Item = new ListViewItem(Path.GetFileNameWithoutExtension(ScenePath)) { Tag = ScenePath };
                try
                {
                    if (!ScenePath.EndsWith(".rse", StringComparison.OrdinalIgnoreCase))
                    {
                        JsonObject Metadata = ReadConvertedEffect(ScenePath);
                        Item.SubItems.Add("Converted GLB" + (ConvertedEffectWarnings(Metadata).Length > 0 ? " (warning)" : ""));
                        Item.ToolTipText = $"Self-contained converted effect. Stored FPS: {Metadata["framesPerSecond"]}; changing FPS requires reconverting the RSE.\r\n"
                            + ConvertedEffectWarnings(Metadata);
                        Item.SubItems.Add(ScenePath);
                        EffectScenes.Items.Add(Item);
                        continue;
                    }
                    RseScene Scene = RseReader.Parse(File.ReadAllText(ScenePath), ScenePath);
                    Item.SubItems.Add(Scene.IsEmpty ? "Empty scene" : Scene.ListEffectCommand.Count > 0 ? "Nested effect scene" :
                        Scene.ListParticleGenerator.Count > 0 ? "Baked particles" :
                        Scene.DicObject.Values.Any(Object => Object.MotionId.HasValue) ? "Motion scene" : "Model scene");
                    if (Scene.HasSoundCues)
                    {
                        Item.ToolTipText = "Silent preview/export: SOUND cues are not played or exported.";
                    }

                    if (Scene.ListEffectCommand.Count > 0)
                    {
                        Item.ToolTipText += "\r\nFrame-zero nested effects require identity generators and nonlooping model-only children. Child resources and terminal fades are validated when loading.";
                    }

                    if (Scene.ListParticleGenerator.Any(Generator => Generator.Spin == 4 || Generator.Spin == 5))
                    {
                        Item.ToolTipText += "\r\nCamera-facing particles use GLB node extras.rendering.billboard; other viewers use fixed orientation.";
                    }

                    if (Scene.ListParticleGenerator.Any(Generator => Generator.ModelFileName != null))
                    {
                        Item.ToolTipText += "\r\nExplorer extension: particle OBJ filenames load directly, unlike the game's integer-prefix parser.";
                    }

                    if (Scene.ListParticleGenerator.Any(Generator => Generator.IgnoredRsmStart.HasValue))
                    {
                        Item.ToolTipText += "\r\nRSMSTART is unrecognized by the executable and has no playback effect; its value is preserved as GLB metadata.";
                    }

                    if (Scene.ListRejectedMotionCommand.Count > 0 || Scene.ListNativeMotionTransition.Count > 0 || Scene.ListRawOpacityCommand.Count > 0
                        || Scene.ListUnmatchedSetupCommand.Count > 0 || Scene.DicIgnoredModelDeclaration.Count > 0
                        || Scene.ListParticleGenerator.Any(Generator => Generator.IsModelObjectMissing))
                    {
                        Item.SubItems[1].Text += " (warning)";
                        if (Scene.ListRawOpacityCommand.Count > 0)
                        {
                            Item.ToolTipText += "\r\nWARNING: raw opacity outside 0-100% is preserved. Explorer uses a custom shader; GLB ordinary-viewer opacity is a bounded approximation. Native blend state is unverified.";
                        }

                        if (Scene.ListRejectedMotionCommand.Count > 0)
                        {
                            Item.ToolTipText += "\r\nWARNING: native-rejected MOTION commands are skipped, not corrected; their RSA animations are not applied. See loaded status and GLB metadata.";
                        }

                        if (Scene.ListNativeMotionTransition.Count > 0)
                        {
                            Item.ToolTipText += "\r\nWARNING: nonstandard MOTION transition text uses the native signed decimal-prefix parser. Original text and parsed values are preserved in GLB metadata.";
                        }

                        if (Scene.ListUnmatchedSetupCommand.Count > 0)
                        {
                            Item.ToolTipText += "\r\nWARNING: setup commands with no constructed target are ignored, matching native binding. No replacement object is created; commands are preserved in GLB metadata.";
                        }

                        if (Scene.ListParticleGenerator.Any(Generator => Generator.IsModelObjectMissing))
                        {
                            Item.ToolTipText += "\r\nWARNING: particle source objects with no native binding leave their groups without geometry. No replacement asset is loaded; references are preserved in GLB metadata.";
                        }

                        if (Scene.DicIgnoredModelDeclaration.Count > 0)
                        {
                            Item.ToolTipText += "\r\nWARNING: .RAM object declarations are not constructed by the native parser; NOT corrected to .RSM. Original declarations are preserved in GLB metadata.";
                        }
                    }
                    if (FailedConversions.TryGetValue(Item.Text, out string Failure))
                    {
                        Item.SubItems[1].Text = "Not converted";
                        Item.ToolTipText += "\r\nConversion failed (source RSE retained): " + Failure;
                    }
                }
                catch (Exception Error) when (Error is NotSupportedException || Error is InvalidDataException || Error is IOException || Error is UnauthorizedAccessException)
                {
                    Item.SubItems.Add("Not supported");
                    Item.ToolTipText = Error.Message;
                }
                Item.SubItems.Add(ScenePath);
                EffectScenes.Items.Add(Item);
            }
        }

        private void LoadSelectedEffect()
        {
            if (EffectScenes.SelectedItems.Count > 0)
            {
                LoadEffect((string)EffectScenes.SelectedItems[0].Tag);
            }
        }

        private void LoadEffect(string ScenePath)
        {
            try
            {
                if (!ScenePath.EndsWith(".rse", StringComparison.OrdinalIgnoreCase))
                {
                    ReadConvertedEffect(ScenePath);
                    view3DModel.LoadModel(ScenePath);
                    viewModelTexture.ClearTexture();
                    PopulateModelDetails(lsEffectModelTextures, lsEffectModelAnimations, 0);
                    view3DModel.Lighting = SceneLighting.Unlit;
                    ResetEffectControls(true);
                    txtModelFileName.Text = ScenePath;
                    JsonObject Metadata = view3DModel.EffectMetadata;
                    EffectStatus.Text = $"Playing converted {Path.GetFileName(ScenePath)} at stored {Metadata["framesPerSecond"]} FPS (assumed), looping for preview."
                        + "\r\nSelf-contained GLB: no source RSE or dependency folder is required. FPS/fallback controls apply only to source RSE loading and conversion."
                        + "\r\nSilent preview/export. Billboards and texture playlists use preserved effect extras."
                        + "\r\n" + ConvertedEffectWarnings(Metadata);
                    tabControl1.SelectedTab = EffectsTab;
                    return;
                }
                RseScene Scene = RseReader.Parse(File.ReadAllText(ScenePath), ScenePath);
                viewModelTexture.ClearTexture();
                view3DModel.LoadModel(ScenePath, (float)EffectFramesPerSecond.Value,
                    EffectDependencyFolder.Text.Length == 0 ? null : EffectDependencyFolder.Text);
                PopulateModelDetails(lsEffectModelTextures, lsEffectModelAnimations, 0);
                view3DModel.Lighting = SceneLighting.Unlit;
                ResetEffectControls(true);
                txtModelFileName.Text = ScenePath;
                EffectStatus.Text = Scene.IsEmpty
                    ? $"Empty scene: {Path.GetFileName(ScenePath)} declares no objects. Nothing to preview; export preserves an empty scene and its duration."
                    : $"Playing {Path.GetFileName(ScenePath)} at {EffectFramesPerSecond.Value} FPS (assumed), looping for preview.\r\nExport includes RSA/node motion, opacity and UV animation. Billboards require extras-aware playback. SOUND cues are not played or exported.";
                if (Scene.ListParticleGenerator.Any(Generator => Generator.ModelFileName != null))
                {
                    EffectStatus.Text += "\r\nExplorer extension: loading particle OBJ filenames directly; not the executable's integer-prefix interpretation.";
                }

                if (Scene.ListEffectCommand.Count > 0)
                {
                    EffectStatus.Text += "\r\nNested model effects share the parent preview clock with independent child motion start times; exact child dependencies and ID mappings are preserved in GLB metadata:\r\n"
                        + string.Join("\r\n", Scene.ListEffectCommand.Select(Command =>
                            $"Generator {Command.ObjectId}, frame {Command.Frame}: {Scene.DicEffect[Command.EffectId]}"));
                }

                if (Scene.ListParticleGenerator.Any(Generator => Generator.IgnoredRsmStart.HasValue))
                {
                    EffectStatus.Text += "\r\nRSMSTART is ignored, matching the executable; its value is preserved as GLB metadata.";
                }

                if (Scene.DicObject.Values.Any(Object => Object.Kind == RseObjectKind.ParticleGenerator && Object.MotionId.HasValue))
                {
                    EffectStatus.Text += "\r\nGenerator MOTION commands are not executed by native particle playback; preserved as GLB metadata, not applied as RSA animation.";
                }

                if (Scene.DicObject.Values.Any(Object => Object.Kind == RseObjectKind.UvAnimationDummy))
                {
                    EffectStatus.Text += "\r\nDUMMY_UVANIM declarations and their keys are ignored by native object binding; preserved as GLB metadata, not applied to another model.";
                }

                if (Scene.ListParticleGenerator.Any(Generator => !Generator.HasModelReference))
                {
                    EffectStatus.Text += "\r\nParticle generators without OBJ have no source model and render nothing; unbound ranges are preserved as GLB metadata.";
                }

                if (Scene.ListUnmatchedSetupCommand.Count > 0)
                {
                    EffectStatus.Text += "\r\nWARNING: setup commands with no constructed target were ignored, matching native binding; NOT redirected to another object:\r\n"
                        + string.Join("\r\n", Scene.ListUnmatchedSetupCommand.Select(Command =>
                            $"Object {Command.ObjectId}: {Command.Command}={Command.Value} (original preserved in GLB metadata)"));
                }

                if (Scene.ListParticleGenerator.Any(Generator => Generator.IsModelObjectMissing))
                {
                    EffectStatus.Text += "\r\nWARNING: particle source objects with no native binding leave these groups without geometry; NOT replaced with another asset:\r\n"
                        + string.Join("\r\n", Scene.ListParticleGenerator.Where(Generator => Generator.IsModelObjectMissing).Select(Generator =>
                            $"Generator {Generator.ObjectId}, OBJ={Generator.ModelObjectId}, particles {Generator.FirstParticle}..{Generator.LastParticle} (preserved in GLB metadata)"));
                }

                if (Scene.DicIgnoredModelDeclaration.Count > 0)
                {
                    EffectStatus.Text += "\r\nWARNING: .RAM object declarations are not constructed by the native parser; NOT corrected to .RSM:\r\n"
                        + string.Join("\r\n", Scene.DicIgnoredModelDeclaration.Select(Declaration =>
                            $"Object {Declaration.Key}: {Declaration.Value} (original preserved in GLB metadata)"));
                }

                if (!Scene.IsEmpty && !Scene.HasRenderableObjects)
                {
                    EffectStatus.Text += "\r\nThis scene has declared objects but no renderable source; only its timeline is previewed.";
                }

                if (Scene.ListRejectedMotionCommand.Count > 0)
                {
                    EffectStatus.Text += "\r\nWARNING: native-rejected MOTION commands were skipped, NOT corrected. Their RSA animations are not applied:\r\n"
                        + string.Join("\r\n", Scene.ListRejectedMotionCommand.Select(Command =>
                            $"Frame {Command.Frame}: MOTION={Command.Value} ({Command.Reason})"));
                }

                if (Scene.ListNativeMotionTransition.Count > 0)
                {
                    EffectStatus.Text += "\r\nWARNING: MOTION transition text was parsed using the native signed decimal-prefix rule:\r\n"
                        + string.Join("\r\n", Scene.ListNativeMotionTransition.Select(Command =>
                            $"Object {Command.ObjectId}, frame {Command.Frame}: '{Command.Value}' -> {Command.Transition} (original preserved in GLB metadata)"));
                }

                if (Scene.ListRawOpacityCommand.Count > 0)
                {
                    EffectStatus.Text += "\r\nWARNING: raw opacity is preserved in source-command metadata; out-of-range combined material curves use extras.rendering.opacityTrack. Custom preview multiplies vertex and texture alpha before final saturation; native blend state is unverified. Ordinary GLB viewers use a bounded approximation:\r\n"
                        + string.Join("\r\n", Scene.ListRawOpacityCommand.Select(Command =>
                            $"Object {Command.ObjectId}, frame {Command.Frame}: {Command.Command}={Command.Value}"));
                }

                if (view3DModel.EffectFallbackDependencies.Count > 0)
                {
                    EffectStatus.Text += "\r\nFallback dependencies (exact names; paths preserved in GLB metadata):\r\n"
                        + string.Join("\r\n", view3DModel.EffectFallbackDependencies);
                }

                JsonArray AbsentFiles = null;
                if (view3DModel.EffectMetadata != null)
                {
                    AbsentFiles = view3DModel.EffectMetadata["absentDependencies"] as JsonArray;
                }

                if (AbsentFiles != null && AbsentFiles.Count > 0)
                {
                    EffectStatus.Text += "\r\nWARNING: declared files are not on the disc. The executable skips that open and continues. Explorer does not substitute another file:\r\n"
                        + string.Join("\r\n", AbsentFiles.Select(Entry =>
                        {
                            string Owner = Entry["objectId"] is JsonValue ? $"Object {Entry["objectId"].GetValue<int>()}: " : "";
                            return $"{Owner}{Entry["kind"].GetValue<string>()} {Entry["file"].GetValue<string>()}";
                        }));
                }
                tabControl1.SelectedTab = EffectsTab;
            }
            catch (Exception Error)
            {
                MessageBox.Show(this, Error.Message, "Effect could not be opened", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static JsonObject ReadConvertedEffect(string Path)
        {
            ModelRoot Model = ModelRoot.Load(Path);
            GltfExportMetadata.PrepareForNativeReaders(Model);
            JsonObject Metadata = null;
            if (Model.Extras != null)
            {
                Metadata = Model.Extras["rse"] as JsonObject;
            }

            if (Metadata == null)
            {
                throw new InvalidDataException("This glTF file is not a converted RSE effect.");
            }

            return Metadata;
        }

        private static string ConvertedEffectWarnings(JsonObject Metadata)
        {
            List<string> Warnings = new List<string>();
            foreach (string Field in new[] { "rejectedMotionCommands", "nativeMotionTransitions", "unmatchedSetupCommands",
                "ignoredModelDeclarations", "unboundParticleGenerators", "ignoredDummyCommands", "ignoredParticleCommands", "nestedEffects" })
            {
                if (Metadata[Field] is JsonArray Entries && Entries.Count > 0)
                {
                    Warnings.Add($"{Field}: {Entries.Count} preserved metadata entries; inspect exported extras for details.");
                }
            }

            if (Metadata["rawOpacityCommands"] is JsonArray Raw && Raw.Count > 0)
            {
                Warnings.Add("WARNING: raw opacity is preserved; extras.rendering.opacityTrack uses custom preview, native blend state is unverified. Ordinary viewers use a bounded approximation.");
            }

            JsonNode EmptyNode = Metadata["empty"];
            if (EmptyNode != null && EmptyNode.GetValue<bool>())
            {
                Warnings.Add("Empty scene: timeline only.");
            }

            JsonNode RenderlessNode = Metadata["renderless"];
            if (RenderlessNode != null && RenderlessNode.GetValue<bool>())
            {
                Warnings.Add("Declared-but-renderless scene: timeline only.");
            }

            return string.Join("\r\n", Warnings);
        }

        private void ResetEffectControls(bool Loaded)
        {
            EffectPaused = false;
            ExportEffectButton.Enabled = Loaded;
            RestartEffectButton.Enabled = Loaded;
            PauseEffectButton.Enabled = Loaded;
            PauseEffectButton.Text = "Pause";
        }
    }
}
