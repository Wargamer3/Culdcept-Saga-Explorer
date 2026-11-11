using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CuldceptSagaExplorer
{
    public partial class XisoProgressForm : Form
    {
        private readonly string ExtractorPath;
        private readonly string WorkingFolder;
        private readonly string Arguments;

        public XisoProgressForm(string ExtractorPath, string WorkingFolder, string Arguments)
        {
            InitializeComponent();

            this.ExtractorPath = ExtractorPath;
            this.WorkingFolder = WorkingFolder;
            this.Arguments = Arguments;
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

        private async Task RunExtractionAsync()
        {
            ProcessStartInfo StartInfo = new ProcessStartInfo
            {
                FileName = ExtractorPath,
                Arguments = Arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = WorkingFolder ?? string.Empty
            };

            using Process XisoProcess = new Process { StartInfo = StartInfo };

            // Real-time UI updates from OutputDataReceived
            XisoProcess.OutputDataReceived += (Sender, DataReceivedArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(DataReceivedArgs.Data))
                {
                    // Marshal updates back to the WinForms UI thread
                    this.BeginInvoke(new Action(() =>
                    {
                        lblExtractionProgress.Text = DataReceivedArgs.Data;
                    }));
                }
            };
            XisoProcess.ErrorDataReceived += (Sender, DataReceivedArgs) =>
            {
                if (DataReceivedArgs.Data != null)
                {
                    Debug.WriteLine($"[extract-xiso] {DataReceivedArgs.Data}");
                    Console.Error.WriteLine($"[extract-xiso ERROR] {DataReceivedArgs.Data}");
                }
            };

            XisoProcess.Start();

            // Begin async reading of streams
            XisoProcess.BeginOutputReadLine();
            XisoProcess.BeginErrorReadLine();

            await XisoProcess.WaitForExitAsync();

            if (XisoProcess.ExitCode != 0)
            {
                throw new Exception($"extract-xiso failed with exit code {XisoProcess.ExitCode}.");
            }
        }
    }
}
