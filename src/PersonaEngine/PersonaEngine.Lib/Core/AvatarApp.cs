using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;
using PersonaEngine.Lib.UI;
using PersonaEngine.Lib.UI.Common;
using PersonaEngine.Lib.UI.GUI;
using PersonaEngine.Lib.UI.Spout;

using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.Core;

public class AvatarApp : IDisposable
{
    private readonly IOptions<AvatarAppConfig> _config;

    private readonly IConversationOrchestrator _conversationOrchestrator;

    private readonly IReadOnlyList<IRenderComponent> _regularComponents;

    private readonly Dictionary<string, List<IRenderComponent>> _spoutComponents = new();

    private readonly IReadOnlyList<IStartupTask> _startupTasks;

    private readonly IWindow _window;

    private readonly WindowConfiguration _windowConfig;

    private readonly WindowManager _windowManager;

    private GL _gl;

    private ImGuiController _imGui;

    private IInputContext _inputContext;
    
    private SpoutRegistry _spoutRegistry;

    public AvatarApp(IOptions<AvatarAppConfig> config, IEnumerable<IRenderComponent> renderComponents, IEnumerable<IStartupTask> startupTasks, IConversationOrchestrator conversationOrchestrator)
    {
        _config                   = config;
        _conversationOrchestrator = conversationOrchestrator;

        var allComponents = renderComponents.OrderByDescending(x => x.Priority).ToList();

        // Group components by spout target
        _regularComponents = allComponents.Where(x => !x.UseSpout).ToList();

        // Group spout components by their target
        foreach ( var component in allComponents.Where(x => x.UseSpout) )
        {
            if ( !_spoutComponents.TryGetValue(component.SpoutTarget, out var componentList) )
            {
                componentList                           = new List<IRenderComponent>();
                _spoutComponents[component.SpoutTarget] = componentList;
            }

            componentList.Add(component);
        }

        _windowConfig  = _config.Value.Window;
        _windowManager = new WindowManager(new Vector2D<int>(_windowConfig.Width, _windowConfig.Height), _windowConfig.Title);
        _window        = _windowManager.MainWindow;

        _windowManager.Load        += OnLoad;
        _windowManager.Update      += OnUpdate;
        _windowManager.RenderFrame += OnRender;
        _windowManager.Resize      += OnResize;
        _windowManager.Close       += OnClose;

        _startupTasks = startupTasks.ToList();
    }

    public void Dispose()
    {
        // Context is destroyed anyway when app closes.

        return;

        _spoutRegistry?.Dispose();
        _imGui.Dispose();
    }

    private void OnLoad()
    {
        // Set up keyboard input.
        _inputContext = _window.CreateInput();
        foreach ( var keyboard in _inputContext.Keyboards )
        {
            keyboard.KeyDown += OnKeyDown;
        }

        _gl = _windowManager.GL;

        foreach ( var task in _startupTasks )
        {
            task.Execute(_gl);
        }

        _spoutRegistry = new SpoutRegistry(_gl, _config.Value.SpoutConfigs);

        _imGui = new ImGuiController(_gl, _window, _inputContext, Path.Combine(@"Resources\Fonts", @"Montserrat-Medium.ttf"), Path.Combine(@"Resources\Fonts", @"seguiemj.ttf"));

        InitializeComponents(_regularComponents);

        foreach ( var componentGroup in _spoutComponents.Values )
        {
            InitializeComponents(componentGroup);
        }

        _ = _conversationOrchestrator.StartNewSessionAsync();
    }

    private void InitializeComponents(IEnumerable<IRenderComponent> components)
    {
        foreach ( var component in components )
        {
            component.Initialize(_gl, _window, _inputContext);
        }
    }

    private void OnUpdate(double deltaTime)
    {
        // Update all components
        UpdateComponents(_regularComponents, (float)deltaTime);

        foreach ( var componentGroup in _spoutComponents.Values )
        {
            UpdateComponents(componentGroup, (float)deltaTime);
        }
    }

    private void UpdateComponents(IEnumerable<IRenderComponent> components, float deltaTime)
    {
        foreach ( var component in components )
        {
            component.Update(deltaTime);
        }
    }

    private void OnRender(double deltaTime)
    {
        _imGui.Update((float)deltaTime);
        _gl.Viewport(0, 0, (uint)_windowConfig.Width, (uint)_windowConfig.Height);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        // Render regular components to the screen
        foreach ( var component in _regularComponents )
        {
            component.Render((float)deltaTime);
        }

        _imGui.Render();

        // Render components to their respective Spout outputs
        foreach ( var spoutGroup in _spoutComponents )
        {
            var spoutTarget = spoutGroup.Key;
            var components  = spoutGroup.Value;

            // Begin frame for this spout target
            _spoutRegistry.BeginFrame(spoutTarget);

            // Render all components for this spout target
            foreach ( var component in components )
            {
                component.Render((float)deltaTime);
            }

            _spoutRegistry.SendFrame(spoutTarget);
        }
    }

    private void OnResize(Vector2D<int> size)
    {
        // Resize all components
        ResizeComponents(_regularComponents);
    }

    private void ResizeComponents(IEnumerable<IRenderComponent> components)
    {
        foreach ( var component in components )
        {
            component.Resize();
        }
    }

    private void OnClose() { }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        switch ( key )
        {
            case Key.Escape:
                _window.Close();

                break;
        }
    }

    public void Run() { _windowManager.Run(); }
}