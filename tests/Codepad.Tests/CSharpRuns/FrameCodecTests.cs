using System.Buffers.Binary;
using System.Text;
using Codepad.Contracts;
using Codepad.Domain.Operations;

namespace Codepad.Tests.CSharpRuns;

public sealed class FrameCodecTests
{
    private static readonly WorkerRunRequest Sample = new WorkerRunRequest(
        WorkerProtocol.Version, Guid.NewGuid(), 7, "1 + 1", ResultPolicy.Default, 100);

    [Fact]
    public async Task WrittenMessageReadsBackEqual()
    {
        using MemoryStream stream = new MemoryStream();

        await FrameCodec.WriteAsync(stream, Sample, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken);
        stream.Position = 0;
        WorkerRunRequest? read = await FrameCodec.ReadAsync<WorkerRunRequest>(stream, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken);

        Assert.Equal(Sample, read);
    }

    [Fact]
    public async Task CleanEndOfStreamReadsNull()
    {
        using MemoryStream stream = new MemoryStream();

        Assert.Null(await FrameCodec.ReadAsync<WorkerRunRequest>(stream, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LengthPrefixAboveTheLimitIsOversizedAndNoPayloadIsRead()
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, 1_000_000);
        using MemoryStream stream = new MemoryStream(header);

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync<WorkerRunRequest>(stream, 1_000, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Oversized, error.Kind);
    }

    [Fact]
    public async Task NegativeLengthPrefixIsOversized()
    {
        byte[] header = [0xFF, 0xFF, 0xFF, 0xFF];
        using MemoryStream stream = new MemoryStream(header);

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync<WorkerRunRequest>(stream, 1_000, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Oversized, error.Kind);
    }

    [Fact]
    public async Task TextInPlaceOfAFrameIsOversized()
    {
        // The text "11.0.100" is what "dotnet --version" prints. Its first four bytes are a huge length.
        using MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes("11.0.100\n"));

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync<WorkerRunRequest>(stream, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Oversized, error.Kind);
    }

    [Fact]
    public async Task PartialLengthPrefixIsTruncated()
    {
        using MemoryStream stream = new MemoryStream([0, 0]);

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync<WorkerRunRequest>(stream, 1_000, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Truncated, error.Kind);
    }

    [Fact]
    public async Task PartialPayloadIsTruncated()
    {
        using MemoryStream complete = new MemoryStream();
        await FrameCodec.WriteAsync(complete, Sample, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken);
        byte[] cut = complete.ToArray()[..^3];
        using MemoryStream stream = new MemoryStream(cut);

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync<WorkerRunRequest>(stream, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Truncated, error.Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"protocolVersion\":\"x\"}")]
    [InlineData("{\"protocolVersion\":1,\"executionId\":\"00000000-0000-0000-0000-000000000000\",\"sourceRevision\":1,\"source\":\"\",\"results\":{\"maxDepth\":0,\"maxItems\":1,\"maxTextLength\":1,\"maxNodes\":1,\"invokePropertyGetters\":false},\"maxOutputChars\":1}")]
    public async Task InvalidPayloadIsMalformed(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        using MemoryStream stream = new MemoryStream(frame);

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync<WorkerRunRequest>(stream, WorkerProtocol.MaxMessageBytes, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Malformed, error.Kind);
    }

    [Fact]
    public async Task WritingAMessageAboveTheLimitIsRejectedBeforeAnyByteIsWritten()
    {
        using MemoryStream stream = new MemoryStream();

        FrameException error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.WriteAsync(stream, Sample, 10, TestContext.Current.CancellationToken));

        Assert.Equal(FrameErrorKind.Oversized, error.Kind);
        Assert.Equal(0, stream.Length);
    }
}
