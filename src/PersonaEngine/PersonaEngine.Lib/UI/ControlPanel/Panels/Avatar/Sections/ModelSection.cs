using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Avatar.Sections;

/// <summary>
///     Live2D model picker card. Scans <see cref="Live2DOptions.ModelPath" /> for
///     subdirectories containing any <c>*.model3.json</c> file and presents them as a
///     combo. The saved model is always shown selected, even if it's not on disk — in
///     that case a muted warning appears so the user is never silently moved off their
///     character.
/// </summary>
public sealed class ModelSection : IDisposable
{
    private const int ModelNameBufferSize = 512;
    private const int ModelPathBufferSize = 1024;
    private const string MissingSuffix = "  (not found)";

    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private Live2DOptions _live2d;
    private string _modelNameBuffer = string.Empty;
    private string _modelPathBuffer = string.Empty;
    private string[] _modelChoices = Array.Empty<string>();
    private bool _currentModelMissing;
    private bool _initialized;

    public ModelSection(IOptionsMonitor<Live2DOptions> monitor, IConfigWriter configWriter)
    {
        _configWriter = configWriter;
        _live2d = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange(
            (updated, _) =>
            {
                var folderChanged = !string.Equals(
                    _live2d.ModelPath,
                    updated.ModelPath,
                    StringComparison.Ordinal
                );
                _live2d = updated;
                if (!_initialized)
                    return;
                _modelNameBuffer = updated.ModelName;
                _modelPathBuffer = updated.ModelPath;
                if (folderChanged)
                    RefreshModels();
                else
                    RecomputeMissingFlag();
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        if (!_initialized)
        {
            _modelNameBuffer = _live2d.ModelName;
            _modelPathBuffer = _live2d.ModelPath;
            RefreshModels();
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
            RenderFolderRow();
            RenderResolutionRow();
        }
    }

    // ── Character row ─────────────────────────────────────────────────────────

    private void RenderCharacterRow()
    {
        ImGuiHelpers.SettingLabel("Character", "The Live2D model to load from your models folder.");

        var selectedIndex = ComputeSelectedIndex();

        // Combo is on the widget half of the row (SettingLabel already called
        // SetNextItemWidth(-1f)); shorten it to leave room for the Refresh button.
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 110f);
        if (
            _modelChoices.Length > 0
            && ImGui.Combo("##ModelCombo", ref selectedIndex, _modelChoices, _modelChoices.Length)
        )
        {
            OnModelPicked(selectedIndex);
        }
        else if (_modelChoices.Length == 0)
        {
            // Nothing to pick — render a disabled "No models" pseudo-combo placeholder
            // so the row layout stays consistent with the Refresh button.
            ImGui.BeginDisabled();
            var empty = 0;
            var placeholder = new[] { "(none)" };
            ImGui.Combo("##ModelCombo", ref empty, placeholder, placeholder.Length);
            ImGui.EndDisabled();
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGui.SameLine();

        if (ImGui.Button("Refresh", new Vector2(100f, 0f)))
        {
            RefreshModels();
        }

        ImGuiHelpers.HandCursorOnHover();
        ImGuiHelpers.Tooltip("Re-scan the models folder for available Live2D characters.");

        if (_currentModelMissing)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted($"'{_live2d.ModelName}' not found on disk");
            ImGui.PopStyleColor();
        }
    }

    private int ComputeSelectedIndex()
    {
        if (_modelChoices.Length == 0)
            return 0;

        // When the saved model is missing, it is inserted at index 0 with a "(not found)"
        // suffix by RefreshModels — so selecting index 0 means "keep showing the missing one".
        for (var i = 0; i < _modelChoices.Length; i++)
        {
            var entry = _modelChoices[i];
            var name = entry.EndsWith(MissingSuffix, StringComparison.Ordinal)
                ? entry[..^MissingSuffix.Length]
                : entry;
            if (string.Equals(name, _live2d.ModelName, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private void OnModelPicked(int index)
    {
        if (index < 0 || index >= _modelChoices.Length)
            return;

        var entry = _modelChoices[index];
        var name = entry.EndsWith(MissingSuffix, StringComparison.Ordinal)
            ? entry[..^MissingSuffix.Length]
            : entry;

        if (string.Equals(name, _live2d.ModelName, StringComparison.Ordinal))
            return;

        _live2d = _live2d with { ModelName = name };
        _modelNameBuffer = name;
        _configWriter.Write(_live2d);
        RecomputeMissingFlag();
    }

    // ── Models folder row ─────────────────────────────────────────────────────

    private void RenderFolderRow()
    {
        ImGuiHelpers.SettingLabel("Models Folder", "Where to look for Live2D models on disk.");

        if (ImGui.InputText("##ModelPath", ref _modelPathBuffer, ModelPathBufferSize))
        {
            _live2d = _live2d with { ModelPath = _modelPathBuffer };
            _configWriter.Write(_live2d);
            RefreshModels();
        }

        // Folder validation feedback — appears just below the text input.
        if (string.IsNullOrWhiteSpace(_live2d.ModelPath) || !Directory.Exists(_live2d.ModelPath))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("Folder not found");
            ImGui.PopStyleColor();
        }
        else if (_modelChoices.Length == 0 || AllChoicesAreMissingOnly())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
            ImGui.TextUnformatted("No models found in this folder");
            ImGui.PopStyleColor();
        }
    }

    private bool AllChoicesAreMissingOnly()
    {
        // Only the placeholder for the currently-saved-but-missing model is present,
        // i.e. the scan returned zero real entries.
        return _modelChoices.Length == 1
            && _modelChoices[0].EndsWith(MissingSuffix, StringComparison.Ordinal);
    }

    // ── Resolution row ────────────────────────────────────────────────────────

    private void RenderResolutionRow()
    {
        ImGuiHelpers.SettingLabel("Resolution", "Render resolution for the avatar.");

        var width = _live2d.Width;
        var height = _live2d.Height;

        if (ImGuiHelpers.ResolutionPicker("Live2DRes", ref width, ref height))
        {
            _live2d = _live2d with { Width = width, Height = height };
            _configWriter.Write(_live2d);
        }
    }

    // ── Scan ──────────────────────────────────────────────────────────────────

    private void RefreshModels()
    {
        var discovered = ScanModels(_live2d.ModelPath);
        var savedName = _live2d.ModelName;
        var savedExists = discovered.Any(n =>
            string.Equals(n, savedName, StringComparison.Ordinal)
        );

        if (!savedExists && !string.IsNullOrEmpty(savedName))
        {
            // Prepend a "(not found)"-suffixed entry so the combo can still show the
            // saved model selected, even though it isn't on disk.
            var prefixed = new List<string>(discovered.Count + 1) { savedName + MissingSuffix };
            prefixed.AddRange(discovered);
            _modelChoices = prefixed.ToArray();
            _currentModelMissing = true;
        }
        else
        {
            _modelChoices = discovered.ToArray();
            _currentModelMissing = false;
        }
    }

    private void RecomputeMissingFlag()
    {
        // Called when ModelName changes but folder didn't — avoid a full rescan.
        var exists = _modelChoices.Any(entry =>
        {
            var name = entry.EndsWith(MissingSuffix, StringComparison.Ordinal)
                ? entry[..^MissingSuffix.Length]
                : entry;
            return string.Equals(name, _live2d.ModelName, StringComparison.Ordinal)
                && !entry.EndsWith(MissingSuffix, StringComparison.Ordinal);
        });
        _currentModelMissing = !exists && !string.IsNullOrEmpty(_live2d.ModelName);

        // If the flag state no longer matches the choices array (either direction —
        // newly missing with no placeholder, or newly found with a stale placeholder),
        // refresh so the combo reflects it correctly.
        var hasMissingEntry = _modelChoices.Any(e =>
            e.EndsWith(MissingSuffix, StringComparison.Ordinal)
        );
        if (_currentModelMissing != hasMissingEntry)
        {
            RefreshModels();
        }
    }

    private static List<string> ScanModels(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return [];

        try
        {
            return Directory
                .EnumerateDirectories(folder)
                .Where(d => Directory.EnumerateFiles(d, "*.model3.json").Any())
                .Select(d => Path.GetFileName(d)!)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            // A transient IO error (permission, handle churn) should never crash the
            // render thread — fall back to an empty list and let the user retry via
            // the Refresh button.
            return [];
        }
    }
}
