using System.Runtime.InteropServices;
using PersonaEngine.Lib.UI.Native;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     Owns the D3D11 device, DXGI swap chain, and DirectComposition plumbing that
///     drive the transparent floating overlay. Replaces the prior
///     OpenGL+UpdateLayeredWindow path: the swap chain is composition-mode with
///     premultiplied alpha, which is bound as a DirectComposition visual's content
///     so the HWND shows per-pixel-alpha pixels produced straight from the GPU.
///
///     Lifecycle:
///       * Construct once per HWND on the render thread.
///       * Draw into <see cref="BackBufferRtv" /> (its size matches the last
///         <see cref="Resize" /> call).
///       * Call <see cref="Present" /> once per frame.
///       * Dispose on teardown.
/// </summary>
public sealed class OverlayD3D11Context : IDisposable
{
    // BGRA matches DWM's native format — zero conversion cost on present and is
    // required for CreateSwapChainForComposition with Premultiplied alpha.
    private const Format BackBufferFormat = Format.B8G8R8A8_UNorm;

    // Flip-sequential is fine for a two-buffer composition swap chain. FlipDiscard
    // is also valid here but the overlay's content changes every frame so the
    // benefit is marginal and sequential gives more predictable latency.
    private const SwapEffect SwapEffectMode = SwapEffect.FlipSequential;

    // Two buffers is the minimum for a flip-model swap chain and keeps memory
    // footprint tight. We don't need triple buffering for a 60 Hz overlay.
    private const int BufferCount = 2;

    private readonly nint _hwnd;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private IDCompositionDevice? _compositionDevice;
    private IDCompositionTarget? _compositionTarget;
    private IDCompositionVisual? _rootVisual;

    private ID3D11RenderTargetView? _rtv;
    private nint _frameLatencyWaitable;
    private int _width;
    private int _height;

    public OverlayD3D11Context(nint hwnd, int initialWidth, int initialHeight)
    {
        if (hwnd == nint.Zero)
        {
            throw new ArgumentException("HWND must be non-zero.", nameof(hwnd));
        }

        _hwnd = hwnd;
        _width = Math.Max(1, initialWidth);
        _height = Math.Max(1, initialHeight);

        CreateDevice();
        CreateSwapChain();
        CreateComposition();
        CreateBackBufferView();
    }

    /// <summary>D3D11 device — use this for every GPU resource created on the overlay thread.</summary>
    public ID3D11Device Device =>
        _device ?? throw new ObjectDisposedException(nameof(OverlayD3D11Context));

    /// <summary>Immediate context — draw commands land here.</summary>
    public ID3D11DeviceContext Context =>
        _context ?? throw new ObjectDisposedException(nameof(OverlayD3D11Context));

    /// <summary>RTV of the current back buffer. Recreated on <see cref="Resize" />.</summary>
    public ID3D11RenderTargetView BackBufferRtv =>
        _rtv ?? throw new ObjectDisposedException(nameof(OverlayD3D11Context));

    public int Width => _width;

    public int Height => _height;

    /// <summary>
    ///     Resizes the swap chain and back buffer. Must be called when the HWND
    ///     is resized — skipping this produces DXGI_ERROR_INVALID_CALL on Present.
    ///     No-op if the size hasn't actually changed.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (_swapChain is null || _device is null)
        {
            throw new ObjectDisposedException(nameof(OverlayD3D11Context));
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && _rtv is not null)
        {
            return;
        }

        // Flip-model swap chains require all outstanding references to the back
        // buffers to be released before ResizeBuffers — RTV first, then flush
        // immediate context to kick any lingering bindings.
        _rtv?.Dispose();
        _rtv = null;
        _context?.ClearState();

        _swapChain.ResizeBuffers(
            BufferCount,
            (uint)width,
            (uint)height,
            BackBufferFormat,
            // Must re-pass the waitable flag on resize — ResizeBuffers replaces
            // the flag set, and dropping it here would orphan the waitable
            // handle we queried at creation.
            SwapChainFlags.FrameLatencyWaitableObject
        );

        _width = width;
        _height = height;
        CreateBackBufferView();
    }

    /// <summary>
    ///     Presents the current back buffer. Does NOT call
    ///     <c>IDCompositionDevice.Commit</c> — Commit is for visual-tree changes
    ///     (transforms, effects, z-order), not for swap-chain content updates.
    ///     A swap chain bound to a DCompo visual updates when you Present it;
    ///     DWM picks up the new back buffer automatically. Committing per frame
    ///     triggers a full DWM scene-graph rebuild — that was eating ~20% GPU
    ///     on the overlay thread for no benefit.
    ///
    ///     <c>Present(1, 0)</c> (VSync) pairs with the frame-latency waitable so
    ///     the DXGI queue naturally paces to vblank rather than dumping frames
    ///     as fast as possible.
    /// </summary>
    public void Present()
    {
        if (_swapChain is null)
        {
            return;
        }

        _swapChain.Present(1, PresentFlags.None);
    }

    /// <summary>
    ///     Blocks the current thread until DWM signals that it's ready for the
    ///     next frame (typically once per vblank). Called at the start of each
    ///     render iteration to prevent unbounded-FPS GPU thrashing.
    /// </summary>
    public void WaitForNextFrame()
    {
        if (_frameLatencyWaitable == nint.Zero)
        {
            return;
        }

        // 1 s timeout: in the unlikely case the object isn't signalled (e.g.
        // DWM stall), we fall through and render anyway rather than deadlocking
        // the overlay thread forever.
        Win32.WaitForSingleObjectEx(_frameLatencyWaitable, 1000, true);
    }

    public void Dispose()
    {
        _rtv?.Dispose();
        _rtv = null;

        _rootVisual?.Dispose();
        _rootVisual = null;

        _compositionTarget?.Dispose();
        _compositionTarget = null;

        _compositionDevice?.Dispose();
        _compositionDevice = null;

        // The waitable handle is owned by the swap chain — releasing the swap
        // chain closes the handle. Null the field so nothing tries to wait on
        // it after Dispose.
        _frameLatencyWaitable = nint.Zero;

        _swapChain?.Dispose();
        _swapChain = null;

        _context?.Dispose();
        _context = null;

        _device?.Dispose();
        _device = null;

        _factory?.Dispose();
        _factory = null;
    }

    private void CreateDevice()
    {
        // Debug layer is off in release builds and stays off here — enabling
        // it requires the Windows SDK and our users don't ship with it.
        var creationFlags = DeviceCreationFlags.BgraSupport;
        var featureLevels = new[] { FeatureLevel.Level_11_0 };

        var hr = Vortice.Direct3D11.D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            creationFlags,
            featureLevels,
            out _device,
            out _context
        );
        hr.CheckError();
    }

    private void CreateSwapChain()
    {
        if (_device is null)
        {
            throw new InvalidOperationException("Device must be created before swap chain.");
        }

        // Obtain the DXGI factory from the adapter that owns our D3D11 device —
        // creating a factory independently can pick a different adapter and
        // fragment GPU usage on multi-GPU machines.
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var dxgiAdapter = dxgiDevice.GetAdapter();
        _factory = dxgiAdapter.GetParent<IDXGIFactory2>();

        var desc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = BackBufferFormat,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = BufferCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffectMode,
            // Premultiplied alpha is the only alpha mode compatible with
            // composition swap chains that show through to transparent HWNDs.
            AlphaMode = AlphaMode.Premultiplied,
            // FrameLatencyWaitableObject hands us a Win32 event we can wait on
            // before each frame — the compositor signals it at most once per
            // vblank, so the render thread sleeps when DWM is busy instead of
            // spinning and pinning the GPU.
            Flags = SwapChainFlags.FrameLatencyWaitableObject,
        };

        _swapChain = _factory.CreateSwapChainForComposition(_device, desc, null);

        // Query the waitable object now so the main loop doesn't have to pay
        // per-frame QueryInterface cost. MaximumFrameLatency=1 keeps the
        // compositor's queue depth at a single frame so the overlay reflects
        // the latest avatar state with minimal lag.
        using var swapChain2 = _swapChain.QueryInterface<IDXGISwapChain2>();
        swapChain2.MaximumFrameLatency = 1;
        _frameLatencyWaitable = swapChain2.FrameLatencyWaitableObject;
    }

    private void CreateComposition()
    {
        if (_device is null || _swapChain is null)
        {
            throw new InvalidOperationException(
                "Device and swap chain must exist before composition."
            );
        }

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _compositionDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);

        // topmost=true controls compositing order within the HWND's subtree —
        // independent of WS_EX_TOPMOST, which is still applied on the HWND to
        // keep us above other windows.
        _compositionDevice.CreateTargetForHwnd(_hwnd, true, out _compositionTarget).CheckError();

        _compositionDevice.CreateVisual(out _rootVisual).CheckError();
        _rootVisual.SetContent(_swapChain);
        _compositionTarget!.SetRoot(_rootVisual);

        // First commit binds the visual tree; subsequent commits happen per-frame
        // in Present so visual updates stay in lockstep with back-buffer updates.
        _compositionDevice.Commit();
    }

    private void CreateBackBufferView()
    {
        if (_device is null || _swapChain is null)
        {
            throw new InvalidOperationException("Device and swap chain must exist before RTV.");
        }

        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _rtv = _device.CreateRenderTargetView(backBuffer);
    }
}
