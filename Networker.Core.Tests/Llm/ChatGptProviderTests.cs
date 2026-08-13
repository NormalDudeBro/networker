using Networker.Core.Llm;
using Networker.Core.Llm.ChatGpt;

namespace Networker.Core.Tests.Llm;

public sealed class ChatGptProviderTests
{
    [Fact]
    public async Task HealthAndModels_ReflectBrowserStatusAndCapabilities()
    {
        var transport = new FakeTransport(new ChatGptStatus(
            ChatGptSessionState.Ready,
            "Ready",
            new[] { new LlmModelInfo { Id = "auto" } },
            LlmProviderCapabilities.Streaming | LlmProviderCapabilities.Models | LlmProviderCapabilities.WebSearch));
        var provider = new ChatGptProvider(new LlmConfig(), transport);

        Assert.True(await provider.HealthCheckAsync());
        Assert.Equal("auto", Assert.Single(await provider.ListModelsAsync()).Id);
        Assert.True(provider.Capabilities.HasFlag(LlmProviderCapabilities.WebSearch));
    }

    [Fact]
    public async Task Models_ThrowsSignedOutStatusWithoutCredentialMaterial()
    {
        var provider = new ChatGptProvider(new LlmConfig(), new FakeTransport(new ChatGptStatus(
            ChatGptSessionState.SignedOut, "Sign in to ChatGPT.", Array.Empty<LlmModelInfo>(), LlmProviderCapabilities.None)));

        LlmException error = await Assert.ThrowsAsync<LlmException>(() => provider.ListModelsAsync());
        Assert.Equal("Sign in to ChatGPT.", error.Message);
    }

    private sealed class FakeTransport(ChatGptStatus status) : IChatGptTransport
    {
        public Task<ChatGptStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(status);
        public Task<LlmResponse> CompleteAsync(ChatGptTurnRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<string> StreamAsync(ChatGptTurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; }
        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
