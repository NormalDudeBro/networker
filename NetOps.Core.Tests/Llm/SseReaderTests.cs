using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NetOps.Core.Llm;

namespace NetOps.Core.Tests.Llm;

public class SseReaderTests
{
    [Fact]
    public async Task ReadEvents_YieldsEventPayloadsAndSkipsComments()
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "data: {\"a\":1}\n\n" +
            "data: {\"b\":2}\n\n" +
            ": keep-alive comment\n\n"));

        var events = new List<string>();
        await foreach (var e in SseReader.ReadEvents(stream, CancellationToken.None))
        {
            events.Add(e);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal("{\"a\":1}", events[0]);
        Assert.Equal("{\"b\":2}", events[1]);
    }

    [Fact]
    public async Task ReadEvents_StopsAtDoneMarker()
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "data: {\"a\":1}\n\ndata: [DONE]\n\ndata: {\"ignored\":true}\n\n"));

        var events = new List<string>();
        await foreach (var e in SseReader.ReadEvents(stream, CancellationToken.None))
        {
            events.Add(e);
        }

        Assert.Single(events);
    }

    [Fact]
    public async Task ReadEvents_JoinsMultiLinePayloads()
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "data: line1\ndata: line2\n\n"));

        var events = new List<string>();
        await foreach (var e in SseReader.ReadEvents(stream, CancellationToken.None))
        {
            events.Add(e);
        }

        Assert.Single(events);
        Assert.Equal("line1\nline2", events[0]);
    }

    [Fact]
    public async Task ReadEvents_EmptyStream_YieldsNothing()
    {
        var events = new List<string>();
        await foreach (var e in SseReader.ReadEvents(new MemoryStream(), CancellationToken.None))
        {
            events.Add(e);
        }

        Assert.Empty(events);
    }
}
