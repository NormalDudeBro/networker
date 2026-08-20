using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace networker.Models
{
    /// <summary>Kinds of structured activity a turn can contain.</summary>
    public enum ActivityBlockKind
    {
        Thinking,
        Activity,
        Tool,
        Edit,
        Plan,
        Error,
    }

    /// <summary>
    /// Lifecycle of a work block, mirroring the reference tool-part state
    /// machine: pending → running → completed | error.
    /// </summary>
    public enum BlockState
    {
        Pending,
        Running,
        Completed,
        Error,
    }

    /// <summary>
    /// Base for every structured activity item inside an <see cref="AssistantTurn"/>.
    /// Long bodies collapse once complete (IsExpanded = false) unless the user
    /// pins them open.
    /// </summary>
    public abstract class ActivityBlock : INotifyPropertyChanged
    {
        public ActivityBlockKind Kind { get; init; }

        /// <summary>Correlates this block with the underlying tool/item id.</summary>
        public string? CallId { get; init; }

        private bool _isExpanded;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    OnExpandedChanged();
                }
            }
        }

        /// <summary>Hook for derived blocks that derive display text from IsExpanded.</summary>
        protected virtual void OnExpandedChanged()
        {
        }

        /// <summary>True once the user manually expanded/collapsed, so auto-collapse stops fighting them.</summary>
        public bool UserPinned { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Model reasoning. Shown in full while streaming; collapses to a short
    /// preview once complete (the reference TUI truncates thinking to 3 lines).
    /// </summary>
    public sealed class ThinkingBlock : ActivityBlock
    {
        public const int PreviewLineCount = 3;

        public ThinkingBlock() => Kind = ActivityBlockKind.Thinking;

        private string _content = string.Empty;

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreviewText));
                    OnPropertyChanged(nameof(DisplayText));
                    OnPropertyChanged(nameof(IsOverflow));
                    OnPropertyChanged(nameof(MoreLines));
                }
            }
        }

        private bool _isStreaming;

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (_isStreaming != value)
                {
                    _isStreaming = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PreviewText => Preview(Content, PreviewLineCount);

        public string DisplayText => string.IsNullOrWhiteSpace(Content)
            ? "Reasoning summary unavailable; follow the live agent activity below."
            : Content;

        public bool IsOverflow => LineCount(Content) > PreviewLineCount;

        public int MoreLines => Math.Max(0, LineCount(Content) - PreviewLineCount);

        /// <summary>"N more" whisper shown beside the collapsed header.</summary>
        public string MoreText => !IsExpanded && IsOverflow ? $"{MoreLines} more" : string.Empty;

        protected override void OnExpandedChanged() => OnPropertyChanged(nameof(MoreText));

        private static int LineCount(string value)
            => string.IsNullOrEmpty(value) ? 0 : value.Split('\n').Length;

        private static string Preview(string value, int lines)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string[] parts = value.Split('\n');
            return parts.Length <= lines ? value : string.Join('\n', parts, 0, lines) + "\n…";
        }
    }

    /// <summary>
    /// A tool or command execution. Verdict uses words (done / exit N / stopped /
    /// failed) and a duration, exactly like the reference tool rows. Long output
    /// collapses once complete.
    /// </summary>
    public sealed class ToolBlock : ActivityBlock
    {
        public const int OutputPreviewLines = 4;

        public ToolBlock() => Kind = ActivityBlockKind.Tool;

        public string? Action { get; init; }

        public string Glyph { get; init; } = "\uE713";

        public string? Detail { get; set; }

        /// <summary>
        /// The full command line as typed (e.g. "git status"), distinct from the
        /// short action label. Populated for terminal-style command blocks.
        /// </summary>
        public string? CommandLine { get; set; }

        public int? ExitCode { get; set; }

        /// <summary>
        /// True for live command blocks that render through the "$ cmd" terminal
        /// style rather than the generic tool row.
        /// </summary>
        public bool IsTerminalStyle { get; set; }

        /// <summary>Header caption shown by the terminal renderer: the command line.</summary>
        public string HeaderText => !string.IsNullOrWhiteSpace(CommandLine) ? CommandLine! : Detail ?? string.Empty;

        private BlockState _state = BlockState.Pending;

        public BlockState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(IsFailed));
                    OnPropertyChanged(nameof(VerdictDisplay));
                }
            }
        }

        public bool IsRunning => State is BlockState.Pending or BlockState.Running;
        public bool IsComplete => State == BlockState.Completed;
        public bool IsFailed => State == BlockState.Error;

        /// <summary>Verdict word: "done", "exit 2", "stopped", "failed".</summary>
        public string? Verdict { get; set; }

        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;

        public DateTimeOffset? EndedAt { get; set; }

        private string _output = string.Empty;

        public string Output
        {
            get => _output;
            set
            {
                if (_output != value)
                {
                    _output = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasOutput));
                    OnPropertyChanged(nameof(PreviewOutput));
                    OnPropertyChanged(nameof(IsOutputOverflow));
                    OnPropertyChanged(nameof(MoreOutputLines));
                }
            }
        }

        public bool HasOutput => _output.Length > 0;

        public string PreviewOutput => Preview(_output, OutputPreviewLines);

        public bool IsOutputOverflow => LineCount(_output) > OutputPreviewLines;

        public int MoreOutputLines => Math.Max(0, LineCount(_output) - OutputPreviewLines);

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
                    OnPropertyChanged(nameof(DurationDisplay));
                }
            }
        }

        public string DurationDisplay => _durationText.Length > 0 ? " · " + _durationText : string.Empty;

        public string VerdictDisplay
        {
            get
            {
                if (Verdict is not null) return " · " + Verdict + DurationDisplay;
                if (IsRunning) return DurationDisplay;
                return string.Empty;
            }
        }

        private static int LineCount(string value)
            => string.IsNullOrEmpty(value) ? 0 : value.Split('\n').Length;

        private static string Preview(string value, int lines)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string[] parts = value.Split('\n');
            return parts.Length <= lines ? value : string.Join('\n', parts, 0, lines) + "\n…";
        }
    }

    /// <summary>
    /// A file/configuration change. Shows the path, +N/−N counts, and a
    /// collapsible diff body.
    /// </summary>
    public sealed class EditBlock : ActivityBlock
    {
        public const int DiffPreviewLines = 6;

        public EditBlock() => Kind = ActivityBlockKind.Edit;

        public string? FilePath { get; set; }

        public int? Additions { get; set; }

        public int? Deletions { get; set; }

        public string AdditionsDisplay => Additions is > 0 ? $"+{Additions}" : string.Empty;

        public string DeletionsDisplay => Deletions is > 0 ? $"-{Deletions}" : string.Empty;

        public bool HasCounts => Additions is > 0 || Deletions is > 0;

        /// <summary>Combined +N/−M badge text, e.g. "+12 −4".</summary>
        public string CountsText
        {
            get
            {
                string left = Additions is > 0 ? $"+{Additions}" : string.Empty;
                string right = Deletions is > 0 ? $"-{Deletions}" : string.Empty;
                return left.Length > 0 && right.Length > 0 ? $"{left} {right}" : left + right;
            }
        }

        private string _diff = string.Empty;

        public string Diff
        {
            get => _diff;
            set
            {
                if (_diff != value)
                {
                    _diff = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasDiff));
                    OnPropertyChanged(nameof(PreviewDiff));
                    OnPropertyChanged(nameof(IsDiffOverflow));
                    OnPropertyChanged(nameof(MoreDiffLines));
                }
            }
        }

        public bool HasDiff => !string.IsNullOrWhiteSpace(_diff);

        public string PreviewDiff => Preview(_diff, DiffPreviewLines);

        public bool IsDiffOverflow => LineCount(_diff) > DiffPreviewLines;

        public int MoreDiffLines => Math.Max(0, LineCount(_diff) - DiffPreviewLines);

        private BlockState _state = BlockState.Running;

        public BlockState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                }
            }
        }

        private static int LineCount(string value)
            => string.IsNullOrEmpty(value) ? 0 : value.Split('\n').Length;

        private static string Preview(string value, int lines)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string[] parts = value.Split('\n');
            return parts.Length <= lines ? value : string.Join('\n', parts, 0, lines) + "\n…";
        }
    }

    /// <summary>Lifecycle of a single plan/todo item inside a <see cref="PlanBlock"/>.</summary>
    public enum PlanStatus
    {
        Pending,
        Running,
        Completed,
        Skipped,
        Failed,
    }

    /// <summary>
    /// One todo row. Rendered as a temperature list: running items are bold and
    /// accented, completed items sink to dim, pending items stay muted.
    /// </summary>
    public sealed class PlanItem : INotifyPropertyChanged
    {
        private string _title = string.Empty;

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        private PlanStatus _status = PlanStatus.Pending;

        public PlanStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRunning));
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(IsPending));
                    OnPropertyChanged(nameof(StatusGlyph));
                }
            }
        }

        public bool IsRunning => _status == PlanStatus.Running;
        public bool IsCompleted => _status == PlanStatus.Completed;
        public bool IsPending => _status == PlanStatus.Pending;

        /// <summary>Check / clock / dash / warning glyph per status.</summary>
        public string StatusGlyph => _status switch
        {
            PlanStatus.Completed => "\uE73E", // CheckMark
            PlanStatus.Running => "\uE823",   // Sync
            PlanStatus.Failed => "\uE711",    // Cancel
            PlanStatus.Skipped => "\uE74D",   // Delete
            _ => "\uE9A8",                    // CircleRing
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// An agent todo/plan list. The list accumulates in place: later plan events
    /// upsert rows by title so status changes stream through without duplicates.
    /// </summary>
    public sealed class PlanBlock : ActivityBlock
    {
        public PlanBlock() => Kind = ActivityBlockKind.Plan;

        public ObservableCollection<PlanItem> Items { get; } = new();

        private BlockState _state = BlockState.Running;

        public BlockState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Compact header summary, e.g. "2/5 done · 1 running".</summary>
        public string SummaryText
        {
            get
            {
                if (Items.Count == 0) return "plan…";
                int completed = Items.Count(item => item.Status == PlanStatus.Completed);
                int running = Items.Count(item => item.Status == PlanStatus.Running);
                string body = $"{completed}/{Items.Count} done";
                if (running > 0) body += $" · {running} running";
                return body;
            }
        }

        public bool IsRunning => _state == BlockState.Running;

        /// <summary>Adds or updates a row by title, preserving first-seen order.</summary>
        public void UpsertItem(string title, PlanStatus status)
        {
            PlanItem? existing = Items.FirstOrDefault(item => item.Title == title);
            if (existing is not null)
            {
                existing.Status = status;
            }
            else
            {
                Items.Add(new PlanItem { Title = title, Status = status });
            }
            OnPropertyChanged(nameof(SummaryText));
        }

        /// <summary>Replaces the whole list (orchestrator plan snapshots).</summary>
        public void SetItems(IEnumerable<PlanItem> items)
        {
            Items.Clear();
            foreach (PlanItem item in items) Items.Add(item);
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    /// <summary>
    /// Coalesced quiet activity (reads, lists, searches) — one dim line instead
    /// of a stack of rows, matching the reference "quiet tools" behavior.
    /// </summary>
    public sealed class ActivityLineBlock : ActivityBlock
    {
        public ActivityLineBlock() => Kind = ActivityBlockKind.Activity;

        public ObservableCollection<ToolBlock> Items { get; } = new();

        public string SummaryText
            => string.Join("   ", Items.Select(item => $"{item.Action} {item.Detail}".Trim()));

        public void AddItem(ToolBlock item)
        {
            Items.Add(item);
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    /// <summary>An error surfaced during the turn (protocol, tool, request).</summary>
    public sealed class ErrorBlock : ActivityBlock
    {
        public ErrorBlock() => Kind = ActivityBlockKind.Error;

        public string? Title { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}
