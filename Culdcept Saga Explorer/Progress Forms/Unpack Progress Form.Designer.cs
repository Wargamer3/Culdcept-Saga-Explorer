namespace CuldceptSagaExplorer
{
    partial class UnpackProgressForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            pbExtractionProgress = new System.Windows.Forms.ProgressBar();
            lblExtractionProgress = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // pbExtractionProgress
            // 
            pbExtractionProgress.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            pbExtractionProgress.Location = new System.Drawing.Point(12, 27);
            pbExtractionProgress.Name = "pbExtractionProgress";
            pbExtractionProgress.Size = new System.Drawing.Size(521, 23);
            pbExtractionProgress.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            pbExtractionProgress.TabIndex = 0;
            // 
            // lblExtractionProgress
            // 
            lblExtractionProgress.Location = new System.Drawing.Point(12, 9);
            lblExtractionProgress.Name = "lblExtractionProgress";
            lblExtractionProgress.Size = new System.Drawing.Size(521, 15);
            lblExtractionProgress.TabIndex = 1;
            lblExtractionProgress.Text = "Extraction in progress";
            // 
            // xisoProgressForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(545, 62);
            ControlBox = false;
            Controls.Add(lblExtractionProgress);
            Controls.Add(pbExtractionProgress);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "xisoProgressForm";
            SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Load += ExtractionProgressForm_Load;
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.ProgressBar pbExtractionProgress;
        private System.Windows.Forms.Label lblExtractionProgress;
    }
}