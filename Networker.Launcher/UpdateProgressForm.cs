namespace Networker.Launcher;

internal sealed class UpdateProgressForm : Form
{
    private readonly Label _status = new() { AutoSize = true, Text = "Updating Networker...", Left = 24, Top = 24 };
    private readonly ProgressBar _progress = new() { Left = 24, Top = 58, Width = 392, Height = 16 };
    private readonly Button _continue = new() { Text = "Continue using current version", AutoSize = true, Left = 224, Top = 92 };
    private readonly CancellationTokenSource _cancellation = new();

    public UpdateProgressForm()
    {
        Text = "Networker Update";
        ClientSize = new Size(440, 136);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.AddRange([_status, _progress, _continue]);
        _continue.Click += (_, _) => { _cancellation.Cancel(); DialogResult = DialogResult.Cancel; Close(); };
        FormClosing += (_, _) => _cancellation.Cancel();
    }

    public CancellationToken CancellationToken => _cancellation.Token;
    public IProgress<int> Progress => new Progress<int>(value => _progress.Value = Math.Clamp(value, 0, 100));
    public void SetStatus(string value) => _status.Text = value;
}
