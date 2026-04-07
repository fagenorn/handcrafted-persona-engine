using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Utilities;
using Hexa.NET.ImNodes;
using PersonaEngine.Lib.UI.Common;
using Silk.NET.Input;
using Silk.NET.Input.Extensions;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Shader = PersonaEngine.Lib.UI.Common.Shader;

namespace PersonaEngine.Lib.UI.GUI;

public class ImGuiController : IDisposable
{
    [FixedAddressValueType]
    private static SetClipboardDelegate setClipboardFn;

    [FixedAddressValueType]
    private static GetClipboardDelegate getClipboardFn;

    private readonly List<char> _pressedChars = new();

    private int _attribLocationProjMtx;

    private int _attribLocationTex;

    private int _attribLocationVtxColor;

    private int _attribLocationVtxPos;

    private int _attribLocationVtxUV;

    private IntPtr _clipboardTextPtr = IntPtr.Zero;

    private bool _ctrlVProcessed = false;

    private uint _elementsHandle;

    private bool _frameBegun;

    private GL _gl;

    private IInputContext _input;

    private IKeyboard _keyboard;

    private ImGuiPlatformIOPtr _platform;

    private Shader _shader;

    private uint _vboHandle;

    private uint _vertexArrayObject;

    private IView _view;

    private bool _wasCtrlVPressed = false;

    private int _windowHeight;

    private int _windowWidth;

    public ImGuiContextPtr Context;

    /// <summary>
    ///     Constructs a new ImGuiController.
    /// </summary>
    public ImGuiController(GL gl, IView view, IInputContext input)
        : this(gl, view, input, null, null) { }

    /// <summary>
    ///     Constructs a new ImGuiController with font configuration.
    /// </summary>
    public ImGuiController(
        GL gl,
        IView view,
        IInputContext input,
        string primaryFontPath,
        string emojiFontPath
    )
        : this(gl, view, input, primaryFontPath, emojiFontPath, null) { }

    /// <summary>
    ///     Constructs a new ImGuiController with an onConfigureIO Action.
    /// </summary>
    public ImGuiController(GL gl, IView view, IInputContext input, Action? onConfigureIO)
        : this(gl, view, input, null, null, onConfigureIO) { }

    /// <summary>
    ///     Constructs a new ImGuiController with font configuration and onConfigure Action.
    /// </summary>
    public ImGuiController(
        GL gl,
        IView view,
        IInputContext input,
        string? primaryFontPath = null,
        string? emojiFontPath = null,
        Action? onConfigureIO = null
    )
    {
        Init(gl, view, input);

        var io = ImGui.GetIO();
        var fontBuilder = new ImGuiFontBuilder();

        if (primaryFontPath != null)
        {
            fontBuilder.AddFontFromFileTTF(primaryFontPath, 18f, [0x1, 0x1FFFF]);
        }
        else
        {
            fontBuilder.AddDefaultFont();
        }

        if (emojiFontPath != null)
        {
            fontBuilder.SetOption(config =>
            {
                config.FontLoaderFlags |= (uint)ImGuiFreeTypeLoaderFlags.LoadColor;
                config.MergeMode = true;
                config.PixelSnapH = true;
            });

            fontBuilder.AddFontFromFileTTF(emojiFontPath, 14f, [0x1, 0x1FFFF]);
        }

        _ = fontBuilder.Build();

        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.ConfigViewportsNoAutoMerge = false;
        io.ConfigViewportsNoTaskBarIcon = false;

        CreateDeviceResources();

        SetPerFrameImGuiData(1f / 60f);

        // Initialize ImNodes as you're already doing
        ImNodes.SetImGuiContext(Context);
        ImNodes.SetCurrentContext(ImNodes.CreateContext());
        ImNodes.StyleColorsDark(ImNodes.GetStyle());

        BeginFrame();
    }

    /// <summary>
    ///     Frees all graphics resources used by the renderer.
    /// </summary>
    public unsafe void Dispose()
    {
        _view.Resize -= WindowResized;
        _keyboard.KeyChar -= OnKeyChar;

        _gl.DeleteBuffer(_vboHandle);
        _gl.DeleteBuffer(_elementsHandle);
        _gl.DeleteVertexArray(_vertexArrayObject);

        // Destroy all backend-managed textures
        var platformIO = ImGui.GetPlatformIO();
        for (var i = 0; i < platformIO.Textures.Size; i++)
        {
            var tex = platformIO.Textures[i];
            if (!tex.IsNull && tex.RefCount == 1)
            {
                var glTex = (uint)tex.TexID.Handle;
                if (glTex != 0)
                {
                    _gl.DeleteTexture(glTex);
                }

                tex.SetTexID(default);
                tex.BackendUserData = null;
                tex.SetStatus(ImTextureStatus.Destroyed);
            }
        }

        _shader.Dispose();

        ImGui.DestroyContext(Context);
    }

    public void MakeCurrent()
    {
        ImGui.SetCurrentContext(Context);
    }

    private unsafe void Init(GL gl, IView view, IInputContext input)
    {
        _gl = gl;
        _view = view;
        _input = input;
        _windowWidth = view.Size.X;
        _windowHeight = view.Size.Y;

        Context = ImGui.CreateContext();
        ImGui.SetCurrentContext(Context);
        ImGui.StyleColorsDark();

        _platform = ImGui.GetPlatformIO();
        var io = ImGui.GetIO();

        setClipboardFn = SetClipboard;
        getClipboardFn = GetClipboard;

        // Register clipboard handlers with both IO and PlatformIO
        _platform.PlatformSetClipboardTextFn = (void*)
            Marshal.GetFunctionPointerForDelegate(setClipboardFn);
        _platform.PlatformGetClipboardTextFn = (void*)
            Marshal.GetFunctionPointerForDelegate(getClipboardFn);
    }

    private void SetClipboard(IntPtr data)
    {
        Debug.WriteLine("SetClipboard called");

        if (data == IntPtr.Zero)
        {
            return;
        }

        var text = Marshal.PtrToStringUTF8(data) ?? string.Empty;
        try
        {
            // Try the Silk.NET method first
            if (_keyboard != null)
            {
                _keyboard.ClipboardText = text;
            }
            else
            {
                Debug.WriteLine("Keyboard reference is null when setting clipboard text");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set clipboard text via Silk.NET: {ex.Message}");
        }
    }

    private IntPtr GetClipboard()
    {
        Debug.WriteLine("GetClipboard called");

        if (_clipboardTextPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_clipboardTextPtr);
            _clipboardTextPtr = IntPtr.Zero;
        }

        var text = string.Empty;
        var success = false;

        // Try primary method
        if (_keyboard != null)
        {
            try
            {
                text = _keyboard.ClipboardText ?? string.Empty;
                success = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get clipboard text via Silk.NET: {ex.Message}");
            }
        }

        // Convert to UTF-8 and allocate memory
        try
        {
            // Ensure null termination for C strings
            var bytes = Encoding.UTF8.GetBytes(text + '\0');
            _clipboardTextPtr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _clipboardTextPtr, bytes.Length);

            return _clipboardTextPtr;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to allocate memory for clipboard text: {ex.Message}");

            return IntPtr.Zero;
        }
    }

    private void BeginFrame()
    {
        ImGui.NewFrame();
        _frameBegun = true;
        _keyboard = _input.Keyboards[0];
        _view.Resize += WindowResized;
        _keyboard.KeyDown += OnKeyDown;
        _keyboard.KeyUp += OnKeyUp;
        _keyboard.KeyChar += OnKeyChar;
    }

    private static void OnKeyDown(IKeyboard keyboard, Key keycode, int scancode)
    {
        OnKeyEvent(keyboard, keycode, scancode, true);
    }

    private static void OnKeyUp(IKeyboard keyboard, Key keycode, int scancode)
    {
        OnKeyEvent(keyboard, keycode, scancode, false);
    }

    private static void OnKeyEvent(IKeyboard keyboard, Key keycode, int scancode, bool down)
    {
        var io = ImGui.GetIO();
        var imGuiKey = TranslateInputKeyToImGuiKey(keycode);
        io.AddKeyEvent(imGuiKey, down);
        io.SetKeyEventNativeData(imGuiKey, (int)keycode, scancode);
    }

    private void OnKeyChar(IKeyboard arg1, char arg2)
    {
        _pressedChars.Add(arg2);
    }

    private void WindowResized(Vector2D<int> size)
    {
        _windowWidth = size.X;
        _windowHeight = size.Y;
    }

    /// <summary>
    ///     Renders the ImGui draw list data.
    ///     This method requires a <see cref="GraphicsDevice" /> because it may create new DeviceBuffers if the size of vertex
    ///     or index data has increased beyond the capacity of the existing buffers.
    ///     A <see cref="CommandList" /> is needed to submit drawing and resource update commands.
    /// </summary>
    public void Render()
    {
        if (_frameBegun)
        {
            ImGui.End();

            var oldCtx = ImGui.GetCurrentContext();

            if (oldCtx != Context)
            {
                ImGui.SetCurrentContext(Context);
            }

            _frameBegun = false;
            ImGui.Render();
            RenderImDrawData(ImGui.GetDrawData());

            if (oldCtx != Context)
            {
                ImGui.SetCurrentContext(oldCtx);
            }
        }
    }

    /// <summary>
    ///     Updates ImGui input and IO configuration state.
    /// </summary>
    public void Update(float deltaSeconds)
    {
        var oldCtx = ImGui.GetCurrentContext();

        if (oldCtx != Context)
        {
            ImGui.SetCurrentContext(Context);
        }

        if (_frameBegun)
        {
            ImGui.Render();
        }

        SetPerFrameImGuiData(deltaSeconds);
        UpdateImGuiInput();
        SetDocking();

        _frameBegun = true;
    }

    private void SetDocking()
    {
        ImGui.NewFrame();
        var viewport = ImGui.GetMainViewport();

        var windowFlags =
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBringToFrontOnFocus
            | ImGuiWindowFlags.NoNavFocus;

        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));

        ImGui.Begin("DockSpace", windowFlags);
        ImGui.PopStyleVar(3);
        var dockspaceId = ImGui.GetID("MyDockSpace");
        ImGui.DockSpace(dockspaceId, new Vector2(0.0f, 0.0f), ImGuiDockNodeFlags.AutoHideTabBar);
    }

    /// <summary>
    ///     Sets per-frame data based on the associated window.
    ///     This is called by Update(float).
    /// </summary>
    private void SetPerFrameImGuiData(float deltaSeconds)
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_windowWidth, _windowHeight);

        if (_windowWidth > 0 && _windowHeight > 0)
        {
            io.DisplayFramebufferScale = new Vector2(
                _view.FramebufferSize.X / _windowWidth,
                _view.FramebufferSize.Y / _windowHeight
            );
        }

        io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
    }

    private void UpdateImGuiInput()
    {
        var io = ImGui.GetIO();

        using var mouseState = _input.Mice[0].CaptureState();

        io.MouseDown[0] = mouseState.IsButtonPressed(MouseButton.Left);
        io.MouseDown[1] = mouseState.IsButtonPressed(MouseButton.Right);
        io.MouseDown[2] = mouseState.IsButtonPressed(MouseButton.Middle);

        var point = new Point((int)mouseState.Position.X, (int)mouseState.Position.Y);
        io.MousePos = new Vector2(point.X, point.Y);

        var wheel = mouseState.GetScrollWheels()[0];
        io.MouseWheel = wheel.Y;
        io.MouseWheelH = wheel.X;

        foreach (var c in _pressedChars)
        {
            io.AddInputCharacter(c);
        }

        _pressedChars.Clear();

        io.KeyCtrl =
            _keyboard.IsKeyPressed(Key.ControlLeft) || _keyboard.IsKeyPressed(Key.ControlRight);
        io.KeyAlt = _keyboard.IsKeyPressed(Key.AltLeft) || _keyboard.IsKeyPressed(Key.AltRight);
        io.KeyShift =
            _keyboard.IsKeyPressed(Key.ShiftLeft) || _keyboard.IsKeyPressed(Key.ShiftRight);
        io.KeySuper =
            _keyboard.IsKeyPressed(Key.SuperLeft) || _keyboard.IsKeyPressed(Key.SuperRight);

        if (io.KeyCtrl && _keyboard.IsKeyPressed(Key.C))
        {
            io.AddKeyEvent(ImGuiKey.C, true);
            io.AddKeyEvent(ImGuiKey.LeftCtrl, true);

            Debug.WriteLine("Adding Ctrl+C event to ImGui");
        }

        var isCtrlVPressed = io.KeyCtrl && _keyboard.IsKeyPressed(Key.V);

        if (isCtrlVPressed && !_wasCtrlVPressed)
        {
            _ctrlVProcessed = false;
        }

        if (isCtrlVPressed && !_ctrlVProcessed)
        {
            io.AddKeyEvent(ImGuiKey.V, true);
            io.AddKeyEvent(ImGuiKey.LeftCtrl, true);

            var clipboardText = ImGui.GetClipboardTextS();
            if (ImGui.IsAnyItemActive())
            {
                foreach (var c in clipboardText)
                {
                    io.AddInputCharacter(c);
                }
            }

            io.AddKeyEvent(ImGuiKey.V, false);
            io.AddKeyEvent(ImGuiKey.LeftCtrl, false);

            _ctrlVProcessed = true;
        }

        _wasCtrlVPressed = isCtrlVPressed;
    }

    internal void PressChar(char keyChar)
    {
        _pressedChars.Add(keyChar);
    }

    /// <summary>
    ///     Translates a Silk.NET.Input.Key to an ImGuiKey.
    /// </summary>
    /// <param name="key">The Silk.NET.Input.Key to translate.</param>
    /// <returns>The corresponding ImGuiKey.</returns>
    /// <exception cref="NotImplementedException">When the key has not been implemented yet.</exception>
    private static ImGuiKey TranslateInputKeyToImGuiKey(Key key)
    {
        return key switch
        {
            Key.Tab => ImGuiKey.Tab,
            Key.Left => ImGuiKey.LeftArrow,
            Key.Right => ImGuiKey.RightArrow,
            Key.Up => ImGuiKey.UpArrow,
            Key.Down => ImGuiKey.DownArrow,
            Key.PageUp => ImGuiKey.PageUp,
            Key.PageDown => ImGuiKey.PageDown,
            Key.Home => ImGuiKey.Home,
            Key.End => ImGuiKey.End,
            Key.Insert => ImGuiKey.Insert,
            Key.Delete => ImGuiKey.Delete,
            Key.Backspace => ImGuiKey.Backspace,
            Key.Space => ImGuiKey.Space,
            Key.Enter => ImGuiKey.Enter,
            Key.Escape => ImGuiKey.Escape,
            Key.Apostrophe => ImGuiKey.Apostrophe,
            Key.Comma => ImGuiKey.Comma,
            Key.Minus => ImGuiKey.Minus,
            Key.Period => ImGuiKey.Period,
            Key.Slash => ImGuiKey.Slash,
            Key.Semicolon => ImGuiKey.Semicolon,
            Key.Equal => ImGuiKey.Equal,
            Key.LeftBracket => ImGuiKey.LeftBracket,
            Key.BackSlash => ImGuiKey.Backslash,
            Key.RightBracket => ImGuiKey.RightBracket,
            Key.GraveAccent => ImGuiKey.GraveAccent,
            Key.CapsLock => ImGuiKey.CapsLock,
            Key.ScrollLock => ImGuiKey.ScrollLock,
            Key.NumLock => ImGuiKey.NumLock,
            Key.PrintScreen => ImGuiKey.PrintScreen,
            Key.Pause => ImGuiKey.Pause,
            Key.Keypad0 => ImGuiKey.Keypad0,
            Key.Keypad1 => ImGuiKey.Keypad1,
            Key.Keypad2 => ImGuiKey.Keypad2,
            Key.Keypad3 => ImGuiKey.Keypad3,
            Key.Keypad4 => ImGuiKey.Keypad4,
            Key.Keypad5 => ImGuiKey.Keypad5,
            Key.Keypad6 => ImGuiKey.Keypad6,
            Key.Keypad7 => ImGuiKey.Keypad7,
            Key.Keypad8 => ImGuiKey.Keypad8,
            Key.Keypad9 => ImGuiKey.Keypad9,
            Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
            Key.KeypadDivide => ImGuiKey.KeypadDivide,
            Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
            Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
            Key.KeypadAdd => ImGuiKey.KeypadAdd,
            Key.KeypadEnter => ImGuiKey.KeypadEnter,
            Key.KeypadEqual => ImGuiKey.KeypadEqual,
            Key.ShiftLeft => ImGuiKey.LeftShift,
            Key.ControlLeft => ImGuiKey.LeftCtrl,
            Key.AltLeft => ImGuiKey.LeftAlt,
            Key.SuperLeft => ImGuiKey.LeftSuper,
            Key.ShiftRight => ImGuiKey.RightShift,
            Key.ControlRight => ImGuiKey.RightCtrl,
            Key.AltRight => ImGuiKey.RightAlt,
            Key.SuperRight => ImGuiKey.RightSuper,
            Key.Menu => ImGuiKey.Menu,
            Key.Number0 => ImGuiKey.Key0,
            Key.Number1 => ImGuiKey.Key1,
            Key.Number2 => ImGuiKey.Key2,
            Key.Number3 => ImGuiKey.Key3,
            Key.Number4 => ImGuiKey.Key4,
            Key.Number5 => ImGuiKey.Key5,
            Key.Number6 => ImGuiKey.Key6,
            Key.Number7 => ImGuiKey.Key7,
            Key.Number8 => ImGuiKey.Key8,
            Key.Number9 => ImGuiKey.Key9,
            Key.A => ImGuiKey.A,
            Key.B => ImGuiKey.B,
            Key.C => ImGuiKey.C,
            Key.D => ImGuiKey.D,
            Key.E => ImGuiKey.E,
            Key.F => ImGuiKey.F,
            Key.G => ImGuiKey.G,
            Key.H => ImGuiKey.H,
            Key.I => ImGuiKey.I,
            Key.J => ImGuiKey.J,
            Key.K => ImGuiKey.K,
            Key.L => ImGuiKey.L,
            Key.M => ImGuiKey.M,
            Key.N => ImGuiKey.N,
            Key.O => ImGuiKey.O,
            Key.P => ImGuiKey.P,
            Key.Q => ImGuiKey.Q,
            Key.R => ImGuiKey.R,
            Key.S => ImGuiKey.S,
            Key.T => ImGuiKey.T,
            Key.U => ImGuiKey.U,
            Key.V => ImGuiKey.V,
            Key.W => ImGuiKey.W,
            Key.X => ImGuiKey.X,
            Key.Y => ImGuiKey.Y,
            Key.Z => ImGuiKey.Z,
            Key.F1 => ImGuiKey.F1,
            Key.F2 => ImGuiKey.F2,
            Key.F3 => ImGuiKey.F3,
            Key.F4 => ImGuiKey.F4,
            Key.F5 => ImGuiKey.F5,
            Key.F6 => ImGuiKey.F6,
            Key.F7 => ImGuiKey.F7,
            Key.F8 => ImGuiKey.F8,
            Key.F9 => ImGuiKey.F9,
            Key.F10 => ImGuiKey.F10,
            Key.F11 => ImGuiKey.F11,
            Key.F12 => ImGuiKey.F12,
            Key.F13 => ImGuiKey.F13,
            Key.F14 => ImGuiKey.F14,
            Key.F15 => ImGuiKey.F15,
            Key.F16 => ImGuiKey.F16,
            Key.F17 => ImGuiKey.F17,
            Key.F18 => ImGuiKey.F18,
            Key.F19 => ImGuiKey.F19,
            Key.F20 => ImGuiKey.F20,
            Key.F21 => ImGuiKey.F21,
            Key.F22 => ImGuiKey.F22,
            Key.F23 => ImGuiKey.F23,
            Key.F24 => ImGuiKey.F24,
            _ => ImGuiKey.None,
        };
    }

    private unsafe void SetupRenderState(
        ImDrawDataPtr drawDataPtr,
        int framebufferWidth,
        int framebufferHeight
    )
    {
        // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
        _gl.Enable(GLEnum.Blend);
        _gl.BlendEquation(GLEnum.FuncAdd);
        _gl.BlendFuncSeparate(
            GLEnum.SrcAlpha,
            GLEnum.OneMinusSrcAlpha,
            GLEnum.One,
            GLEnum.OneMinusSrcAlpha
        );
        _gl.Disable(GLEnum.CullFace);
        _gl.Disable(GLEnum.DepthTest);
        _gl.Disable(GLEnum.StencilTest);
        _gl.Enable(GLEnum.ScissorTest);
#if !GLES && !LEGACY
        _gl.Disable(GLEnum.PrimitiveRestart);
        _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
#endif

        var L = drawDataPtr.DisplayPos.X;
        var R = drawDataPtr.DisplayPos.X + drawDataPtr.DisplaySize.X;
        var T = drawDataPtr.DisplayPos.Y;
        var B = drawDataPtr.DisplayPos.Y + drawDataPtr.DisplaySize.Y;

        Span<float> orthoProjection =
            stackalloc float[] {
                2.0f / (R - L),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                2.0f / (T - B),
                0.0f,
                0.0f,
                0.0f,
                0.0f,
                -1.0f,
                0.0f,
                (R + L) / (L - R),
                (T + B) / (B - T),
                0.0f,
                1.0f,
            };

        _shader.Use();
        _gl.Uniform1(_attribLocationTex, 0);
        _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);
        _gl.CheckError("Projection");

        _gl.BindSampler(0, 0);

        // Setup desired GL state
        // Recreate the VAO every time (this is to easily allow multiple GL contexts to be rendered to. VAO are not shared among GL contexts)
        // The renderer would actually work without any VAO bound, but then our VertexAttrib calls would overwrite the default one currently bound.
        _vertexArrayObject = _gl.GenVertexArray();
        _gl.BindVertexArray(_vertexArrayObject);
        _gl.CheckError("VAO");

        // Bind vertex/index buffers and setup attributes for ImDrawVert
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
        _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
        _gl.VertexAttribPointer(
            (uint)_attribLocationVtxPos,
            2,
            GLEnum.Float,
            false,
            (uint)sizeof(ImDrawVert),
            (void*)0
        );
        _gl.VertexAttribPointer(
            (uint)_attribLocationVtxUV,
            2,
            GLEnum.Float,
            false,
            (uint)sizeof(ImDrawVert),
            (void*)8
        );
        _gl.VertexAttribPointer(
            (uint)_attribLocationVtxColor,
            4,
            GLEnum.UnsignedByte,
            true,
            (uint)sizeof(ImDrawVert),
            (void*)16
        );
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawDataPtr)
    {
        var framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
        var framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return;
        }

        // Process texture create/update/destroy requests
        UpdateTextures(drawDataPtr);

        // Backup GL state
        _gl.GetInteger(GLEnum.ActiveTexture, out var lastActiveTexture);
        _gl.ActiveTexture(GLEnum.Texture0);

        _gl.GetInteger(GLEnum.CurrentProgram, out var lastProgram);
        _gl.GetInteger(GLEnum.TextureBinding2D, out var lastTexture);

        _gl.GetInteger(GLEnum.SamplerBinding, out var lastSampler);

        _gl.GetInteger(GLEnum.ArrayBufferBinding, out var lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out var lastVertexArrayObject);

#if !GLES
        Span<int> lastPolygonMode = stackalloc int[2];
        _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);
#endif

        Span<int> lastScissorBox = stackalloc int[4];
        _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

        _gl.GetInteger(GLEnum.BlendSrcRgb, out var lastBlendSrcRgb);
        _gl.GetInteger(GLEnum.BlendDstRgb, out var lastBlendDstRgb);

        _gl.GetInteger(GLEnum.BlendSrcAlpha, out var lastBlendSrcAlpha);
        _gl.GetInteger(GLEnum.BlendDstAlpha, out var lastBlendDstAlpha);

        _gl.GetInteger(GLEnum.BlendEquationRgb, out var lastBlendEquationRgb);
        _gl.GetInteger(GLEnum.BlendEquationAlpha, out var lastBlendEquationAlpha);

        var lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
        var lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
        var lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
        var lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
        var lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

#if !GLES && !LEGACY
        var lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);
#endif

        SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);

        // Will project scissor/clipping rectangles into framebuffer space
        var clipOff = drawDataPtr.DisplayPos; // (0,0) unless using multi-viewports
        var clipScale = drawDataPtr.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

        // Render command lists
        for (var n = 0; n < drawDataPtr.CmdListsCount; n++)
        {
            var cmdListPtr = drawDataPtr.CmdLists[n];

            // Upload vertex/index buffers

            _gl.BufferData(
                GLEnum.ArrayBuffer,
                (nuint)(cmdListPtr.VtxBuffer.Size * sizeof(ImDrawVert)),
                cmdListPtr.VtxBuffer.Data,
                GLEnum.StreamDraw
            );
            _gl.CheckError($"Data Vert {n}");
            _gl.BufferData(
                GLEnum.ElementArrayBuffer,
                (nuint)(cmdListPtr.IdxBuffer.Size * sizeof(ushort)),
                cmdListPtr.IdxBuffer.Data,
                GLEnum.StreamDraw
            );
            _gl.CheckError($"Data Idx {n}");

            for (var cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Size; cmd_i++)
            {
                var cmdPtr = cmdListPtr.CmdBuffer[cmd_i];

                if (cmdPtr.UserCallback != null)
                {
                    throw new NotImplementedException();
                }

                Vector4 clipRect;
                clipRect.X = (cmdPtr.ClipRect.X - clipOff.X) * clipScale.X;
                clipRect.Y = (cmdPtr.ClipRect.Y - clipOff.Y) * clipScale.Y;
                clipRect.Z = (cmdPtr.ClipRect.Z - clipOff.X) * clipScale.X;
                clipRect.W = (cmdPtr.ClipRect.W - clipOff.Y) * clipScale.Y;

                if (
                    clipRect.X < framebufferWidth
                    && clipRect.Y < framebufferHeight
                    && clipRect.Z >= 0.0f
                    && clipRect.W >= 0.0f
                )
                {
                    // Apply scissor/clipping rectangle
                    _gl.Scissor(
                        (int)clipRect.X,
                        (int)(framebufferHeight - clipRect.W),
                        (uint)(clipRect.Z - clipRect.X),
                        (uint)(clipRect.W - clipRect.Y)
                    );
                    _gl.CheckError("Scissor");

                    // Bind texture, Draw
                    // With RendererHasTextures, TexRef.TexID may be 0;
                    // read through the TexData pointer instead.
                    var texRef = cmdPtr.TexRef;
                    var texId =
                        texRef.TexData != null
                            ? (uint)texRef.TexData->TexID.Handle
                            : (uint)texRef.TexID.Handle;
                    _gl.BindTexture(GLEnum.Texture2D, texId);
                    _gl.CheckError("Texture");

                    _gl.DrawElementsBaseVertex(
                        GLEnum.Triangles,
                        cmdPtr.ElemCount,
                        GLEnum.UnsignedShort,
                        (void*)(cmdPtr.IdxOffset * sizeof(ushort)),
                        (int)cmdPtr.VtxOffset
                    );
                    _gl.CheckError("Draw");
                }
            }
        }

        // Destroy the temporary VAO
        _gl.DeleteVertexArray(_vertexArrayObject);
        _vertexArrayObject = 0;

        // Restore modified GL state
        _gl.UseProgram((uint)lastProgram);
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);

        _gl.BindSampler(0, (uint)lastSampler);

        _gl.ActiveTexture((GLEnum)lastActiveTexture);

        _gl.BindVertexArray((uint)lastVertexArrayObject);

        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
        _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
        _gl.BlendFuncSeparate(
            (GLEnum)lastBlendSrcRgb,
            (GLEnum)lastBlendDstRgb,
            (GLEnum)lastBlendSrcAlpha,
            (GLEnum)lastBlendDstAlpha
        );

        if (lastEnableBlend)
        {
            _gl.Enable(GLEnum.Blend);
        }
        else
        {
            _gl.Disable(GLEnum.Blend);
        }

        if (lastEnableCullFace)
        {
            _gl.Enable(GLEnum.CullFace);
        }
        else
        {
            _gl.Disable(GLEnum.CullFace);
        }

        if (lastEnableDepthTest)
        {
            _gl.Enable(GLEnum.DepthTest);
        }
        else
        {
            _gl.Disable(GLEnum.DepthTest);
        }

        if (lastEnableStencilTest)
        {
            _gl.Enable(GLEnum.StencilTest);
        }
        else
        {
            _gl.Disable(GLEnum.StencilTest);
        }

        if (lastEnableScissorTest)
        {
            _gl.Enable(GLEnum.ScissorTest);
        }
        else
        {
            _gl.Disable(GLEnum.ScissorTest);
        }

#if !GLES && !LEGACY
        if (lastEnablePrimitiveRestart)
        {
            _gl.Enable(GLEnum.PrimitiveRestart);
        }
        else
        {
            _gl.Disable(GLEnum.PrimitiveRestart);
        }

        _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);
#endif

        _gl.Scissor(
            lastScissorBox[0],
            lastScissorBox[1],
            (uint)lastScissorBox[2],
            (uint)lastScissorBox[3]
        );
    }

    private void CreateDeviceResources()
    {
        // Backup GL state

        _gl.GetInteger(GLEnum.TextureBinding2D, out var lastTexture);
        _gl.GetInteger(GLEnum.ArrayBufferBinding, out var lastArrayBuffer);
        _gl.GetInteger(GLEnum.VertexArrayBinding, out var lastVertexArray);

        var vertexSource =
            @"#version 330
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }";

        var fragmentSource =
            @"#version 330
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }";

        _shader = new Shader(_gl, vertexSource, fragmentSource);

        _attribLocationTex = _shader.GetUniformLocation("Texture");
        _attribLocationProjMtx = _shader.GetUniformLocation("ProjMtx");
        _attribLocationVtxPos = _shader.GetAttribLocation("Position");
        _attribLocationVtxUV = _shader.GetAttribLocation("UV");
        _attribLocationVtxColor = _shader.GetAttribLocation("Color");

        _vboHandle = _gl.GenBuffer();
        _elementsHandle = _gl.GenBuffer();

        // Restore modified GL state
        _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);

        _gl.BindVertexArray((uint)lastVertexArray);

        _gl.CheckError("End of ImGui setup");
    }

    private unsafe void UpdateTextures(ImDrawDataPtr drawDataPtr)
    {
        for (var i = 0; i < drawDataPtr.Textures.Size; i++)
        {
            var tex = drawDataPtr.Textures[i];
            if (tex.Status == ImTextureStatus.Ok)
            {
                continue;
            }

            UpdateTexture(tex);
        }
    }

    private unsafe void UpdateTexture(ImTextureDataPtr tex)
    {
        // Set pixel store alignment for create/update operations
        if (tex.Status == ImTextureStatus.WantCreate || tex.Status == ImTextureStatus.WantUpdates)
        {
            _gl.PixelStore(GLEnum.UnpackRowLength, 0);
            _gl.PixelStore(GLEnum.UnpackAlignment, 1);
        }

        if (tex.Status == ImTextureStatus.WantCreate)
        {
            _gl.GetInteger(GLEnum.TextureBinding2D, out var lastTexture);

            var glTex = _gl.GenTexture();
            _gl.BindTexture(GLEnum.Texture2D, glTex);
            _gl.TexParameterI(
                GLEnum.Texture2D,
                TextureParameterName.TextureMinFilter,
                (int)GLEnum.Linear
            );
            _gl.TexParameterI(
                GLEnum.Texture2D,
                TextureParameterName.TextureMagFilter,
                (int)GLEnum.Linear
            );
            _gl.TexParameterI(
                GLEnum.Texture2D,
                TextureParameterName.TextureWrapS,
                (int)GLEnum.ClampToEdge
            );
            _gl.TexParameterI(
                GLEnum.Texture2D,
                TextureParameterName.TextureWrapT,
                (int)GLEnum.ClampToEdge
            );

            _gl.TexImage2D(
                GLEnum.Texture2D,
                0,
                (int)GLEnum.Rgba,
                (uint)tex.Width,
                (uint)tex.Height,
                0,
                GLEnum.Rgba,
                GLEnum.UnsignedByte,
                tex.GetPixels()
            );

            tex.SetTexID(new ImTextureID((nuint)glTex));
            tex.SetStatus(ImTextureStatus.Ok);

            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        }
        else if (tex.Status == ImTextureStatus.WantUpdates)
        {
            _gl.GetInteger(GLEnum.TextureBinding2D, out var lastTexture);

            var glTex = (uint)tex.TexID.Handle;
            _gl.BindTexture(GLEnum.Texture2D, glTex);

            _gl.PixelStore(GLEnum.UnpackRowLength, tex.Width);
            for (var i = 0; i < tex.Updates.Size; i++)
            {
                var r = tex.Updates[i];
                _gl.TexSubImage2D(
                    GLEnum.Texture2D,
                    0,
                    r.X,
                    r.Y,
                    r.W,
                    r.H,
                    GLEnum.Rgba,
                    GLEnum.UnsignedByte,
                    tex.GetPixelsAt(r.X, r.Y)
                );
            }

            _gl.PixelStore(GLEnum.UnpackRowLength, 0);
            tex.SetStatus(ImTextureStatus.Ok);

            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        }
        else if (tex.Status == ImTextureStatus.WantDestroy && tex.UnusedFrames > 0)
        {
            var glTex = (uint)tex.TexID.Handle;
            if (glTex != 0)
            {
                _gl.DeleteTexture(glTex);
            }

            tex.SetTexID(default);
            tex.BackendUserData = null;
            tex.SetStatus(ImTextureStatus.Destroyed);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetClipboardDelegate(IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetClipboardDelegate();
}
