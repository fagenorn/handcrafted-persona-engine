using System.Diagnostics;
using Hexa.NET.ImGui;
using PersonaEngine.Lib.UI.ControlPanel.Services;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Dashboard.Sections;

/// <summary>
///     Bottom stats strip: Uptime, Turns, Avg Latency, Interruptions.
/// </summary>
public sealed class SessionStatsSection(SessionStatsCollector stats)
{
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    public void Render(float dt)
    {
        ImGuiHelpers.SectionHeader("Session");

        if (!ImGui.BeginTable("##Stats", 4, ImGuiTableFlags.SizingStretchSame))
            return;

        ImGui.TableNextRow();

        RenderStatCell("Uptime", FormatUptime(_uptime.Elapsed));

        var turns = stats.TurnsStarted;
        RenderStatCell("Turns", turns > 0 ? turns.ToString() : "--");

        var avgLatency = stats.AvgFirstAudioLatencyMs;
        RenderStatCell("Avg Latency", avgLatency.HasValue ? $"{avgLatency.Value:F0}ms" : "--");

        var interruptions = stats.TurnsInterrupted;
        RenderStatCell("Interruptions", interruptions > 0 ? interruptions.ToString() : "--");

        ImGui.EndTable();
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
