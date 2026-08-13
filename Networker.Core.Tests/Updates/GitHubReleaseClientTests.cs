using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Networker.Core.Updates;

namespace Networker.Core.Tests.Updates;

public class GitHubReleaseClientTests
{
    private const string StableUrl = "https://api.github.com/repos/NormalDudeBro/networker/releases/latest";
    private const string PreviewUrl = "https://api.github.com/repos/NormalDudeBro/networker/releases?per_page=100";
    private const string TagUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.3";

    private readonly TestInstalledVersionProvider _installed = new(UpdateTestFakes.Dev());
    private readonly TestLog _log = new();

    private static GitHubReleaseDto DefaultStableDto() => new()
    {
        TagName = "v1.2.3",
        Name = "Networker 1.2.3",
        Body = "notes",
        PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        HtmlUrl = TagUrl,
        Draft = false,
        Prerelease = false,
        Assets = new List<GitHubAssetDto>
        {
            new() { Name = "Networker-1.2.3-win-x64.msix", Size = 5_000_000, BrowserDownloadUrl = "https://github.com/NormalDudeBro/networker/releases/download/v1.2.3/Networker-1.2.3-win-x64.msix" },
            new() { Name = "Networker-1.2.3-win-x64.msix.sha256", Size = 66, BrowserDownloadUrl = "https://github.com/NormalDudeBro/networker/releases/download/v1.2.3/Networker-1.2.3-win-x64.msix.sha256" },
        },
    };

    private static string Json(GitHubReleaseDto dto)
        => JsonSerializer.Serialize(dto, GitHubReleaseJsonContext.Default.GitHubReleaseDto);

    private static string Json(List<GitHubReleaseDto> list)
        => JsonSerializer.Serialize(list, GitHubReleaseJsonContext.Default.ListGitHubReleaseDto);

    private static void AddEtag(HttpResponseMessage response, string tag)
        => response.Headers.ETag = new EntityTagHeaderValue($"\"{tag}\"");

    private GitHubReleaseClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(AsyncStubHttpMessageHandler.Client(handler), _installed, _log);

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        => (request, _) => Task.FromResult(respond(request));

    [Fact]
    public async Task CheckAsync_Stable_MapsValidRelease()
    {
        var client = Client(Respond(_ =>
        {
            var response = AsyncStubHttpMessageHandler.Json(Json(DefaultStableDto()));
            AddEtag(response, "etag1");
            return response;
        }));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.False(result.NotModified);
        Assert.Equal("\"etag1\"", result.NextETag);
        Assert.NotNull(result.Release);
        Assert.Equal("v1.2.3", result.Release!.TagName);
        Assert.False(result.Release.IsPrerelease);
        Assert.Equal(2, result.Release.Assets.Count);
        Assert.Equal("Networker-1.2.3-win-x64.msix", result.Release.Assets[0].Name);
        Assert.Equal(5_000_000, result.Release.Assets[0].Size);
    }

    [Fact]
    public async Task CheckAsync_Stable_SendsHeaders()
    {
        HttpRequestMessage? captured = null;
        var client = Client(Respond(request =>
        {
            captured = request;
            return AsyncStubHttpMessageHandler.Json(Json(DefaultStableDto()));
        }));

        await client.CheckAsync(UpdateChannel.Stable, "old-etag", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(StableUrl, captured!.RequestUri!.AbsoluteUri);
        Assert.True(captured.Headers.TryGetValues("If-None-Match", out var ifNoneMatch));
        Assert.Equal("old-etag", Assert.Single(ifNoneMatch));
        Assert.Equal("application/vnd.github+json", captured.Headers.Accept.Single().MediaType);
        Assert.True(captured.Headers.TryGetValues("User-Agent", out var userAgents));
        Assert.Equal("Networker/1.0.0-dev (+https://github.com/NormalDudeBro/networker)", string.Join(" ", userAgents));
        Assert.True(captured.Headers.TryGetValues("X-GitHub-Api-Version", out var apiVersion));
        Assert.Equal("2022-11-28", Assert.Single(apiVersion));
    }

    [Fact]
    public async Task CheckAsync_Preview_UsesListPathAndPicksHighestEligible()
    {
        var list = new List<GitHubReleaseDto>
        {
            DefaultStableDto() with { TagName = "v1.2.2", HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.2" },
            DefaultStableDto() with { TagName = "v1.2.3-preview.1", HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.3-preview.1", Prerelease = true },
            DefaultStableDto() with { TagName = "v1.2.3-preview.2", HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.3-preview.2", Prerelease = true },
        };

        var client = Client(Respond(request =>
        {
            Assert.Equal(PreviewUrl, request.RequestUri!.AbsoluteUri);
            return AsyncStubHttpMessageHandler.Json(Json(list));
        }));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Preview, null, CancellationToken.None);

        Assert.Equal("v1.2.3-preview.2", result.Release!.TagName);
        Assert.True(result.Release.IsPrerelease);
    }

    [Theory]
    [InlineData(UpdateChannel.Stable)]
    [InlineData(UpdateChannel.Preview)]
    public async Task CheckAsync_NotModified_ReturnsFlag(UpdateChannel channel)
    {
        var client = Client(Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            AddEtag(response, "etag9");
            return response;
        }));

        ReleaseCheckResult result = await client.CheckAsync(channel, "etag9", CancellationToken.None);

        Assert.True(result.NotModified);
        Assert.Null(result.Release);
        Assert.Equal("\"etag9\"", result.NextETag);
    }

    [Theory]
    [InlineData(UpdateChannel.Stable)]
    [InlineData(UpdateChannel.Preview)]
    public async Task CheckAsync_NotFound_ReturnsNoRelease(UpdateChannel channel)
    {
        var client = Client(Respond(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        ReleaseCheckResult result = await client.CheckAsync(channel, null, CancellationToken.None);

        Assert.False(result.NotModified);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_EmptyPreview_ReturnsNoRelease()
    {
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json("[]")));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Preview, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_RateLimited_WithRetryAfterDelta()
    {
        var client = Client(Respond(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return response;
        }));

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));

        Assert.NotNull(ex.RetryAfterUtc);
        Assert.InRange(ex.RetryAfterUtc!.Value - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(110), TimeSpan.FromSeconds(130));
    }

    [Fact]
    public async Task CheckAsync_RateLimited_WithUnixResetHeader()
    {
        long unix = DateTimeOffset.UtcNow.AddMinutes(7).ToUnixTimeSeconds();
        var client = Client(Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", unix.ToString());
            return response;
        }));

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));

        Assert.NotNull(ex.RetryAfterUtc);
        Assert.InRange(ex.RetryAfterUtc!.Value, DateTimeOffset.FromUnixTimeSeconds(unix - 5), DateTimeOffset.FromUnixTimeSeconds(unix + 5));
    }

    [Fact]
    public async Task CheckAsync_RateLimited_WithoutAnyHeader()
    {
        var client = Client(Respond(_ => new HttpResponseMessage((HttpStatusCode)429)));

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));

        Assert.Null(ex.RetryAfterUtc);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task CheckAsync_UnexpectedStatus_Throws(int status)
    {
        var client = Client(Respond(_ => new HttpResponseMessage((HttpStatusCode)status)));

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));

        Assert.Contains(status.ToString(), ex.Message);
        Assert.Null(ex.RetryAfterUtc);
    }

    [Fact]
    public async Task CheckAsync_InvalidJson_Throws()
    {
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json("{not json")));

        await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_NetworkFailure_Throws()
    {
        var client = Client((_, _) => throw new HttpRequestException("boom"));

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task CheckAsync_HttpClientTimeout_Throws()
    {
        var client = new GitHubReleaseClient(
            new HttpClient(new AsyncStubHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                throw new OperationCanceledException(ct);
            }))
            {
                BaseAddress = new Uri("https://api.github.com"),
                Timeout = TimeSpan.FromMilliseconds(150),
            },
            _installed,
            _log);

        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));

        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task CheckAsync_WithoutBaseAddress_Throws()
    {
        var client = new GitHubReleaseClient(new HttpClient(), _installed, _log);
        var ex = await Assert.ThrowsAsync<UpdateException>(
            () => client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None));
        Assert.Contains("no base address", ex.Message);
    }

    [Fact]
    public async Task CheckAsync_Stable_SkipsDraft()
    {
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(DefaultStableDto() with { Draft = true }))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_Stable_SkipsInvalidTag()
    {
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(DefaultStableDto() with { TagName = "v1.2.3.4" }))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_Stable_SkipsPrereleaseFlagMismatch()
    {
        // Tag is a preview but the GitHub prerelease flag says stable.
        var dto = DefaultStableDto() with
        {
            TagName = "v1.2.3-preview.1",
            HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.3-preview.1",
            Prerelease = false,
        };
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(dto))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_Stable_SkipsPreviewOnStableChannel()
    {
        var dto = DefaultStableDto() with
        {
            TagName = "v1.2.3-preview.1",
            HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.3-preview.1",
            Prerelease = true,
        };
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(dto))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_SkipsWrongHtmlUrl()
    {
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(DefaultStableDto() with { HtmlUrl = "https://github.com/evil/networker/releases/tag/v1.2.3" }))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_SkipsMissingPublishedAt()
    {
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(DefaultStableDto() with { PublishedAt = null }))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Stable, null, CancellationToken.None);

        Assert.Null(result.Release);
    }

    [Fact]
    public async Task CheckAsync_Preview_SkipsDraftAndInvalid_StillPicksBest()
    {
        var list = new List<GitHubReleaseDto>
        {
            DefaultStableDto() with { TagName = "v1.2.3-preview.1", HtmlUrl = "https://github.com/NormalDudeBro/networker/releases/tag/v1.2.3-preview.1", Prerelease = true },
            DefaultStableDto() with { Draft = true },
            DefaultStableDto() with { TagName = "bad-tag" },
        };
        var client = Client(Respond(_ => AsyncStubHttpMessageHandler.Json(Json(list))));

        ReleaseCheckResult result = await client.CheckAsync(UpdateChannel.Preview, null, CancellationToken.None);

        Assert.Equal("v1.2.3-preview.1", result.Release!.TagName);
    }
}
