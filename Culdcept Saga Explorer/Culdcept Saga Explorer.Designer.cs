using GraphicsManager;
using System.Drawing;
using System.Windows.Forms;

namespace CuldceptSagaExplorer
{
    partial class CuldceptSagaExplorer
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            view3DModel = new Model3DViewer();
            tabControl1 = new TabControl();
            tabMenus = new TabPage();
            lblMenuModelAnimations = new Label();
            lsMenuModelAnimations = new ListView();
            lblMenuModelTextures = new Label();
            lsMenuModelTextures = new ListView();
            lblMenuModels = new Label();
            lsMenuModels = new ListView();
            tabCustomCepter = new TabPage();
            label10 = new Label();
            lsCustomCepterModelAnimations = new ListView();
            label11 = new Label();
            lsCustomCepterModelTextures = new ListView();
            label12 = new Label();
            lsCustomCepterModels = new ListView();
            tabMaps = new TabPage();
            label7 = new Label();
            lsMapModelAnimations = new ListView();
            label8 = new Label();
            lsMapModelTextures = new ListView();
            label9 = new Label();
            lsMapModels = new ListView();
            tabCreatures = new TabPage();
            label4 = new Label();
            lsCreatureModelAnimations = new ListView();
            label5 = new Label();
            lsCreatureModelTextures = new ListView();
            label6 = new Label();
            lsCreatureModels = new ListView();
            tabCepters = new TabPage();
            label3 = new Label();
            lsCepterModelAnimations = new ListView();
            label2 = new Label();
            lsCepterModelTextures = new ListView();
            label1 = new Label();
            lsCepterModels = new ListView();
            btnReplaceSelectedTexture = new Button();
            btnReplaceSelectedModel = new Button();
            viewModelTexture = new TextureViewer();
            txtWorkingFolder = new TextBox();
            lblWorkingFolder = new Label();
            btnSetWorkingFolder = new Button();
            btnExtractCuldceptSagaISO = new Button();
            btnUnpackCuldceptSagaAssets = new Button();
            btnCreateNewCuldceptSagaISO = new Button();
            btnRepackCuldceptSagaAssets = new Button();
            btnExportCuldceptSagaAssetsToGLB = new Button();
            btnExportCuldceptSagaAssetsToDAE = new Button();
            lblModelFileName = new Label();
            txtModelFileName = new TextBox();
            tabEffects = new TabPage();
            lblEffectModelAnimations = new Label();
            lsEffectModelAnimations = new ListView();
            lblEffectModelTextures = new Label();
            lsEffectModelTextures = new ListView();
            lblEffectsModels = new Label();
            lsEffectsModels = new ListView();
            tabControl1.SuspendLayout();
            tabMenus.SuspendLayout();
            tabCustomCepter.SuspendLayout();
            tabMaps.SuspendLayout();
            tabCreatures.SuspendLayout();
            tabCepters.SuspendLayout();
            tabEffects.SuspendLayout();
            SuspendLayout();
            // 
            // view3DModel
            // 
            view3DModel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            view3DModel.Location = new Point(798, 12);
            view3DModel.Name = "view3DModel";
            view3DModel.Size = new Size(507, 470);
            view3DModel.TabIndex = 0;
            view3DModel.Text = "myViewportControl1";
            // 
            // tabControl1
            // 
            tabControl1.Controls.Add(tabMenus);
            tabControl1.Controls.Add(tabCustomCepter);
            tabControl1.Controls.Add(tabMaps);
            tabControl1.Controls.Add(tabCreatures);
            tabControl1.Controls.Add(tabCepters);
            tabControl1.Controls.Add(tabEffects);
            tabControl1.Location = new Point(160, 12);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(632, 709);
            tabControl1.TabIndex = 4;
            // 
            // tabMenus
            // 
            tabMenus.Controls.Add(lblMenuModelAnimations);
            tabMenus.Controls.Add(lsMenuModelAnimations);
            tabMenus.Controls.Add(lblMenuModelTextures);
            tabMenus.Controls.Add(lsMenuModelTextures);
            tabMenus.Controls.Add(lblMenuModels);
            tabMenus.Controls.Add(lsMenuModels);
            tabMenus.Location = new Point(4, 24);
            tabMenus.Name = "tabMenus";
            tabMenus.Size = new Size(624, 681);
            tabMenus.TabIndex = 4;
            tabMenus.Text = "Menus";
            tabMenus.UseVisualStyleBackColor = true;
            // 
            // lblMenuModelAnimations
            // 
            lblMenuModelAnimations.AutoSize = true;
            lblMenuModelAnimations.Location = new Point(409, 167);
            lblMenuModelAnimations.Name = "lblMenuModelAnimations";
            lblMenuModelAnimations.Size = new Size(105, 15);
            lblMenuModelAnimations.TabIndex = 29;
            lblMenuModelAnimations.Text = "Model Animations";
            // 
            // lsMenuModelAnimations
            // 
            lsMenuModelAnimations.Location = new Point(406, 186);
            lsMenuModelAnimations.MultiSelect = false;
            lsMenuModelAnimations.Name = "lsMenuModelAnimations";
            lsMenuModelAnimations.Size = new Size(217, 139);
            lsMenuModelAnimations.TabIndex = 28;
            lsMenuModelAnimations.UseCompatibleStateImageBehavior = false;
            lsMenuModelAnimations.View = View.List;
            lsMenuModelAnimations.SelectedIndexChanged += lsMenuModelAnimations_SelectedIndexChanged;
            // 
            // lblMenuModelTextures
            // 
            lblMenuModelTextures.AutoSize = true;
            lblMenuModelTextures.Location = new Point(409, 7);
            lblMenuModelTextures.Name = "lblMenuModelTextures";
            lblMenuModelTextures.Size = new Size(87, 15);
            lblMenuModelTextures.TabIndex = 27;
            lblMenuModelTextures.Text = "Model Textures";
            // 
            // lsMenuModelTextures
            // 
            lsMenuModelTextures.Location = new Point(406, 26);
            lsMenuModelTextures.Name = "lsMenuModelTextures";
            lsMenuModelTextures.Size = new Size(217, 139);
            lsMenuModelTextures.TabIndex = 26;
            lsMenuModelTextures.UseCompatibleStateImageBehavior = false;
            lsMenuModelTextures.View = View.List;
            lsMenuModelTextures.SelectedIndexChanged += lsMenuModelTextures_SelectedIndexChanged;
            // 
            // lblMenuModels
            // 
            lblMenuModels.AutoSize = true;
            lblMenuModels.Location = new Point(5, 4);
            lblMenuModels.Name = "lblMenuModels";
            lblMenuModels.Size = new Size(46, 15);
            lblMenuModels.TabIndex = 25;
            lblMenuModels.Text = "Models";
            // 
            // lsMenuModels
            // 
            lsMenuModels.Location = new Point(2, 23);
            lsMenuModels.Name = "lsMenuModels";
            lsMenuModels.Size = new Size(398, 653);
            lsMenuModels.TabIndex = 24;
            lsMenuModels.UseCompatibleStateImageBehavior = false;
            lsMenuModels.View = View.List;
            lsMenuModels.SelectedIndexChanged += lsMenuModels_SelectedIndexChanged;
            // 
            // tabCustomCepter
            // 
            tabCustomCepter.Controls.Add(label10);
            tabCustomCepter.Controls.Add(lsCustomCepterModelAnimations);
            tabCustomCepter.Controls.Add(label11);
            tabCustomCepter.Controls.Add(lsCustomCepterModelTextures);
            tabCustomCepter.Controls.Add(label12);
            tabCustomCepter.Controls.Add(lsCustomCepterModels);
            tabCustomCepter.Location = new Point(4, 24);
            tabCustomCepter.Name = "tabCustomCepter";
            tabCustomCepter.Size = new Size(624, 681);
            tabCustomCepter.TabIndex = 3;
            tabCustomCepter.Text = "Custom Cepter";
            tabCustomCepter.UseVisualStyleBackColor = true;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(409, 167);
            label10.Name = "label10";
            label10.Size = new Size(105, 15);
            label10.TabIndex = 23;
            label10.Text = "Model Animations";
            // 
            // lsCustomCepterModelAnimations
            // 
            lsCustomCepterModelAnimations.Location = new Point(406, 186);
            lsCustomCepterModelAnimations.MultiSelect = false;
            lsCustomCepterModelAnimations.Name = "lsCustomCepterModelAnimations";
            lsCustomCepterModelAnimations.Size = new Size(217, 139);
            lsCustomCepterModelAnimations.TabIndex = 22;
            lsCustomCepterModelAnimations.UseCompatibleStateImageBehavior = false;
            lsCustomCepterModelAnimations.View = View.List;
            lsCustomCepterModelAnimations.SelectedIndexChanged += lsCustomCepterModelAnimations_SelectedIndexChanged;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(409, 7);
            label11.Name = "label11";
            label11.Size = new Size(87, 15);
            label11.TabIndex = 21;
            label11.Text = "Model Textures";
            // 
            // lsCustomCepterModelTextures
            // 
            lsCustomCepterModelTextures.Location = new Point(406, 26);
            lsCustomCepterModelTextures.Name = "lsCustomCepterModelTextures";
            lsCustomCepterModelTextures.Size = new Size(217, 139);
            lsCustomCepterModelTextures.TabIndex = 20;
            lsCustomCepterModelTextures.UseCompatibleStateImageBehavior = false;
            lsCustomCepterModelTextures.View = View.List;
            lsCustomCepterModelTextures.SelectedIndexChanged += lsCustomCepterModelTextures_SelectedIndexChanged;
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(5, 4);
            label12.Name = "label12";
            label12.Size = new Size(46, 15);
            label12.TabIndex = 19;
            label12.Text = "Models";
            // 
            // lsCustomCepterModels
            // 
            lsCustomCepterModels.Location = new Point(2, 23);
            lsCustomCepterModels.Name = "lsCustomCepterModels";
            lsCustomCepterModels.Size = new Size(398, 653);
            lsCustomCepterModels.TabIndex = 18;
            lsCustomCepterModels.UseCompatibleStateImageBehavior = false;
            lsCustomCepterModels.View = View.List;
            lsCustomCepterModels.SelectedIndexChanged += lsCustomCepterModels_SelectedIndexChanged;
            // 
            // tabMaps
            // 
            tabMaps.Controls.Add(label7);
            tabMaps.Controls.Add(lsMapModelAnimations);
            tabMaps.Controls.Add(label8);
            tabMaps.Controls.Add(lsMapModelTextures);
            tabMaps.Controls.Add(label9);
            tabMaps.Controls.Add(lsMapModels);
            tabMaps.Location = new Point(4, 24);
            tabMaps.Name = "tabMaps";
            tabMaps.Size = new Size(624, 681);
            tabMaps.TabIndex = 2;
            tabMaps.Text = "Maps";
            tabMaps.UseVisualStyleBackColor = true;
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(407, 163);
            label7.Name = "label7";
            label7.Size = new Size(105, 15);
            label7.TabIndex = 17;
            label7.Text = "Model Animations";
            // 
            // lsMapModelAnimations
            // 
            lsMapModelAnimations.Location = new Point(404, 182);
            lsMapModelAnimations.MultiSelect = false;
            lsMapModelAnimations.Name = "lsMapModelAnimations";
            lsMapModelAnimations.Size = new Size(217, 139);
            lsMapModelAnimations.TabIndex = 16;
            lsMapModelAnimations.UseCompatibleStateImageBehavior = false;
            lsMapModelAnimations.View = View.List;
            lsMapModelAnimations.SelectedIndexChanged += lsMapAnimations_SelectedIndexChanged;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(407, 3);
            label8.Name = "label8";
            label8.Size = new Size(87, 15);
            label8.TabIndex = 15;
            label8.Text = "Model Textures";
            // 
            // lsMapModelTextures
            // 
            lsMapModelTextures.Location = new Point(404, 22);
            lsMapModelTextures.Name = "lsMapModelTextures";
            lsMapModelTextures.Size = new Size(217, 139);
            lsMapModelTextures.TabIndex = 14;
            lsMapModelTextures.UseCompatibleStateImageBehavior = false;
            lsMapModelTextures.View = View.List;
            lsMapModelTextures.SelectedIndexChanged += lsMapTextures_SelectedIndexChanged;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(3, 0);
            label9.Name = "label9";
            label9.Size = new Size(46, 15);
            label9.TabIndex = 13;
            label9.Text = "Models";
            // 
            // lsMapModels
            // 
            lsMapModels.Location = new Point(0, 19);
            lsMapModels.Name = "lsMapModels";
            lsMapModels.Size = new Size(398, 653);
            lsMapModels.TabIndex = 12;
            lsMapModels.UseCompatibleStateImageBehavior = false;
            lsMapModels.View = View.List;
            lsMapModels.SelectedIndexChanged += lsMapModels_SelectedIndexChanged;
            // 
            // tabCreatures
            // 
            tabCreatures.Controls.Add(label4);
            tabCreatures.Controls.Add(lsCreatureModelAnimations);
            tabCreatures.Controls.Add(label5);
            tabCreatures.Controls.Add(lsCreatureModelTextures);
            tabCreatures.Controls.Add(label6);
            tabCreatures.Controls.Add(lsCreatureModels);
            tabCreatures.Location = new Point(4, 24);
            tabCreatures.Name = "tabCreatures";
            tabCreatures.Padding = new Padding(3);
            tabCreatures.Size = new Size(624, 681);
            tabCreatures.TabIndex = 1;
            tabCreatures.Text = "Creatures";
            tabCreatures.UseVisualStyleBackColor = true;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(404, 172);
            label4.Name = "label4";
            label4.Size = new Size(105, 15);
            label4.TabIndex = 11;
            label4.Text = "Model Animations";
            // 
            // lsCreatureModelAnimations
            // 
            lsCreatureModelAnimations.Location = new Point(401, 191);
            lsCreatureModelAnimations.MultiSelect = false;
            lsCreatureModelAnimations.Name = "lsCreatureModelAnimations";
            lsCreatureModelAnimations.Size = new Size(217, 139);
            lsCreatureModelAnimations.TabIndex = 10;
            lsCreatureModelAnimations.UseCompatibleStateImageBehavior = false;
            lsCreatureModelAnimations.View = View.List;
            lsCreatureModelAnimations.SelectedIndexChanged += lsCreatureAnimations_SelectedIndexChanged;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(404, 3);
            label5.Name = "label5";
            label5.Size = new Size(87, 15);
            label5.TabIndex = 9;
            label5.Text = "Model Textures";
            // 
            // lsCreatureModelTextures
            // 
            lsCreatureModelTextures.Location = new Point(401, 22);
            lsCreatureModelTextures.Name = "lsCreatureModelTextures";
            lsCreatureModelTextures.Size = new Size(217, 139);
            lsCreatureModelTextures.TabIndex = 8;
            lsCreatureModelTextures.UseCompatibleStateImageBehavior = false;
            lsCreatureModelTextures.View = View.List;
            lsCreatureModelTextures.SelectedIndexChanged += lsCreatureTextures_SelectedIndexChanged;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(6, 3);
            label6.Name = "label6";
            label6.Size = new Size(46, 15);
            label6.TabIndex = 7;
            label6.Text = "Models";
            // 
            // lsCreatureModels
            // 
            lsCreatureModels.Location = new Point(3, 22);
            lsCreatureModels.Name = "lsCreatureModels";
            lsCreatureModels.Size = new Size(392, 653);
            lsCreatureModels.TabIndex = 6;
            lsCreatureModels.UseCompatibleStateImageBehavior = false;
            lsCreatureModels.View = View.List;
            lsCreatureModels.SelectedIndexChanged += lsCreatureModels_SelectedIndexChanged;
            // 
            // tabCepters
            // 
            tabCepters.Controls.Add(label3);
            tabCepters.Controls.Add(lsCepterModelAnimations);
            tabCepters.Controls.Add(label2);
            tabCepters.Controls.Add(lsCepterModelTextures);
            tabCepters.Controls.Add(label1);
            tabCepters.Controls.Add(lsCepterModels);
            tabCepters.Location = new Point(4, 24);
            tabCepters.Name = "tabCepters";
            tabCepters.Padding = new Padding(3);
            tabCepters.Size = new Size(624, 681);
            tabCepters.TabIndex = 0;
            tabCepters.Text = "Cepters";
            tabCepters.UseVisualStyleBackColor = true;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(404, 174);
            label3.Name = "label3";
            label3.Size = new Size(105, 15);
            label3.TabIndex = 5;
            label3.Text = "Model Animations";
            // 
            // lsCepterModelAnimations
            // 
            lsCepterModelAnimations.Location = new Point(401, 193);
            lsCepterModelAnimations.MultiSelect = false;
            lsCepterModelAnimations.Name = "lsCepterModelAnimations";
            lsCepterModelAnimations.Size = new Size(217, 139);
            lsCepterModelAnimations.TabIndex = 4;
            lsCepterModelAnimations.UseCompatibleStateImageBehavior = false;
            lsCepterModelAnimations.View = View.List;
            lsCepterModelAnimations.SelectedIndexChanged += lsCepterModelAnimations_SelectedIndexChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(404, 3);
            label2.Name = "label2";
            label2.Size = new Size(87, 15);
            label2.TabIndex = 3;
            label2.Text = "Model Textures";
            // 
            // lsCepterModelTextures
            // 
            lsCepterModelTextures.Location = new Point(401, 22);
            lsCepterModelTextures.Name = "lsCepterModelTextures";
            lsCepterModelTextures.Size = new Size(217, 139);
            lsCepterModelTextures.TabIndex = 2;
            lsCepterModelTextures.UseCompatibleStateImageBehavior = false;
            lsCepterModelTextures.View = View.List;
            lsCepterModelTextures.SelectedIndexChanged += lsCepterModelTextures_SelectedIndexChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(6, 3);
            label1.Name = "label1";
            label1.Size = new Size(46, 15);
            label1.TabIndex = 1;
            label1.Text = "Models";
            // 
            // lsCepterModels
            // 
            lsCepterModels.Location = new Point(3, 22);
            lsCepterModels.Name = "lsCepterModels";
            lsCepterModels.Size = new Size(392, 653);
            lsCepterModels.TabIndex = 0;
            lsCepterModels.UseCompatibleStateImageBehavior = false;
            lsCepterModels.View = View.List;
            lsCepterModels.SelectedIndexChanged += lsCepterModels_SelectedIndexChanged;
            // 
            // btnReplaceSelectedTexture
            // 
            btnReplaceSelectedTexture.Location = new Point(1047, 517);
            btnReplaceSelectedTexture.Name = "btnReplaceSelectedTexture";
            btnReplaceSelectedTexture.Size = new Size(215, 23);
            btnReplaceSelectedTexture.TabIndex = 31;
            btnReplaceSelectedTexture.Text = "Replace Selected Texture";
            btnReplaceSelectedTexture.UseVisualStyleBackColor = true;
            btnReplaceSelectedTexture.Click += btnReplaceSelectedTexture_Click;
            // 
            // btnReplaceSelectedModel
            // 
            btnReplaceSelectedModel.Location = new Point(1047, 488);
            btnReplaceSelectedModel.Name = "btnReplaceSelectedModel";
            btnReplaceSelectedModel.Size = new Size(215, 23);
            btnReplaceSelectedModel.TabIndex = 30;
            btnReplaceSelectedModel.Text = "Replace Selected Model";
            btnReplaceSelectedModel.UseVisualStyleBackColor = true;
            btnReplaceSelectedModel.Click += btnReplaceSelectedModel_Click;
            // 
            // viewModelTexture
            // 
            viewModelTexture.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            viewModelTexture.Location = new Point(794, 488);
            viewModelTexture.Name = "viewModelTexture";
            viewModelTexture.Size = new Size(247, 229);
            viewModelTexture.TabIndex = 5;
            viewModelTexture.Text = "textureViewer1";
            // 
            // txtWorkingFolder
            // 
            txtWorkingFolder.Location = new Point(12, 30);
            txtWorkingFolder.Name = "txtWorkingFolder";
            txtWorkingFolder.ReadOnly = true;
            txtWorkingFolder.Size = new Size(142, 23);
            txtWorkingFolder.TabIndex = 24;
            // 
            // lblWorkingFolder
            // 
            lblWorkingFolder.AutoSize = true;
            lblWorkingFolder.Location = new Point(12, 12);
            lblWorkingFolder.Name = "lblWorkingFolder";
            lblWorkingFolder.Size = new Size(88, 15);
            lblWorkingFolder.TabIndex = 24;
            lblWorkingFolder.Text = "Working Folder";
            // 
            // btnSetWorkingFolder
            // 
            btnSetWorkingFolder.Location = new Point(12, 59);
            btnSetWorkingFolder.Name = "btnSetWorkingFolder";
            btnSetWorkingFolder.Size = new Size(115, 23);
            btnSetWorkingFolder.TabIndex = 25;
            btnSetWorkingFolder.Text = "Set Working Folder";
            btnSetWorkingFolder.UseVisualStyleBackColor = true;
            btnSetWorkingFolder.Click += btnSetWorkingFolder_Click;
            // 
            // btnExtractCuldceptSagaISO
            // 
            btnExtractCuldceptSagaISO.Enabled = false;
            btnExtractCuldceptSagaISO.Location = new Point(12, 88);
            btnExtractCuldceptSagaISO.Name = "btnExtractCuldceptSagaISO";
            btnExtractCuldceptSagaISO.Size = new Size(142, 52);
            btnExtractCuldceptSagaISO.TabIndex = 26;
            btnExtractCuldceptSagaISO.Text = "Extract Culdcept Saga ISO";
            btnExtractCuldceptSagaISO.UseVisualStyleBackColor = true;
            btnExtractCuldceptSagaISO.Click += btnExtractCuldceptSagaISO_Click;
            // 
            // btnUnpackCuldceptSagaAssets
            // 
            btnUnpackCuldceptSagaAssets.Enabled = false;
            btnUnpackCuldceptSagaAssets.Location = new Point(12, 146);
            btnUnpackCuldceptSagaAssets.Name = "btnUnpackCuldceptSagaAssets";
            btnUnpackCuldceptSagaAssets.Size = new Size(142, 52);
            btnUnpackCuldceptSagaAssets.TabIndex = 27;
            btnUnpackCuldceptSagaAssets.Text = "Unpack Culdcept Saga Assets";
            btnUnpackCuldceptSagaAssets.UseVisualStyleBackColor = true;
            btnUnpackCuldceptSagaAssets.Click += btnUnpackCuldceptSagaAssets_Click;
            // 
            // btnCreateNewCuldceptSagaISO
            // 
            btnCreateNewCuldceptSagaISO.Enabled = false;
            btnCreateNewCuldceptSagaISO.Location = new Point(12, 660);
            btnCreateNewCuldceptSagaISO.Name = "btnCreateNewCuldceptSagaISO";
            btnCreateNewCuldceptSagaISO.Size = new Size(142, 52);
            btnCreateNewCuldceptSagaISO.TabIndex = 28;
            btnCreateNewCuldceptSagaISO.Text = "Create new Culdcept Saga ISO";
            btnCreateNewCuldceptSagaISO.UseVisualStyleBackColor = true;
            btnCreateNewCuldceptSagaISO.Click += btnCreateNewCuldceptSagaISO_Click;
            // 
            // btnRepackCuldceptSagaAssets
            // 
            btnRepackCuldceptSagaAssets.Enabled = false;
            btnRepackCuldceptSagaAssets.Location = new Point(12, 602);
            btnRepackCuldceptSagaAssets.Name = "btnRepackCuldceptSagaAssets";
            btnRepackCuldceptSagaAssets.Size = new Size(142, 52);
            btnRepackCuldceptSagaAssets.TabIndex = 29;
            btnRepackCuldceptSagaAssets.Text = "Repack Culdcept Saga Assets";
            btnRepackCuldceptSagaAssets.UseVisualStyleBackColor = true;
            btnRepackCuldceptSagaAssets.Click += btnRepackCuldceptSagaAssets_Click;
            // 
            // btnExportCuldceptSagaAssetsToGLB
            // 
            btnExportCuldceptSagaAssetsToGLB.Enabled = false;
            btnExportCuldceptSagaAssetsToGLB.Location = new Point(12, 204);
            btnExportCuldceptSagaAssetsToGLB.Name = "btnExportCuldceptSagaAssetsToGLB";
            btnExportCuldceptSagaAssetsToGLB.Size = new Size(142, 52);
            btnExportCuldceptSagaAssetsToGLB.TabIndex = 30;
            btnExportCuldceptSagaAssetsToGLB.Text = "Export Culdcept Saga Assets to GLB";
            btnExportCuldceptSagaAssetsToGLB.UseVisualStyleBackColor = true;
            btnExportCuldceptSagaAssetsToGLB.Click += btnExportCuldceptSagaAssetsToGLB_Click;
            // 
            // btnExportCuldceptSagaAssetsToDAE
            // 
            btnExportCuldceptSagaAssetsToDAE.Enabled = false;
            btnExportCuldceptSagaAssetsToDAE.Location = new Point(12, 262);
            btnExportCuldceptSagaAssetsToDAE.Name = "btnExportCuldceptSagaAssetsToDAE";
            btnExportCuldceptSagaAssetsToDAE.Size = new Size(142, 52);
            btnExportCuldceptSagaAssetsToDAE.TabIndex = 32;
            btnExportCuldceptSagaAssetsToDAE.Text = "Export Culdcept Saga Assets to DAE";
            btnExportCuldceptSagaAssetsToDAE.UseVisualStyleBackColor = true;
            btnExportCuldceptSagaAssetsToDAE.Click += btnExportCuldceptSagaAssetsToDAE_Click;
            // 
            // lblModelFileName
            // 
            lblModelFileName.AutoSize = true;
            lblModelFileName.Location = new Point(1047, 543);
            lblModelFileName.Name = "lblModelFileName";
            lblModelFileName.Size = new Size(60, 15);
            lblModelFileName.TabIndex = 33;
            lblModelFileName.Text = "File Name";
            // 
            // txtModelFileName
            // 
            txtModelFileName.Location = new Point(1047, 561);
            txtModelFileName.Name = "txtModelFileName";
            txtModelFileName.ReadOnly = true;
            txtModelFileName.Size = new Size(215, 23);
            txtModelFileName.TabIndex = 34;
            // 
            // tabEffects
            // 
            tabEffects.Controls.Add(lblEffectModelAnimations);
            tabEffects.Controls.Add(lsEffectModelAnimations);
            tabEffects.Controls.Add(lblEffectModelTextures);
            tabEffects.Controls.Add(lsEffectModelTextures);
            tabEffects.Controls.Add(lblEffectsModels);
            tabEffects.Controls.Add(lsEffectsModels);
            tabEffects.Location = new Point(4, 24);
            tabEffects.Name = "tabEffects";
            tabEffects.Padding = new Padding(3);
            tabEffects.Size = new Size(624, 681);
            tabEffects.TabIndex = 5;
            tabEffects.Text = "Effects";
            tabEffects.UseVisualStyleBackColor = true;
            // 
            // lblEffectModelAnimations
            // 
            lblEffectModelAnimations.AutoSize = true;
            lblEffectModelAnimations.Location = new Point(409, 167);
            lblEffectModelAnimations.Name = "lblEffectModelAnimations";
            lblEffectModelAnimations.Size = new Size(105, 15);
            lblEffectModelAnimations.TabIndex = 35;
            lblEffectModelAnimations.Text = "Model Animations";
            // 
            // lsEffectModelAnimations
            // 
            lsEffectModelAnimations.Location = new Point(406, 186);
            lsEffectModelAnimations.MultiSelect = false;
            lsEffectModelAnimations.Name = "lsEffectModelAnimations";
            lsEffectModelAnimations.Size = new Size(217, 139);
            lsEffectModelAnimations.TabIndex = 34;
            lsEffectModelAnimations.UseCompatibleStateImageBehavior = false;
            lsEffectModelAnimations.View = View.List;
            // 
            // lblEffectModelTextures
            // 
            lblEffectModelTextures.AutoSize = true;
            lblEffectModelTextures.Location = new Point(409, 7);
            lblEffectModelTextures.Name = "lblEffectModelTextures";
            lblEffectModelTextures.Size = new Size(87, 15);
            lblEffectModelTextures.TabIndex = 33;
            lblEffectModelTextures.Text = "Model Textures";
            // 
            // lsEffectModelTextures
            // 
            lsEffectModelTextures.Location = new Point(406, 26);
            lsEffectModelTextures.Name = "lsEffectModelTextures";
            lsEffectModelTextures.Size = new Size(217, 139);
            lsEffectModelTextures.TabIndex = 32;
            lsEffectModelTextures.UseCompatibleStateImageBehavior = false;
            lsEffectModelTextures.View = View.List;
            // 
            // lblEffectsModels
            // 
            lblEffectsModels.AutoSize = true;
            lblEffectsModels.Location = new Point(5, 4);
            lblEffectsModels.Name = "lblEffectsModels";
            lblEffectsModels.Size = new Size(46, 15);
            lblEffectsModels.TabIndex = 31;
            lblEffectsModels.Text = "Models";
            // 
            // lsEffectsModels
            // 
            lsEffectsModels.Location = new Point(2, 23);
            lsEffectsModels.Name = "lsEffectsModels";
            lsEffectsModels.Size = new Size(398, 653);
            lsEffectsModels.TabIndex = 30;
            lsEffectsModels.UseCompatibleStateImageBehavior = false;
            lsEffectsModels.View = View.List;
            // 
            // CuldceptSagaExplorer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1317, 729);
            Controls.Add(lblModelFileName);
            Controls.Add(txtModelFileName);
            Controls.Add(btnExportCuldceptSagaAssetsToDAE);
            Controls.Add(btnReplaceSelectedTexture);
            Controls.Add(btnExportCuldceptSagaAssetsToGLB);
            Controls.Add(btnReplaceSelectedModel);
            Controls.Add(btnRepackCuldceptSagaAssets);
            Controls.Add(btnCreateNewCuldceptSagaISO);
            Controls.Add(btnUnpackCuldceptSagaAssets);
            Controls.Add(btnExtractCuldceptSagaISO);
            Controls.Add(btnSetWorkingFolder);
            Controls.Add(lblWorkingFolder);
            Controls.Add(txtWorkingFolder);
            Controls.Add(viewModelTexture);
            Controls.Add(tabControl1);
            Controls.Add(view3DModel);
            Name = "CuldceptSagaExplorer";
            Text = "Culdcept Saga Explorer";
            tabControl1.ResumeLayout(false);
            tabMenus.ResumeLayout(false);
            tabMenus.PerformLayout();
            tabCustomCepter.ResumeLayout(false);
            tabCustomCepter.PerformLayout();
            tabMaps.ResumeLayout(false);
            tabMaps.PerformLayout();
            tabCreatures.ResumeLayout(false);
            tabCreatures.PerformLayout();
            tabCepters.ResumeLayout(false);
            tabCepters.PerformLayout();
            tabEffects.ResumeLayout(false);
            tabEffects.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Model3DViewer view3DModel;
        private TabControl tabControl1;
        private TabPage tabCepters;
        private TabPage tabCreatures;
        private ListView lsCepterModels;
        private Label label1;
        private Label label3;
        private ListView lsCepterModelAnimations;
        private Label label2;
        private ListView lsCepterModelTextures;
        private Label label4;
        private ListView lsCreatureModelAnimations;
        private Label label5;
        private ListView lsCreatureModelTextures;
        private Label label6;
        private ListView lsCreatureModels;
        private TextureViewer viewModelTexture;
        private TabPage tabMaps;
        private Label label7;
        private ListView lsMapModelAnimations;
        private Label label8;
        private ListView lsMapModelTextures;
        private Label label9;
        private ListView lsMapModels;
        private TabPage tabCustomCepter;
        private Label label10;
        private ListView lsCustomCepterModelAnimations;
        private Label label11;
        private ListView lsCustomCepterModelTextures;
        private Label label12;
        private ListView lsCustomCepterModels;
        private TextBox txtWorkingFolder;
        private Label lblWorkingFolder;
        private Button btnSetWorkingFolder;
        private Button btnExtractCuldceptSagaISO;
        private Button btnUnpackCuldceptSagaAssets;
        private Button btnCreateNewCuldceptSagaISO;
        private Button btnRepackCuldceptSagaAssets;
        private TabPage tabMenus;
        private Label lblMenuModelAnimations;
        private ListView lsMenuModelAnimations;
        private Label lblMenuModelTextures;
        private ListView lsMenuModelTextures;
        private Label lblMenuModels;
        private ListView lsMenuModels;
        private Button btnExportCuldceptSagaAssetsToGLB;
        private Button btnReplaceSelectedModel;
        private Button btnReplaceSelectedTexture;
        private Button btnExportCuldceptSagaAssetsToDAE;
        private Label lblModelFileName;
        private TextBox txtModelFileName;
        private TabPage tabEffects;
        private Label lblEffectModelAnimations;
        private ListView lsEffectModelAnimations;
        private Label lblEffectModelTextures;
        private ListView lsEffectModelTextures;
        private Label lblEffectsModels;
        private ListView lsEffectsModels;
    }
}
