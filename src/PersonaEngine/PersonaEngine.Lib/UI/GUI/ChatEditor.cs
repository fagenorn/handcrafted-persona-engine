using System.Numerics;

using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Widgets;

using Microsoft.SemanticKernel.ChatCompletion;

using PersonaEngine.Lib.LLM;

namespace PersonaEngine.Lib.UI.GUI;

public class ChatEditor : ConfigSectionEditorBase
{
    private readonly IChatEngine _chatEngine;

    private readonly FontProvider _fontProvider;

    private readonly IUiThemeManager _themeManager;

    private string _editMessageContent;

    private Guid _editMessageId;

    private Guid _insertMessageId;

    private IReadOnlyList<dynamic> _messages;

    private string _newMessageText;

    private bool _shouldScrollToBottom;

    public ChatEditor(
        IUiConfigurationManager configManager,
        IEditorStateManager     stateManager,
        IUiThemeManager         themeManager,
        IChatEngine             chatEngine,
        FontProvider            fontProvider)
        : base(configManager, stateManager)
    {
        _chatEngine   = chatEngine;
        _themeManager = themeManager;
        _fontProvider = fontProvider;

        _messages = new []{new {Id = "asdasd", Content = "asdasd"}};
        // _chatEngine.HistoryManager.OnChatHistoryChanged += (sender, args) =>
        //                                                    {
        //                                                        _messages             = _chatEngine.HistoryManager.ChatHistoryItems;
        //                                                        _shouldScrollToBottom = true;
        //                                                    };
    }

    public override string SectionKey => "Chat";

    public override string DisplayName => "Chat Configuration";

    public override void Render() { RenderChat(); }

    private void RenderChat()
    {
        var availWidth = ImGui.GetContentRegionAvail().X;

        var maxBubbleWidth = availWidth * 0.7f;

        var editPopup = false;
        ImGui.BeginChild("chat", new Vector2(availWidth, 600), ImGuiChildFlags.Borders);
        {
            for ( var i = 0; i < _messages.Count; i++ )
            {
                var message     = _messages[i];
                var isMyMessage = false;// message.Role == AuthorRole.User;

                var textSize     = ImGui.CalcTextSize(message.Content, false, maxBubbleWidth - 10 - ImGui.GetStyle().WindowPadding.X * 2);
                var bubbleWidth  = Math.Min(textSize.X + ImGui.GetStyle().WindowPadding.X * 2, maxBubbleWidth);
                var bubbleHeight = textSize.Y + ImGui.GetStyle().WindowPadding.Y * 2;

                if ( isMyMessage )
                {
                    ImGui.SetCursorPosX(availWidth - bubbleWidth - 20);
                }

                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6.0f);

                if ( isMyMessage )
                {
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.68f, 0.38f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1.0f));
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.92f, 0.89f, 0.85f, 1.0f));
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.27f, 0.27f, 0.3f, 1.0f));
                }

                ImGui.BeginChild(message.Id.ToString(), new Vector2(bubbleWidth, bubbleHeight), ImGuiChildFlags.FrameStyle);
                {
                    ImGui.SetCursorPosY((float)((bubbleHeight - textSize.Y) * 0.5));
                    ImGui.TextWrapped(message.Content);

                    ImGui.PopStyleColor(2);

                    if ( ImGui.BeginPopupContextItem(i.ToString()) )
                    {
                        if ( ImGui.MenuItem("Edit") )
                        {
                            _editMessageId      = message.Id;
                            _editMessageContent = message.Content;
                            editPopup           = true;
                        }

                        if ( ImGui.MenuItem("Delete") )
                        {
                            // _chatEngine.HistoryManager.RemoveMessage(message.Id);
                            i--;
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.EndChild();
                }

                ImGui.PopStyleVar();

                ImGui.Dummy(new Vector2(0, 10));
            }

            // Auto-scroll to bottom when new messages are added
            if ( _shouldScrollToBottom )
            {
                ImGui.SetScrollHereY(1.0f);
                _shouldScrollToBottom = false;
            }

            ImGui.EndChild();
        }

        if ( editPopup )
        {
            ImGui.OpenPopup("Edit Message");
        }

        if ( ImGui.BeginPopupModal("Edit Message", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize) )
        {
            ImGui.InputTextMultiline("##edit_content", ref _editMessageContent, 1024, new Vector2(400, 100));

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.SetCursorPosX(400 * 0.5f - 150);

            if ( ImGui.Button("Save", new Vector2(150, 0)) )
            {
                if ( _editMessageId != Guid.Empty )
                {
                    // _chatEngine.HistoryManager.UpdateMessage(_editMessageId, _editMessageContent);
                    _editMessageId = Guid.Empty;
                }

                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(0, 10);

            if ( ImGui.Button("Cancel", new Vector2(150, 0)) )
            {
                _editMessageId = Guid.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.Spacing();

        // New message input
        ImGui.SetNextItemWidth(availWidth - 85);
        var inputMessage = string.Empty;
        var enterPressed = ImGui.InputTextWithHint("##new_message", "Type a message...", ref inputMessage, 1024, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.SameLine();

        if ( enterPressed )
        {
            if ( !string.IsNullOrWhiteSpace(inputMessage) )
            {
                // _chatEngine.HistoryManager.AddUserMessage(inputMessage);
            }
        }

        ImGui.SameLine();

        // Clear all messages button
        if ( ImGui.Button("Clear All", new Vector2(80, 0)) )
        {
            ImGui.OpenPopup("Confirm Clear");
        }

        // Confirmation popup for clearing messages
        if ( ImGui.BeginPopupModal("Confirm Clear", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize) )
        {
            ImGui.BeginChild("##ConfirmClear"u8, new Vector2(400, 0), ImGuiChildFlags.None | ImGuiChildFlags.AutoResizeY);
            {
                TextHelper.TextCenteredH("Are you sure you want to clear all messages?");
                TextHelper.TextCenteredH("This action cannot be undone.");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.SetCursorPosX(400 * 0.5f - 120);

                if ( ImGui.Button("Yes", new Vector2(120, 0)) )
                {
                    // _chatEngine.HistoryManager.Clear();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine(0, 10);

                if ( ImGui.Button("No", new Vector2(120, 0)) )
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }
    }
}