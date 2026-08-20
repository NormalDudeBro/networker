using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Networker.Core.Text;
using Networker.Core.Terminal;
using Windows.System;

namespace networker.Controls;

/// <summary>
/// The interactive terminal panel: a dark, mono-spaced scrollback view over a
/// <see cref="TerminalSession"/>. Commands are typed in the bottom row and echoed
/// locally before being sent (the shell cannot echo pipe-fed input itself).
/// </summary>
public sealed partial class TerminalPane : UserControl, INotifyPropertyChanged
{
    private readonly StringBuilder _output = new();
    private TerminalSession? _session;

    public TerminalPane()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks the close button; the host decides visibility.</summary>
    public event EventHandler? CloseRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Human-readable session status for the header.</summary>
    public string SessionStatus { get; private set; } = "stopped";

    public Visibility SessionRunningVisibility { get; private set; } = Visibility.Collapsed;

    public TerminalSession? Session
    {
        get => _session;
        set
        {
            if (_session is not null)
            {
                _session.OutputReceived -= OnOutputReceived;
                _session.ProcessExited -= OnProcessExited;
            }
            _session = value;
            if (_session is not null)
            {
                _session.OutputReceived += OnOutputReceived;
                _session.ProcessExited += OnProcessExited;
            }
            UpdateStatus();
        }
    }

    /// <summary>Starts the session if it is not already running.</summary>
    public void EnsureStarted()
    {
        if (_session is null || _session.IsRunning) return;
        try
        {
            _session.Start();
        }
        catch (Exception ex)
        {
            AppendLine($"failed to start terminal: {ex.Message}");
        }
        UpdateStatus();
    }

    /// <summary>Echoes and sends one command line to the running shell.</summary>
    public void RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        AppendLine("$ " + command);
        _session?.WriteLine(command);
    }

    /// <summary>Clears the scrollback (UI only; the live session keeps running).</summary>
    public void ClearOutput()
    {
        _output.Clear();
        OutputText.Text = string.Empty;
    }

    private void OnOutputReceived(string chunk)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            string stripped = AnsiStripper.Strip(chunk).Replace("\r\n", "\n", StringComparison.Ordinal);
            if (stripped.Length > 0) AppendLine(stripped);
        });
    }

    private void OnProcessExited()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AppendLine("session exited");
            UpdateStatus();
        });
    }

    private void AppendLine(string text)
    {
        _output.Append(text);
        OutputText.Text = _output.ToString();
        OutputScroller.ChangeView(null, double.MaxValue, null, disableAnimation: true);
    }

    private void UpdateStatus()
    {
        bool running = _session is not null && _session.IsRunning;
        SessionStatus = running ? "running" : "stopped";
        SessionRunningVisibility = running ? Visibility.Visible : Visibility.Collapsed;
        RaisePropertyChanged(nameof(SessionStatus));
        RaisePropertyChanged(nameof(SessionRunningVisibility));
        Bindings.Update();
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        string command = InputBox.Text.TrimEnd();
        if (command.Length > 0) RunCommand(command);
        InputBox.Text = string.Empty;
        e.Handled = true;
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try
        {
            _session.Restart();
        }
        catch (Exception ex)
        {
            AppendLine($"failed to restart terminal: {ex.Message}");
        }
        UpdateStatus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
