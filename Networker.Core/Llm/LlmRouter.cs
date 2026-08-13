using System.Net.Http;

namespace Networker.Core.Llm;

public sealed class LlmRouterStatusChangedEventArgs : EventArgs
{
    public required string Provider { get; init; }
    public required string Message { get; init; }
    public bool IsError { get; init; }
}

/// <summary>
/// Routes requests across a chain of LLM providers with retry and fallback.
/// The primary provider is tried first (with exponential backoff retries);
/// on persistent failure the next provider in the chain is tried, and so on.
/// </summary>
public sealed class LlmRouter
{
    private readonly LlmConfig _config;
    private List<ILlmProvider> _chain;
    private CancellationTokenSource _globalCancel = new();

    public LlmRouter(LlmConfig config, HttpClient http, IReadOnlyList<LlmProviderKind>? orderOverride = null)
    {
        _config = config;
        var order = orderOverride ?? BuildOrder(config);
        _chain = order.Select(kind => LlmProviderFactory.Create(kind, config, http)).ToList();
    }

    public LlmRouter(LlmConfig config, IReadOnlyList<ILlmProvider> providers)
    {
        _config = config;
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one LLM provider is required.", nameof(providers));
        }

        _chain = providers.ToList();
    }

    public LlmConfig Config => _config;

    public event EventHandler<LlmRouterStatusChangedEventArgs>? StatusChanged;

    public IReadOnlyList<ILlmProvider> Providers => _chain;

    public ILlmProvider Primary => _chain[0];

    /// <summary>
    /// Moves a provider to the front of the chain so subsequent requests try it
    /// first. Other providers keep their relative order as fallbacks.
    /// </summary>
    public void SetPrimary(LlmProviderKind kind)
    {
        var provider = _chain.FirstOrDefault(p => p.Kind == kind);
        if (provider is null)
        {
            return;
        }

        var reordered = new List<ILlmProvider>(_chain.Count) { provider };
        reordered.AddRange(_chain.Where(p => !ReferenceEquals(p, provider)));
        _chain = reordered;
    }

    public void Cancel()
    {
        var previous = _globalCancel;
        _globalCancel = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
    }

    public Task<LlmResponse> CompleteAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _globalCancel.Token);
        return CompleteCoreAsync(messages, linked.Token);
    }

    public IAsyncEnumerable<string> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        return StreamWithCancellationAsync(messages, cancellationToken);
    }

    private async IAsyncEnumerable<string> StreamWithCancellationAsync(
        IReadOnlyList<LlmMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _globalCancel.Token);
        await foreach (string delta in StreamCoreAsync(messages, linked.Token).ConfigureAwait(false))
        {
            yield return delta;
        }
    }

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        Exception? last = null;
        foreach (var provider in _chain)
        {
            try
            {
                var models = await provider.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                if (models.Count > 0)
                {
                    return models;
                }
            }
            catch (Exception ex)
            {
                last = ex;
                Emit(provider.Name, $"List models failed: {ex.Message}", isError: true);
            }
        }

        throw new LlmException(
            "Could not list models from any provider: " + (last?.Message ?? "No provider returned models."),
            last ?? new LlmException("No provider returned models."));
    }

    public async Task<IReadOnlyList<LlmProviderStatus>> HealthCheckAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<LlmProviderStatus>(_chain.Count);
        foreach (var provider in _chain)
        {
            try
            {
                var ok = await provider.HealthCheckAsync(cancellationToken).ConfigureAwait(false);
                results.Add(new LlmProviderStatus
                {
                    Kind = provider.Kind,
                    Provider = provider.Name,
                    IsAvailable = ok,
                    Model = provider.Model,
                });
            }
            catch (Exception ex)
            {
                results.Add(new LlmProviderStatus
                {
                    Kind = provider.Kind,
                    Provider = provider.Name,
                    IsAvailable = false,
                    Model = provider.Model,
                    Message = ex.Message,
                });
            }
        }

        return results;
    }

    private async Task<LlmResponse> CompleteCoreAsync(IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var provider in _chain)
        {
            try
            {
                var response = await RetryPolicy.ExecuteAsync(
                    (ct) => provider.CompleteAsync(messages, ct),
                    _config.RetryCount,
                    _config.BaseRetryDelay,
                    _config.Timeout,
                    cancellationToken).ConfigureAwait(false);

                Emit(provider.Name, "Responded", isError: false);
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Emit(provider.Name, ex.Message, isError: true);
                if (ex is LlmException { MayHaveSubmittedRequest: true })
                {
                    throw;
                }
            }
        }

        throw new LlmException(
            "All providers failed. " + (lastError?.Message ?? "No providers available."),
            lastError ?? new LlmException("All providers failed."));
    }

    private async IAsyncEnumerable<string> StreamCoreAsync(
        IReadOnlyList<LlmMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var provider in _chain)
        {
            var streamed = false;
            await using var enumerator = provider.StreamAsync(messages, cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                string delta;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    delta = enumerator.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastError = ex;
                    Emit(provider.Name, ex.Message, isError: true);
                    if (ex is LlmException { MayHaveSubmittedRequest: true })
                    {
                        throw;
                    }
                    break;
                }

                streamed = true;
                yield return delta;
            }

            if (streamed)
            {
                Emit(provider.Name, "Responded", isError: false);
                yield break;
            }
        }

        throw new LlmException(
            "All providers failed. " + (lastError?.Message ?? "No providers available."),
            lastError ?? new LlmException("All providers failed."));
    }

    private static IReadOnlyList<LlmProviderKind> BuildOrder(LlmConfig config)
    {
        var order = new List<LlmProviderKind> { config.Provider };
        foreach (var fallback in config.FallbackChain)
        {
            if (!order.Contains(fallback))
            {
                order.Add(fallback);
            }
        }

        return order;
    }

    private void Emit(string provider, string message, bool isError)
    {
        StatusChanged?.Invoke(this, new LlmRouterStatusChangedEventArgs
        {
            Provider = provider,
            Message = message,
            IsError = isError,
        });
    }
}

