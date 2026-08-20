using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Networker.Core.Agent;
using Networker.Core.Llm;
using networker.Services.Codex;

namespace networker.Services
{
    public sealed class AgentService
    {
        private CancellationTokenSource? _activeRun;
        private Task<AgentResult>? _activeTask;
        private readonly object _sync = new();
        private readonly CodexAgentService? _codexAgent;
        private AgentOrchestrator? _orchestrator;
        private string? _orchestratorKey;
        public event Action<AgentActivity>? Activity;

        public AgentService()
        {
            try
            {
                _codexAgent = ((App)Application.Current).Services.GetService<CodexAgentService>();
                if (_codexAgent is not null)
                    _codexAgent.Activity += item => Activity?.Invoke(item);
            }
            catch
            {
                _codexAgent = null;
            }
        }

        public AgentService(CodexAgentService? codexAgent)
        {
            _codexAgent = codexAgent;
            if (_codexAgent is not null)
                _codexAgent.Activity += item => Activity?.Invoke(item);
        }

        public async Task<AgentResult> RunAsync(string goal, string? clientContext = null, CancellationToken cancellationToken = default)
        {
            if (LlmConfig.ParseProvider(AppSettings.SelectedProvider) == LlmProviderKind.Codex)
            {
                if (_codexAgent is null)
                    throw new InvalidOperationException("OpenAI Codex is not available.");
                return await _codexAgent.RunAsync(goal, clientContext, cancellationToken).ConfigureAwait(false);
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            AgentOrchestrator orchestrator;
            lock (_sync)
            {
                if (_activeRun is not null) throw new InvalidOperationException("An agent run is already active.");
                _activeRun = linked;
                string key = AppSettings.SelectedProvider + "\n" + AppSettings.SelectedModel;
                if (_orchestrator is null || !string.Equals(_orchestratorKey, key, StringComparison.Ordinal))
                {
                    _orchestrator = new AgentOrchestrator((messages, token) => LlmRuntime.Router.CompleteAsync(messages, token));
                    _orchestrator.Activity += item => Activity?.Invoke(item);
                    _orchestratorKey = key;
                }
                orchestrator = _orchestrator;
            }
            Task<AgentResult> task = orchestrator.RunAsync(goal, clientContext, linked.Token);
            lock (_sync) _activeTask = task;
            try { return await task; }
            finally { lock (_sync) { _activeRun = null; _activeTask = null; } }
        }

        public void Stop()
        {
            lock (_sync) _activeRun?.Cancel();
            _codexAgent?.Stop();
        }

        public void ResetConversation()
        {
            Stop();
            lock (_sync)
            {
                _orchestrator = null;
                _orchestratorKey = null;
            }
            _codexAgent?.ResetConversation();
        }
    }
}
