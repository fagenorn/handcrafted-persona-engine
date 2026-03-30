using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Widgets;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.GUI;

public class RouletteWheelEditor : ConfigSectionEditorBase
{
    private readonly string[] _anchorOptions =
    {
        "TopLeft",
        "TopCenter",
        "TopRight",
        "MiddleLeft",
        "MiddleCenter",
        "MiddleRight",
        "BottomLeft",
        "BottomCenter",
        "BottomRight",
    };

    private readonly FontProvider _fontProvider;

    private readonly string[] _positionOptions = { "Center", "Custom Position", "Anchor Position" };

    private readonly IUiThemeManager _themeManager;

    private readonly RouletteWheel.RouletteWheel _wheel;

    private bool _animateToggle;

    private float _animationDuration;

    private List<string> _availableFonts = new();

    private RouletteWheelOptions _currentConfig;

    private bool _defaultEnabled;

    private string _fontName;

    private int _fontSize;

    private int _height;

    private bool _loadingFonts = false;

    private float _minRotations;

    private string _newSectionLabel = string.Empty;

    private bool _radialTextOrientation = true;

    private float _rotationDegrees;

    private string[] _sectionLabels;

    private int _selectedAnchor = 4;

    private int _selectedPositionOption = 0;

    private int _selectedSection = 0;

    private float _spinDuration;

    private ActiveOperation? _spinningOperation = null;

    private int _strokeWidth;

    private string _textColor;

    private float _textScale;

    private bool _useAdaptiveSize = true;

    private int _width;

    private float _xPosition = 0.5f;

    private float _yPosition = 0.5f;

    public RouletteWheelEditor(
        IUiConfigurationManager configManager,
        IEditorStateManager stateManager,
        IUiThemeManager themeManager,
        RouletteWheel.RouletteWheel wheel,
        FontProvider fontProvider
    )
        : base(configManager, stateManager)
    {
        _wheel = wheel;
        _themeManager = themeManager;
        _fontProvider = fontProvider;

        _currentConfig = ConfigManager.GetConfiguration<RouletteWheelOptions>("RouletteWheel");
        LoadConfiguration(_currentConfig);
    }

    public override string SectionKey => "Roulette";

    public override string DisplayName => "Roulette Configuration";

    public override void Initialize()
    {
        LoadAvailableFontsAsync();
    }

    private async void Spin(int? targetSection = null)
    {
        if (_wheel.IsSpinning)
        {
            return;
        }

        try
        {
            _spinningOperation = new ActiveOperation("wheel-spinning", "Spinning Wheel");
            StateManager.RegisterActiveOperation(_spinningOperation);
            await _wheel.SpinAsync(targetSection);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Spinning cancelled");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during spinning: {ex.Message}");

            if (_spinningOperation != null)
            {
                StateManager.ClearActiveOperation(_spinningOperation.Id);
                _spinningOperation = null;
            }
        }
    }

    private async void LoadAvailableFontsAsync()
    {
        try
        {
            _loadingFonts = true;

            var operation = new ActiveOperation("load-fonts", "Loading Fonts");
            StateManager.RegisterActiveOperation(operation);

            var fonts = await _fontProvider.GetAvailableFontsAsync();
            _availableFonts = fonts.ToList();

            // Clear operation
            StateManager.ClearActiveOperation(operation.Id);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading fonts: {ex.Message}");
            _availableFonts = [];
        }
        finally
        {
            _loadingFonts = false;
        }
    }

    private void LoadConfiguration(RouletteWheelOptions config)
    {
        _width = config.Width;
        _height = config.Height;
        _fontName = config.Font;
        _fontSize = config.FontSize;
        _textScale = config.TextScale;
        _textColor = config.TextColor;
        _strokeWidth = config.TextStroke;
        _useAdaptiveSize = config.AdaptiveText;
        _radialTextOrientation = config.RadialTextOrientation;
        _sectionLabels = config.SectionLabels;
        _spinDuration = config.SpinDuration;
        _minRotations = config.MinRotations;
        _animateToggle = config.AnimateToggle;
        _animationDuration = config.AnimationDuration;
        _defaultEnabled = config.Enabled;
    }

    public override void Render()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        if (ImGui.BeginTabBar("RouletteConfigTabs"))
        {
            if (ImGui.BeginTabItem("Basic Settings"))
            {
                RenderTestingSection();
                RenderBasicSettingsTab();
                RenderSectionsTab();
                RenderBehaviorTab();
                RenderStyleTab();

                ImGui.EndTabItem();
            }

            RenderPositioningTab();

            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetCursorPosX(availWidth * .5f * .5f);
        if (ImGui.Button("Reset", new Vector2(150, 0)))
        {
            ResetToDefaults();
        }

        ImGui.SameLine(0, 10);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Reset all Wheel settings to default values");
        }

        if (!StateManager.HasUnsavedChanges)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Save", new Vector2(150, 0));
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Save", new Vector2(150, 0)))
        {
            SaveConfiguration();
        }
    }

    private void RenderBasicSettingsTab()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        bool widthChanged,
            heightChanged;

        ImGui.Spacing();
        ImGui.SeparatorText("Wheel Dimensions");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(125);
        widthChanged = ImGui.InputInt("Width##WheelWidth", ref _width, 10);
        if (widthChanged)
        {
            _width = Math.Max(50, _width); // Minimum width
        }

        ImGui.SameLine();

        ImGui.SetCursorPosX(availWidth - 175);
        if (ImGui.Button("Equal Dimensions", new Vector2(175, 0)))
        {
            var size = Math.Max(_width, _height);
            _width = _height = size;
            _wheel.SetDiameter(size);
            widthChanged = true;
            heightChanged = true;
        }

        ImGui.SetNextItemWidth(125);
        heightChanged = ImGui.InputInt("Height##WheelHeight", ref _height, 10);
        if (heightChanged)
        {
            _height = Math.Max(50, _height); // Minimum height
        }

        if (widthChanged || heightChanged)
        {
            UpdateConfiguration();
        }
    }

    private void RenderSectionsTab()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var sectionsChanged = false;

        ImGui.Spacing();
        ImGui.SeparatorText("Wheel Sections");
        ImGui.Spacing();

        if (_sectionLabels.Length > 0)
        {
            ImGui.BeginChild("SectionsList", new Vector2(0, 0), ImGuiChildFlags.AutoResizeY);

            for (var i = 0; i < _sectionLabels.Length; i++)
            {
                ImGui.PushID(i);

                var tempLabel = _sectionLabels[i];
                ImGui.SetNextItemWidth(availWidth - 10 - 30);
                if (ImGui.InputText("##SectionLabel", ref tempLabel, 128))
                {
                    _sectionLabels[i] = tempLabel;
                    sectionsChanged = true;
                }

                ImGui.SameLine(0, 10);

                if (ImGui.Button("X##RemoveSection" + i, new Vector2(30, 0)))
                {
                    var newLabels = new string[_sectionLabels.Length - 1];
                    Array.Copy(_sectionLabels, 0, newLabels, 0, i);
                    Array.Copy(_sectionLabels, i + 1, newLabels, i, _sectionLabels.Length - i - 1);
                    _sectionLabels = newLabels;
                    sectionsChanged = true;
                }

                ImGui.PopID();
            }

            ImGui.EndChild();
        }
        else
        {
            TextHelper.TextCenteredH("No sections defined. Add your first section below.");
        }

        // Add new section
        ImGui.Spacing();
        // ImGui.Separator();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(availWidth - 80 - 10);
        ImGui.InputText("##NewSectionLabel", ref _newSectionLabel, 128);

        ImGui.SameLine(0, 10);

        if (ImGui.Button("Add", new Vector2(80, 0)) && !string.IsNullOrWhiteSpace(_newSectionLabel))
        {
            // Add the new section
            var newLabels = new string[(_sectionLabels?.Length ?? 0) + 1];
            if (_sectionLabels is { Length: > 0 })
            {
                Array.Copy(_sectionLabels, newLabels, _sectionLabels.Length);
            }

            newLabels[^1] = _newSectionLabel;
            _sectionLabels = newLabels;
            _newSectionLabel = string.Empty;
            sectionsChanged = true;
        }

        if (sectionsChanged)
        {
            UpdateConfiguration();
        }
    }

    private void RenderStyleTab()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;
        var configChanged = false;

        ImGui.Spacing();
        ImGui.SeparatorText("Text Appearance");
        ImGui.Spacing();

        // Font selection with refresh button
        {
            ImGui.SetNextItemWidth(availWidth - 120);

            if (_loadingFonts)
            {
                ImGui.BeginDisabled();
                var loadingText = "Loading fonts...";
                ImGui.InputText("##Font", ref loadingText, 100, ImGuiInputTextFlags.ReadOnly);
                ImGui.EndDisabled();
            }
            else
            {
                var fontChanged = false;
                if (
                    ImGui.BeginCombo(
                        "##Font",
                        string.IsNullOrEmpty(_fontName) ? "<Select font>" : _fontName
                    )
                )
                {
                    if (_availableFonts.Count == 0)
                    {
                        ImGui.TextColored(
                            new Vector4(0.7f, 0.7f, 0.7f, 1.0f),
                            "No fonts available"
                        );
                    }
                    else
                    {
                        foreach (var font in _availableFonts)
                        {
                            var isSelected = font == _fontName;
                            if (ImGui.Selectable(font, isSelected))
                            {
                                _fontName = font;
                                fontChanged = true;
                            }

                            if (isSelected)
                            {
                                ImGui.SetItemDefaultFocus();
                            }
                        }
                    }

                    ImGui.EndCombo();
                }

                configChanged |= fontChanged;
            }

            ImGui.SameLine(0, 10);

            if (ImGui.Button("Refresh", new Vector2(-1, 0)))
            {
                LoadAvailableFontsAsync();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh available fonts list");
            }
        }

        ImGui.Spacing();

        // ImGui.SetCursorPosX((availWidth - 110f - 200f - 110f - 200f) * .5f);
        // Create a 2-column table for consistent alignment
        if (ImGui.BeginTable("StyleProperties", 4, ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("1", 110f);
            ImGui.TableSetupColumn("2", 200f);
            ImGui.TableSetupColumn("3", 120f);
            ImGui.TableSetupColumn("4", 220f);

            // Font Size
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Font Size");
            ImGui.TableNextColumn();
            var fontSizeChanged = ImGui.InputInt("##FontSize", ref _fontSize, 1);
            if (fontSizeChanged)
            {
                _fontSize = Math.Clamp(_fontSize, 1, 72);
                configChanged = true;
            }

            // Text Scale
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Scale");
            ImGui.TableNextColumn();
            var scaleChanged = ImGui.SliderFloat("##Scale", ref _textScale, 0.5f, 2.0f, "%.1f");
            configChanged |= scaleChanged;

            // Text Color
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Color");
            ImGui.TableNextColumn();
            var color = ColorTranslator.FromHtml(_textColor);
            var colorVec = new Vector4(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f
            );
            var colorChanged = ImGui.ColorEdit4(
                "##Color",
                ref colorVec,
                ImGuiColorEditFlags.DisplayHex
            );
            if (colorChanged)
            {
                _textColor =
                    $"#{(int)(colorVec.W * 255):X2}{(int)(colorVec.X * 255):X2}{(int)(colorVec.Y * 255):X2}{(int)(colorVec.Z * 255):X2}";
                configChanged = true;
            }

            // Stroke Width
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Stroke Width");
            ImGui.TableNextColumn();
            var strokeChanged = ImGui.InputInt("##StrokeWidth", ref _strokeWidth, 1);
            if (strokeChanged)
            {
                _strokeWidth = Math.Clamp(_strokeWidth, 0, 5);
                configChanged = true;
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();

        // Checkbox options in two columns
        var checkboxWidth = availWidth / 2 - 10;
        var adaptSizeChanged = ImGui.Checkbox("Adaptive Size", ref _useAdaptiveSize);
        ImGui.SameLine(checkboxWidth + 43);
        var radialOrienChanged = ImGui.Checkbox("Radial Orientation", ref _radialTextOrientation);

        configChanged |= adaptSizeChanged | radialOrienChanged;

        // Apply configuration changes if needed
        if (configChanged)
        {
            UpdateConfiguration();
        }
    }

    private void RenderPositioningTab()
    {
        if (ImGui.BeginTabItem("Position & Rotation"))
        {
            ImGui.Text("Wheel Position");
            ImGui.Separator();

            // Position method selector
            ImGui.Combo(
                "Position Method",
                ref _selectedPositionOption,
                _positionOptions,
                _positionOptions.Length
            );

            ImGui.Spacing();

            // Different UI based on position method
            switch (_selectedPositionOption)
            {
                case 0: // Center
                    if (ImGui.Button("Center in Viewport", new Vector2(200, 0)))
                    {
                        _wheel.CenterInViewport();
                    }

                    break;

                case 1: // Custom Position
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
                    ImGui.SliderFloat("X Position %", ref _xPosition, 0.0f, 1.0f, "%.2f");

                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.4f);
                    ImGui.SliderFloat("Y Position %", ref _yPosition, 0.0f, 1.0f, "%.2f");

                    if (ImGui.Button("Apply Position", new Vector2(200, 0)))
                    {
                        _wheel.PositionByPercentage(_xPosition, _yPosition);
                    }

                    break;

                case 2: // Anchor Position
                    ImGui.Combo(
                        "Anchor Point",
                        ref _selectedAnchor,
                        _anchorOptions,
                        _anchorOptions.Length
                    );

                    if (ImGui.Button("Apply Anchor", new Vector2(200, 0)))
                    {
                        // This would need a conversion from index to the actual ViewportAnchor enum
                        // For now, just showing the concept
                        // _wheel.PositionAt((ViewportAnchor)_selectedAnchor, new Vector2(0, 0));

                        // Simple implementation for demo purposes:
                        switch (_selectedAnchor)
                        {
                            case 0:
                                _wheel.PositionByPercentage(0.0f, 0.0f);

                                break; // TopLeft
                            case 1:
                                _wheel.PositionByPercentage(0.5f, 0.0f);

                                break; // TopCenter
                            case 2:
                                _wheel.PositionByPercentage(1.0f, 0.0f);

                                break; // TopRight
                            case 3:
                                _wheel.PositionByPercentage(0.0f, 0.5f);

                                break; // MiddleLeft
                            case 4:
                                _wheel.PositionByPercentage(0.5f, 0.5f);

                                break; // MiddleCenter
                            case 5:
                                _wheel.PositionByPercentage(1.0f, 0.5f);

                                break; // MiddleRight
                            case 6:
                                _wheel.PositionByPercentage(0.0f, 1.0f);

                                break; // BottomLeft
                            case 7:
                                _wheel.PositionByPercentage(0.5f, 1.0f);

                                break; // BottomCenter
                            case 8:
                                _wheel.PositionByPercentage(1.0f, 1.0f);

                                break; // BottomRight
                        }
                    }

                    break;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Wheel Rotation");

            // Display current rotation
            ImGui.Text($"Current Rotation: {_rotationDegrees:F1}°");

            // Rotation slider
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.7f);
            if (
                ImGui.SliderFloat("Rotation (degrees)", ref _rotationDegrees, 0.0f, 360.0f, "%.1f°")
            )
            {
                _wheel.RotateByDegrees(_rotationDegrees - _wheel.RotationDegrees);
            }

            // Quick rotation buttons
            if (ImGui.Button("+45°", new Vector2(60, 0)))
            {
                _wheel.RotateByDegrees(45);
                _rotationDegrees = _wheel.RotationDegrees;
            }

            ImGui.SameLine();

            if (ImGui.Button("+90°", new Vector2(60, 0)))
            {
                _wheel.RotateByDegrees(90);
                _rotationDegrees = _wheel.RotationDegrees;
            }

            ImGui.SameLine();

            if (ImGui.Button("-45°", new Vector2(60, 0)))
            {
                _wheel.RotateByDegrees(-45);
                _rotationDegrees = _wheel.RotationDegrees;
            }

            ImGui.SameLine();

            if (ImGui.Button("-90°", new Vector2(60, 0)))
            {
                _wheel.RotateByDegrees(-90);
                _rotationDegrees = _wheel.RotationDegrees;
            }

            ImGui.EndTabItem();
        }
    }

    private void RenderBehaviorTab()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.SeparatorText("Spin Configuration");
        ImGui.Spacing();

        // Spin duration
        ImGui.SetNextItemWidth(125);
        var spinDurationChanged = ImGui.SliderFloat(
            "Spin Duration (sec)",
            ref _spinDuration,
            1.0f,
            10.0f,
            "%.1f"
        );

        // Min rotations
        ImGui.SetNextItemWidth(125);
        var minRotChanged = ImGui.SliderFloat(
            "Min Rotations",
            ref _minRotations,
            1.0f,
            10.0f,
            "%.0f"
        );

        ImGui.Spacing();
        ImGui.SeparatorText("Animation Settings");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(125);
        var aniDurChanged = ImGui.SliderFloat(
            "Animation Duration (sec)",
            ref _animationDuration,
            0.1f,
            2.0f,
            "%.1f"
        );

        ImGui.SameLine();
        ImGui.SetCursorPosX(availWidth - 200);

        // Animation toggle
        ImGui.SetNextItemWidth(200);
        var aniShowHideChanged = ImGui.Checkbox("Animate Show/Hide", ref _animateToggle);

        ImGui.SetCursorPosX(availWidth - 200);

        ImGui.SetNextItemWidth(200);
        var defaultEnabledChanged = ImGui.Checkbox("Default Enabled", ref _defaultEnabled);

        if (
            spinDurationChanged
            || minRotChanged
            || aniDurChanged
            || aniShowHideChanged
            || defaultEnabledChanged
        )
        {
            UpdateConfiguration();
        }
    }

    private void RenderTestingSection()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.Spacing();
        ImGui.SeparatorText("Playground");
        ImGui.Spacing();

        if (_sectionLabels.Length > 0)
        {
            ImGui.SetNextItemWidth(availWidth - 155);
            ImGui.Combo(
                "##TargetSection",
                ref _selectedSection,
                _sectionLabels,
                _sectionLabels.Length
            );

            ImGui.SameLine(0, 5);

            if (_wheel.IsSpinning)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Spin to Selected", new Vector2(150, 0));
                ImGui.EndDisabled();
            }
            else if (ImGui.Button("Spin to Selected", new Vector2(150, 0)))
            {
                Spin(_selectedSection);
            }

            ImGui.Spacing();
            ImGui.SetCursorPosX(availWidth - 150);

            if (_wheel.IsSpinning)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Spin Random", new Vector2(150, 0));
                ImGui.EndDisabled();
            }
            else if (ImGui.Button("Spin Random", new Vector2(150, 0)))
            {
                Spin();
            }

            ImGui.SameLine(-1, 0);

            if (_wheel.IsEnabled)
            {
                if (ImGui.Button("Hide Wheel", new Vector2(155, 0)))
                {
                    _wheel.Disable();
                }
            }
            else
            {
                if (ImGui.Button("Show Wheel", new Vector2(155, 0)))
                {
                    _wheel.Enable();
                }
            }

            if (_wheel.IsSpinning)
            {
                ImGui.SameLine(0, 10);
                ImGui.SetNextItemWidth(availWidth - 155 - 150 - 10 - 5);
                ImGui.ProgressBar(
                    _spinningOperation?.Progress ?? 0,
                    new Vector2(0, 0),
                    "Spinning..."
                );
            }
        }
        else
        {
            var txt = "Add sections to test spinning";
            ImGui.SetCursorPosX((availWidth - ImGui.CalcTextSize(txt).X) * 0.5f);
            ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), txt);
        }
    }

    private void UpdateConfiguration()
    {
        var updatedConfig = _currentConfig with
        {
            Font = _fontName,
            FontSize = _fontSize,
            TextColor = _textColor,
            TextScale = _textScale,
            TextStroke = _strokeWidth,
            AdaptiveText = _useAdaptiveSize,
            RadialTextOrientation = _radialTextOrientation,
            SectionLabels = _sectionLabels,
            SpinDuration = _spinDuration,
            MinRotations = _minRotations,
            Enabled = _defaultEnabled,
            Width = _width,
            Height = _height,
            RotationDegrees = _rotationDegrees,
            AnimateToggle = _animateToggle,
            AnimationDuration = _animationDuration,
        };

        _currentConfig = updatedConfig;
        ConfigManager.UpdateConfiguration(updatedConfig, SectionKey);

        MarkAsChanged();
    }

    public override void Update(float deltaTime)
    {
        if (_spinningOperation != null && _wheel.IsSpinning)
        {
            _spinningOperation.Progress = _wheel.Progress;
        }
    }

    private void SaveConfiguration()
    {
        ConfigManager.SaveConfiguration();
        MarkAsSaved();
    }

    private void ResetToDefaults()
    {
        var defaultConfig = new RouletteWheelOptions();
        _currentConfig = defaultConfig;

        LoadConfiguration(_currentConfig);

        ConfigManager.UpdateConfiguration(defaultConfig, SectionKey);
        MarkAsChanged();
    }
}
