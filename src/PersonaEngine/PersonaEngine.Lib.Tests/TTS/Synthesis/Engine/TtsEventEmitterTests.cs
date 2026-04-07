using System.Threading.Channels;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Events;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Common;
using PersonaEngine.Lib.Core.Conversation.Implementations.Events.Output;
using PersonaEngine.Lib.TTS.Synthesis;
using PersonaEngine.Lib.TTS.Synthesis.Engine;
using Xunit;

namespace PersonaEngine.Lib.Tests.TTS.Synthesis.Engine;

public class TtsEventEmitterTests
{
    private readonly Channel<IOutputEvent> _channel = Channel.CreateUnbounded<IOutputEvent>();
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _turnId = Guid.NewGuid();

    private TtsEventEmitter CreateEmitter() => new(_channel.Writer, _sessionId, _turnId);

    [Fact]
    public async Task EmitReadyToSynthesizeAsync_FirstCall_WritesEvent()
    {
        var emitter = CreateEmitter();

        await emitter.EmitReadyToSynthesizeAsync();

        Assert.True(_channel.Reader.TryRead(out var evt));
        var ready = Assert.IsType<TtsReadyToSynthesizeEvent>(evt);
        Assert.Equal(_sessionId, ready.SessionId);
        Assert.Equal(_turnId, ready.TurnId);
    }

    [Fact]
    public async Task EmitReadyToSynthesizeAsync_SecondCall_IsNoOp()
    {
        var emitter = CreateEmitter();

        await emitter.EmitReadyToSynthesizeAsync();
        await emitter.EmitReadyToSynthesizeAsync();

        Assert.True(_channel.Reader.TryRead(out _));
        Assert.False(_channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task EmitChunkAsync_FirstChunk_EmitsStreamStartThenChunk()
    {
        var emitter = CreateEmitter();
        var segment = new AudioSegment(new float[] { 1f, 2f }, 24000, new List<Token>());

        await emitter.EmitChunkAsync(segment);

        Assert.True(_channel.Reader.TryRead(out var first));
        Assert.IsType<TtsStreamStartEvent>(first);

        Assert.True(_channel.Reader.TryRead(out var second));
        var chunk = Assert.IsType<TtsChunkEvent>(second);
        Assert.Same(segment, chunk.Chunk);
    }

    [Fact]
    public async Task EmitChunkAsync_SecondChunk_EmitsOnlyChunk()
    {
        var emitter = CreateEmitter();
        var segment1 = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());
        var segment2 = new AudioSegment(new float[] { 2f }, 24000, new List<Token>());

        await emitter.EmitChunkAsync(segment1);
        // Drain the StreamStart + first chunk
        _channel.Reader.TryRead(out _);
        _channel.Reader.TryRead(out _);

        await emitter.EmitChunkAsync(segment2);

        Assert.True(_channel.Reader.TryRead(out var evt));
        var chunk = Assert.IsType<TtsChunkEvent>(evt);
        Assert.Same(segment2, chunk.Chunk);
        Assert.False(_channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task EmitStreamEndAsync_AfterChunks_EmitsEnd()
    {
        var emitter = CreateEmitter();
        var segment = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());
        await emitter.EmitChunkAsync(segment);

        // Drain prior events
        while (_channel.Reader.TryRead(out _)) { }

        await emitter.EmitStreamEndAsync();

        Assert.True(_channel.Reader.TryRead(out var evt));
        var end = Assert.IsType<TtsStreamEndEvent>(evt);
        Assert.Equal(CompletionReason.Completed, end.FinishReason);
    }

    [Fact]
    public async Task EmitStreamEndAsync_NoChunksNoError_DoesNotEmit()
    {
        var emitter = CreateEmitter();

        await emitter.EmitStreamEndAsync();

        Assert.False(_channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task EmitStreamEndAsync_NoChunksButError_EmitsEndToPreventFsmHang()
    {
        var emitter = CreateEmitter();
        await emitter.EmitErrorAsync(new InvalidOperationException("test"));

        // Drain error event
        while (_channel.Reader.TryRead(out _)) { }

        await emitter.EmitStreamEndAsync();

        Assert.True(_channel.Reader.TryRead(out var evt));
        var end = Assert.IsType<TtsStreamEndEvent>(evt);
        Assert.Equal(CompletionReason.Error, end.FinishReason);
    }

    [Fact]
    public async Task EmitErrorAsync_WritesErrorEventAndSetsReason()
    {
        var emitter = CreateEmitter();
        var ex = new InvalidOperationException("boom");

        await emitter.EmitErrorAsync(ex);

        Assert.Equal(CompletionReason.Error, emitter.CompletionReason);
        Assert.True(_channel.Reader.TryRead(out var evt));
        var error = Assert.IsType<ErrorOutputEvent>(evt);
        Assert.Same(ex, error.Exception);
    }

    [Fact]
    public void SetReason_UpdatesCompletionReason()
    {
        var emitter = CreateEmitter();

        emitter.SetReason(CompletionReason.Cancelled);

        Assert.Equal(CompletionReason.Cancelled, emitter.CompletionReason);
    }

    [Fact]
    public async Task EmitStreamEndAsync_UsesCancelledReason_WhenSet()
    {
        var emitter = CreateEmitter();
        var segment = new AudioSegment(new float[] { 1f }, 24000, new List<Token>());
        await emitter.EmitChunkAsync(segment);
        while (_channel.Reader.TryRead(out _)) { }

        emitter.SetReason(CompletionReason.Cancelled);
        await emitter.EmitStreamEndAsync();

        Assert.True(_channel.Reader.TryRead(out var evt));
        var end = Assert.IsType<TtsStreamEndEvent>(evt);
        Assert.Equal(CompletionReason.Cancelled, end.FinishReason);
    }
}
