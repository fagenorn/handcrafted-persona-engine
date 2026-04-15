using PersonaEngine.Lib.UI.Rendering;
using Spout.Interop;

namespace PersonaEngine.Lib.UI.Overlay;

/// <summary>
///     Consumes a Spout sender from the same (or any) process via
///     <see cref="SpoutReceiver" />. Spout uses NV_DX_interop2 under the hood, so the
///     frame travels GPU→GPU as a DirectX11 shared texture bridged into the overlay's
///     own independent OpenGL context. This is the whole point of switching away
///     from shared GL contexts: the overlay gets its own pixel format (alpha-capable)
///     and DWM per-pixel transparency finally works.
///
///     Must be created, updated, and disposed on the thread whose GL context is
///     current (the overlay thread) — the SpoutReceiver registers GL objects on the
///     thread it was constructed on.
/// </summary>
public sealed class SpoutFrameSource : IFrameSource, IDisposable
{
    private readonly SpoutReceiver _receiver;

    private bool _bound;

    public SpoutFrameSource(string senderName)
    {
        _receiver = new SpoutReceiver();
        _receiver.SetReceiverName(senderName);
    }

    // Spout publishes frames in DirectX orientation (Y down). Consumers using
    // GL-convention clip space (Y up) must flip V when sampling.
    public bool OriginBottomLeft => false;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public uint ColorTextureHandle => _bound ? _receiver.SharedTextureID : 0u;

    /// <summary>
    ///     Call once per frame before sampling <see cref="ColorTextureHandle" />.
    ///     Binds the Spout shared texture into the current GL context and returns
    ///     <see langword="true" /> if the texture is ready to sample.
    /// </summary>
    public bool Acquire()
    {
        if (_bound)
        {
            // Always unbind from the prior frame before reusing.
            // CppSharp exposes UnBindSharedTexture as a Boolean property; reading it
            // invokes the underlying call.
            _ = _receiver.UnBindSharedTexture;
            _bound = false;
        }

        if (!_receiver.ReceiveTexture())
        {
            return false;
        }

        if (_receiver.IsUpdated)
        {
            Width = (int)_receiver.SenderWidth;
            Height = (int)_receiver.SenderHeight;
        }

        if (!_receiver.BindSharedTexture())
        {
            return false;
        }

        _bound = true;
        return true;
    }

    public void Dispose()
    {
        if (_bound)
        {
            // CppSharp exposes UnBindSharedTexture as a Boolean property; reading it
            // invokes the underlying call.
            _ = _receiver.UnBindSharedTexture;
            _bound = false;
        }

        _receiver.ReleaseReceiver();
        _receiver.Dispose();
    }
}
