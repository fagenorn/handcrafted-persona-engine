using System.Numerics;
using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Assets;
using PersonaEngine.Lib.Configuration;
using PersonaEngine.Lib.UI.ControlPanel.Layout;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Audition;
using PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Models;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Voice.Sections;

/// <summary>
///     Horizontal-scrolling voice strip scoped to the active mode. Gender filter chips
///     (Any / Female / Male) narrow the list. Selecting a tile writes the default-voice
///     setting; clicking ▶ previews via <see cref="IVoiceAuditionService" />.
/// </summary>
public sealed class VoiceGallery : IDisposable
{
    /// <summary>Fixed tile height — enough for name + gender + ~3 lines of description.</summary>
    private const float TileHeight = 160f;

    /// <summary>Gap between tiles in the horizontal strip.</summary>
    private const float TileGap = 12f;

    private readonly IOptionsMonitor<TtsConfiguration> _ttsOptions;
    private readonly IOptionsMonitor<RVCFilterOptions> _rvcOptions;
    private readonly VoiceMetadataCatalog _catalog;
    private readonly IAssetCatalog _assetCatalog;
    private readonly IVoiceAuditionService _audition;
    private readonly IConfigWriter _configWriter;

    private KokoroVoiceOptions _kokoro;
    private Qwen3TtsOptions _qwen3;
    private readonly IDisposable? _changeSubscription;

    private VoiceGender? _genderFilter;
    private float _elapsed;
    private readonly UniformHeightTracker _tileHeight = new();

    // Scratch buffer for the per-frame gender-filtered slice. Reused across
    // frames so we don't allocate Where()+ToArray() every render. Cleared and
    // refilled at the top of Render.
    private readonly List<VoiceDescriptor> _filtered = new(capacity: 64);

    // Per-descriptor tile id cache, keyed on descriptor identity, so the tile
    // loop doesn't interpolate $"tile_{engine}_{id}" every frame. Capacity
    // matches the gallery size (dozens).
    private readonly Dictionary<VoiceDescriptor, string> _tilePreviewIds = new();

    public VoiceGallery(
        IOptionsMonitor<TtsConfiguration> ttsOptions,
        IOptionsMonitor<RVCFilterOptions> rvcOptions,
        VoiceMetadataCatalog catalog,
        IAssetCatalog assetCatalog,
        IVoiceAuditionService audition,
        IConfigWriter configWriter
    )
    {
        _ttsOptions = ttsOptions;
        _rvcOptions = rvcOptions;
        _catalog = catalog;
        _assetCatalog = assetCatalog;
        _audition = audition;
        _configWriter = configWriter;

        var current = ttsOptions.CurrentValue;
        _kokoro = current.Kokoro;
        _qwen3 = current.Qwen3;

        _changeSubscription = ttsOptions.OnChange(
            (updated, _) =>
            {
                _kokoro = updated.Kokoro;
                _qwen3 = updated.Qwen3;
            }
        );
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt, VoiceMode mode)
    {
        _elapsed += dt;
        ImGuiHelpers.SectionHeader("Voices");

        // Expressive (Qwen3) voices live in the BuildWithIt bundle. If the user is
        // viewing Expressive on a profile that doesn't ship them, show a single
        // locked notice instead of an empty strip — the strip would be confusing
        // because Kokoro voices don't apply here.
        if (mode == VoiceMode.Expressive && !_assetCatalog.IsFeatureEnabled(FeatureIds.TtsQwen3))
        {
            ImGuiHelpers.LockedSection(
                "Expressive voices",
                FeatureProfileMap.MinimumProfileLabel(FeatureIds.TtsQwen3)
            );
            return;
        }

        var engine = mode == VoiceMode.Clear ? VoiceEngine.Kokoro : VoiceEngine.Qwen3;

        RenderFilters();

        _filtered.Clear();
        var source = _catalog.List(engine);
        for (var i = 0; i < source.Count; i++)
        {
            var d = source[i];
            if (PassesFilter(d))
            {
                _filtered.Add(d);
            }
        }

        var currentVoice = GetCurrentVoice(mode);

        // Strip height: on the first frame _tileHeight.Height is 0 → use a reasonable
        // default; on subsequent frames use the tracked max tile height + scrollbar.
        var tileH = _tileHeight.Height > 0f ? _tileHeight.Height : TileHeight;
        var stripHeight = tileH + ImGui.GetStyle().ScrollbarSize + 4f;
        if (
            ImGui.BeginChild(
                "##voice_strip",
                new Vector2(0f, stripHeight),
                ImGuiChildFlags.None,
                ImGuiWindowFlags.HorizontalScrollbar
            )
        )
        {
            for (var i = 0; i < _filtered.Count; i++)
            {
                var descriptor = _filtered[i];
                if (i > 0)
                    ImGui.SameLine(0f, TileGap);

                var selected = string.Equals(descriptor.Id, currentVoice, StringComparison.Ordinal);

                var tilePreviewId = GetTilePreviewId(descriptor);
                var tilePreviewState =
                    _audition.ActivePreviewId == tilePreviewId
                        ? ImGuiHelpers.PreviewButtonState.Playing
                    : _audition.IsPreviewing ? ImGuiHelpers.PreviewButtonState.Disabled
                    : ImGuiHelpers.PreviewButtonState.Idle;

                var result = VoiceTile.Render(
                    descriptor,
                    selected,
                    tilePreviewState,
                    _elapsed,
                    _tileHeight.Height
                );
                _tileHeight.Track(result.ContentHeight);

                if (result.PreviewClicked)
                {
                    if (tilePreviewState == ImGuiHelpers.PreviewButtonState.Playing)
                    {
                        _ = _audition.StopAsync();
                    }
                    else
                    {
                        _ = _audition.PreviewAsync(
                            BuildPreviewRequest(mode, descriptor.Id, tilePreviewId)
                        );
                    }
                }

                if (result.SelectClicked)
                    SelectVoice(mode, descriptor.Id);
            }
        }

        ImGui.EndChild();
        _tileHeight.EndFrame();
    }

    private void RenderFilters()
    {
        if (ImGuiHelpers.Chip("Any", _genderFilter is null))
            _genderFilter = null;

        ImGui.SameLine();
        if (ImGuiHelpers.Chip("Female", _genderFilter == VoiceGender.Female))
            _genderFilter = _genderFilter == VoiceGender.Female ? null : VoiceGender.Female;

        ImGui.SameLine();
        if (ImGuiHelpers.Chip("Male", _genderFilter == VoiceGender.Male))
            _genderFilter = _genderFilter == VoiceGender.Male ? null : VoiceGender.Male;
    }

    private bool PassesFilter(VoiceDescriptor d) =>
        _genderFilter is null || d.Gender == _genderFilter;

    private string GetTilePreviewId(VoiceDescriptor descriptor)
    {
        if (_tilePreviewIds.TryGetValue(descriptor, out var id))
            return id;

        id = $"tile_{descriptor.Engine}_{descriptor.Id}";
        _tilePreviewIds[descriptor] = id;
        return id;
    }

    private string GetCurrentVoice(VoiceMode mode) =>
        mode == VoiceMode.Clear ? _kokoro.DefaultVoice : _qwen3.Speaker;

    private void SelectVoice(VoiceMode mode, string voiceId)
    {
        if (mode == VoiceMode.Clear)
        {
            _kokoro = _kokoro with { DefaultVoice = voiceId };
            _configWriter.Write(_kokoro);
        }
        else
        {
            _qwen3 = _qwen3 with { Speaker = voiceId };
            _configWriter.Write(_qwen3);
        }
    }

    private VoiceAuditionRequest BuildPreviewRequest(
        VoiceMode mode,
        string voiceId,
        string previewId
    ) =>
        mode == VoiceMode.Clear
            ? new VoiceAuditionRequest
            {
                Id = previewId,
                Engine = "kokoro",
                Voice = voiceId,
                Speed = _ttsOptions.CurrentValue.Kokoro.DefaultSpeed,
                RvcEnabled = _rvcOptions.CurrentValue.Enabled,
                RvcVoice = _rvcOptions.CurrentValue.DefaultVoice,
                RvcPitchShift = _rvcOptions.CurrentValue.F0UpKey,
            }
            : new VoiceAuditionRequest
            {
                Id = previewId,
                Engine = "qwen3",
                Voice = voiceId,
                Expressiveness = _ttsOptions.CurrentValue.Qwen3.Temperature,
            };
}
