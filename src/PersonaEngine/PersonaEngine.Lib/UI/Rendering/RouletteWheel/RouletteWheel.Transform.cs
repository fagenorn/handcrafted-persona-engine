using System.Numerics;

namespace PersonaEngine.Lib.UI.Rendering.RouletteWheel;

public partial class RouletteWheel
{
    /// <summary>
    ///     Defines how the wheel position is tracked and updated during window resizing.
    /// </summary>
    public enum PositionMode
    {
        /// <summary>
        ///     Position is fixed in absolute pixels and won't change during resizing.
        /// </summary>
        Absolute,

        /// <summary>
        ///     Position is based on viewport percentages and will scale with resizing.
        /// </summary>
        Percentage,

        /// <summary>
        ///     Position is based on viewport anchors and will adapt during resizing.
        /// </summary>
        Anchored,
    }

    /// <summary>
    ///     Represents anchor points within the viewport for positioning the wheel.
    /// </summary>
    public enum ViewportAnchor
    {
        Center,

        TopLeft,

        TopCenter,

        TopRight,

        MiddleLeft,

        MiddleRight,

        BottomLeft,

        BottomCenter,

        BottomRight,
    }

    private Vector2 _anchorOffset = Vector2.Zero;

    private ViewportAnchor _currentAnchor = ViewportAnchor.Center;

    private PositionMode _positionMode = PositionMode.Absolute;

    private Vector2 _positionPercentage = new(0.5f, 0.5f);

    /// <summary>
    ///     Gets the current radius of the wheel in pixels.
    /// </summary>
    public float Radius => Diameter / 2;

    /// <summary>
    ///     Gets the current diameter of the wheel in pixels.
    /// </summary>
    public float Diameter =>
        Math.Min(_viewportWidth, _viewportHeight) * _wheelSize * OUTER_RADIUS_FACTOR;

    /// <summary>
    ///     Gets or sets the rotation of the wheel in radians.
    /// </summary>
    public float Rotation { get; private set; } = 0f;

    /// <summary>
    ///     Gets or sets the rotation of the wheel in degrees.
    /// </summary>
    public float RotationDegrees => Rotation * 180 / MathF.PI;

    public void Resize()
    {
        _viewportWidth = _config.CurrentValue.Width;
        _viewportHeight = _config.CurrentValue.Height;
        _textRenderer.OnViewportChanged(_viewportWidth, _viewportHeight);

        switch (_positionMode)
        {
            case PositionMode.Anchored:
                UpdatePositionFromAnchor();

                break;
            case PositionMode.Percentage:
                UpdatePositionFromPercentage();

                break;
            case PositionMode.Absolute:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (_useAdaptiveTextSize)
        {
            CalculateAllSegmentFontSizes();
        }
    }

    /// <summary>
    ///     Rotates the wheel by the specified angle in radians.
    /// </summary>
    public void RotateBy(float angleRadians)
    {
        Rotation += angleRadians;
    }

    /// <summary>
    ///     Rotates the wheel by the specified angle in radians.
    /// </summary>
    public void RotateByDegrees(float angleDegrees)
    {
        Rotation += angleDegrees * MathF.PI / 180;
    }

    /// <summary>
    ///     Sets the diameter of the wheel in pixels.
    /// </summary>
    public void SetDiameter(float diameter)
    {
        float minDimension = Math.Min(_viewportWidth, _viewportHeight);
        _wheelSize = Math.Clamp(diameter / minDimension, 0.1f, 1.0f);
    }

    /// <summary>
    ///     Sets the radius of the wheel in pixels.
    /// </summary>
    public void SetRadius(float radius)
    {
        SetDiameter(radius * 2);
    }

    /// <summary>
    ///     Sets the wheel size as a percentage of the viewport (0.0 to 1.0).
    /// </summary>
    /// <param name="percentage">Value between 0.0 and 1.0</param>
    public void SetSizePercentage(float percentage)
    {
        _wheelSize = Math.Clamp(percentage, 0.1f, 1.0f);
    }

    /// <summary>
    ///     Scales the wheel size by the specified factor relative to its current size.
    /// </summary>
    public void Scale(float factor)
    {
        _wheelSize = Math.Clamp(_wheelSize * factor, 0.1f, 1.0f);
    }

    /// <summary>
    ///     Translates the wheel by the specified amount.
    /// </summary>
    public void Translate(float deltaX, float deltaY)
    {
        _positionMode = PositionMode.Absolute;
        _position = new Vector2(_position.X + deltaX, _position.Y + deltaY);
    }

    /// <summary>
    ///     Translates the wheel by the specified vector.
    /// </summary>
    public void Translate(Vector2 delta)
    {
        _positionMode = PositionMode.Absolute;
        _position += delta;
    }

    /// <summary>
    ///     Sets the position using viewport percentages (0.0 to 1.0 for each axis)
    /// </summary>
    public void PositionByPercentage(float xPercent, float yPercent)
    {
        _positionMode = PositionMode.Percentage;
        _positionPercentage = new Vector2(Math.Clamp(xPercent, 0, 1), Math.Clamp(yPercent, 0, 1));

        UpdatePositionFromPercentage();
    }

    /// <summary>
    ///     Updates position based on viewport percentages
    /// </summary>
    private void UpdatePositionFromPercentage()
    {
        _position = new Vector2(
            _viewportWidth * _positionPercentage.X,
            _viewportHeight * _positionPercentage.Y
        );
    }

    /// <summary>
    ///     Sets the position in absolute pixels. This will not adjust with window resizing.
    /// </summary>
    public void SetAbsolutePosition(float x, float y)
    {
        _positionMode = PositionMode.Absolute;
        _position = new Vector2(x, y);
    }

    /// <summary>
    ///     Positions the wheel at the specified viewport anchor with an optional offset.
    /// </summary>
    public void PositionAt(ViewportAnchor anchor, Vector2 offset = default)
    {
        _positionMode = PositionMode.Anchored;
        _currentAnchor = anchor;
        _anchorOffset = offset;

        UpdatePositionFromAnchor();
    }

    /// <summary>
    ///     Updates position based on the current anchor and offset
    /// </summary>
    private void UpdatePositionFromAnchor()
    {
        float x = 0,
            y = 0;

        switch (_currentAnchor)
        {
            case ViewportAnchor.Center:
                x = _viewportWidth / 2.0f;
                y = _viewportHeight / 2.0f;

                break;
            case ViewportAnchor.TopLeft:
                x = Radius;
                y = Radius;

                break;
            case ViewportAnchor.TopCenter:
                x = _viewportWidth / 2.0f;
                y = Radius;

                break;
            case ViewportAnchor.TopRight:
                x = _viewportWidth - Radius;
                y = Radius;

                break;
            case ViewportAnchor.MiddleLeft:
                x = Radius;
                y = _viewportHeight / 2.0f;

                break;
            case ViewportAnchor.MiddleRight:
                x = _viewportWidth - Radius;
                y = _viewportHeight / 2.0f;

                break;
            case ViewportAnchor.BottomLeft:
                x = Radius;
                y = _viewportHeight - Radius;

                break;
            case ViewportAnchor.BottomCenter:
                x = _viewportWidth / 2.0f;
                y = _viewportHeight - Radius;

                break;
            case ViewportAnchor.BottomRight:
                x = _viewportWidth - Radius;
                y = _viewportHeight - Radius;

                break;
        }

        _position = new Vector2(x + _anchorOffset.X, y + _anchorOffset.Y);
    }

    /// <summary>
    ///     Centers the wheel in the viewport.
    /// </summary>
    public void CenterInViewport()
    {
        _position = new Vector2(_viewportWidth / 2.0f, _viewportHeight / 2.0f);
    }
}
