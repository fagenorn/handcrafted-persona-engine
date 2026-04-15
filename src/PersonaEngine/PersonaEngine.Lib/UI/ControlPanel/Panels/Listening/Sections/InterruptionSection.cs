using Hexa.NET.ImGui;
using Microsoft.Extensions.Options;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Configuration;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Strategies;
using PersonaEngine.Lib.UI.ControlPanel.Layout;

namespace PersonaEngine.Lib.UI.ControlPanel.Panels.Listening.Sections;

/// <summary>
///     Two-axis composer for <see cref="ConversationOptions.BargeInType" />:
///     master enable toggle, "trigger after N words" slider, and "allow while avatar
///     is speaking" toggle. Maps onto all five <see cref="BargeInType" /> values
///     with no loss of expressivity.
/// </summary>
public sealed class InterruptionSection : IDisposable
{
    private readonly IConfigWriter _configWriter;
    private readonly IDisposable? _changeSubscription;

    private ConversationOptions _conversation;
    private AnimatedFloat _enabledKnob;
    private AnimatedFloat _midSpeechKnob;
    private bool _initialized;

    public InterruptionSection(
        IOptionsMonitor<ConversationOptions> monitor,
        IConfigWriter configWriter
    )
    {
        _configWriter = configWriter;
        _conversation = monitor.CurrentValue;
        _changeSubscription = monitor.OnChange((updated, _) => _conversation = updated);
    }

    public void Dispose() => _changeSubscription?.Dispose();

    public void Render(float dt)
    {
        var (enabled, allowDuringSpeech) = InterruptionComposer.Decompose(
            _conversation.BargeInType
        );

        if (!_initialized)
        {
            _enabledKnob = new AnimatedFloat(enabled ? 1f : 0f);
            _midSpeechKnob = new AnimatedFloat(allowDuringSpeech ? 1f : 0f);
            _initialized = true;
        }

        using (Ui.Card("##interruption", padding: 12f))
        {
            RenderHeader();

            // Master toggle
            var rowY = ImGui.GetCursorPosY();
            ImGuiHelpers.SettingLabel(
                "Allow interruptions",
                "Master switch. When off, the avatar ignores your voice mid-response."
            );
            var enabledChanged = ImGuiHelpers.ToggleSwitch(
                "##barge_enabled",
                ref enabled,
                ref _enabledKnob,
                dt
            );
            ImGuiHelpers.SettingEndRow(rowY);

            ImGui.BeginDisabled(!enabled);

            // Words-to-trigger slider
            var minWords = _conversation.BargeInMinWords;
            rowY = ImGui.GetCursorPosY();
            ImGuiHelpers.SettingLabel(
                "Trigger after",
                "How many recognised words you need before the avatar yields. 1 = any sound; higher = only real sentences."
            );
            var wordsChanged = ImGuiHelpers.LabeledSlider(
                "##barge_words",
                ref minWords,
                1,
                10,
                "Any sound",
                "Real sentences",
                dt
            );
            ImGuiHelpers.SettingEndRow(rowY);

            // Mid-speech toggle
            rowY = ImGui.GetCursorPosY();
            ImGuiHelpers.SettingLabel(
                "Allow while the avatar is speaking",
                "If off, you can only interrupt when the avatar is already listening or waiting — not in the middle of its response."
            );
            var midChanged = ImGuiHelpers.ToggleSwitch(
                "##mid_speech",
                ref allowDuringSpeech,
                ref _midSpeechKnob,
                dt
            );
            ImGuiHelpers.SettingEndRow(rowY);

            ImGui.EndDisabled();

            if (enabledChanged || wordsChanged || midChanged)
            {
                var composed = InterruptionComposer.Compose(enabled, minWords, allowDuringSpeech);
                _conversation = _conversation with
                {
                    BargeInType = composed,
                    BargeInMinWords = minWords,
                };
                _configWriter.Write(_conversation);
            }
        }
    }

    private static void RenderHeader()
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextTertiary);
        ImGui.TextUnformatted("Interruption");
        ImGui.PopStyleColor();

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.TextSecondary);
        ImGui.TextUnformatted("When you can cut the avatar off mid-response");
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }
}

/// <summary>
///     Pure mapping between the two-axis UI composer and the five <see cref="BargeInType" /> values.
///     Extracted from <see cref="InterruptionSection" /> so the mapping is unit-testable
///     without an ImGui context.
/// </summary>
public static class InterruptionComposer
{
    public static BargeInType Compose(bool enabled, int minWords, bool allowDuringSpeech) =>
        enabled switch
        {
            false => BargeInType.Ignore,
            true when minWords <= 1 && allowDuringSpeech => BargeInType.Allow,
            true when minWords <= 1 && !allowDuringSpeech => BargeInType.NoSpeaking,
            true when minWords > 1 && allowDuringSpeech => BargeInType.MinWords,
            _ => BargeInType.MinWordsNoSpeaking,
        };

    public static (bool Enabled, bool AllowDuringSpeech) Decompose(BargeInType type) =>
        type switch
        {
            BargeInType.Ignore => (false, true),
            BargeInType.Allow => (true, true),
            BargeInType.NoSpeaking => (true, false),
            BargeInType.MinWords => (true, true),
            BargeInType.MinWordsNoSpeaking => (true, false),
            _ => (false, true),
        };
}
