using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Configuration;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels;

/// <summary>
///     Avatar panel: Live2D model settings and lip-sync engine configuration.
/// </summary>
public sealed class Avatar(
    IOptionsMonitor<Live2DOptions> live2DOptions,
    IOptionsMonitor<LipSyncOptions> lipSyncOptions,
    IConfigWriter configWriter
)
{
    private static readonly string[] _engineLabels = ["VBridger", "Audio2Face"];
    private static readonly string[] _engineIds = ["VBridger", "Audio2Face"];

    private static readonly string[] _solverLabels = ["BVLS", "PGD"];
    private static readonly string[] _solverIds = ["BVLS", "PGD"];

    private Live2DOptions _live2d = null!;
    private LipSyncOptions _lipSync = null!;

    private string _modelNameBuffer = string.Empty;
    private string _modelPathBuffer = string.Empty;
    private string _a2fIdentityBuffer = string.Empty;
    private bool _initialized;

    public void Render()
    {
        EnsureInitialized();
        RenderLive2DModel();
        RenderLipSync();
    }

    // ── Initialization ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _live2d = CloneLive2D(live2DOptions.CurrentValue);
        _lipSync = CloneLipSync(lipSyncOptions.CurrentValue);

        _modelNameBuffer = _live2d.ModelName;
        _modelPathBuffer = _live2d.ModelPath;
        _a2fIdentityBuffer = _lipSync.Audio2Face.Identity;

        _initialized = true;
    }

    // ── Clone helpers ────────────────────────────────────────────────────────────

    private static Live2DOptions CloneLive2D(Live2DOptions src) =>
        new()
        {
            ModelName = src.ModelName,
            ModelPath = src.ModelPath,
            Width = src.Width,
            Height = src.Height,
        };

    private static LipSyncOptions CloneLipSync(LipSyncOptions src) =>
        new()
        {
            Engine = src.Engine,
            Audio2Face = new Audio2FaceOptions
            {
                Identity = src.Audio2Face.Identity,
                UseGpu = src.Audio2Face.UseGpu,
                SolverType = src.Audio2Face.SolverType,
            },
        };

    // ── Live2D Model ─────────────────────────────────────────────────────────────

    private void RenderLive2DModel()
    {
        ImGuiHelpers.SectionHeader("Live2D Model");

        // Model Name
        {
            ImGuiHelpers.SettingLabel("Model Name", "The name of the Live2D model to load.");

            if (ImGui.InputText("##ModelName", ref _modelNameBuffer, 512))
            {
                _live2d.ModelName = _modelNameBuffer;
                configWriter.Write(CloneLive2D(_live2d));
            }
        }

        // Models Folder
        {
            ImGuiHelpers.SettingLabel(
                "Models Folder",
                "Root directory where Live2D model folders are stored."
            );

            if (ImGui.InputText("##ModelPath", ref _modelPathBuffer, 1024))
            {
                _live2d.ModelPath = _modelPathBuffer;
                configWriter.Write(CloneLive2D(_live2d));
            }
        }

        // Render Width
        {
            var width = _live2d.Width;

            ImGuiHelpers.SettingLabel(
                "Render Width",
                "Horizontal resolution of the Live2D render target in pixels."
            );

            if (ImGui.InputInt("##RenderWidth", ref width))
            {
                _live2d.Width = width;
                configWriter.Write(CloneLive2D(_live2d));
            }
        }

        // Render Height
        {
            var height = _live2d.Height;

            ImGuiHelpers.SettingLabel(
                "Render Height",
                "Vertical resolution of the Live2D render target in pixels."
            );

            if (ImGui.InputInt("##RenderHeight", ref height))
            {
                _live2d.Height = height;
                configWriter.Write(CloneLive2D(_live2d));
            }
        }
    }

    // ── Lip Sync ─────────────────────────────────────────────────────────────────

    private void RenderLipSync()
    {
        ImGuiHelpers.SectionHeader("Lip Sync");

        // Engine selector
        {
            var currentIndex = Array.IndexOf(_engineIds, _lipSync.Engine);
            if (currentIndex < 0)
                currentIndex = 0;

            ImGuiHelpers.SettingLabel(
                "Engine",
                "The lip-sync backend used to drive mouth animations."
            );

            if (
                ImGui.Combo(
                    "##LipSyncEngine",
                    ref currentIndex,
                    _engineLabels,
                    _engineLabels.Length
                )
            )
            {
                _lipSync.Engine = _engineIds[currentIndex];
                configWriter.Write(CloneLipSync(_lipSync));
            }
        }

        // Audio2Face sub-settings — only shown when Audio2Face engine is active
        if (!string.Equals(_lipSync.Engine, "Audio2Face", StringComparison.OrdinalIgnoreCase))
            return;

        ImGui.Spacing();

        // Character (Identity)
        {
            ImGuiHelpers.SettingLabel(
                "Character",
                "The Audio2Face character identity used for blendshape generation."
            );

            if (ImGui.InputText("##A2FIdentity", ref _a2fIdentityBuffer, 256))
            {
                _lipSync.Audio2Face.Identity = _a2fIdentityBuffer;
                configWriter.Write(CloneLipSync(_lipSync));
            }
        }

        // Use GPU
        {
            var useGpu = _lipSync.Audio2Face.UseGpu;

            ImGuiHelpers.SettingLabel("Use GPU", "Run Audio2Face inference on the GPU.");

            if (ImGui.Checkbox("##A2FUseGpu", ref useGpu))
            {
                _lipSync.Audio2Face.UseGpu = useGpu;
                configWriter.Write(CloneLipSync(_lipSync));
            }
        }

        // Solver
        {
            var currentSolverIndex = Array.IndexOf(_solverIds, _lipSync.Audio2Face.SolverType);
            if (currentSolverIndex < 0)
                currentSolverIndex = 0;

            ImGuiHelpers.SettingLabel(
                "Solver",
                "Blendshape solver algorithm: BVLS (default, more accurate) or PGD (faster)."
            );

            if (
                ImGui.Combo(
                    "##A2FSolver",
                    ref currentSolverIndex,
                    _solverLabels,
                    _solverLabels.Length
                )
            )
            {
                _lipSync.Audio2Face.SolverType = _solverIds[currentSolverIndex];
                configWriter.Write(CloneLipSync(_lipSync));
            }
        }
    }
}
