using System.Numerics;

namespace PersonaEngine.Lib.UI.Overlay.Rendering;

/// <summary>
///     All theme colours and size constants for the overlay's hover chrome.
///     Extracted from the former <c>ChromePipeline</c> so each pipeline class
///     can reference the same palette without duplicating magic numbers.
///
///     Theme palette mapped from PersonaEngine.Lib.UI.ControlPanel.Theme so the
///     overlay chrome reads as part of the same product rather than a generic
///     dark overlay. Values are normalized 0..1 with straight alpha — the
///     pixel shaders premultiply on output.
/// </summary>
public static class ChromeTheme
{
    // ── Button geometry ────────────────────────────────────────────────────────

    public const int ButtonSize = OverlayChromeLayout.ButtonSize;
    public const int ButtonCornerRadius = OverlayChromeLayout.ButtonCornerRadius;

    // ── Button colours ─────────────────────────────────────────────────────────

    // Surface1 0x1E1A2E — deep theme purple, used for the button fill.
    public static readonly Vector4 ButtonBg = new(0.118f, 0.102f, 0.180f, 0.92f);

    // Surface2 0x262040 — slightly lighter on hover for that soft "hot" feel.
    public static readonly Vector4 ButtonBgHot = new(0.149f, 0.125f, 0.251f, 0.96f);

    // AccentSecondary 0xB68FD0 — lavender default border + icon colour.
    public static readonly Vector4 ButtonBorder = new(0.714f, 0.561f, 0.816f, 0.95f);

    // AccentPrimary 0xE8A0BF — soft pink for the hot state on both border and
    // icon so hover has a clear, coordinated highlight.
    public static readonly Vector4 ButtonBorderHot = new(0.910f, 0.627f, 0.749f, 1.0f);

    // ── Icon colours ───────────────────────────────────────────────────────────

    public static readonly Vector4 IconColor = new(0.714f, 0.561f, 0.816f, 0.95f);
    public static readonly Vector4 IconColorHot = new(0.910f, 0.627f, 0.749f, 1.0f);

    // ── Button halo ────────────────────────────────────────────────────────────

    // Outer dark halo around each button — a subtle ~3-px dark shadow that
    // sits just outside the button edge. Without it, when the avatar behind
    // the overlay has a similar purple/pink tint to the accent border, the
    // button blends into the background and becomes invisible. The halo
    // guarantees contrast against any background.
    public static readonly Vector4 ButtonHaloColor = new(0f, 0f, 0f, 0.65f);
    public const float ButtonHaloRadius = 2.5f;

    // ── Outline ────────────────────────────────────────────────────────────────

    // AccentSecondary at low alpha — just enough to outline the overlay's
    // hit-region when the user hovers, without drawing attention away from
    // the avatar.
    public static readonly Vector4 OutlineColor = new(0.714f, 0.561f, 0.816f, 0.55f);

    // Match WindowRounding (8 px) from the control-panel theme for visual
    // consistency with everything else in the product.
    public const float OutlineCornerRadius = 8f;

    // 1.5 px reads as a crisp stroke on most displays — thicker feels heavy
    // over animated avatars, thinner hairlines on hidpi monitors.
    public const float OutlineThickness = 1.5f;

    // ── Outline halo ───────────────────────────────────────────────────────────

    // Dark halo inside the window outline — same purpose as the button halo
    // (keep the ring visible on similar-colored backgrounds) but sits on the
    // interior side since everything outside the outline is transparent.
    // Lower alpha than the button halo to avoid a hard dark rim around the
    // avatar; just enough contrast for the accent outline to register.
    public static readonly Vector4 OutlineHaloColor = new(0f, 0f, 0f, 0.45f);
    public const float OutlineHaloRadius = 3.0f;
}
