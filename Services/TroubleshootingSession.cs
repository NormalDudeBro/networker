using System;
using System.Threading;
using Networker.Core.Workflow;

namespace networker.Services
{
    /// <summary>Owns the single recoverable troubleshooting workspace for this app instance.</summary>
    public sealed class TroubleshootingSession : IDisposable
    {
        private readonly TroubleshootingWorkspaceStore _store;
        private readonly object _gate = new();
        private Timer? _saveTimer;

        public TroubleshootingSession(TroubleshootingWorkspaceStore store)
        {
            _store = store;
            var result = store.Load();
            Current = result.Workspace;
            RestoreWarning = result.Warning;
        }

        public TroubleshootingWorkspace Current { get; private set; }
        public string? RestoreWarning { get; private set; }
        public event Action? Changed;

        public void SelectStage(WorkflowStage stage)
        {
            if (Current.SelectedStage == stage) return;
            Current.SelectedStage = stage;
            Current.UpdatedAt = DateTimeOffset.UtcNow;
            QueueSave();
            Changed?.Invoke();
        }

        public void SetCompleted(WorkflowStage stage, string message)
            => SetProgress(stage, WorkflowProgressState.Completed, message);

        public void SetError(WorkflowStage stage, string message)
            => SetProgress(stage, WorkflowProgressState.Error, message);

        public void SetAvailable(WorkflowStage stage, string? message = null)
            => SetProgress(stage, WorkflowProgressState.Available, message);

        public void NotifyChanged(bool saveImmediately = false)
        {
            Current.UpdatedAt = DateTimeOffset.UtcNow;
            if (saveImmediately) SaveNow(); else QueueSave();
            Changed?.Invoke();
        }

        public void QueueSave()
        {
            lock (_gate)
            {
                _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(500, Timeout.Infinite);
            }
        }

        public void SaveNow()
        {
            lock (_gate)
            {
                _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _store.Save(Current);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _store.Clear();
                Current = TroubleshootingWorkspace.CreateEmpty();
                RestoreWarning = null;
            }
            Changed?.Invoke();
        }

        private void SetProgress(WorkflowStage stage, WorkflowProgressState state, string? message)
        {
            var progress = Current.GetProgress(stage);
            progress.State = state;
            progress.Message = message ?? string.Empty;
            progress.UpdatedAt = DateTimeOffset.UtcNow;
            NotifyChanged(saveImmediately: true);
        }

        public void Dispose()
        {
            SaveNow();
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
    }
}
