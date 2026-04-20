using System.Globalization;
using Spectre.Console;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Shared palette for every Spectre renderable in this UI. Hex literals mirror
/// <c>UI/ControlPanel/Layout/Theme.cs</c> so the CLI reads as a continuation of
/// the in-app look. Kept as constants (used in markup strings) plus pre-parsed
/// <see cref="Color" /> values (used in <see cref="Style" /> ctors).
/// </summary>
public sealed partial class SpectreBootstrapUserInterface
{
    internal const string AccentPrimary = "#e8a0bf"; // soft pink
    internal const string AccentSecondary = "#b68fd0"; // lavender
    internal const string Success = "#82d4a8"; // mint
    internal const string Warning = "#f0ca78"; // amber
    internal const string Error = "#e87878"; // coral
    internal const string TextPrimary = "#f3eaf2";
    internal const string TextSecondary = "#d4b8d8";
    internal const string TextTertiary = "#9a8aa3";

    // Pre-parsed Spectre Color values — Style ctors need Color, not string.
    // Parse-once-at-type-init avoids repeating the byte.Parse dance per frame.
    private static readonly Color AccentPrimaryColor = Hex(AccentPrimary);
    private static readonly Color AccentSecondaryColor = Hex(AccentSecondary);
    private static readonly Color SuccessColor = Hex(Success);
    private static readonly Color ErrorColor = Hex(Error);
    private static readonly Color RemainingTrackColor = Hex("#3a2a3f");

    private static Color Hex(string hex)
    {
        var s = hex.AsSpan().TrimStart('#');
        var r = byte.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color(r, g, b);
    }

    private void RenderBanner()
    {
        // ASCII banner sized to fit a typical 80-col terminal. Pink sparkle/heart
        // garland sits underneath the lavender figlet — the two-tone fade mirrors
        // the in-app accent gradient.
        const string sparkleBottom =
            "  \u00b7 \uff61 \u22c6 \u2727 \u22b9 \u02d9 \u00b7 \u2661 \u00b7 \u22c6 \u22c6 \u00b7 \u2661 \u00b7 \uff61 \u22c6 \u2727 \u22b9 \u02d9 \u00b7 \u2661 \u00b7 \u22c6 \u22c6 \u00b7 \u2661 \u00b7";
        const string banner = """
             ___                                           ___
            (  _`\                                        (  _`\                _
            | |_) )  __   _ __   ___    _     ___     _ _ | (_(_)  ___     __  (_)  ___     __
            | ,__/'/'__`\( '__)/',__) /'_`\ /' _ `\ /'_` )|  _)_ /' _ `\ /'_ `\| |/' _ `\ /'__`\
            | |   (  ___/| |   \__, \( (_) )| ( ) |( (_| || (_( )| ( ) |( (_) || || ( ) |(  ___/
            (_)   `\____)(_)   (____/`\___/'(_) (_)`\__,_)(____/'(_) (_)`\__  |(_)(_) (_)`\____)
                                                                        ( )_) |
                                                                         \___/'
            """;

        _console.MarkupLine($"[bold {AccentSecondary}]{Markup.Escape(banner)}[/]");
        _console.MarkupLine($"[{AccentPrimary}]{Markup.Escape(sparkleBottom)}[/]");
        _console.WriteLine();
        _console.MarkupLine(
            $"  [{TextSecondary}]Handcrafted, locally-run AI persona for VTubers, streamers and tinkerers.[/]"
        );
        _console.MarkupLine(
            $"  [{TextTertiary}]First-run setup — pick what you want to install.[/]"
        );
        _console.WriteLine();
    }
}
