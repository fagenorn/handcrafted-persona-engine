using System.Drawing;
using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Subtitles panel: font selection, colors, layout, and output size.
/// </summary>
public sealed class Subtitles(
    IOptionsMonitor<SubtitleOptions> subtitleOptions,
    FontProvider fontProvider,
    IConfigWriter configWriter
)
{
    private SubtitleOptions _opts = null!;

    private string[] _fonts = [];
    private bool _initialized;

    public void Render()
    {
        EnsureInitialized();
        RenderFontSection();
        RenderColorsSection();
        RenderLayoutSection();
        RenderOutputSizeSection();
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _opts = CloneOpts(subtitleOptions.CurrentValue);
        _fonts = fontProvider.GetAvailableFonts().ToArray();
        _initialized = true;
    }

    // ── Font section ─────────────────────────────────────────────────────────────

    private void RenderFontSection()
    {
        ImGuiHelpers.SectionHeader("Font");

        // Font combo
        {
            var currentIndex = Array.IndexOf(_fonts, _opts.Font);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel("Font", "The font file used to render subtitle text.");

            if (ImGui.Combo("##SubtitleFont", ref currentIndex, _fonts, _fonts.Length))
            {
                var selected = _fonts.Length > 0 ? _fonts[currentIndex] : _opts.Font;
                _opts.Font = selected;
                configWriter.Write(CloneOpts(_opts));
            }
        }

        // Font Size
        {
            var fontSize = _opts.FontSize;

            ImGuiHelpers.SettingLabel("Font Size", "Point size for subtitle text (24–200).");

            if (ImGui.SliderInt("##SubtitleFontSize", ref fontSize, 24, 200))
            {
                _opts.FontSize = fontSize;
                configWriter.Write(CloneOpts(_opts));
            }

            ImGuiHelpers.SliderGlow();
        }

        // Outline Thickness
        {
            var stroke = _opts.StrokeThickness;

            ImGuiHelpers.SettingLabel(
                "Outline Thickness",
                "Width of the text outline stroke in pixels (0–10)."
            );

            if (ImGui.SliderInt("##SubtitleStroke", ref stroke, 0, 10))
            {
                _opts.StrokeThickness = stroke;
                configWriter.Write(CloneOpts(_opts));
            }

            ImGuiHelpers.SliderGlow();
        }
    }

    // ── Colors section ───────────────────────────────────────────────────────────

    private void RenderColorsSection()
    {
        ImGuiHelpers.SectionHeader("Colors");

        // Text Color
        {
            var color = ParseHexColor(_opts.Color);
            var vec = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

            ImGuiHelpers.SettingLabel("Text Color", "Main color for subtitle text.");

            if (ImGui.ColorEdit4("##SubtitleColor", ref vec, ImGuiColorEditFlags.AlphaBar))
            {
                _opts.Color = FormatHexColor(vec);
                configWriter.Write(CloneOpts(_opts));
            }
        }

        // Highlight Color
        {
            var color = ParseHexColor(_opts.HighlightColor);
            var vec = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

            ImGuiHelpers.SettingLabel(
                "Highlight Color",
                "Color used to highlight the currently spoken word."
            );

            if (ImGui.ColorEdit4("##SubtitleHighlight", ref vec, ImGuiColorEditFlags.AlphaBar))
            {
                _opts.HighlightColor = FormatHexColor(vec);
                configWriter.Write(CloneOpts(_opts));
            }
        }
    }

    // ── Layout section ───────────────────────────────────────────────────────────

    private void RenderLayoutSection()
    {
        ImGuiHelpers.SectionHeader("Layout");

        // Max Lines
        {
            var maxLines = _opts.MaxVisibleLines;

            ImGuiHelpers.SettingLabel(
                "Max Lines",
                "Maximum number of subtitle lines visible at once (1–10)."
            );

            if (ImGui.SliderInt("##SubtitleMaxLines", ref maxLines, 1, 10))
            {
                _opts.MaxVisibleLines = maxLines;
                configWriter.Write(CloneOpts(_opts));
            }

            ImGuiHelpers.SliderGlow();
        }

        // Bottom Margin
        {
            var margin = _opts.BottomMargin;

            ImGuiHelpers.SettingLabel(
                "Bottom Margin",
                "Distance in pixels from the bottom of the output."
            );

            if (ImGui.InputInt("##SubtitleBottomMargin", ref margin))
            {
                margin = Math.Max(0, margin);
                _opts.BottomMargin = margin;
                configWriter.Write(CloneOpts(_opts));
            }
        }

        // Side Margin
        {
            var margin = _opts.SideMargin;

            ImGuiHelpers.SettingLabel(
                "Side Margin",
                "Horizontal padding in pixels on each side of the subtitle area."
            );

            if (ImGui.InputInt("##SubtitleSideMargin", ref margin))
            {
                margin = Math.Max(0, margin);
                _opts.SideMargin = margin;
                configWriter.Write(CloneOpts(_opts));
            }
        }

        // Animation Speed
        {
            var duration = _opts.AnimationDuration;

            ImGuiHelpers.SettingLabel(
                "Animation Speed",
                "Duration of the subtitle fade/slide animation in seconds (0.05–1.0)."
            );

            if (ImGui.SliderFloat("##SubtitleAnimDuration", ref duration, 0.05f, 1.0f, "%.2f"))
            {
                _opts.AnimationDuration = duration;
                configWriter.Write(CloneOpts(_opts));
            }

            ImGuiHelpers.SliderGlow();
        }
    }

    // ── Output Size section ──────────────────────────────────────────────────────

    private void RenderOutputSizeSection()
    {
        ImGuiHelpers.SectionHeader("Output Size");

        // Width
        {
            var width = _opts.Width;

            ImGuiHelpers.SettingLabel("Width", "Width of the subtitle output in pixels.");

            if (ImGui.InputInt("##SubtitleWidth", ref width))
            {
                width = Math.Max(1, width);
                _opts.Width = width;
                configWriter.Write(CloneOpts(_opts));
            }
        }

        // Height
        {
            var height = _opts.Height;

            ImGuiHelpers.SettingLabel("Height", "Height of the subtitle output in pixels.");

            if (ImGui.InputInt("##SubtitleHeight", ref height))
            {
                height = Math.Max(1, height);
                _opts.Height = height;
                configWriter.Write(CloneOpts(_opts));
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static Color ParseHexColor(string hex)
    {
        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return Color.White;
        }
    }

    private static string FormatHexColor(Vector4 vec) =>
        $"#{(byte)(vec.W * 255):X2}{(byte)(vec.X * 255):X2}{(byte)(vec.Y * 255):X2}{(byte)(vec.Z * 255):X2}";

    private static SubtitleOptions CloneOpts(SubtitleOptions src) =>
        new()
        {
            Font = src.Font,
            FontSize = src.FontSize,
            StrokeThickness = src.StrokeThickness,
            Color = src.Color,
            HighlightColor = src.HighlightColor,
            MaxVisibleLines = src.MaxVisibleLines,
            BottomMargin = src.BottomMargin,
            SideMargin = src.SideMargin,
            InterSegmentSpacing = src.InterSegmentSpacing,
            AnimationDuration = src.AnimationDuration,
            Width = src.Width,
            Height = src.Height,
        };
}
