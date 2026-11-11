using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.RSE;
using CuldceptSagaExplorer.AssetManager.RSM;
using CuldceptSagaExplorer.GraphicsManager;
using CuldceptSagaExplorer.ModelManager;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace GraphicsManager
{
    public partial class Model3DViewer : XNAControl
    {
        private AlphaTestEffect ModelEffect;
        private Animated3DModel DisplayedModel;
        private ModelRoot DisplayedEffect;
        private MouseButtons DragButton;
        private System.Drawing.Point LastMousePosition;

        private SceneLighting CurrentLighting = SceneLighting.Default;

        public IReadOnlyList<ModelTexture> Textures
        {
            get
            {
                if (DisplayedModel == null)
                {
                    return Array.Empty<ModelTexture>();
                }

                return DisplayedModel.ListTexture;
            }
        }

        public System.Text.Json.Nodes.JsonObject EffectMetadata
        {
            get
            {
                if (DisplayedEffect == null || DisplayedEffect.Extras == null)
                {
                    return null;
                }

                return DisplayedEffect.Extras["rse"] as System.Text.Json.Nodes.JsonObject;
            }
        }

        public IReadOnlyList<string> EffectFallbackDependencies
        {
            get
            {
                if (DisplayedEffect == null || DisplayedEffect.Extras == null)
                {
                    return Array.Empty<string>();
                }

                System.Text.Json.Nodes.JsonNode Scene = DisplayedEffect.Extras["rse"];
                if (Scene == null)
                {
                    return Array.Empty<string>();
                }

                System.Text.Json.Nodes.JsonArray Dependencies = Scene["dependencies"] as System.Text.Json.Nodes.JsonArray;
                if (Dependencies == null)
                {
                    return Array.Empty<string>();
                }

                return Dependencies.Where(Entry => Entry["fromFallback"].GetValue<bool>())
                    .Select(Entry => Entry["path"].GetValue<string>()).ToArray();
            }
        }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public SceneLighting Lighting
        {
            get => CurrentLighting;
            set
            {
                CurrentLighting = value ?? SceneLighting.Unlit;
                if (DisplayedModel != null)
                {
                    DisplayedModel.Lighting = CurrentLighting;
                }
                Invalidate();
            }
        }

        /// <summary>Draws through the display gamma ramp the game sets, so colours match the console/Xenia.</summary>
        [System.ComponentModel.DefaultValue(true)]
        public bool GameGammaRamp = true;

        protected override ushort[] DisplayGammaRampTable => GameGammaRamp ? DisplayGammaRamp.CuldceptSaga : null;

        protected override void Initialize()
        {
            if (GraphicsDevice == null)
            {
                return;
            }

            ModelEffect = new AlphaTestEffect(GraphicsDevice)
            {
                VertexColorEnabled = true
            };
        }

        public void LoadModel(string ModelPath, float SceneFramesPerSecond = RseExporter.DefaultPreviewFramesPerSecond,
            string EffectDependencyDirectory = null)
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Load models on the viewport's UI thread.");
            }
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(Model3DViewer));
            }

            if (GraphicsDevice != null)
            {
                if (string.Equals(Path.GetExtension(ModelPath), ".rse", StringComparison.OrdinalIgnoreCase))
                {
                    ModelRoot GLTFModel = RseExporter.Load(ModelPath, SceneFramesPerSecond, EffectDependencyDirectory);
                    Animated3DModel NewModel = new Animated3DModel(GraphicsDevice, GLTFModel) { Lighting = CurrentLighting };
                    if (DisplayedModel != null)
                    {
                        DisplayedModel.UnloadContent();
                    }
                    DisplayedModel = NewModel;
                    DisplayedEffect = GLTFModel;
                }
                else if (string.Equals(Path.GetExtension(ModelPath), ".rsm", StringComparison.OrdinalIgnoreCase))
                {
                    ModelRoot GLTFModel = RsmExtractor.ConvertRsmToGlb(ModelPath);
                    Animated3DModel NewModel = new Animated3DModel(GraphicsDevice, GLTFModel) { Lighting = CurrentLighting };
                    if (DisplayedModel != null)
                    {
                        DisplayedModel.UnloadContent();
                    }
                    DisplayedModel = NewModel;
                    DisplayedEffect = null;
                }
                else
                {
                    ModelRoot GLTFModel = ModelRoot.Load(ModelPath);
                    GltfExportMetadata.PrepareForNativeReaders(GLTFModel);
                    Animated3DModel NewModel = new Animated3DModel(GraphicsDevice, GLTFModel) { Lighting = CurrentLighting };
                    if (DisplayedModel != null)
                    {
                        DisplayedModel.UnloadContent();
                    }
                    DisplayedModel = NewModel;
                    DisplayedEffect = null;
                    if (GLTFModel.Extras != null && GLTFModel.Extras["rse"] is System.Text.Json.Nodes.JsonObject)
                    {
                        DisplayedEffect = GLTFModel;
                    }
                }

                EndCameraDrag();
            }
            else
            {
                throw new InvalidOperationException("The viewport must be initialized before loading a model.");
            }

            Invalidate();
        }

        public List<string> GetTextureNames()
        {
            if (DisplayedModel == null)
            {
                return new List<string>();
            }

            return DisplayedModel.GetTextureNames();
        }

        public List<string> GetAnimationNames()
        {
            if (DisplayedModel == null)
            {
                return new List<string>();
            }

            return DisplayedModel.GetAnimationNames();
        }

        public void PlayAnimation(int Index)
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Select animations on the viewport's UI thread.");
            }
            if (DisplayedModel == null)
            {
                throw new InvalidOperationException("Load a model before selecting an animation.");
            }
            DisplayedModel.PlayAnimation(Index);
            Invalidate();
        }

        public void SetAnimationPaused(bool Paused)
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Control playback on the viewport's UI thread.");
            }

            if (DisplayedModel == null)
            {
                throw new InvalidOperationException("Load a model before controlling playback.");
            }

            DisplayedModel.IsPaused = Paused;
        }

        public void RestartAnimation()
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Control playback on the viewport's UI thread.");
            }

            if (DisplayedModel == null)
            {
                throw new InvalidOperationException("Load a model before controlling playback.");
            }

            DisplayedModel.RestartAnimation();
            Invalidate();
        }

        public void ExportEffect(string OutputPath)
        {
            if (InvokeRequired)
            {
                throw new InvalidOperationException("Export on the viewport's UI thread.");
            }

            if (DisplayedEffect == null)
            {
                throw new InvalidOperationException("Load an RSE scene or converted effect GLB before exporting an effect.");
            }

            GltfExportMetadata.SaveGlb(DisplayedEffect, OutputPath);
        }

        protected override void Update(GameTime GameTime)
        {
            if (DisplayedModel != null)
            {
                DisplayedModel.Update(GameTime);
            }
        }

        protected override void OnMouseDown(MouseEventArgs MouseArgs)
        {
            base.OnMouseDown(MouseArgs);
            if (DisplayedModel == null || (MouseArgs.Button != MouseButtons.Left && MouseArgs.Button != MouseButtons.Right))
            {
                return;
            }
            Focus();
            LastMousePosition = MouseArgs.Location;
            DragButton = MouseArgs.Button;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs MouseArgs)
        {
            base.OnMouseMove(MouseArgs);
            if (DisplayedModel == null || DragButton == MouseButtons.None)
            {
                return;
            }
            int DeltaX = MouseArgs.X - LastMousePosition.X;
            int DeltaY = MouseArgs.Y - LastMousePosition.Y;
            LastMousePosition = MouseArgs.Location;
            if (DragButton == MouseButtons.Left)
            {
                DisplayedModel.Camera.Rotate(DeltaX, DeltaY);
            }
            else if (ClientSize.Width > 0 && ClientSize.Height > 0)
            {
                DisplayedModel.Camera.Pan(DeltaX, DeltaY, new Viewport(0, 0, ClientSize.Width, ClientSize.Height));
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs MouseArgs)
        {
            base.OnMouseUp(MouseArgs);
            if (MouseArgs.Button == DragButton)
            {
                EndCameraDrag();
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs EventArgs)
        {
            base.OnMouseCaptureChanged(EventArgs);
            if (!Capture)
            {
                DragButton = MouseButtons.None;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs MouseArgs)
        {
            base.OnMouseWheel(MouseArgs);
            if (DisplayedModel == null)
            {
                return;
            }
            DisplayedModel.Camera.Zoom((float)MouseArgs.Delta / SystemInformation.MouseWheelScrollDelta);
            Invalidate();
        }

        private void EndCameraDrag()
        {
            DragButton = MouseButtons.None;
            Capture = false;
        }

        protected override void Draw(GameTime GameTime)
        {
            if (DisplayedModel != null)
            {
                DisplayedModel.Draw(GraphicsDevice, GameTime, ModelEffect);
            }
        }

        protected override void UnloadContent()
        {
            EndCameraDrag();
            if (DisplayedModel != null)
            {
                DisplayedModel.UnloadContent();
                DisplayedModel = null;
            }

            if (ModelEffect != null)
            {
                ModelEffect.Dispose();
            }
            DisplayedEffect = null;
            ModelEffect = null;
            base.UnloadContent();
        }
    }
}
