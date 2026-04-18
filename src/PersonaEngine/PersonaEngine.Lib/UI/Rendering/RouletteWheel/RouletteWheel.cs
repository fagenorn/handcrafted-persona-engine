using System.Drawing;
using System.Numerics;
using FontStashSharp;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.Rendering.Text;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Shader = PersonaEngine.Lib.UI.Common.Shader;

namespace PersonaEngine.Lib.UI.Rendering.RouletteWheel;

public partial class RouletteWheel : IRenderComponent
{
    private const int MAX_VERTICES = 64;

    private const int MAX_INDICES = MAX_VERTICES * 6 / 4;

    private const float OUTER_RADIUS_FACTOR = 0.8f;

    private const float INNER_RADIUS_FACTOR = 0.64f; // OUTER_RADIUS_FACTOR * 0.8f

    private const float CENTER_RADIUS_FACTOR = 0.128f; // INNER_RADIUS_FACTOR * 0.2f

    private static readonly short[] _indexData = GenerateIndexArray();

    private readonly Lock _applyingConfig = new();

    private readonly IOptionsMonitor<RouletteWheelOptions> _config;

    private readonly FontProvider _fontProvider;

    private readonly Dictionary<int, int> _segmentFontSizes = new();

    private readonly float _textSizeAdaptationFactor = 1.0f;

    private readonly VertexPositionTexture[] _vertexData = new VertexPositionTexture[MAX_VERTICES];

    private GL _gl;

    private BufferObject<short> _indexBuffer;

    private float _minRotations;

    private int _numSections = 0;

    private Action<int>? _onSpinCompleteCallback;

    private Vector2 _position = Vector2.Zero;

    private bool _radialTextOrientation = true;

    private string[] _sectionLabels;

    private Shader _shader;

    private float _spinDuration;

    private float _spinStartTime = 0f;

    private float _startSegment = 0f;

    private float _targetSegment = 0f;

    private FSColor _textColor = new(255, 255, 255, 255);

    private TextRenderer _textRenderer;

    private float _textScale = 1.0f;

    private int _textStrokeWidth = 2;

    private float _time = 0f;

    private bool _useAdaptiveTextSize = true;

    private VertexArrayObject _vao;

    private BufferObject<VertexPositionTexture> _vertexBuffer;

    private int _vertexIndex = 0;

    private int _viewportHeight;

    private int _viewportWidth;

    private float _wheelSize = 1;

    public RouletteWheel(IOptionsMonitor<RouletteWheelOptions> config, FontProvider fontProvider)
    {
        _config = config;
        _fontProvider = fontProvider;
    }

    public bool IsSpinning { get; private set; } = false;

    public float Progress { get; private set; } = 1f;

    public int NumberOfSections
    {
        get => _numSections;
        private set
        {
            _numSections = Math.Clamp(value, 2, 24);
            _targetSegment = Math.Min(_targetSegment, _numSections - 1);
            _startSegment = Math.Min(_startSegment, _numSections - 1);
            ResizeSectionLabels();

            if (_useAdaptiveTextSize)
            {
                CalculateAllSegmentFontSizes();
            }
        }
    }

    public bool UseSpout => true;

    public string SpoutTarget => "RouletteWheel";

    public int Priority => 0;

    public void Initialize(GL gl, IView view, IInputContext input)
    {
        _gl = gl;
        _textRenderer = new TextRenderer(gl);

        // Initialize buffers
        _vertexBuffer = new BufferObject<VertexPositionTexture>(
            gl,
            MAX_VERTICES,
            BufferTargetARB.ArrayBuffer,
            true
        );
        _indexBuffer = new BufferObject<short>(
            gl,
            _indexData.Length,
            BufferTargetARB.ElementArrayBuffer,
            false
        );
        _indexBuffer.SetData(_indexData, 0, _indexData.Length);

        // Initialize shader
        var vertSrc = File.ReadAllText(Path.Combine(@"Resources/Shaders", "wheel_shader.vert"));
        var fragSrc = File.ReadAllText(Path.Combine(@"Resources/Shaders", "wheel_shader.frag"));
        _shader = new Shader(_gl, vertSrc, fragSrc);

        // Setup VAO
        unsafe
        {
            _vao = new VertexArrayObject(gl, sizeof(VertexPositionTexture));
            _vao.Bind();
        }

        var location = _shader.GetAttribLocation("a_position");
        _vao.VertexAttribPointer(location, 3, VertexAttribPointerType.Float, false, 0);

        location = _shader.GetAttribLocation("a_texCoords0");
        _vao.VertexAttribPointer(location, 2, VertexAttribPointerType.Float, false, 12);

        ResizeSectionLabels();

        ApplyConfiguration(_config.CurrentValue);

        _config.OnChange(ApplyConfiguration);

        // Prevent wheel from spinning on creation
        _spinStartTime = -_spinDuration;
    }

    public void Update(float deltaTime)
    {
        _time += deltaTime;

        if (IsSpinning)
        {
            var progress = Math.Min((_time - _spinStartTime) / _spinDuration, 1.0f);
            Progress = progress;
        }

        if (IsSpinning && _time - _spinStartTime >= _spinDuration)
        {
            IsSpinning = false;
            _onSpinCompleteCallback?.Invoke((int)_targetSegment);
            _onSpinCompleteCallback = null;
        }

        UpdateVisibilityAnimation();
    }

    public void Render(float deltaTime)
    {
        lock (_applyingConfig)
        {
            // Skip rendering if wheel is disabled and not animating
            if (!IsEnabled && CurrentAnimationState == AnimationState.Idle)
            {
                return;
            }

            var originalWheelSize = _wheelSize;
            _wheelSize *= _animationCurrentScale;

            Begin();
            DrawWheel();
            End();

            // Only render labels if wheel is sufficiently visible
            if (_animationCurrentScale > 0.25f)
            {
                RenderSectionLabels();
            }

            _wheelSize = originalWheelSize;
        }
    }

    public void Dispose()
    {
        // GL context is destroyed when the app closes — individual resource
        // disposal is unnecessary and the handles are already invalid by the
        // time the DI container tears down singletons.
    }

    public string GetLabel(int index)
    {
        return _sectionLabels[index];
    }

    public string[] GetLabels()
    {
        return _sectionLabels.ToArray();
    }

    public void Spin(int targetSection, Action<int>? onSpinComplete = null)
    {
        if (IsSpinning || !IsEnabled || CurrentAnimationState != AnimationState.Idle)
        {
            return;
        }

        _targetSegment = Math.Clamp(targetSection, 0, _numSections - 1);
        _startSegment = GetCurrentWheelPosition();
        _spinStartTime = _time;
        IsSpinning = true;
        _onSpinCompleteCallback = onSpinComplete;
        Progress = 0;
    }

    public int SpinRandom(Action<int>? onSpinComplete = null)
    {
        if (IsSpinning || !IsEnabled || CurrentAnimationState != AnimationState.Idle)
        {
            return -1;
        }

        var target = Random.Shared.Next(_numSections);
        Spin(target, onSpinComplete);

        return target;
    }

    public Task<int> SpinAsync(int? targetSection = null)
    {
        if (IsSpinning || !IsEnabled || CurrentAnimationState != AnimationState.Idle)
        {
            return Task.FromResult(-1);
        }

        var tcs = new TaskCompletionSource<int>();
        var target = targetSection ?? Random.Shared.Next(_numSections);

        Spin(target, result => tcs.SetResult(result));

        return tcs.Task;
    }

    public void SetSectionLabels(string[] labels)
    {
        if (labels.Length == 0)
        {
            return;
        }

        if (labels.Length != _numSections)
        {
            NumberOfSections = labels.Length;
        }

        for (var i = 0; i < _numSections; i++)
        {
            _sectionLabels[i] = labels[i];
        }

        if (_useAdaptiveTextSize)
        {
            CalculateAllSegmentFontSizes();
        }
    }

    public void ConfigureTextStyle(
        bool radialText = true,
        Color? textColor = null,
        float scale = 1.0f,
        int strokeWidth = 2,
        bool useAdaptiveSize = true
    )
    {
        var color = textColor ?? Color.White;

        _textColor = new FSColor(color.R, color.G, color.B, color.A);
        _textScale = scale;
        _textStrokeWidth = strokeWidth;
        _radialTextOrientation = radialText;
        _useAdaptiveTextSize = useAdaptiveSize;

        if (_useAdaptiveTextSize)
        {
            CalculateAllSegmentFontSizes();
        }
    }

    private static short[] GenerateIndexArray()
    {
        var result = new short[MAX_INDICES];
        for (int i = 0, vi = 0; i < MAX_INDICES; i += 6, vi += 4)
        {
            result[i] = (short)vi;
            result[i + 1] = (short)(vi + 1);
            result[i + 2] = (short)(vi + 2);
            result[i + 3] = (short)(vi + 1);
            result[i + 4] = (short)(vi + 3);
            result[i + 5] = (short)(vi + 2);
        }

        return result;
    }

    private void Begin()
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _shader.Use();
        UpdateShaderParameters();

        var projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            0,
            _viewportWidth,
            _viewportHeight,
            0,
            -1,
            1
        );
        _shader.SetUniform("MatrixTransform", projectionMatrix);

        _vao.Bind();
        _vertexBuffer.Bind();
        _indexBuffer.Bind();
    }

    private void End()
    {
        FlushBuffer();
    }

    private unsafe void FlushBuffer()
    {
        if (_vertexIndex == 0)
        {
            return;
        }

        _vertexBuffer.SetData(_vertexData, 0, _vertexIndex);
        _gl.DrawElements(
            PrimitiveType.Triangles,
            (uint)(_vertexIndex * 6 / 4),
            DrawElementsType.UnsignedShort,
            null
        );
        _vertexIndex = 0;
    }

    private void DrawWheel()
    {
        // Cache these calculations for performance
        float minDimension = Math.Min(_viewportWidth, _viewportHeight);
        var radius = minDimension * _wheelSize * OUTER_RADIUS_FACTOR / 2.0f;
        var centerX = _position.X;
        var centerY = _position.Y;
        var cosR = (float)Math.Cos(Rotation);
        var sinR = (float)Math.Sin(Rotation);
        var left = centerX - radius;
        var right = centerX + radius;
        var top = centerY - radius;
        var bottom = centerY + radius;

        // Top-left vertex
        _vertexData[_vertexIndex++] = new VertexPositionTexture(
            RotatePoint(new Vector3(left, top, 0), centerX, centerY, cosR, sinR),
            new Vector2(0, 0)
        );

        // Top-right vertex
        _vertexData[_vertexIndex++] = new VertexPositionTexture(
            RotatePoint(new Vector3(right, top, 0), centerX, centerY, cosR, sinR),
            new Vector2(1, 0)
        );

        // Bottom-left vertex
        _vertexData[_vertexIndex++] = new VertexPositionTexture(
            RotatePoint(new Vector3(left, bottom, 0), centerX, centerY, cosR, sinR),
            new Vector2(0, 1)
        );

        // Bottom-right vertex
        _vertexData[_vertexIndex++] = new VertexPositionTexture(
            RotatePoint(new Vector3(right, bottom, 0), centerX, centerY, cosR, sinR),
            new Vector2(1, 1)
        );
    }

    private void RenderSectionLabels()
    {
        // Cache these calculations for performance
        float minDimension = Math.Min(_viewportWidth, _viewportHeight);
        var radius = minDimension * _wheelSize * OUTER_RADIUS_FACTOR / 2.0f;
        var innerRadius = radius * INNER_RADIUS_FACTOR;
        var centerRadius = radius * CENTER_RADIUS_FACTOR;
        var centerX = _position.X != 0 ? _position.X : _viewportWidth / 2.0f;
        var centerY = _position.Y != 0 ? _position.Y : _viewportHeight / 2.0f;
        var availableRadialSpace = innerRadius - centerRadius;
        var textRadius = centerRadius + availableRadialSpace * 0.5f;
        var currentRotation = GetCurrentWheelRotation();
        var segmentAngle = 2.0f * MathF.PI / _numSections;

        if (_useAdaptiveTextSize && _segmentFontSizes.Count != _numSections)
        {
            CalculateAllSegmentFontSizes();
        }

        _textRenderer.Begin();

        var fontSystem = _fontProvider.GetFontSystem(_config.CurrentValue.Font);

        // Reusable Vector2 for text position to avoid allocation in the loop
        var position = new Vector2();

        for (var i = 0; i < _numSections; i++)
        {
            if (string.IsNullOrEmpty(_sectionLabels[i]))
            {
                continue;
            }

            var fontSize = _useAdaptiveTextSize
                ? _segmentFontSizes[i]
                : _config.CurrentValue.FontSize;
            var segmentFont = fontSystem.GetFont(fontSize);

            var rotatedStartAngle = -currentRotation;
            var rotatedEndAngle = segmentAngle - currentRotation;
            var segmentCenter =
                (rotatedStartAngle + rotatedEndAngle) / 2.0f
                + (_numSections - 1 - i) * segmentAngle;

            // Position text
            var textX = centerX + textRadius * MathF.Cos(segmentCenter);
            var textY = centerY + textRadius * MathF.Sin(segmentCenter);
            var textSize = segmentFont.MeasureString(_sectionLabels[i]);

            // Update position instead of creating a new Vector2
            position.X = textX;
            position.Y = textY;

            // Calculate text rotation
            var textRotation = _radialTextOrientation
                ? MathF.Atan2(textY - centerY, textX - centerX)
                : 0.0f;

            // Draw text
            segmentFont.DrawText(
                _textRenderer,
                _sectionLabels[i],
                position,
                _textColor,
                textRotation,
                scale: new Vector2(_textScale),
                effect: FontSystemEffect.Stroked,
                effectAmount: _textStrokeWidth,
                origin: textSize / 2
            );
        }

        _textRenderer.End();
    }

    private Vector3 RotatePoint(Vector3 point, float centerX, float centerY, float cosR, float sinR)
    {
        var x = point.X - centerX;
        var y = point.Y - centerY;

        var newX = x * cosR - y * sinR + centerX;
        var newY = x * sinR + y * cosR + centerY;

        return new Vector3(newX, newY, point.Z);
    }

    private float GetCurrentWheelPosition()
    {
        if (!IsSpinning)
        {
            return _targetSegment;
        }

        if (Progress >= 1.0f)
        {
            IsSpinning = false;

            return _targetSegment;
        }

        var easedProgress = EaseOutQuint(Progress);

        var segmentDiff = (_targetSegment - _startSegment + _numSections) % _numSections;
        if (segmentDiff > _numSections / 2.0f)
        {
            segmentDiff -= _numSections;
        }

        return (_startSegment + segmentDiff * easedProgress + _numSections) % _numSections;
    }

    private float GetCurrentWheelRotation()
    {
        var baseRotation = -Rotation;
        var segmentAngle = 2.0f * MathF.PI / _numSections;

        var targetAngle = baseRotation + -(_targetSegment + 0.5f) * segmentAngle + MathF.PI * 1.5f;

        if (!IsSpinning)
        {
            return targetAngle;
        }

        var startAngle = baseRotation + -(_startSegment + 0.5f) * segmentAngle + MathF.PI * 1.5f;
        var easedProgress = EaseOutQuint(Progress);

        var adjustedExtraAngle = targetAngle - startAngle;
        if (adjustedExtraAngle > 0.0)
        {
            adjustedExtraAngle -= 2.0f * MathF.PI;
        }

        var spinningAngle =
            startAngle + (adjustedExtraAngle - _minRotations * 2.0f * MathF.PI) * easedProgress;

        return spinningAngle;
    }

    private void UpdateShaderParameters()
    {
        _shader.SetUniform("u_targetSegment", _targetSegment);
        _shader.SetUniform("u_startSegment", _startSegment);
        _shader.SetUniform("u_spinDuration", _spinDuration);
        _shader.SetUniform("u_minRotations", _minRotations);
        _shader.SetUniform("u_spinStartTime", _spinStartTime);
        _shader.SetUniform("u_time", _time);
        _shader.SetUniform("u_numSlices", (float)_numSections);

        float size = Math.Min(_viewportWidth, _viewportHeight);
        _shader.SetUniform("u_resolution", size, size);
    }

    private int CalculateOptimalTextSizeForSegment(string label, int segmentIndex)
    {
        if (string.IsNullOrEmpty(label))
        {
            return 24;
        }

        // Cache these calculations for performance
        float minDimension = Math.Min(_viewportWidth, _viewportHeight);
        var radius = minDimension * _wheelSize * OUTER_RADIUS_FACTOR / 2.0f;
        var innerRadius = radius * INNER_RADIUS_FACTOR;
        var centerRadius = radius * CENTER_RADIUS_FACTOR;
        var availableRadialSpace = innerRadius - centerRadius;
        var textRadius = centerRadius + availableRadialSpace * 0.5f;
        var segmentAngle = 2.0f * MathF.PI / _numSections;
        var maxWidth = 2.0f * textRadius * MathF.Sin(segmentAngle / 2.0f);

        var fontSystem = _fontProvider.GetFontSystem(_config.CurrentValue.Font);

        // Binary search for optimal font size
        var minFontSize = 1;
        var maxFontSize = 72;
        var optimalFontSize = _config.CurrentValue.FontSize;

        while (minFontSize <= maxFontSize)
        {
            var currentFontSize = (minFontSize + maxFontSize) / 2;

            var testFont = fontSystem.GetFont(currentFontSize);
            var textSize = testFont.MeasureString(label);
            var scaledWidth = textSize.X * _textScale * _textSizeAdaptationFactor;

            if (scaledWidth <= maxWidth * INNER_RADIUS_FACTOR)
            {
                optimalFontSize = currentFontSize;
                minFontSize = currentFontSize + 1;
            }
            else
            {
                maxFontSize = currentFontSize - 1;
            }
        }

        return optimalFontSize;
    }

    private void CalculateAllSegmentFontSizes()
    {
        if (!_useAdaptiveTextSize)
        {
            return;
        }

        _segmentFontSizes.Clear();

        for (var i = 0; i < _numSections; i++)
        {
            _segmentFontSizes[i] = string.IsNullOrEmpty(_sectionLabels[i])
                ? 24
                : CalculateOptimalTextSizeForSegment(_sectionLabels[i], i);
        }
    }

    private void ResizeSectionLabels()
    {
        var newLabels = new string[_numSections];
        for (var i = 0; i < _numSections; i++)
        {
            newLabels[i] = i < _sectionLabels?.Length ? _sectionLabels[i] : $"Section {i + 1}";
        }

        _sectionLabels = newLabels;
    }

    private static float EaseOutQuint(float t)
    {
        return 1.0f - MathF.Pow(1.0f - t, 5);
    }

    private void ApplyConfiguration(RouletteWheelOptions options)
    {
        lock (_applyingConfig)
        {
            // Apply text configuration
            _radialTextOrientation = options.RadialTextOrientation;
            _textScale = options.TextScale;
            _textStrokeWidth = options.TextStroke;
            _useAdaptiveTextSize = options.AdaptiveText;

            if (!string.IsNullOrEmpty(options.TextColor))
            {
                var color = ColorTranslator.FromHtml(options.TextColor);
                _textColor = new FSColor(color.R, color.G, color.B, color.A);
            }

            // Apply section labels
            if (options.SectionLabels?.Length > 0)
            {
                SetSectionLabels(options.SectionLabels);
            }

            // Apply spin parameters
            _spinDuration = Math.Max(options.SpinDuration, 0.1f);
            _minRotations = Math.Max(options.MinRotations, 1.0f);

            // Apply wheel size
            _wheelSize = Math.Clamp(options.WheelSizePercentage, 0.1f, 1.0f);

            // Apply positioning
            if (!string.IsNullOrEmpty(options.PositionMode))
            {
                switch (options.PositionMode)
                {
                    case "Absolute":
                        _positionMode = PositionMode.Absolute;
                        SetAbsolutePosition(options.AbsolutePositionX, options.AbsolutePositionY);

                        break;
                    case "Percentage":
                        _positionMode = PositionMode.Percentage;
                        PositionByPercentage(
                            options.PositionXPercentage,
                            options.PositionYPercentage
                        );

                        break;
                    case "Anchored":
                        _positionMode = PositionMode.Anchored;
                        if (Enum.TryParse(options.ViewportAnchor, out ViewportAnchor anchor))
                        {
                            PositionAt(
                                anchor,
                                new Vector2(options.AnchorOffsetX, options.AnchorOffsetY)
                            );
                        }
                        else
                        {
                            PositionAt(ViewportAnchor.Center);
                        }

                        break;
                }
            }

            // Apply rotation
            Rotation = options.RotationDegrees * MathF.PI / 180;

            // Handle viewport size changes
            var viewportChanged =
                _viewportWidth != options.Width || _viewportHeight != options.Height;
            if (viewportChanged)
            {
                _viewportWidth = options.Width;
                _viewportHeight = options.Height;

                // Update text renderer viewport
                _textRenderer.OnViewportChanged(_viewportWidth, _viewportHeight);

                // Update position based on mode
                switch (_positionMode)
                {
                    case PositionMode.Anchored:
                        UpdatePositionFromAnchor();

                        break;
                    case PositionMode.Percentage:
                        UpdatePositionFromPercentage();

                        break;
                }
            }

            // Apply enabled state (if changed)
            if (options.Enabled != IsEnabled)
            {
                if (options.Enabled)
                {
                    Enable();
                }
                else
                {
                    Disable();
                }
            }

            // Recalculate font sizes if needed (after all other changes)
            if (_useAdaptiveTextSize)
            {
                CalculateAllSegmentFontSizes();
            }
        }
    }
}
