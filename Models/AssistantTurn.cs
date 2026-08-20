using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace networker.Models
{
    public enum TurnState
    {
        Running,
        Completed,
        Failed,
        Cancelled,
    }

    /// <summary>
    /// One assistant interaction. Structured activity lives in <see cref="Blocks"/>
    /// (thinking, tools, edits, coalesced activity), while the conclusion streams
    /// into <see cref="Text"/>. This mirrors the reference model where an
    /// assistant message is an ordered sequence of typed parts followed by the
    /// final answer.
    /// </summary>
    public sealed class AssistantTurn : INotifyPropertyChanged
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public DateTime Timestamp { get; init; } = DateTime.Now;

        public string? Provider { get; set; }

        public string? Model { get; set; }

        /// <summary>True when this turn came from an agent run rather than chat mode.</summary>
        public ObservableCollection<ActivityBlock> Blocks { get; } = new();

        private TurnState _state = TurnState.Running;

        public TurnState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsStreaming));
                    OnPropertyChanged(nameof(StateVerb));
                }
            }
        }

        public bool IsStreaming => _state == TurnState.Running;

        public string StateVerb => _state switch
        {
            TurnState.Completed => "Completed",
            TurnState.Failed => "Failed",
            TurnState.Cancelled => "Cancelled",
            _ => "Working",
        };

        private string _text = string.Empty;

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasText));
                }
            }
        }

        public bool HasText => _text.Length > 0;

        public bool HasBlocks => Blocks.Count > 0;

        public DateTime StartedAt { get; init; } = DateTime.Now;

        public DateTimeOffset? EndedAt { get; set; }

        private string _durationText = string.Empty;

        public string DurationText
        {
            get => _durationText;
            set
            {
                if (_durationText != value)
                {
                    _durationText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Footer summary line: state verb · provider model · duration.</summary>
        public string FooterText
        {
            get
            {
                string body = StateVerb;
                string model = string.Join(" ", new[] { Provider, Model }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (model.Length > 0) body += " · " + model;
                if (_durationText.Length > 0) body += " · " + _durationText;
                return body;
            }
        }

        /// <summary>
        /// The "one live line" — a short sentence describing the current in-flight
        /// work, derived from the newest running block.
        /// </summary>
        public string BusyText
        {
            get
            {
                for (int index = Blocks.Count - 1; index >= 0; index--)
                {
                    switch (Blocks[index])
                    {
                        case ThinkingBlock:
                            return "thinking…";
                        case PlanBlock:
                            return "planning…";
                        case ToolBlock tool when tool.IsRunning:
                            return $"{tool.Action} {tool.Detail}".Trim();
                        case EditBlock:
                            return "editing files…";
                    }
                }
                return "working…";
            }
        }

        public bool IsBusyActivity
            => _state == TurnState.Running
               && Blocks.Any(block => block is ToolBlock tool && tool.IsRunning);

        /// <summary>
        /// True while streaming and there is no conclusion text yet and no live
        /// activity line to show — the honest "Generating…" placeholder.
        /// </summary>
        public bool ShowGeneratingIndicator
            => _state == TurnState.Running && !HasText && !IsBusyActivity;

        /// <summary>Raises changed notifications for all derived status text.</summary>
        public void RefreshStatus()
        {
            OnPropertyChanged(nameof(BusyText));
            OnPropertyChanged(nameof(IsBusyActivity));
            OnPropertyChanged(nameof(ShowGeneratingIndicator));
            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(HasBlocks));
            OnPropertyChanged(nameof(HasText));
            OnPropertyChanged(nameof(IsStreaming));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
