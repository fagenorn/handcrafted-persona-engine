using System.Runtime.InteropServices;
using Spout.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Receives a Spout sender's DirectX shared texture straight into our own
///     <see cref="ID3D11Device" /> — no OpenGL interop, no extra DLL. Spout
///     senders publish a <c>D3D11_RESOURCE_MISC_SHARED</c> texture whose NT
///     handle is stored in the sender registry; <see cref="SpoutSenderNames" />
///     exposes that handle via <c>GetSenderInfo</c>. We call
///     <c>ID3D11Device1.OpenSharedResource</c> on the handle and sample the
///     resulting texture.
///
///     Caches the opened texture by handle — reopens only when the sender
///     restarts or resizes (at which point the handle value changes).
///
///     No explicit frame synchronization: Spout senders issue a single
///     <c>CopyResource</c> per frame so reads across a frame boundary are
///     unlikely to tear visibly at 60 FPS. If tearing manifests in practice,
///     Spout exposes a named mutex (<c>{senderName}_SpoutAccessMutex</c>) that
///     can be acquired around the sample.
/// </summary>
public sealed class SpoutD3D11Source : IDisposable
{
    private readonly ID3D11Device _device;
    private readonly string _senderName;
    private readonly SpoutSenderNames _registry;

    private nint _cachedHandle;
    private ID3D11Texture2D? _sharedTexture;
    private ID3D11ShaderResourceView? _srv;
    private int _width;
    private int _height;

    public SpoutD3D11Source(ID3D11Device device, string senderName)
    {
        _device = device;
        _senderName = senderName;
        _registry = new SpoutSenderNames();
    }

    /// <summary>SRV for the latest shared texture, or null if no sender is connected.</summary>
    public ID3D11ShaderResourceView? ShaderResourceView => _srv;

    public int Width => _width;

    public int Height => _height;

    /// <summary>
    ///     Polls the sender registry once and — if a valid handle is published —
    ///     ensures <see cref="ShaderResourceView" /> points at the current shared
    ///     texture. Returns true iff the SRV is safe to sample this frame.
    /// </summary>
    public unsafe bool Acquire()
    {
        uint width = 0;
        uint height = 0;
        nint handle = nint.Zero;
        uint format = 0;

        if (!_registry.GetSenderInfo(_senderName, ref width, ref height, &handle, ref format))
        {
            return false;
        }

        if (handle == nint.Zero || width == 0 || height == 0)
        {
            return false;
        }

        if (handle != _cachedHandle)
        {
            ReopenSharedTexture(handle, format);
        }

        _width = (int)width;
        _height = (int)height;
        return _srv is not null;
    }

    public void Dispose()
    {
        _srv?.Dispose();
        _srv = null;

        _sharedTexture?.Dispose();
        _sharedTexture = null;

        _registry.Dispose();
    }

    private void ReopenSharedTexture(nint handle, uint format)
    {
        // Release prior references before taking a new handle — opened shared
        // resources hold a reference to the sender's DX texture, and leaking
        // them would keep the old texture alive after the sender updates.
        _srv?.Dispose();
        _srv = null;

        _sharedTexture?.Dispose();
        _sharedTexture = null;

        try
        {
            _sharedTexture = _device.OpenSharedResource<ID3D11Texture2D>(handle);

            // Spout shared textures are typically BGRA_UNorm; some senders use
            // RGBA_UNorm. Use the sender-reported format when non-zero,
            // otherwise fall back to the swap chain's BGRA convention.
            var viewFormat = format != 0 ? (Format)format : Format.B8G8R8A8_UNorm;
            var viewDesc = new ShaderResourceViewDescription
            {
                Format = viewFormat,
                ViewDimension = Vortice.Direct3D.ShaderResourceViewDimension.Texture2D,
                Texture2D = new Texture2DShaderResourceView { MipLevels = 1, MostDetailedMip = 0 },
            };

            _srv = _device.CreateShaderResourceView(_sharedTexture, viewDesc);
            _cachedHandle = handle;
        }
        catch (COMException)
        {
            // The sender may have torn down between the registry query and our
            // open call; treat as "no sender" for this frame.
            _sharedTexture?.Dispose();
            _sharedTexture = null;
            _cachedHandle = nint.Zero;
        }
    }
}
