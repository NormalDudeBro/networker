namespace Networker.Launcher;

internal sealed class InstalledVersionForm : Form
{
    public InstalledVersionForm(string version)
    {
        Text = "Networker updated";
        ClientSize = new Size(320, 84);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(new Label { Text = $"Networker {version} installed.", AutoSize = true, Left = 24, Top = 28 });
        var timer = new System.Windows.Forms.Timer { Interval = 2200 };
        timer.Tick += (_, _) => { timer.Stop(); Close(); timer.Dispose(); };
        Shown += (_, _) => timer.Start();
    }
}
