namespace Networker.Launcher;

internal sealed class FirstRunForm : Form
{
    public FirstRunForm(bool migrating)
    {
        Text = "Welcome to Networker";
        ClientSize = new Size(420, 132);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var label = new Label
        {
            Text = migrating ? "Networker will preserve your existing settings." : "Networker is ready to use.",
            AutoSize = true,
            Left = 24,
            Top = 24,
        };
        var start = new Button { Text = "Start Networker", AutoSize = true, Left = 286, Top = 92, DialogResult = DialogResult.OK };
        Controls.AddRange([label, start]);
        AcceptButton = start;
    }
}
