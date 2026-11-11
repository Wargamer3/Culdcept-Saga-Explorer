using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using CuldceptSagaExplorer.AssetManager;
using CuldceptSagaExplorer.ModelManager;
using CuldceptSagaExplorer.AssetManager.RSM;

namespace CuldceptSagaExplorer
{
    public partial class CuldceptSagaExplorer : Form
    {
        public Dictionary<string, string> DicModelNameByFile;

        public CuldceptSagaExplorer()
        {
            InitializeComponent();
            InitializeEffectsTab();
            DicModelNameByFile = new Dictionary<string, string>();
        }

        private void btnSetWorkingFolder_Click(object Sender, EventArgs EventArgs)
        {
            FolderBrowserDialog FolderDialog = new FolderBrowserDialog();

            if (FolderDialog.ShowDialog() == DialogResult.OK)
            {
                txtWorkingFolder.Text = FolderDialog.SelectedPath;
                OpenEffectFolder();
                string ExtractionDirectory = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted");

                if (Directory.Exists(ExtractionDirectory))
                {
                    btnExtractCuldceptSagaISO.Enabled = false;
                    btnUnpackCuldceptSagaAssets.Enabled = true;
                    btnCreateNewCuldceptSagaISO.Enabled = true;
                }
                else
                {
                    btnExtractCuldceptSagaISO.Enabled = true;
                    btnUnpackCuldceptSagaAssets.Enabled = false;
                    btnCreateNewCuldceptSagaISO.Enabled = false;
                }

                string UnpackingDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_unpacked");
                string ExportDirectory = Path.Combine(txtWorkingFolder.Text, "glTF");
                if (Directory.Exists(UnpackingDirectory))
                {
                    if (Directory.Exists(ExportDirectory))
                    {
                        OpenGLBAssetFolder(ExportDirectory);
                    }
                    else
                    {
                        OpenRSMAssetFolder(UnpackingDirectory);
                    }

                    btnRepackCuldceptSagaAssets.Enabled = true;
                    btnExportCuldceptSagaAssetsToGLB.Enabled = true;
                    btnExportCuldceptSagaAssetsToDAE.Enabled = true;
                }
                else
                {
                    btnRepackCuldceptSagaAssets.Enabled = false;
                    btnExportCuldceptSagaAssetsToGLB.Enabled = false;
                    btnExportCuldceptSagaAssetsToDAE.Enabled = false;
                }
            }
        }

        private void btnExtractCuldceptSagaISO_Click(object Sender, EventArgs EventArgs)
        {
            OpenFileDialog IsoDialog = new OpenFileDialog();
            IsoDialog.Filter = "Culdcept Saga ISO|*.iso";

            if (IsoDialog.ShowDialog() == DialogResult.OK)
            {
                string ExtractorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extract-xiso.exe");
                string ExtractionDirectory = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted");
                string Arguments = $"-x \"{IsoDialog.FileName}\"";

                using (XisoProgressForm ProgressBarForm = new XisoProgressForm(ExtractorPath, txtWorkingFolder.Text, Arguments))
                {
                    if (ProgressBarForm.ShowDialog(this) == DialogResult.OK)
                    {
                        MessageBox.Show("Extraction finished successfully!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        btnUnpackCuldceptSagaAssets.Enabled = true;
                        btnCreateNewCuldceptSagaISO.Enabled = true;

                        //It will always extract to a folder with the same name of the ISO, move it to a known name instead.
                        string XisoFolder = Path.Combine(txtWorkingFolder.Text, Path.GetFileNameWithoutExtension(IsoDialog.FileName));
                        if (XisoFolder != ExtractionDirectory)
                        {
                            Directory.Move(XisoFolder, ExtractionDirectory);
                        }
                        OpenEffectFolder();
                    }
                }
            }
        }

        private void btnUnpackCuldceptSagaAssets_Click(object Sender, EventArgs EventArgs)
        {
            string ExtractionDirectory = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted");
            string DataDirectory = Path.Combine(ExtractionDirectory, "Data");
            string UnpackingDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_unpacked");

            using (UnpackProgressForm ProgressBarForm = new UnpackProgressForm(DataDirectory, UnpackingDirectory))
            {
                if (ProgressBarForm.ShowDialog(this) == DialogResult.OK)
                {
                    MessageBox.Show("Extraction finished successfully!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    btnExportCuldceptSagaAssetsToGLB.Enabled = true;
                    btnExportCuldceptSagaAssetsToDAE.Enabled = true;
                }
            }
        }

        private void btnExportCuldceptSagaAssetsToGLB_Click(object Sender, EventArgs EventArgs)
        {
            string UnpackingDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_unpacked");
            string ExportDirectory = Path.Combine(txtWorkingFolder.Text, "glTF");

            string EffectDirectory = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted", "data", "EFFECT");
            using (ExportProgressForm ProgressBarForm = new ExportProgressForm(UnpackingDirectory, ExportDirectory, false,
                Directory.Exists(EffectDirectory) ? EffectDirectory : null,
                EffectDependencyFolder.Text.Length == 0 ? null : EffectDependencyFolder.Text,
                (float)EffectFramesPerSecond.Value))
            {
                if (ProgressBarForm.ShowDialog(this) == DialogResult.OK)
                {
                    OpenGLBAssetFolder(ExportDirectory);
                    RefreshEffectScenes();
                    int FailedEffects = ProgressBarForm.EffectResults.Count(Result => !Result.Exported);
                    MessageBox.Show(this, $"GLB conversion finished. Effects: {ProgressBarForm.EffectResults.Count - FailedEffects} converted, {FailedEffects} unavailable."
                        + (ProgressBarForm.EffectResults.Count > 0
                            ? $"\r\nPer-scene results: {Path.Combine(ExportDirectory, "EFFECT", AssetManager.RSE.RseBatchExporter.ReportFileName)}" : ""),
                        "Conversion complete", MessageBoxButtons.OK, FailedEffects > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
        }

        private void btnExportCuldceptSagaAssetsToDAE_Click(object Sender, EventArgs EventArgs)
        {
            string UnpackingDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_unpacked");
            string ExportDirectory = Path.Combine(txtWorkingFolder.Text, "DAE");

            using (ExportProgressForm ProgressBarForm = new ExportProgressForm(UnpackingDirectory, ExportDirectory, true))
            {
                if (ProgressBarForm.ShowDialog(this) == DialogResult.OK)
                {
                    MessageBox.Show("Extraction finished successfully!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void btnRepackCuldceptSagaAssets_Click(object Sender, EventArgs EventArgs)
        {
            string UnpackingDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_unpacked");
            string RepackingDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_repacked");

            int Total = RspUnpacker.RepackAllRspFiles(UnpackingDirectory, RepackingDirectory);
            Debug.WriteLine($"repacked {Total} files");
        }

        private void btnCreateNewCuldceptSagaISO_Click(object Sender, EventArgs EventArgs)
        {
            string ExtractorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extract-xiso.exe");
            string ExtractionDirectory = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Extracted");
            string RepackDirectory = Path.Combine(txtWorkingFolder.Text, "Culdcept Saga Repacked");
            string RSPDataDirectory = Path.Combine(RepackDirectory, "data");
            string RepackedRSPDirectory = Path.Combine(txtWorkingFolder.Text, "RSP_repacked");

            if (!Directory.Exists(RepackDirectory) && !Directory.Exists(RSPDataDirectory))
            {
                CopyDirectory(ExtractionDirectory, RepackDirectory, true);
            }

            File.Copy(Path.Combine(RepackedRSPDirectory, "DATA00.RSP"), Path.Combine(RSPDataDirectory, "DATA00.RSP"), true);
            File.Copy(Path.Combine(RepackedRSPDirectory, "DATA01.RSP"), Path.Combine(RSPDataDirectory, "DATA01.RSP"), true);
            File.Copy(Path.Combine(RepackedRSPDirectory, "DATA02.RSP"), Path.Combine(RSPDataDirectory, "DATA02.RSP"), true);
            File.Copy(Path.Combine(RepackedRSPDirectory, "DATA04.RSP"), Path.Combine(RSPDataDirectory, "DATA04.RSP"), true);
            File.Copy(Path.Combine(RepackedRSPDirectory, "DATA06.RSP"), Path.Combine(RSPDataDirectory, "DATA06.RSP"), true);

            string IsoFileName = "Culdcept Saga New ISO.iso";
            string Arguments = $"-c \"{RepackDirectory}\" \"{IsoFileName}\"";

            using (XisoProgressForm ProgressBarForm = new XisoProgressForm(ExtractorPath, txtWorkingFolder.Text, Arguments))
            {
                if (ProgressBarForm.ShowDialog(this) == DialogResult.OK)
                {
                    MessageBox.Show("Extraction finished successfully!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    btnCreateNewCuldceptSagaISO.Enabled = true;
                }
            }
        }

        private void OpenGLBAssetFolder(string FolderPath)
        {
            lsMenuModels.Items.Clear();
            lsCustomCepterModels.Items.Clear();
            lsMapModels.Items.Clear();
            lsCreatureModels.Items.Clear();
            lsCepterModels.Items.Clear();
            LoadModelNames();

            ListView Target = null;
            foreach (string ActiveSubFolder in Directory.GetDirectories(FolderPath))
            {
                string ActiveSubFolderName = Path.GetFileNameWithoutExtension(ActiveSubFolder);

                if (ActiveSubFolderName == "DATA00")
                {
                    Target = lsMenuModels;
                }
                else if (ActiveSubFolderName == "DATA01")
                {
                    Target = lsCustomCepterModels;
                }
                else if (ActiveSubFolderName == "DATA02")
                {
                    Target = lsMapModels;
                }
                else if (ActiveSubFolderName == "DATA04")
                {
                    Target = lsCreatureModels;
                }
                else if (ActiveSubFolderName == "DATA06")
                {
                    Target = lsCepterModels;
                }
                else
                {
                    continue;
                }

                foreach (string ActiveFilePath in Directory.GetFiles(ActiveSubFolder, "*.glb"))
                {
                    try
                    {
                        string FileName = Path.GetFileNameWithoutExtension(ActiveFilePath);
                        if (DicModelNameByFile.ContainsKey(FileName))
                        {
                            FileName = DicModelNameByFile[FileName];
                        }

                        ListViewItem NewListItem = new ListViewItem(FileName);
                        NewListItem.Tag = ActiveFilePath;

                        Target.Items.Add(NewListItem);
                    }
                    catch (Exception CaughtException)
                    {
                        MessageBox.Show(
                            CaughtException.Message,
                            "Erreur",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OpenRSMAssetFolder(string FolderPath)
        {
            lsCepterModels.Items.Clear();
            LoadModelNames();

            ListView Target = null;
            foreach (string ActiveSubFolder in Directory.GetDirectories(FolderPath))
            {
                string ActiveSubFolderName = Path.GetFileNameWithoutExtension(ActiveSubFolder);

                if (ActiveSubFolderName == "DATA00")
                {
                    Target = lsMenuModels;
                }
                else if (ActiveSubFolderName == "DATA01")
                {
                    Target = lsCustomCepterModels;
                }
                else if (ActiveSubFolderName == "DATA02")
                {
                    Target = lsMapModels;
                }
                else if (ActiveSubFolderName == "DATA04")
                {
                    Target = lsCreatureModels;
                }
                else if (ActiveSubFolderName == "DATA06")
                {
                    Target = lsCepterModels;
                }
                else
                {
                    continue;
                }

                foreach (string ActiveFilePath in Directory.GetFiles(ActiveSubFolder, "*.rsm"))
                {
                    try
                    {
                        string FileName = Path.GetFileNameWithoutExtension(ActiveFilePath);
                        if (DicModelNameByFile.ContainsKey(FileName))
                        {
                            FileName = DicModelNameByFile[FileName];
                        }

                        ListViewItem NewListItem = new ListViewItem(FileName);
                        NewListItem.Tag = ActiveFilePath;

                        Target.Items.Add(NewListItem);
                    }
                    catch (Exception CaughtException)
                    {
                        MessageBox.Show(
                            CaughtException.Message,
                            "Erreur",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }

        private static void CopyDirectory(string SourceFolder, string DestinationFolder, bool Recursive)
        {
            DirectoryInfo ActiveFolder = new DirectoryInfo(SourceFolder);

            if (!ActiveFolder.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory not found: {ActiveFolder.FullName}");
            }

            DirectoryInfo[] ArraySubFolder = ActiveFolder.GetDirectories();

            Directory.CreateDirectory(DestinationFolder);

            foreach (FileInfo ActiveFileToCopy in ActiveFolder.GetFiles())
            {
                string DestinationFilePath = Path.Combine(DestinationFolder, ActiveFileToCopy.Name);
                ActiveFileToCopy.CopyTo(DestinationFilePath, true);
            }

            if (Recursive)
            {
                foreach (DirectoryInfo SubDir in ArraySubFolder)
                {
                    string NewDestinationDir = Path.Combine(DestinationFolder, SubDir.Name);
                    CopyDirectory(SubDir.FullName, NewDestinationDir, true);
                }
            }
        }

        #region Menus

        private void lsMenuModels_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedModel(lsMenuModels, lsMenuModelTextures, lsMenuModelAnimations, 0);
        }

        private void lsMenuModelTextures_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedTexture(lsMenuModelTextures);
        }

        private void lsMenuModelAnimations_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            PlaySelectedAnimation(lsMenuModelAnimations);
        }

        #endregion

        #region Custom Cepters

        private void lsCustomCepterModels_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedModel(lsCustomCepterModels, lsCustomCepterModelTextures, lsCustomCepterModelAnimations, 0);
        }

        private void lsCustomCepterModelTextures_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedTexture(lsCustomCepterModelTextures);
        }

        private void lsCustomCepterModelAnimations_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            PlaySelectedAnimation(lsCustomCepterModelAnimations);
        }

        #endregion

        #region Maps

        private void lsMapModels_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedModel(lsMapModels, lsMapModelTextures, lsMapModelAnimations, 1);
        }

        private void lsMapTextures_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedTexture(lsMapModelTextures);
        }

        private void lsMapAnimations_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            PlaySelectedAnimation(lsMapModelAnimations);
        }

        #endregion

        #region Creatures

        private void lsCreatureModels_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedModel(lsCreatureModels, lsCreatureModelTextures, lsCreatureModelAnimations, 1);
        }

        private void lsCreatureAnimations_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            PlaySelectedAnimation(lsCreatureModelAnimations);
        }

        private void lsCreatureTextures_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedTexture(lsCreatureModelTextures);
        }

        #endregion

        #region Cepters

        private void lsCepterModels_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedModel(lsCepterModels, lsCepterModelTextures, lsCepterModelAnimations, 0);
        }

        private void lsCepterModelTextures_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            LoadSelectedTexture(lsCepterModelTextures);
        }

        private void lsCepterModelAnimations_SelectedIndexChanged(object Sender, EventArgs EventArgs)
        {
            PlaySelectedAnimation(lsCepterModelAnimations);
        }

        #endregion

        private void btnReplaceSelectedModel_Click(object Sender, EventArgs EventArgs)
        {
            ListView Target = null;

            if (tabControl1.SelectedIndex == 0)
            {
                Target = lsMenuModels;
            }
            else if (tabControl1.SelectedIndex == 1)
            {
                Target = lsCustomCepterModels;
            }
            else if (tabControl1.SelectedIndex == 2)
            {
                Target = lsMapModels;
            }
            else if (tabControl1.SelectedIndex == 3)
            {
                Target = lsCreatureModels;
            }
            else if (tabControl1.SelectedIndex == 4)
            {
                Target = lsCepterModels;
            }

            if (Target != null && Target.SelectedItems.Count > 0)
            {
                OpenFileDialog ModelDialog = new OpenFileDialog();
                ModelDialog.Filter = "Collada Model|*.glb";

                if (ModelDialog.ShowDialog() == DialogResult.OK)
                {
                    string RSMToReplacePath = Target.SelectedItems[0].Tag.ToString();
                    string RSTToReplacePath = CuldceptModel.GetRSTTexturePath(RSMToReplacePath);

                    string ReplacementDaePath = ModelDialog.FileName;

                    RsmExtractor.ReplaceModelGltf(RSMToReplacePath, RSTToReplacePath, ReplacementDaePath);
                }
            }
        }

        private void btnReplaceSelectedTexture_Click(object Sender, EventArgs EventArgs)
        {
            ListView Target = null;

            if (tabControl1.SelectedIndex == 0)
            {
                Target = lsMenuModelTextures;
            }
            else if (tabControl1.SelectedIndex == 1)
            {
                Target = lsCustomCepterModelTextures;
            }
            else if (tabControl1.SelectedIndex == 2)
            {
                Target = lsMapModelTextures;
            }
            else if (tabControl1.SelectedIndex == 3)
            {
                Target = lsCreatureModelTextures;
            }
            else if (tabControl1.SelectedIndex == 4)
            {
                Target = lsCepterModelTextures;
            }

            if (Target != null)
            {
                OpenFileDialog TextureDialog = new OpenFileDialog();
                TextureDialog.Filter = "Culdcept Saga Texture|*.rst";

                if (TextureDialog.ShowDialog() == DialogResult.OK)
                {
                }
            }
        }

        private void LoadSelectedModel(ListView Models, ListView Textures, ListView Animations, int DefaultAnimationIndex)
        {
            if (Models.SelectedItems.Count == 0)
            {
                Textures.Items.Clear();
                Animations.Items.Clear();
                txtModelFileName.Text = string.Empty;
                return;
            }
            try
            {
                view3DModel.Lighting = Models == lsMapModels ? SceneLighting.Alcyon : SceneLighting.CharacterSelection;
                view3DModel.LoadModel(Models.SelectedItems[0].Tag.ToString());
                ResetEffectControls(false);
                txtModelFileName.Text = Models.SelectedItems[0].Tag.ToString();
                PopulateModelDetails(Textures, Animations, DefaultAnimationIndex);
            }
            catch (Exception CaughtException)
            {
                MessageBox.Show(CaughtException.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PopulateModelDetails(ListView Textures, ListView Animations, int DefaultAnimationIndex)
        {
            ClearModelDetails();

            foreach (ModelTexture ActiveTexture in view3DModel.Textures)
            {
                Textures.Items.Add(new ListViewItem(ActiveTexture.Name) { Tag = ActiveTexture });
            }

            foreach (string Name in view3DModel.GetAnimationNames())
            {
                Animations.Items.Add(Name);
            }

            if (Textures.Items.Count > 0)
            {
                Textures.Items[0].Selected = true;
            }
            if (Animations.Items.Count > DefaultAnimationIndex)
            {
                Animations.Items[DefaultAnimationIndex].Selected = true;
            }
        }

        private void ClearModelDetails()
        {
            viewModelTexture.ClearTexture();
            lsMenuModelTextures.Items.Clear();
            lsMenuModelAnimations.Items.Clear();
            lsCustomCepterModelTextures.Items.Clear();
            lsCustomCepterModelAnimations.Items.Clear();
            lsMapModelTextures.Items.Clear();
            lsMapModelAnimations.Items.Clear();
            lsCreatureModelTextures.Items.Clear();
            lsCreatureModelAnimations.Items.Clear();
            lsCepterModelTextures.Items.Clear();
            lsCepterModelAnimations.Items.Clear();
            lsEffectModelTextures.Items.Clear();
            lsEffectModelAnimations.Items.Clear();
        }

        private void PlaySelectedAnimation(ListView Animations)
        {
            if (Animations.SelectedIndices.Count == 0)
            {
                return;
            }
            try
            {
                view3DModel.PlayAnimation(Animations.SelectedIndices[0]);
            }
            catch (Exception CaughtException)
            {
                MessageBox.Show(CaughtException.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadSelectedTexture(ListView Textures)
        {
            if (Textures.SelectedItems.Count == 0)
            {
                viewModelTexture.ClearTexture();
                return;
            }
            try
            {
                viewModelTexture.LoadTexture((ModelTexture)Textures.SelectedItems[0].Tag);
            }
            catch (Exception CaughtException)
            {
                MessageBox.Show(CaughtException.Message, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadModelNames()
        {
            DicModelNameByFile.Clear();

            // DATA00
            DicModelNameByFile.Add("DATA00_0029", "DATA00_0029");
            DicModelNameByFile.Add("DATA00_0030", "DATA00_0030");
            DicModelNameByFile.Add("DATA00_0122", "DATA00_0122");
            DicModelNameByFile.Add("DATA00_0123", "DATA00_0123");
            DicModelNameByFile.Add("DATA00_0137", "DATA00_0137");
            DicModelNameByFile.Add("DATA00_0162", "DATA00_0162");
            DicModelNameByFile.Add("DATA00_0164", "DATA00_0164");
            DicModelNameByFile.Add("DATA00_0167", "DATA00_0167");
            DicModelNameByFile.Add("DATA00_0169", "DATA00_0169");
            DicModelNameByFile.Add("DATA00_0171", "DATA00_0171");
            DicModelNameByFile.Add("DATA00_0174", "DATA00_0174");
            DicModelNameByFile.Add("DATA00_0184", "DATA00_0184");
            DicModelNameByFile.Add("DATA00_0187", "DATA00_0187");
            DicModelNameByFile.Add("DATA00_0189", "DATA00_0189");
            DicModelNameByFile.Add("DATA00_0191", "DATA00_0191");
            DicModelNameByFile.Add("DATA00_0195", "DATA00_0195");
            DicModelNameByFile.Add("DATA00_0197", "DATA00_0197");
            DicModelNameByFile.Add("DATA00_0200", "DATA00_0200");
            DicModelNameByFile.Add("DATA00_0202", "DATA00_0202");
            DicModelNameByFile.Add("DATA00_0219", "DATA00_0219");
            DicModelNameByFile.Add("DATA00_0222", "DATA00_0222");
            DicModelNameByFile.Add("DATA00_0228", "DATA00_0228");
            DicModelNameByFile.Add("DATA00_0233", "DATA00_0233");
            DicModelNameByFile.Add("DATA00_0349", "DATA00_0349");
            DicModelNameByFile.Add("DATA00_0350", "Faustina");
            DicModelNameByFile.Add("DATA00_0352", "Rilara");
            DicModelNameByFile.Add("DATA00_0354", "Koenig");
            DicModelNameByFile.Add("DATA00_0356", "Pondon");
            DicModelNameByFile.Add("DATA00_0358", "Markt");
            DicModelNameByFile.Add("DATA00_0360", "Ticomun");
            DicModelNameByFile.Add("DATA00_0362", "Meltia");
            DicModelNameByFile.Add("DATA00_0364", "Borival");
            DicModelNameByFile.Add("DATA00_0366", "Krone");
            DicModelNameByFile.Add("DATA00_0368", "Wongen");
            DicModelNameByFile.Add("DATA00_0370", "Drachma");
            DicModelNameByFile.Add("DATA00_0372", "Diarna");
            DicModelNameByFile.Add("DATA00_0374", "Fontaciel");
            DicModelNameByFile.Add("DATA00_0376", "Flammefleur");
            DicModelNameByFile.Add("DATA00_0378", "Rouetsoleil");
            DicModelNameByFile.Add("DATA00_0380", "Gottlieb");
            DicModelNameByFile.Add("DATA00_0382", "Castora");
            DicModelNameByFile.Add("DATA00_0384", "Guilder & Rouble");
            DicModelNameByFile.Add("DATA00_0386", "Escudo");
            DicModelNameByFile.Add("DATA00_0388", "Dirham");
            DicModelNameByFile.Add("DATA00_0390", "Rupiah");
            DicModelNameByFile.Add("DATA00_0392", "Irville");
            DicModelNameByFile.Add("DATA00_0394", "High Priest Sapphius");
            DicModelNameByFile.Add("DATA00_0396", "Ancient God Baltias");
            DicModelNameByFile.Add("DATA00_0398", "Zeromn");
            DicModelNameByFile.Add("DATA00_0400", "Slave Trader");
            DicModelNameByFile.Add("DATA00_0402", "King Gieve");
            DicModelNameByFile.Add("DATA00_0404", "Anedia");
            DicModelNameByFile.Add("DATA00_0406", "Faustina (Darkness)");
            DicModelNameByFile.Add("DATA00_0408", "Rilara (Oracle)");
            DicModelNameByFile.Add("DATA00_0410", "Zeneth");
            DicModelNameByFile.Add("DATA00_0412", "Najaran");
            DicModelNameByFile.Add("DATA00_0414", "Goligan");
            DicModelNameByFile.Add("DATA00_0416", "Culdra");
            DicModelNameByFile.Add("DATA00_0420", "DATA00_0420");
            DicModelNameByFile.Add("DATA00_0421", "DATA00_0421");
            DicModelNameByFile.Add("DATA00_0422", "DATA00_0422");
            DicModelNameByFile.Add("DATA00_0423", "DATA00_0423");
            DicModelNameByFile.Add("DATA00_0424", "DATA00_0424");
            DicModelNameByFile.Add("DATA00_0425", "DATA00_0425");
            DicModelNameByFile.Add("DATA00_0426", "DATA00_0426");
            DicModelNameByFile.Add("DATA00_0427", "DATA00_0427");
            DicModelNameByFile.Add("DATA00_0428", "DATA00_0428");
            DicModelNameByFile.Add("DATA00_0429", "DATA00_0429");
            DicModelNameByFile.Add("DATA00_0430", "DATA00_0430");
            DicModelNameByFile.Add("DATA00_0431", "DATA00_0431");
            DicModelNameByFile.Add("DATA00_0432", "DATA00_0432");
            DicModelNameByFile.Add("DATA00_0433", "DATA00_0433");
            DicModelNameByFile.Add("DATA00_0434", "DATA00_0434");
            DicModelNameByFile.Add("DATA00_0435", "DATA00_0435");
            DicModelNameByFile.Add("DATA00_0436", "DATA00_0436");
            DicModelNameByFile.Add("DATA00_0437", "DATA00_0437");
            DicModelNameByFile.Add("DATA00_0438", "DATA00_0438");
            DicModelNameByFile.Add("DATA00_0439", "DATA00_0439");
            DicModelNameByFile.Add("DATA00_0440", "DATA00_0440");
            DicModelNameByFile.Add("DATA00_0442", "DATA00_0442");
            DicModelNameByFile.Add("DATA00_0444", "READY");
            DicModelNameByFile.Add("DATA00_0446", "DATA00_0446");
            DicModelNameByFile.Add("DATA00_0448", "Rule Scroll");
            DicModelNameByFile.Add("DATA00_0463", "DATA00_0463");

            // DATA01
            DicModelNameByFile.Add("DATA01_0133", "DATA01_0133");
            DicModelNameByFile.Add("DATA01_0297", "Leather Helm");
            DicModelNameByFile.Add("DATA01_0299", "Chain Helm");
            DicModelNameByFile.Add("DATA01_0301", "DATA01_0301");
            DicModelNameByFile.Add("DATA01_0303", "DATA01_0303");
            DicModelNameByFile.Add("DATA01_0305", "Lion Helm");
            DicModelNameByFile.Add("DATA01_0307", "Valkyria Helm");
            DicModelNameByFile.Add("DATA01_0309", "DATA01_0309");
            DicModelNameByFile.Add("DATA01_0311", "DATA01_0311");
            DicModelNameByFile.Add("DATA01_0313", "DATA01_0313");
            DicModelNameByFile.Add("DATA01_0315", "DATA01_0315");
            DicModelNameByFile.Add("DATA01_0317", "DATA01_0317");
            DicModelNameByFile.Add("DATA01_0319", "Crown");
            DicModelNameByFile.Add("DATA01_0321", "DATA01_0321");
            DicModelNameByFile.Add("DATA01_0323", "Pirate Hat");
            DicModelNameByFile.Add("DATA01_0327", "Jester Hood");
            DicModelNameByFile.Add("DATA01_0329", "DATA01_0329");
            DicModelNameByFile.Add("DATA01_0331", "Ninja Mask");
            DicModelNameByFile.Add("DATA01_0333", "DATA01_0333");
            DicModelNameByFile.Add("DATA01_0335", "DATA01_0335");
            DicModelNameByFile.Add("DATA01_0337", "Holder Blade");
            DicModelNameByFile.Add("DATA01_0339", "Mace");
            DicModelNameByFile.Add("DATA01_0341", "Claymore");
            DicModelNameByFile.Add("DATA01_0343", "Blade of Culdra");
            DicModelNameByFile.Add("DATA01_0345", "DATA01_0345");
            DicModelNameByFile.Add("DATA01_0347", "DATA01_0347");
            DicModelNameByFile.Add("DATA01_0349", "DATA01_0349");
            DicModelNameByFile.Add("DATA01_0351", "DATA01_0351");
            DicModelNameByFile.Add("DATA01_0353", "DATA01_0353");
            DicModelNameByFile.Add("DATA01_0355", "DATA01_0355");
            DicModelNameByFile.Add("DATA01_0357", "Prismatic Wand");
            DicModelNameByFile.Add("DATA01_0359", "Wizard Staff");
            DicModelNameByFile.Add("DATA01_0361", "Buckler");
            DicModelNameByFile.Add("DATA01_0363", "Knight Shield");
            DicModelNameByFile.Add("DATA01_0365", "DATA01_0365");
            DicModelNameByFile.Add("DATA01_0367", "Earth Shield");
            DicModelNameByFile.Add("DATA01_0369", "Water Shield");
            DicModelNameByFile.Add("DATA01_0371", "Fire Shield");
            DicModelNameByFile.Add("DATA01_0373", "Air Shield");
            DicModelNameByFile.Add("DATA01_0375", "DATA01_0375");
            DicModelNameByFile.Add("DATA01_0377", "Champion's Cloak");
            DicModelNameByFile.Add("DATA01_0384", "DATA01_0384");
            DicModelNameByFile.Add("DATA01_0386", "DATA01_0386");
            DicModelNameByFile.Add("DATA01_0388", "DATA01_0388");
            DicModelNameByFile.Add("DATA01_0390", "DATA01_0390");
            DicModelNameByFile.Add("DATA01_0398", "Shogun Helm");
            DicModelNameByFile.Add("DATA01_0566", "DATA01_0566");
            DicModelNameByFile.Add("DATA01_0574", "DATA01_0574");
            DicModelNameByFile.Add("DATA01_0582", "DATA01_0582");
            DicModelNameByFile.Add("DATA01_0590", "DATA01_0590");
            DicModelNameByFile.Add("DATA01_0598", "DATA01_0598");
            DicModelNameByFile.Add("DATA01_0606", "DATA01_0606");
            DicModelNameByFile.Add("DATA01_0614", "DATA01_0614");
            DicModelNameByFile.Add("DATA01_0622", "DATA01_0622");
            DicModelNameByFile.Add("DATA01_0630", "DATA01_0630");
            DicModelNameByFile.Add("DATA01_0638", "DATA01_0638");
            DicModelNameByFile.Add("DATA01_0639", "DATA01_0639");
            DicModelNameByFile.Add("DATA01_0640", "DATA01_0640");
            DicModelNameByFile.Add("DATA01_0641", "DATA01_0641");
            DicModelNameByFile.Add("DATA01_0642", "DATA01_0642");
            DicModelNameByFile.Add("DATA01_0643", "DATA01_0643");
            DicModelNameByFile.Add("DATA01_0672", "DATA01_0672");
            DicModelNameByFile.Add("DATA01_0680", "DATA01_0680");
            DicModelNameByFile.Add("DATA01_0688", "DATA01_0688");
            DicModelNameByFile.Add("DATA01_0696", "DATA01_0696");
            DicModelNameByFile.Add("DATA01_0704", "DATA01_0704");
            DicModelNameByFile.Add("DATA01_0712", "DATA01_0712");
            DicModelNameByFile.Add("DATA01_0720", "DATA01_0720");
            DicModelNameByFile.Add("DATA01_0722", "DATA01_0722");
            DicModelNameByFile.Add("DATA01_0724", "DATA01_0724");
            DicModelNameByFile.Add("DATA01_0732", "DATA01_0732");
            DicModelNameByFile.Add("DATA01_0733", "DATA01_0733");
            DicModelNameByFile.Add("DATA01_0734", "DATA01_0734");
            DicModelNameByFile.Add("DATA01_0735", "DATA01_0735");
            DicModelNameByFile.Add("DATA01_0747", "DATA01_0747");
            DicModelNameByFile.Add("DATA01_0755", "DATA01_0755");
            DicModelNameByFile.Add("DATA01_0763", "DATA01_0763");
            DicModelNameByFile.Add("DATA01_0771", "DATA01_0771");
            DicModelNameByFile.Add("DATA01_0779", "DATA01_0779");
            DicModelNameByFile.Add("DATA01_0787", "DATA01_0787");
            DicModelNameByFile.Add("DATA01_0795", "DATA01_0795");
            DicModelNameByFile.Add("DATA01_0803", "DATA01_0803");
            DicModelNameByFile.Add("DATA01_0811", "DATA01_0811");
            DicModelNameByFile.Add("DATA01_0819", "DATA01_0819");
            DicModelNameByFile.Add("DATA01_0827", "DATA01_0827");
            DicModelNameByFile.Add("DATA01_0835", "DATA01_0835");
            DicModelNameByFile.Add("DATA01_0843", "DATA01_0843");
            DicModelNameByFile.Add("DATA01_0851", "DATA01_0851");
            DicModelNameByFile.Add("DATA01_0859", "DATA01_0859");
            DicModelNameByFile.Add("DATA01_0868", "DATA01_0868");
            DicModelNameByFile.Add("DATA01_0869", "DATA01_0869");
            DicModelNameByFile.Add("DATA01_0870", "DATA01_0870");
            DicModelNameByFile.Add("DATA01_0871", "DATA01_0871");
            DicModelNameByFile.Add("DATA01_0872", "DATA01_0872");
            DicModelNameByFile.Add("DATA01_0880", "DATA01_0880");
            DicModelNameByFile.Add("DATA01_0888", "DATA01_0888");
            DicModelNameByFile.Add("DATA01_0896", "DATA01_0896");
            DicModelNameByFile.Add("DATA01_0904", "DATA01_0904");
            DicModelNameByFile.Add("DATA01_0912", "DATA01_0912");
            DicModelNameByFile.Add("DATA01_0913", "DATA01_0913");
            DicModelNameByFile.Add("DATA01_0914", "DATA01_0914");
            DicModelNameByFile.Add("DATA01_0915", "DATA01_0915");
            DicModelNameByFile.Add("DATA01_0916", "DATA01_0916");
            DicModelNameByFile.Add("DATA01_0917", "DATA01_0917");
            DicModelNameByFile.Add("DATA01_0926", "DATA01_0926");
            DicModelNameByFile.Add("DATA01_0927", "DATA01_0927");
            DicModelNameByFile.Add("DATA01_0928", "DATA01_0928");
            DicModelNameByFile.Add("DATA01_0929", "DATA01_0929");
            DicModelNameByFile.Add("DATA01_0930", "DATA01_0930");
            DicModelNameByFile.Add("DATA01_0938", "DATA01_0938");
            DicModelNameByFile.Add("DATA01_0946", "DATA01_0946");
            DicModelNameByFile.Add("DATA01_0954", "DATA01_0954");
            DicModelNameByFile.Add("DATA01_0962", "DATA01_0962");
            DicModelNameByFile.Add("DATA01_0963", "DATA01_0963");
            DicModelNameByFile.Add("DATA01_0967", "DATA01_0967");
            DicModelNameByFile.Add("DATA01_0968", "DATA01_0968");
            DicModelNameByFile.Add("DATA01_0969", "DATA01_0969");
            DicModelNameByFile.Add("DATA01_0970", "DATA01_0970");
            DicModelNameByFile.Add("DATA01_0971", "DATA01_0971");
            DicModelNameByFile.Add("DATA01_0979", "DATA01_0979");
            DicModelNameByFile.Add("DATA01_0987", "DATA01_0987");
            DicModelNameByFile.Add("DATA01_0995", "DATA01_0995");
            DicModelNameByFile.Add("DATA01_1003", "DATA01_1003");
            DicModelNameByFile.Add("DATA01_1011", "DATA01_1011");
            DicModelNameByFile.Add("DATA01_1012", "DATA01_1012");
            DicModelNameByFile.Add("DATA01_1013", "DATA01_1013");
            DicModelNameByFile.Add("DATA01_1014", "DATA01_1014");
            DicModelNameByFile.Add("DATA01_1015", "DATA01_1015");
            DicModelNameByFile.Add("DATA01_1016", "DATA01_1016");

            // DATA02
            DicModelNameByFile.Add("DATA02_0000", "Earth battle leaves");
            DicModelNameByFile.Add("DATA02_0003", "Earth battle background");
            DicModelNameByFile.Add("DATA02_0005", "Water battle background");
            DicModelNameByFile.Add("DATA02_0007", "Fire battle background");
            DicModelNameByFile.Add("DATA02_0010", "Air battle grass");
            DicModelNameByFile.Add("DATA02_0013", "Air battle windmill ");
            DicModelNameByFile.Add("DATA02_0015", "Air battle background");
            DicModelNameByFile.Add("DATA02_0017", "Neutral battle background");
            DicModelNameByFile.Add("DATA02_0019", "Multi color battle background");
            DicModelNameByFile.Add("DATA02_0022", "Santana Village waterwheel");
            DicModelNameByFile.Add("DATA02_0025", "Santana Village donkey");
            DicModelNameByFile.Add("DATA02_0027", "Santana Village");
            DicModelNameByFile.Add("DATA02_0029", "Colosseum");
            DicModelNameByFile.Add("DATA02_0031", "Colosseum II");
            DicModelNameByFile.Add("DATA02_0033", "Colosseum III");
            DicModelNameByFile.Add("DATA02_0035", "Alcyon");
            DicModelNameByFile.Add("DATA02_0037", "Melkizekiah");
            DicModelNameByFile.Add("DATA02_0040", "Phoenixion");
            DicModelNameByFile.Add("DATA02_0042", "Blumeer");
            DicModelNameByFile.Add("DATA02_0044", "Batuan");
            DicModelNameByFile.Add("DATA02_0046", "Alcyon II");
            DicModelNameByFile.Add("DATA02_0048", "Dinar Mountains");
            DicModelNameByFile.Add("DATA02_0050", "Mt. Etumer");
            DicModelNameByFile.Add("DATA02_0052", "Venice");
            DicModelNameByFile.Add("DATA02_0054", "Kaningun rotating platform");
            DicModelNameByFile.Add("DATA02_0056", "Kaningun");
            DicModelNameByFile.Add("DATA02_0058", "Grave Canyon");
            DicModelNameByFile.Add("DATA02_0060", "Provedin");
            DicModelNameByFile.Add("DATA02_0062", "Cravonia");
            DicModelNameByFile.Add("DATA02_0064", "Kargard");
            DicModelNameByFile.Add("DATA02_0066", "Alcyon III");
            DicModelNameByFile.Add("DATA02_0068", "Omengald");
            DicModelNameByFile.Add("DATA02_0071", "DATA02_0071");
            DicModelNameByFile.Add("DATA02_0073", "Altar of Light");
            DicModelNameByFile.Add("DATA02_0075", "Basisk");
            DicModelNameByFile.Add("DATA02_0077", "Belizalberg");
            DicModelNameByFile.Add("DATA02_0079", "Zanador");
            DicModelNameByFile.Add("DATA02_0082", "DATA02_0082");
            DicModelNameByFile.Add("DATA02_0084", "Altar of Darkness");
            DicModelNameByFile.Add("DATA02_0088", "Lalasati");
            DicModelNameByFile.Add("DATA02_0090", "Illusia");
            DicModelNameByFile.Add("DATA02_0092", "Eusia rotating platform");
            DicModelNameByFile.Add("DATA02_0094", "Eusia gear 1");
            DicModelNameByFile.Add("DATA02_0096", "Eusia gear 2");
            DicModelNameByFile.Add("DATA02_0098", "Eusia");
            DicModelNameByFile.Add("DATA02_0101", "DATA02_0101");
            DicModelNameByFile.Add("DATA02_0103", "Altar of Blessing");
            DicModelNameByFile.Add("DATA02_0105", "Stairway to the Gods");
            DicModelNameByFile.Add("DATA02_0107", "DATA02_0107");
            DicModelNameByFile.Add("DATA02_0109", "Culdcept Saga logo");
            DicModelNameByFile.Add("DATA02_0111", "The Two Towers's Culdcept banner");
            DicModelNameByFile.Add("DATA02_0114", "Culdcept flag");
            DicModelNameByFile.Add("DATA02_0116", "DATA02_0116");
            DicModelNameByFile.Add("DATA02_0118", "Frontline Base's torch");
            DicModelNameByFile.Add("DATA02_0121", "Vaulting Horse's bird statue effect");
            DicModelNameByFile.Add("DATA02_0123", "Vaulting Horse's bird statue");
            DicModelNameByFile.Add("DATA02_0126", "360's lamp effect");
            DicModelNameByFile.Add("DATA02_0128", "360's lamp");
            DicModelNameByFile.Add("DATA02_0131", "The Two Towers's Snake statue effect");
            DicModelNameByFile.Add("DATA02_0133", "The Two Towers's Snake statue");
            DicModelNameByFile.Add("DATA02_0135", "Vaulting Horse's Horse statue");
            DicModelNameByFile.Add("DATA02_0137", "Angel statue");
            DicModelNameByFile.Add("DATA02_0139", "Grim reaper statue");
            DicModelNameByFile.Add("DATA02_0142", "The Second Morning's Hut");
            DicModelNameByFile.Add("DATA02_0144", "The Two Towers's Castle Tower");
            DicModelNameByFile.Add("DATA02_0147", "Rocket's Windmill blades");
            DicModelNameByFile.Add("DATA02_0149", "Rocket's Windmill base");
            DicModelNameByFile.Add("DATA02_0151", "Rocks");
            DicModelNameByFile.Add("DATA02_0153", "Arrow's rocks");
            DicModelNameByFile.Add("DATA02_0155", "Tree");
            DicModelNameByFile.Add("DATA02_0157", "Rocket's Crates");
            DicModelNameByFile.Add("DATA02_0159", "The Second Morning's Hut");
            DicModelNameByFile.Add("DATA02_0162", "X's green crystal");
            DicModelNameByFile.Add("DATA02_0164", "X's green crystal base");
            DicModelNameByFile.Add("DATA02_0167", "X's yellow crystal");
            DicModelNameByFile.Add("DATA02_0169", "X's yellow crystal base");
            DicModelNameByFile.Add("DATA02_0172", "X's blue crystal");
            DicModelNameByFile.Add("DATA02_0174", "X's blue crystal base");
            DicModelNameByFile.Add("DATA02_0177", "X's blue crystal");
            DicModelNameByFile.Add("DATA02_0179", "X's red crystal base");
            DicModelNameByFile.Add("DATA02_0279", "Earth Tile");
            DicModelNameByFile.Add("DATA02_0282", "Castle tile flags");
            DicModelNameByFile.Add("DATA02_0284", "Castle tile");
            DicModelNameByFile.Add("DATA02_0291", "East tower flag");
            DicModelNameByFile.Add("DATA02_0293", "East tower");
            DicModelNameByFile.Add("DATA02_0295", "DATA02_0295");
            DicModelNameByFile.Add("DATA02_0297", "DATA02_0297");
            DicModelNameByFile.Add("DATA02_0299", "DATA02_0299");
            DicModelNameByFile.Add("DATA02_0301", "DATA02_0301");
            DicModelNameByFile.Add("DATA02_0303", "DATA02_0303");
            DicModelNameByFile.Add("DATA02_0305", "DATA02_0305");
            DicModelNameByFile.Add("DATA02_0307", "DATA02_0307");
            DicModelNameByFile.Add("DATA02_0310", "DATA02_0310");
            DicModelNameByFile.Add("DATA02_0312", "Shop");

            // DATA04
            DicModelNameByFile.Add("DATA04_0002", "Giant Rat");
            DicModelNameByFile.Add("DATA04_0006", "Goblin");
            DicModelNameByFile.Add("DATA04_0010", "Thief");
            DicModelNameByFile.Add("DATA04_0014", "Fighter");
            DicModelNameByFile.Add("DATA04_0018", "Skeleton");
            DicModelNameByFile.Add("DATA04_0022", "Bandit");
            DicModelNameByFile.Add("DATA04_0026", "Wraith");
            DicModelNameByFile.Add("DATA04_0030", "Samurai");
            DicModelNameByFile.Add("DATA04_0034", "Tyrannosaurus");
            DicModelNameByFile.Add("DATA04_0038", "Lunatic Hare");
            DicModelNameByFile.Add("DATA04_0042", "Baldanders");
            DicModelNameByFile.Add("DATA04_0046", "Doppelganger");
            DicModelNameByFile.Add("DATA04_0050", "Horned Chameleon");
            DicModelNameByFile.Add("DATA04_0054", "Borgess");
            DicModelNameByFile.Add("DATA04_0058", "Basalt Idol");
            DicModelNameByFile.Add("DATA04_0062", "Silver Idol");
            DicModelNameByFile.Add("DATA04_0066", "Clay Idol");
            DicModelNameByFile.Add("DATA04_0070", "Gold Idol");
            DicModelNameByFile.Add("DATA04_0074", "Iron Idol");
            DicModelNameByFile.Add("DATA04_0078", "Brass Idol");
            DicModelNameByFile.Add("DATA04_0082", "Ivory Idol");
            DicModelNameByFile.Add("DATA04_0086", "Ebony Idol");
            DicModelNameByFile.Add("DATA04_0090", "Marble Idol");
            DicModelNameByFile.Add("DATA04_0094", "Granite Idol");
            DicModelNameByFile.Add("DATA04_0098", "Living Shield");
            DicModelNameByFile.Add("DATA04_0102", "Living Spear");
            DicModelNameByFile.Add("DATA04_0106", "Statue");
            DicModelNameByFile.Add("DATA04_0110", "Trojan Horse");
            DicModelNameByFile.Add("DATA04_0114", "Archbishop");
            DicModelNameByFile.Add("DATA04_0118", "Clockwork Owl");
            DicModelNameByFile.Add("DATA04_0122", "Wonder Wall");
            DicModelNameByFile.Add("DATA04_0126", "Dancing Doll");
            DicModelNameByFile.Add("DATA04_0130", "Colossus");
            DicModelNameByFile.Add("DATA04_0134", "Jade Idol");
            DicModelNameByFile.Add("DATA04_0138", "Migoal");
            DicModelNameByFile.Add("DATA04_0142", "Dragon's Egg");
            DicModelNameByFile.Add("DATA04_0146", "Armored Dragon");
            DicModelNameByFile.Add("DATA04_0150", "Living Rapier");
            DicModelNameByFile.Add("DATA04_0154", "Living Scroll");
            DicModelNameByFile.Add("DATA04_0158", "Living Mirror");
            DicModelNameByFile.Add("DATA04_0162", "Living Claw");
            DicModelNameByFile.Add("DATA04_0166", "Gemoid");
            DicModelNameByFile.Add("DATA04_0170", "Little Fossil");
            DicModelNameByFile.Add("DATA04_0174", "Great Fossil");
            DicModelNameByFile.Add("DATA04_0178", "Opal Idol");
            DicModelNameByFile.Add("DATA04_0182", "Lead Idol");
            DicModelNameByFile.Add("DATA04_0186", "Red Cap");
            DicModelNameByFile.Add("DATA04_0190", "Possession");
            DicModelNameByFile.Add("DATA04_0194", "Trap Spider");
            DicModelNameByFile.Add("DATA04_0198", "Shapeshifter");
            DicModelNameByFile.Add("DATA04_0202", "Ogre Lord");
            DicModelNameByFile.Add("DATA04_0206", "Sky Gear");
            DicModelNameByFile.Add("DATA04_0210", "Ground Gear");
            DicModelNameByFile.Add("DATA04_0214", "Beast Gear");
            DicModelNameByFile.Add("DATA04_0218", "Metal Shambler");
            DicModelNameByFile.Add("DATA04_0222", "Sorcerer");
            DicModelNameByFile.Add("DATA04_0226", "Phoenix");
            DicModelNameByFile.Add("DATA04_0230", "Golem");
            DicModelNameByFile.Add("DATA04_0234", "Minotaur");
            DicModelNameByFile.Add("DATA04_0238", "Manticore");
            DicModelNameByFile.Add("DATA04_0242", "Cait Sith");
            DicModelNameByFile.Add("DATA04_0246", "Gas Cloud");
            DicModelNameByFile.Add("DATA04_0250", "Hellhound");
            DicModelNameByFile.Add("DATA04_0254", "Salamander");
            DicModelNameByFile.Add("DATA04_0258", "Fire Giant");
            DicModelNameByFile.Add("DATA04_0262", "Chimera");
            DicModelNameByFile.Add("DATA04_0266", "Ba-al");
            DicModelNameByFile.Add("DATA04_0270", "Barrow Wight");
            DicModelNameByFile.Add("DATA04_0274", "Lava Wurm");
            DicModelNameByFile.Add("DATA04_0278", "Conjurer");
            DicModelNameByFile.Add("DATA04_0282", "Heat Imp");
            DicModelNameByFile.Add("DATA04_0286", "Gladiator");
            DicModelNameByFile.Add("DATA04_0290", "Sulfurous Balloon");
            DicModelNameByFile.Add("DATA04_0294", "Ares");
            DicModelNameByFile.Add("DATA04_0298", "Domovikha");
            DicModelNameByFile.Add("DATA04_0302", "Sharazad");
            DicModelNameByFile.Add("DATA04_0306", "Boggart");
            DicModelNameByFile.Add("DATA04_0310", "Peri");
            DicModelNameByFile.Add("DATA04_0314", "Domovoi");
            DicModelNameByFile.Add("DATA04_0318", "Phlogiston");
            DicModelNameByFile.Add("DATA04_0322", "Red Ogre");
            DicModelNameByFile.Add("DATA04_0326", "Creeping Flame");
            DicModelNameByFile.Add("DATA04_0330", "Volcanic Dragon");
            DicModelNameByFile.Add("DATA04_0334", "Hell Pyron");
            DicModelNameByFile.Add("DATA04_0338", "Dragonewt");
            DicModelNameByFile.Add("DATA04_0342", "Death Scythe");
            DicModelNameByFile.Add("DATA04_0346", "Barbarian");
            DicModelNameByFile.Add("DATA04_0350", "Hive Queen");
            DicModelNameByFile.Add("DATA04_0354", "Hive Worker");
            DicModelNameByFile.Add("DATA04_0358", "Avenger");
            DicModelNameByFile.Add("DATA04_0362", "Fay");
            DicModelNameByFile.Add("DATA04_0366", "Burning Titan");
            DicModelNameByFile.Add("DATA04_0370", "Tokebi");
            DicModelNameByFile.Add("DATA04_0374", "Helltic");
            DicModelNameByFile.Add("DATA04_0378", "Peluda");
            DicModelNameByFile.Add("DATA04_0382", "Kasha");
            DicModelNameByFile.Add("DATA04_0386", "Coaloid");
            DicModelNameByFile.Add("DATA04_0390", "Bia");
            DicModelNameByFile.Add("DATA04_0394", "Ukobach");
            DicModelNameByFile.Add("DATA04_0398", "Burnacle");
            DicModelNameByFile.Add("DATA04_0402", "Wall of Ice");
            DicModelNameByFile.Add("DATA04_0406", "Undine");
            DicModelNameByFile.Add("DATA04_0410", "Medusa");
            DicModelNameByFile.Add("DATA04_0414", "Lilith");
            DicModelNameByFile.Add("DATA04_0418", "Giant Amoeba");
            DicModelNameByFile.Add("DATA04_0422", "Lizardman");
            DicModelNameByFile.Add("DATA04_0426", "Kelpie");
            DicModelNameByFile.Add("DATA04_0430", "Storm Giant");
            DicModelNameByFile.Add("DATA04_0434", "Hydra");
            DicModelNameByFile.Add("DATA04_0438", "Anubias");
            DicModelNameByFile.Add("DATA04_0442", "Lung");
            DicModelNameByFile.Add("DATA04_0446", "Bloody Pudding");
            DicModelNameByFile.Add("DATA04_0450", "Behemoth");
            DicModelNameByFile.Add("DATA04_0454", "Bookworm");
            DicModelNameByFile.Add("DATA04_0458", "Fate");
            DicModelNameByFile.Add("DATA04_0462", "Amazon");
            DicModelNameByFile.Add("DATA04_0466", "Hyde");
            DicModelNameByFile.Add("DATA04_0470", "Drool");
            DicModelNameByFile.Add("DATA04_0474", "Grendel");
            DicModelNameByFile.Add("DATA04_0478", "Charon");
            DicModelNameByFile.Add("DATA04_0482", "Fungonoid");
            DicModelNameByFile.Add("DATA04_0486", "Bunyip");
            DicModelNameByFile.Add("DATA04_0490", "Sea Bonze");
            DicModelNameByFile.Add("DATA04_0494", "Yeti");
            DicModelNameByFile.Add("DATA04_0498", "Aqualing");
            DicModelNameByFile.Add("DATA04_0502", "Blue Ogre");
            DicModelNameByFile.Add("DATA04_0506", "Deep-Sea Dragon");
            DicModelNameByFile.Add("DATA04_0510", "Rahab");
            DicModelNameByFile.Add("DATA04_0514", "Aquahorn");
            DicModelNameByFile.Add("DATA04_0518", "Mistwing");
            DicModelNameByFile.Add("DATA04_0522", "Luk-Oie");
            DicModelNameByFile.Add("DATA04_0526", "Sir Gawain");
            DicModelNameByFile.Add("DATA04_0530", "Aspidochelone");
            DicModelNameByFile.Add("DATA04_0534", "Noble Shaman");
            DicModelNameByFile.Add("DATA04_0538", "Ahuizotl");
            DicModelNameByFile.Add("DATA04_0542", "Lamia");
            DicModelNameByFile.Add("DATA04_0546", "Gelatinous Wall");
            DicModelNameByFile.Add("DATA04_0550", "Saint");
            DicModelNameByFile.Add("DATA04_0554", "Deathquito");
            DicModelNameByFile.Add("DATA04_0558", "Tarasque");
            DicModelNameByFile.Add("DATA04_0562", "Triton");
            DicModelNameByFile.Add("DATA04_0566", "Sea Hag");
            DicModelNameByFile.Add("DATA04_0570", "Swamp Spawn");
            DicModelNameByFile.Add("DATA04_0574", "Poseidon");
            DicModelNameByFile.Add("DATA04_0578", "Quicksilver");
            DicModelNameByFile.Add("DATA04_0582", "Wall Of Stone");
            DicModelNameByFile.Add("DATA04_0586", "Dryad");
            DicModelNameByFile.Add("DATA04_0590", "Dwarf");
            DicModelNameByFile.Add("DATA04_0594", "Woodfolk");
            DicModelNameByFile.Add("DATA04_0598", "Cerberus");
            DicModelNameByFile.Add("DATA04_0602", "Mummy");
            DicModelNameByFile.Add("DATA04_0606", "Gargoyle");
            DicModelNameByFile.Add("DATA04_0610", "Vampire");
            DicModelNameByFile.Add("DATA04_0614", "Dragon Zombie");
            DicModelNameByFile.Add("DATA04_0618", "T'ao T'ieh");
            DicModelNameByFile.Add("DATA04_0622", "Wereboar");
            DicModelNameByFile.Add("DATA04_0626", "Cockatrice");
            DicModelNameByFile.Add("DATA04_0630", "Mudman");
            DicModelNameByFile.Add("DATA04_0634", "Odradek");
            DicModelNameByFile.Add("DATA04_0638", "Grimalkin");
            DicModelNameByFile.Add("DATA04_0642", "Trench Wurm");
            DicModelNameByFile.Add("DATA04_0646", "Sage");
            DicModelNameByFile.Add("DATA04_0650", "Humbaba");
            DicModelNameByFile.Add("DATA04_0654", "Gigantherium");
            DicModelNameByFile.Add("DATA04_0658", "Angostura");
            DicModelNameByFile.Add("DATA04_0662", "Healer");
            DicModelNameByFile.Add("DATA04_0666", "Blynx");
            DicModelNameByFile.Add("DATA04_0670", "Leshy");
            DicModelNameByFile.Add("DATA04_0674", "Raksas");
            DicModelNameByFile.Add("DATA04_0678", "Squirrine");
            DicModelNameByFile.Add("DATA04_0682", "Green Ogre");
            DicModelNameByFile.Add("DATA04_0686", "Hardrock Dragon");
            DicModelNameByFile.Add("DATA04_0690", "Brontides");
            DicModelNameByFile.Add("DATA04_0694", "Spiny Agama");
            DicModelNameByFile.Add("DATA04_0698", "Seismodon");
            DicModelNameByFile.Add("DATA04_0702", "Lichenoid");
            DicModelNameByFile.Add("DATA04_0706", "Herald Spirit");
            DicModelNameByFile.Add("DATA04_0710", "Revenant");
            DicModelNameByFile.Add("DATA04_0714", "Venomouspore");
            DicModelNameByFile.Add("DATA04_0718", "Sakuya");
            DicModelNameByFile.Add("DATA04_0722", "Necromancer");
            DicModelNameByFile.Add("DATA04_0726", "Spudfolk");
            DicModelNameByFile.Add("DATA04_0730", "Unnameable");
            DicModelNameByFile.Add("DATA04_0734", "Weretiger");
            DicModelNameByFile.Add("DATA04_0738", "Wolverine");
            DicModelNameByFile.Add("DATA04_0742", "Hypno Sloth");
            DicModelNameByFile.Add("DATA04_0746", "Branch Army");
            DicModelNameByFile.Add("DATA04_0750", "Pangolin");
            DicModelNameByFile.Add("DATA04_0754", "Rock Shell");
            DicModelNameByFile.Add("DATA04_0758", "Bone Golem");
            DicModelNameByFile.Add("DATA04_0762", "Harpy");
            DicModelNameByFile.Add("DATA04_0766", "Gremlin");
            DicModelNameByFile.Add("DATA04_0770", "Succubus");
            DicModelNameByFile.Add("DATA04_0774", "Spectre");
            DicModelNameByFile.Add("DATA04_0778", "Pegasus");
            DicModelNameByFile.Add("DATA04_0782", "Nightmare");
            DicModelNameByFile.Add("DATA04_0786", "Knight");
            DicModelNameByFile.Add("DATA04_0790", "Griffon");
            DicModelNameByFile.Add("DATA04_0794", "Wyvern");
            DicModelNameByFile.Add("DATA04_0798", "Paladin");
            DicModelNameByFile.Add("DATA04_0802", "Powder Eater");
            DicModelNameByFile.Add("DATA04_0806", "Leveller");
            DicModelNameByFile.Add("DATA04_0810", "Nike");
            DicModelNameByFile.Add("DATA04_0814", "Garuda");
            DicModelNameByFile.Add("DATA04_0818", "Sphinx");
            DicModelNameByFile.Add("DATA04_0822", "Soul Collector");
            DicModelNameByFile.Add("DATA04_0826", "Wendigo");
            DicModelNameByFile.Add("DATA04_0830", "Night Fiend");
            DicModelNameByFile.Add("DATA04_0834", "Witch");
            DicModelNameByFile.Add("DATA04_0838", "Trample Weed");
            DicModelNameByFile.Add("DATA04_0842", "Leprechaun");
            DicModelNameByFile.Add("DATA04_0846", "Centaur");
            DicModelNameByFile.Add("DATA04_0850", "Dhampir");
            DicModelNameByFile.Add("DATA04_0854", "Holy Llama");
            DicModelNameByFile.Add("DATA04_0858", "Blitz Raven");
            DicModelNameByFile.Add("DATA04_0862", "Yellow Ogre");
            DicModelNameByFile.Add("DATA04_0866", "Lightning Dragon");
            DicModelNameByFile.Add("DATA04_0870", "Lord of Bane");
            DicModelNameByFile.Add("DATA04_0874", "Sylph");
            DicModelNameByFile.Add("DATA04_0878", "Spriggan");
            DicModelNameByFile.Add("DATA04_0882", "Sword Princess");
            DicModelNameByFile.Add("DATA04_0886", "Mutitan");
            DicModelNameByFile.Add("DATA04_0890", "Tetrarm");
            DicModelNameByFile.Add("DATA04_0894", "Cloud Gizmo");
            DicModelNameByFile.Add("DATA04_0898", "Mothman");
            DicModelNameByFile.Add("DATA04_0902", "Tengu");
            DicModelNameByFile.Add("DATA04_0906", "Cherub");
            DicModelNameByFile.Add("DATA04_0910", "Assassin");
            DicModelNameByFile.Add("DATA04_0914", "Fortress");
            DicModelNameByFile.Add("DATA04_0918", "Pulgasari");
            DicModelNameByFile.Add("DATA04_0922", "Tornado");
            DicModelNameByFile.Add("DATA04_0926", "Mirage");
            DicModelNameByFile.Add("DATA04_0930", "Doodle Beetle");
            DicModelNameByFile.Add("DATA04_0934", "Sleipnir");
            DicModelNameByFile.Add("DATA04_0938", "Flying Puffer");
            DicModelNameByFile.Add("DATA04_0942", "Jack-o'-Lantern");
            DicModelNameByFile.Add("DATA04_0946", "Crimson Vine");
            DicModelNameByFile.Add("DATA04_0950", "Frost Beast");
            DicModelNameByFile.Add("DATA04_0954", "Stymphalides");
            DicModelNameByFile.Add("DATA04_0958", "Magma Avatar");
            DicModelNameByFile.Add("DATA04_0962", "Storm Avatar");
            DicModelNameByFile.Add("DATA04_0966", "Warlock");
            DicModelNameByFile.Add("DATA04_0970", "Gogma");
            DicModelNameByFile.Add("DATA04_0974", "Sanctum Guard");
            DicModelNameByFile.Add("DATA04_0978", "Leoknight");
            DicModelNameByFile.Add("DATA04_0982", "Nil");
            DicModelNameByFile.Add("DATA04_0986", "Androgear");
            DicModelNameByFile.Add("DATA04_0990", "Gearion");
            DicModelNameByFile.Add("DATA04_0994", "Dragon Lord");
            DicModelNameByFile.Add("DATA04_0998", "Armed Princess");
            DicModelNameByFile.Add("DATA04_1002", "Grass Strider");
            DicModelNameByFile.Add("DATA04_1006", "Fontaciel");
            DicModelNameByFile.Add("DATA04_1010", "Flammefleur");
            DicModelNameByFile.Add("DATA04_1014", "Giant Rat (Copy)");

            // DATA06
            DicModelNameByFile.Add("DATA06_0001", "DATA06_0001");
            DicModelNameByFile.Add("DATA06_0004", "DATA06_0004");
            DicModelNameByFile.Add("DATA06_0007", "DATA06_0007");
            DicModelNameByFile.Add("DATA06_0010", "DATA06_0010");
            DicModelNameByFile.Add("DATA06_0013", "DATA06_0013");
            DicModelNameByFile.Add("DATA06_0016", "DATA06_0016");
            DicModelNameByFile.Add("DATA06_0019", "DATA06_0019");
            DicModelNameByFile.Add("DATA06_0022", "DATA06_0022");
            DicModelNameByFile.Add("DATA06_0024", "DATA06_0024");
            DicModelNameByFile.Add("DATA06_0026", "DATA06_0026");
            DicModelNameByFile.Add("DATA06_0028", "DATA06_0028");
            DicModelNameByFile.Add("DATA06_0030", "DATA06_0030");
            DicModelNameByFile.Add("DATA06_0032", "DATA06_0032");
            DicModelNameByFile.Add("DATA06_0034", "DATA06_0034");
            DicModelNameByFile.Add("DATA06_0036", "DATA06_0036");
            DicModelNameByFile.Add("DATA06_0038", "DATA06_0038");
            DicModelNameByFile.Add("DATA06_0040", "DATA06_0040");
            DicModelNameByFile.Add("DATA06_0042", "DATA06_0042");
            DicModelNameByFile.Add("DATA06_0044", "DATA06_0044");
            DicModelNameByFile.Add("DATA06_0046", "DATA06_0046");
            DicModelNameByFile.Add("DATA06_0048", "DATA06_0048");
            DicModelNameByFile.Add("DATA06_0050", "DATA06_0050");
            DicModelNameByFile.Add("DATA06_0052", "DATA06_0052");
            DicModelNameByFile.Add("DATA06_0054", "DATA06_0054");
            DicModelNameByFile.Add("DATA06_0056", "DATA06_0056");
            DicModelNameByFile.Add("DATA06_0058", "DATA06_0058");
            DicModelNameByFile.Add("DATA06_0060", "DATA06_0060");
            DicModelNameByFile.Add("DATA06_0062", "DATA06_0062");
            DicModelNameByFile.Add("DATA06_0064", "DATA06_0064");
            DicModelNameByFile.Add("DATA06_0066", "DATA06_0066");
            DicModelNameByFile.Add("DATA06_0068", "DATA06_0068");
            DicModelNameByFile.Add("DATA06_0070", "DATA06_0070");
            DicModelNameByFile.Add("DATA06_0072", "DATA06_0072");
            DicModelNameByFile.Add("DATA06_0074", "DATA06_0074");
            DicModelNameByFile.Add("DATA06_0076", "DATA06_0076");
            DicModelNameByFile.Add("DATA06_0078", "DATA06_0078");
            DicModelNameByFile.Add("DATA06_0080", "DATA06_0080");
            DicModelNameByFile.Add("DATA06_0082", "DATA06_0082");
            DicModelNameByFile.Add("DATA06_0084", "DATA06_0084");
            DicModelNameByFile.Add("DATA06_0086", "DATA06_0086");
            DicModelNameByFile.Add("DATA06_0088", "DATA06_0088");
            DicModelNameByFile.Add("DATA06_0090", "DATA06_0090");
            DicModelNameByFile.Add("DATA06_0093", "DATA06_0093");
            DicModelNameByFile.Add("DATA06_0095", "DATA06_0095");
            DicModelNameByFile.Add("DATA06_0097", "DATA06_0097");
            DicModelNameByFile.Add("DATA06_0099", "DATA06_0099");
            DicModelNameByFile.Add("DATA06_0101", "DATA06_0101");
            DicModelNameByFile.Add("DATA06_0103", "DATA06_0103");
            DicModelNameByFile.Add("DATA06_0137", "Faustina");
            DicModelNameByFile.Add("DATA06_0179", "Rilara");
            DicModelNameByFile.Add("DATA06_0198", "Koenig");
            DicModelNameByFile.Add("DATA06_0203", "Pondon");
            DicModelNameByFile.Add("DATA06_0213", "Markt");
            DicModelNameByFile.Add("DATA06_0220", "Ticomun");
            DicModelNameByFile.Add("DATA06_0232", "Meltia");
            DicModelNameByFile.Add("DATA06_0244", "Borival");
            DicModelNameByFile.Add("DATA06_0252", "Krone");
            DicModelNameByFile.Add("DATA06_0259", "Krone base");
            DicModelNameByFile.Add("DATA06_0262", "Wongen");
            DicModelNameByFile.Add("DATA06_0269", "Drachma");
            DicModelNameByFile.Add("DATA06_0293", "Diarna");
            DicModelNameByFile.Add("DATA06_0306", "Fontaciel");
            DicModelNameByFile.Add("DATA06_0311", "Flammefleur");
            DicModelNameByFile.Add("DATA06_0315", "Rouetsoleil");
            DicModelNameByFile.Add("DATA06_0319", "Gottlieb");
            DicModelNameByFile.Add("DATA06_0328", "Castora");
            DicModelNameByFile.Add("DATA06_0342", "Guilder & Rouble");
            DicModelNameByFile.Add("DATA06_0347", "Escudo");
            DicModelNameByFile.Add("DATA06_0362", "Dirham");
            DicModelNameByFile.Add("DATA06_0374", "Rupiah");
            DicModelNameByFile.Add("DATA06_0386", "Irville");
            DicModelNameByFile.Add("DATA06_0393", "High Priest Sapphius");
            DicModelNameByFile.Add("DATA06_0408", "Ancient God Baltias");
            DicModelNameByFile.Add("DATA06_0417", "Zeromn");
            DicModelNameByFile.Add("DATA06_0420", "Slave Trader");
            DicModelNameByFile.Add("DATA06_0430", "King Gieve");
            DicModelNameByFile.Add("DATA06_0441", "Anedia");
            DicModelNameByFile.Add("DATA06_0453", "Faustina (Darkness)");
            DicModelNameByFile.Add("DATA06_0479", "Rilara (Oracle)");
            DicModelNameByFile.Add("DATA06_0493", "Culdra");
            DicModelNameByFile.Add("DATA06_0496", "Generic victory pose");
            DicModelNameByFile.Add("DATA06_0499", "Menacing Baltias");
            DicModelNameByFile.Add("DATA06_0505", "Minotaur");
            DicModelNameByFile.Add("DATA06_0510", "Griffon");
            DicModelNameByFile.Add("DATA06_0515", "Goblin");
            DicModelNameByFile.Add("DATA06_0519", "Skeleton");
            DicModelNameByFile.Add("DATA06_0528", "Card opening");
            DicModelNameByFile.Add("DATA06_0531", "Card opening");
            DicModelNameByFile.Add("DATA06_0533", "Card");
            DicModelNameByFile.Add("DATA06_0535", "Fence");
            DicModelNameByFile.Add("DATA06_0537", "Half circle (right)");
            DicModelNameByFile.Add("DATA06_0539", "Half circle (right)");
            DicModelNameByFile.Add("DATA06_0541", "DATA06_0541");
            DicModelNameByFile.Add("DATA06_0543", "DATA06_0543");
            DicModelNameByFile.Add("DATA06_0545", "DATA06_0545");
            DicModelNameByFile.Add("DATA06_0548", "DATA06_0548");
            DicModelNameByFile.Add("DATA06_0550", "DATA06_0550");
            DicModelNameByFile.Add("DATA06_0552", "DATA06_0552");
            DicModelNameByFile.Add("DATA06_0554", "DATA06_0554");
            DicModelNameByFile.Add("DATA06_0556", "DATA06_0556");
            DicModelNameByFile.Add("DATA06_0559", "DATA06_0559");
            DicModelNameByFile.Add("DATA06_0561", "DATA06_0561");
            DicModelNameByFile.Add("DATA06_0563", "DATA06_0563");
            DicModelNameByFile.Add("DATA06_0566", "DATA06_0566");
            DicModelNameByFile.Add("DATA06_0568", "DATA06_0568");
            DicModelNameByFile.Add("DATA06_0570", "DATA06_0570");
            DicModelNameByFile.Add("DATA06_0572", "DATA06_0572");
            DicModelNameByFile.Add("DATA06_0575", "DATA06_0575");
            DicModelNameByFile.Add("DATA06_0577", "DATA06_0577");
            DicModelNameByFile.Add("DATA06_0579", "DATA06_0579");
            DicModelNameByFile.Add("DATA06_0581", "DATA06_0581");
            DicModelNameByFile.Add("DATA06_0583", "DATA06_0583");
            DicModelNameByFile.Add("DATA06_0586", "DATA06_0586");
            DicModelNameByFile.Add("DATA06_0589", "DATA06_0589");
            DicModelNameByFile.Add("DATA06_0592", "DATA06_0592");
            DicModelNameByFile.Add("DATA06_0594", "DATA06_0594");
            DicModelNameByFile.Add("DATA06_0596", "DATA06_0596");
            DicModelNameByFile.Add("DATA06_0599", "DATA06_0599");
            DicModelNameByFile.Add("DATA06_0602", "DATA06_0602");
            DicModelNameByFile.Add("DATA06_0605", "DATA06_0605");
            DicModelNameByFile.Add("DATA06_0608", "DATA06_0608");
            DicModelNameByFile.Add("DATA06_0611", "DATA06_0611");
            DicModelNameByFile.Add("DATA06_0614", "DATA06_0614");
            DicModelNameByFile.Add("DATA06_0617", "DATA06_0617");
            DicModelNameByFile.Add("DATA06_0619", "DATA06_0619");
            DicModelNameByFile.Add("DATA06_0622", "DATA06_0622");
            DicModelNameByFile.Add("DATA06_0624", "DATA06_0624");
            DicModelNameByFile.Add("DATA06_0627", "DATA06_0627");
            DicModelNameByFile.Add("DATA06_0630", "DATA06_0630");
            DicModelNameByFile.Add("DATA06_0632", "DATA06_0632");
            DicModelNameByFile.Add("DATA06_0634", "DATA06_0634");
            DicModelNameByFile.Add("DATA06_0636", "DATA06_0636");
            DicModelNameByFile.Add("DATA06_0638", "DATA06_0638");
            DicModelNameByFile.Add("DATA06_0640", "DATA06_0640");
            DicModelNameByFile.Add("DATA06_0642", "DATA06_0642");
            DicModelNameByFile.Add("DATA06_0645", "DATA06_0645");
            DicModelNameByFile.Add("DATA06_0647", "DATA06_0647");
            DicModelNameByFile.Add("DATA06_0650", "DATA06_0650");
            DicModelNameByFile.Add("DATA06_0652", "DATA06_0652");
            DicModelNameByFile.Add("DATA06_0654", "DATA06_0654");
            DicModelNameByFile.Add("DATA06_0656", "DATA06_0656");
            DicModelNameByFile.Add("DATA06_0658", "DATA06_0658");
            DicModelNameByFile.Add("DATA06_0661", "DATA06_0661");
            DicModelNameByFile.Add("DATA06_0663", "DATA06_0663");
            DicModelNameByFile.Add("DATA06_0665", "DATA06_0665");
            DicModelNameByFile.Add("DATA06_0668", "DATA06_0668");
            DicModelNameByFile.Add("DATA06_0693", "Custom character outfit 1 Male");
            DicModelNameByFile.Add("DATA06_0696", "Custom character outfit 2 Male");
            DicModelNameByFile.Add("DATA06_0699", "Custom character outfit 3 Male");
            DicModelNameByFile.Add("DATA06_0762", "Custom character outfit 4 Male");
            DicModelNameByFile.Add("DATA06_0825", "Custom character outfit 5 Male");
            DicModelNameByFile.Add("DATA06_0828", "Custom character outfit 1 Female");
            DicModelNameByFile.Add("DATA06_0831", "Custom character outfit 2 Female");
            DicModelNameByFile.Add("DATA06_0834", "Custom character outfit 3 Female");
            DicModelNameByFile.Add("DATA06_0837", "Custom character outfit 4 Female");
            DicModelNameByFile.Add("DATA06_0840", "Custom character outfit 5 Female");
        }
    }
}
