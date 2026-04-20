using System.Text;
using PersonaEngine.Lib.Assets.Manifest;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PersonaEngine.Lib.Bootstrapper;

/// <summary>
/// Profile-selection UI: an interactive tab picker for capable terminals with a
/// fallback to Spectre's linear <see cref="SelectionPrompt{T}" /> when stdin is
/// redirected or the terminal can't drive our key-event loop.
/// </summary>
public sealed partial class SpectreBootstrapUserInterface
{
    public async Task<ProfileTier> PickProfileAsync(
        IReadOnlyList<ProfileChoice> choices,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(choices);

        // Non-interactive fallback (redirected stdin, dumb terminals): fall
        // back to Spectre's linear SelectionPrompt so CI jobs and piped input
        // still work. The tab-nav loop below depends on Console.ReadKey.
        if (!IsInteractiveTerminal())
        {
            RenderBanner();
            var prompt = new SelectionPrompt<ProfileChoice>()
                .Title($"[bold {AccentPrimary}]?[/] [{TextPrimary}]Pick an install profile[/]")
                .HighlightStyle(
                    new Style(foreground: AccentPrimaryColor, decoration: Decoration.Bold)
                )
                .UseConverter(c =>
                    $"[{TextPrimary}]{c.Title}[/] [{TextTertiary}]({c.SizeLabel})[/]"
                )
                .AddChoices(choices);

            return _console.Prompt(prompt).Profile;
        }

        var index = 0;

        // Manual redraw loop — Spectre's Live display does not play well with
        // a Console.ReadKey-driven tab picker (the initial target only shows
        // after the first internal refresh tick, which made the panel never
        // render). Clearing and re-writing the full view on every keypress is
        // a few bytes of flicker but deterministic across Windows Terminal,
        // conhost, ConEmu, and IDE-embedded terminals.
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            _console.Clear();
            RenderBanner();
            _console.Write(BuildPickerView(choices, index));

            var key = await _readKey(ct).ConfigureAwait(false);
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                case ConsoleKey.UpArrow:
                case ConsoleKey.H:
                case ConsoleKey.K:
                    index = (index - 1 + choices.Count) % choices.Count;
                    break;

                case ConsoleKey.RightArrow:
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                case ConsoleKey.L:
                case ConsoleKey.J:
                    index = (index + 1) % choices.Count;
                    break;

                case ConsoleKey.Enter:
                case ConsoleKey.Spacebar:
                    // Clear the picker so the plan summary / download progress
                    // renders on a fresh screen — nothing stale left over.
                    _console.Clear();
                    return choices[index].Profile;

                case ConsoleKey.Escape:
                    _console.Clear();
                    throw new OperationCanceledException("Profile selection cancelled by user.");

                default:
                    // Number-key shortcut: D1..D9 / NumPad1..9 jump to that choice.
                    // Written as a single range check so adding a 4th profile later
                    // doesn't need another switch arm.
                    if (TryNumberKeyToIndex(key.Key, choices.Count, out var picked))
                        index = picked;
                    break;
            }
        }
    }

    private IRenderable BuildPickerView(IReadOnlyList<ProfileChoice> choices, int selectedIndex)
    {
        // Tab bar — one cell per profile, the current one highlighted on a
        // pink background. Numeric hint (1/2/3) so users notice the shortcut.
        var tabs = new StringBuilder();
        for (var i = 0; i < choices.Count; i++)
        {
            var label = $" {i + 1}. {choices[i].Title} ";
            tabs.Append(
                i == selectedIndex
                    ? $"[bold black on {AccentPrimary}]{Markup.Escape(label)}[/]"
                    : $"[{TextSecondary}]{Markup.Escape(label)}[/]"
            );
            if (i < choices.Count - 1)
                tabs.Append($"[{TextTertiary}]│[/]");
        }

        var current = choices[selectedIndex];
        var body = new StringBuilder();
        body.AppendLine(tabs.ToString());
        body.AppendLine();
        body.AppendLine(
            $"[bold {AccentPrimary}]{Markup.Escape(current.Title)}[/]  [{TextTertiary}]({Markup.Escape(current.SizeLabel)})[/]"
        );
        body.AppendLine();
        body.AppendLine($"[italic {TextSecondary}]\u201C{Markup.Escape(current.Tagline)}\u201D[/]");
        body.AppendLine();
        foreach (var bullet in current.Bullets)
        {
            body.AppendLine(
                $"  [{AccentPrimary}]\u2022[/] [{TextPrimary}]{Markup.Escape(bullet)}[/]"
            );
        }

        var panel = new Panel(new Markup(body.ToString().TrimEnd()))
        {
            Header = new PanelHeader($" [bold {AccentPrimary}]Choose a profile[/] "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: AccentSecondaryColor),
            Padding = new Padding(2, 1, 2, 1),
        };

        var hint = new Markup(
            $"  [{TextTertiary}]\u2190/\u2192[/] [{TextSecondary}]navigate[/]   "
                + $"[{TextTertiary}]1/2/3[/] [{TextSecondary}]jump[/]   "
                + $"[{TextTertiary}]Enter[/] [{TextSecondary}]select[/]   "
                + $"[{TextTertiary}]Esc[/] [{TextSecondary}]cancel[/]"
        );

        return new Rows(panel, hint);
    }
}
