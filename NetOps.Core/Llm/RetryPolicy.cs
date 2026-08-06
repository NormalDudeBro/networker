namespace NetOps.Core.Llm;

public static class RetryPolicy
{
    /// <summary>
    /// Executes an operation with exponential backoff between attempts. A per-attempt
    /// timeout is enforced via a linked CancellationTokenSource.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        int retryCount,
        TimeSpan baseDelay,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt <= retryCount; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (attempt > 0)
            {
                var delay = ExponentialBackoff(baseDelay, attempt);
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(timeout);

            try
            {
                return await operation(attemptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new LlmException(
                    $"Request timed out after {timeout.TotalSeconds:0} seconds.");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
        {
            throw new LlmException(lastError.Message, lastError)
            {
                Provider = lastError is LlmException llm ? llm.Provider : null,
            };
        }

        throw new LlmException("Operation failed without an underlying error.");
    }

    private static TimeSpan ExponentialBackoff(TimeSpan baseDelay, int attempt)
    {
        var multiplier = Math.Pow(2, attempt - 1);
        var cappedMs = Math.Min(baseDelay.TotalMilliseconds * multiplier, 30000);
        return TimeSpan.FromMilliseconds(cappedMs);
    }
}
