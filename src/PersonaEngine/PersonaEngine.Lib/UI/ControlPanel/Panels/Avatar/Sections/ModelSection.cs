using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Shared;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar.Sections;

/// <summary>
///     Live2D model picker card. Sources the available characters from
///     <see cref="IAssetCatalog.GetUserAssets" /> for <see cref="UserAssetType.Live2DModel" />,
///     so bundled defaults and user-added models surface uniformly. Subscribes to
///     <see cref="IAssetCatalog.Changed" /> to refresh the picker live when the user
///     drops a new model into the live2d folder. The saved model is always shown
///     selected, even if it's not on disk — in that case a muted warning appears so
///     the user is never silently moved off their character.
/// </summary>
public sealed class ModelSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly IAssetCatalog _catalog;
    private readonly IDisposable? _changeSubscription;
    private readonly ScannedNamePicker _picker;

    private Live2DOptions _live2d;
    private bool _initialized;

    public ModelSection(
        IOptionsMonitor<Live2DOptions> monitor,
        IConfigWriter configWriter,
        IAssetCatalog catalog
    )
    {
        _configWriter = configWriter;
        _catalog = catalog;
        _live2d = monitor.CurrentValue;
        _picker = new ScannedNamePicker(ScanModels);

        _changeSubscription = monitor.OnChange(
            (updated, _) =>
            {
                _live2d = updated;
                if (!_initialized)
                    return;
                _picker.RecomputeMissing(_live2d.ModelName);
            }
        );

        // Live refresh: catalog watches the live2d directory and fires Changed
        // when content changes, so newly-dropped models surface without UI restart.
        _catalog.Changed += OnCatalogChanged;
    }

    public void Dispose()
    {
        _catalog.Changed -= OnCatalogChanged;
        _changeSubscription?.Dispose();
    }

    private void OnCatalogChanged(object? sender, AssetCatalogChangedEventArgs e)
    {
        if (_initialized)
        {
            _picker.Refresh(_live2d.ModelName);
        }
    }

    public void Render(float dt)
    {
        if (!_initialized)
        {
            _picker.Refresh(_live2d.ModelName);
            _initialized = true;
        }

        using (Ui.Card("##model", padding: 12f))
        {
            // Header
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
            ImGui.TextUnformatted("Live2D Model");
            ImGui.PopStyleColor();

            // Description
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Which character to render, and at what resolution");
            ImGui.PopStyleColor();
            ImGui.Spacing();

            RenderCharacterRow();
            RenderResolutionRow();
        }
    }

    // ── Character row ─────────────────────────────────────────────────────────

    private void RenderCharacterRow()
    {
        ImGuiHelpers.SettingLabel("Character", "The Live2D model to load from your models folder.");

        if (
            ImGuiHelpers.ScannedCombo(
                "ModelCombo",
                _picker,
                _live2d.ModelName,
                out var picked,
                onRefresh: () => _picker.Refresh(_live2d.ModelName),
                refreshTooltip: "Re-scan the models folder for available Live2D characters."
            )
        )
        {
            _live2d = _live2d with { ModelName = picked };
            _configWriter.Write(_live2d);
            _picker.RecomputeMissing(_live2d.ModelName);
        }

        if (_picker.IsMissing)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted($"'{_live2d.ModelName}' not found on disk");
            ImGui.PopStyleColor();
        }
    }

    // ── Resolution row ────────────────────────────────────────────────────────

    private void RenderResolutionRow()
    {
        ImGuiHelpers.SettingLabel(
            "Resolution",
            "Canvas size for the avatar. Pick an orientation, then a preset that matches your scene in OBS."
        );

        var width = _live2d.Width;
        var height = _live2d.Height;

        if (ImGuiHelpers.ResolutionChips("Live2DRes", ref width, ref height))
        {
            _live2d = _live2d with { Width = width, Height = height };
            _configWriter.Write(_live2d);
        }
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    private IEnumerable<string> ScanModels()
    {
        // Catalog already returns alphabetised entries scoped to the Live2D root,
        // so no extra sort/filter needed here.
        return _catalog
            .GetUserAssets(UserAssetType.Live2DModel)
            .Select(a => a.DisplayName)
            .Where(n => !string.IsNullOrEmpty(n));
    }
}
