using Microsoft.Extensions.Options;

using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.Live2D.App;
using PersonaEngine.Lib.Live2D.Behaviour;
using PersonaEngine.Lib.Live2D.Framework.Rendering;
using PersonaEngine.Lib.UI;

using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace PersonaEngine.Lib.Live2D;

public class Live2DManager : IRenderComponent
{
    private readonly IList<ILive2DAnimationService> _live2DAnimationServices;

    private readonly IOptionsMonitor<Live2DOptions> _options;

    private LAppDelegate? _lapp;

    public Live2DManager(IOptionsMonitor<Live2DOptions> options, IEnumerable<ILive2DAnimationService> live2DAnimationServices)
    {
        _options                 = options;
        _live2DAnimationServices = live2DAnimationServices.ToList();
    }

    public bool UseSpout => true;

    public string SpoutTarget => "Live2D";

    public int Priority => 100;

    public void Update(float deltaTime) { }

    public void Render(float deltaTime)
    {
        if ( _lapp == null )
        {
            return;
        }

        _lapp.Update(deltaTime);
        _lapp.Run();
    }

    public void Resize() { _lapp?.Resize(); }

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        var config = _options.CurrentValue;
        _lapp = new LAppDelegate(new SilkNetApi(gl, config.Width, config.Height), _ => { }) { BGColor = new CubismTextureColor(0, 0, 0, 0) };

        LoadModel(config.ModelPath, config.ModelName);
    }

    public void Dispose()
    {
        // Context is destroyed anyway when app closes.
    }

    /// <summary>
    ///     Loads a Live2D model from the given path.
    /// </summary>
    public void LoadModel(string modelPath, string modelName)
    {
        if ( _lapp == null )
        {
            throw new InvalidOperationException("Live2DManager is not initialized.");
        }

        var model = _lapp.Live2dManager.LoadModel(modelPath, modelName);
        model.RandomMotion      = false;
        model.CustomValueUpdate = true;

        // model.ModelMatrix.Translate(0.0f, -1.8f);
        model.ModelMatrix.ScaleRelative(0.7f, 0.7f);

        Resize();

        foreach ( var animationService in _live2DAnimationServices )
        {
            model.ValueUpdate += _ => animationService.Update(LAppPal.DeltaTime);
            animationService.Start(model);
        }
    }
}