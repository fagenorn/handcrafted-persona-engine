using System.Diagnostics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Services;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Bottom stats strip: Uptime, Turns, Avg Latency, Interruptions.
/// </summary>
public sealed class SessionStatsSection(SessionStatsCollector stats)
{
    private const string EmDash = "--";

    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    // Last-value caches for ToString / format calls so repeat frames with
    // unchanged counters skip the allocation entirely. Counters only change on
    // turn events, so the common case is a cache hit every frame.
    private long _cachedTurns = -1;
    private string _cachedTurnsText = EmDash;
    private long _cachedInterruptions = -1;
    private string _cachedInterruptionsText = EmDash;

    // Avg latency rounds to an integer millisecond before formatting so we
    // only rebuild the string when the displayed number actually changes.
    private int _cachedAvgLatencyMs = int.MinValue;
    private string _cachedAvgLatencyText = EmDash;

    public void Render(float dt)
    {
        ImGuiHelpers.SectionHeader("Session");

        if (!ImGui.BeginTable("##Stats", 4, ImGuiTableFlags.SizingStretchSame))
            return;

        ImGui.TableNextRow();

        RenderStatCell("Uptime", FormatUptime(_uptime.Elapsed));
        RenderStatCell("Turns", FormatTurns(stats.TurnsStarted));
        RenderStatCell("Avg Latency", FormatAvgLatency(stats.AvgFirstAudioLatencyMs));
        RenderStatCell("Interruptions", FormatInterruptions(stats.TurnsInterrupted));

        ImGui.EndTable();
    }

    private string FormatTurns(long turns)
    {
        if (turns == _cachedTurns)
            return _cachedTurnsText;

        _cachedTurns = turns;
        _cachedTurnsText = turns > 0 ? turns.ToString() : EmDash;
        return _cachedTurnsText;
    }

    private string FormatInterruptions(long interruptions)
    {
        if (interruptions == _cachedInterruptions)
            return _cachedInterruptionsText;

        _cachedInterruptions = interruptions;
        _cachedInterruptionsText = interruptions > 0 ? interruptions.ToString() : EmDash;
        return _cachedInterruptionsText;
    }

    private string FormatAvgLatency(double? avgLatencyMs)
    {
        if (!avgLatencyMs.HasValue)
        {
            if (_cachedAvgLatencyMs == int.MinValue)
                return _cachedAvgLatencyText;

            _cachedAvgLatencyMs = int.MinValue;
            _cachedAvgLatencyText = EmDash;
            return _cachedAvgLatencyText;
        }

        var rounded = (int)Math.Round(avgLatencyMs.Value);
        if (rounded == _cachedAvgLatencyMs)
            return _cachedAvgLatencyText;

        _cachedAvgLatencyMs = rounded;
        _cachedAvgLatencyText = string.Create(
            null,
            stackalloc char[16],
            $"{rounded}ms"
        );
        return _cachedAvgLatencyText;
    }

    private static void RenderStatCell(string label, string value)
    {
        ImGui.TableNextColumn();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        ImGui.TextUnformatted(value);
    }

    private static string FormatUptime(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return string.Create(
                null,
                stackalloc char[16],
                $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            );
        }

        return string.Create(
            null,
            stackalloc char[8],
            $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
        );
    }
}
