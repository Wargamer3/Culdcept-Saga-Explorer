using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.AssetManager.ExternalModel;
using CuldceptSagaExplorer.AssetManager.RSE;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CuldceptSagaExplorer
{
    public partial class ExportProgressForm : Form
    {
        private readonly string UnpackingDirectory;
        private readonly string ExportDirectory;
        private readonly bool ExportDAE;
        private readonly string EffectDirectory;
        private readonly string EffectDependencyDirectory;
        private readonly float EffectFramesPerSecond;
        public IReadOnlyList<RseBatchResult> EffectResults = Array.Empty<RseBatchResult>();

        public ExportProgressForm(string UnpackingDirectory, string ExportDirectory, bool ExportDAE,
            string EffectDirectory = null, string EffectDependencyDirectory = null,
            float EffectFramesPerSecond = RseExporter.DefaultPreviewFramesPerSecond)
        {
            InitializeComponent();

            this.UnpackingDirectory = UnpackingDirectory;
            this.ExportDirectory = ExportDirectory;
            this.ExportDAE = ExportDAE;
            this.EffectDirectory = EffectDirectory;
            this.EffectDependencyDirectory = EffectDependencyDirectory;
            this.EffectFramesPerSecond = EffectFramesPerSecond;
        }

        private async void ExtractionProgressForm_Load(object Sender, EventArgs EventArgs)
        {
            try
            {
                await RunExtractionAsync();

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception CaughtException)
            {
                MessageBox.Show(this, $"Extraction failed:\n{CaughtException.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                DialogResult = DialogResult.Abort;
                Close();
            }
        }
        private void ProgressHandler(string Message)
        {
            if (!string.IsNullOrWhiteSpace(Message))
            {
                // Marshal updates back to the WinForms UI thread safely
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        lblExtractionProgress.Text = Message;
                    }));
                }
            }
        }

        private async Task RunExtractionAsync()
        {
            try
            {
                ColladaExporter.OnProgress += ProgressHandler;

                if (ExportDAE)
                {
                    await Task.Run(() => ColladaExporter.ExtractAll(UnpackingDirectory, ExportDirectory));
                }
                else
                {
                    await Task.Run(() =>
                    {
                        RsmExtractor.ExtractAll(UnpackingDirectory, ExportDirectory, ProgressHandler);
                        if (EffectDirectory != null)
                        {
                            EffectResults = RseBatchExporter.ExportAll(EffectDirectory, Path.Combine(ExportDirectory, "EFFECT"),
                                EffectFramesPerSecond, EffectDependencyDirectory, ProgressHandler);
                        }
                    });
                }
            }
            finally
            {
                ColladaExporter.OnProgress -= ProgressHandler;
            }
        }
    }
}
