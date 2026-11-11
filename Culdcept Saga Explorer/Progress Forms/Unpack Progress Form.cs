using CuldceptSagaExplorer.AssetManager;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CuldceptSagaExplorer
{
    public partial class UnpackProgressForm : Form
    {
        private readonly string DataDirectory;
        private readonly string UnpackingDirectory;

        public UnpackProgressForm(string DataDirectory, string UnpackingDirectory)
        {
            InitializeComponent();

            this.DataDirectory = DataDirectory;
            this.UnpackingDirectory = UnpackingDirectory;
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

        private void ProgressHandler(int Current, int Total, string Message)
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
                // Subscribe to progress event
                RspUnpacker.OnProgress += ProgressHandler;

                // Offload the synchronous depacking operation to a background thread
                int Total = await Task.Run(() => RspUnpacker.DepackAllRspFiles(DataDirectory, UnpackingDirectory));

                Debug.WriteLine($"unpacked {Total} files");
            }
            finally
            {
                // Unsubscribe to avoid memory leaks or duplicate event handlers
                RspUnpacker.OnProgress -= ProgressHandler;
            }
        }
    }
}
