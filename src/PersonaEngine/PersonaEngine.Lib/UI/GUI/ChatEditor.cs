using System.Numerics;

using Hexa.NET.ImGui;

using OpenAI.Chat;

using PersonaEngine.Lib.Core.Conversation.Abstractions.Context;
using PersonaEngine.Lib.Core.Conversation.Abstractions.Session;

namespace PersonaEngine.Lib.UI.GUI;

public class ChatEditor : ConfigSectionEditorBase
{
    private readonly FontProvider _fontProvider;

    private readonly IConversationOrchestrator _orchestrator;

    private readonly float _participantsPanelWidth = 200f;

    private readonly IUiThemeManager _themeManager;

    private IConversationContext? _currentContext;

    private string _editMessageContent = string.Empty;

    private Guid _editMessageId = Guid.Empty;

    private Guid _editTurnId = Guid.Empty;

    private IReadOnlyList<InteractionTurn> _historySnapshot = Array.Empty<InteractionTurn>();

    private bool _needsRedraw = true;

    private IReadOnlyDictionary<string, ParticipantInfo> _participantsSnapshot = new Dictionary<string, ParticipantInfo>();

    private InteractionTurn? _pendingTurnSnapshot;

    private bool _showConfirmClearPopup = false;

    private bool _showEditPopup = false;

    public ChatEditor(
        IUiConfigurationManager   configManager,
        IEditorStateManager       stateManager,
        IUiThemeManager           themeManager,
        IConversationOrchestrator orchestrator,
        FontProvider              fontProvider)
        : base(configManager, stateManager)
    {
        _orchestrator = orchestrator;
        _themeManager = themeManager;
        _fontProvider = fontProvider;

        _orchestrator.SessionsUpdated += OnCurrentContextChanged;
        OnCurrentContextChanged(null, EventArgs.Empty);
    }

    public override string SectionKey => "Chat";

    public override string DisplayName => "Conversation";

    private void OnCurrentContextChanged(object? sender, EventArgs e)
    {
        var sessionId = _orchestrator.GetActiveSessionIds().FirstOrDefault();
        if ( sessionId == Guid.Empty )
        {
            return;
        }

        var context = _orchestrator.GetSession(sessionId).Context;
        SetCurrentContext(context);
    }

    private void SetCurrentContext(IConversationContext? newContext)
    {
        if ( _currentContext == newContext )
        {
            return;
        }

        if ( _currentContext != null )
        {
            _currentContext.ConversationUpdated -= OnConversationUpdated;
        }

        _currentContext = newContext;

        if ( _currentContext != null )
        {
            _currentContext.ConversationUpdated += OnConversationUpdated;
            UpdateSnapshots();
        }
        else
        {
            _historySnapshot      = Array.Empty<InteractionTurn>();
            _participantsSnapshot = new Dictionary<string, ParticipantInfo>();
            _pendingTurnSnapshot  = null;
        }

        _needsRedraw = true;
    }

    private void OnConversationUpdated(object? sender, EventArgs e)
    {
        UpdateSnapshots();
        _needsRedraw = true;
    }

    private void UpdateSnapshots()
    {
        if ( _currentContext == null )
        {
            return;
        }

        _participantsSnapshot = new Dictionary<string, ParticipantInfo>(_currentContext.Participants);
        _historySnapshot      = _currentContext.History.Select(turn => turn.CreateSnapshot()).ToList();
        _pendingTurnSnapshot  = _currentContext.PendingTurn;
    }

    public override void Render()
    {
        if ( _needsRedraw )
        {
            _needsRedraw = false;
        }

        var availWidth = ImGui.GetContentRegionAvail().X;
        if ( _currentContext == null )
        {
            ImGui.TextWrapped("No active conversation selected.");

            return;
        }

        // --- Left Panel: Participants and Controls ---
        ImGui.BeginChild("ParticipantsPanel", new Vector2(_participantsPanelWidth, 700), ImGuiChildFlags.Borders);
        {
            RenderParticipantsPanel(_currentContext);
            RenderControlsPanel(_currentContext);
        }

        ImGui.EndChild();

        ImGui.SameLine();

        // --- Right Panel: Chat History ---
        var chatAreaWidth = availWidth - _participantsPanelWidth - ImGui.GetStyle().ItemSpacing.X;
        ImGui.BeginChild("ChatHistoryPanel", new Vector2(chatAreaWidth, 700), ImGuiChildFlags.None);
        {
            RenderChatHistory(_currentContext, chatAreaWidth);
        }

        ImGui.EndChild();

        // --- Popups ---
        RenderEditPopup(_currentContext);
        RenderConfirmClearPopup(_currentContext);
    }

    private void RenderParticipantsPanel(IConversationContext context)
    {
        ImGui.Text("Participants");
        ImGui.Separator();

        if ( !_participantsSnapshot.Any() )
        {
            ImGui.TextDisabled("No participants.");

            return;
        }

        foreach ( var participant in _participantsSnapshot.Values )
        {
            // Simple display: Icon (optional) + Name + Role
            ImGui.BulletText($"{participant.Name} ({participant.Role})");
            // Optional: Add context menu to remove participant?
            // if (ImGui.BeginPopupContextItem($"participant_{participant.Id}")) { ... }
        }
    }

    private void RenderControlsPanel(IConversationContext context)
    {
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Controls");
        ImGui.Separator();

        if ( ImGui.Button("Clear Conversation", new Vector2(-1, 0)) )
        {
            _showConfirmClearPopup = true;
        }

        // Optional: Add other controls like Apply Cleanup Strategy
        if ( ImGui.Button("Apply Cleanup", new Vector2(-1, 0)) )
        {
            _currentContext?.ApplyCleanupStrategy();
        }
    }

    private void RenderChatHistory(IConversationContext context, float availableWidth)
    {
        var maxBubbleWidth = availableWidth * 0.75f;
        var style          = ImGui.GetStyle();

        ImGui.Text("Conversation: Main");
        ImGui.Separator();

        ImGui.BeginChild("ChatScrollRegion", new Vector2(availableWidth, -1), ImGuiChildFlags.None);
        {
            for ( var turnIndex = 0; turnIndex < _historySnapshot.Count; turnIndex++ )
            {
                var turn = _historySnapshot[turnIndex];
                RenderTurn(context, turn, turnIndex, maxBubbleWidth, availableWidth);

                ImGui.Separator();
                ImGui.Spacing();
            }

            // if ( _pendingTurnSnapshot != null )
            // {
            //     ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            //     RenderTurn(context, _pendingTurnSnapshot, -1, maxBubbleWidth, availableWidth, true);
            //     ImGui.PopStyleColor();
            // }

            if ( ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 10 )
            {
                ImGui.SetScrollHereY(1.0f);
            }
        }

        ImGui.EndChild();
    }

    private void RenderTurn(IConversationContext context, InteractionTurn turn, int turnIndex, float maxBubbleWidth, float availableWidth, bool isPending = false)
    {
        var style = ImGui.GetStyle();

        ImGui.TextDisabled($"Turn {turnIndex + 1} ({turn.StartTime.ToLocalTime():g})");
        ImGui.Spacing();

        for ( var msgIndex = 0; msgIndex < turn.Messages.Count; msgIndex++ )
        {
            var message     = turn.Messages[msgIndex];
            var participant = _participantsSnapshot.GetValueOrDefault(message.ParticipantId);
            var role        = participant?.Role ?? message.Role;

            var isUserMessage = role == ChatMessageRole.User;

            var messageText = message.Text;
            if ( message.IsPartial && isPending )
            {
                messageText += " ...";
            }

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8.0f);

            var textSize     = ImGui.CalcTextSize(messageText, false, maxBubbleWidth - style.WindowPadding.X * 2);
            var nameSize     = ImGui.CalcTextSize(message.ParticipantName, false, maxBubbleWidth - style.WindowPadding.X * 2);
            var bubbleWidth  = Math.Min(textSize.X + style.WindowPadding.X * 2, maxBubbleWidth);
            var bubbleHeight = nameSize.Y + textSize.Y + style.WindowPadding.Y * 2 + (isUserMessage ? ImGui.GetTextLineHeight() : 0);

            var startPosX = style.WindowPadding.X;
            if ( isUserMessage )
            {
                startPosX = availableWidth - bubbleWidth - style.WindowPadding.X - style.ScrollbarSize;
            }

            ImGui.SetCursorPosX(startPosX);

            // --- Styling ---
            Vector4 bgColor,
                    textColor;

            if ( isUserMessage )
            {
                bgColor   = new Vector4(0.15f, 0.48f, 0.88f, 1.0f); // Blueish
                textColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);    // White text
            }
            else
            {
                bgColor   = new Vector4(0.92f, 0.92f, 0.92f, 1.0f); // Light gray
                textColor = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);    // Dark text
            }

            ImGui.PushStyleColor(ImGuiCol.ChildBg, bgColor);
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);

            // --- Render Bubble ---
            var childId = $"msg_{turn.TurnId}_{message.MessageId}";
            ImGui.BeginChild(childId, new Vector2(bubbleWidth, bubbleHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.None);
            {
                if ( isUserMessage )
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.52f, 0.92f, 0.89f, 1f));
                    ImGui.TextWrapped(message.ParticipantName);
                    ImGui.PopStyleColor();
                }

                ImGui.TextWrapped(messageText);

                ImGui.EndChild();
            }

            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);

            // --- Context Menu (Edit/Delete) - Only for committed messages ---
            if ( !isPending && ImGui.BeginPopupContextItem($"ctx_{childId}") )
            {
                if ( ImGui.MenuItem("Edit") )
                {
                    _editTurnId         = turn.TurnId;
                    _editMessageId      = message.MessageId;
                    _editMessageContent = message.Text;
                    _showEditPopup      = true;
                }

                ImGui.Separator();
                if ( ImGui.MenuItem("Delete") )
                {
                    context.TryDeleteMessage(turn.TurnId, message.MessageId);
                }

                ImGui.EndPopup();
            }

            ImGui.Dummy(new Vector2(0, style.ItemSpacing.Y));
        }
    }

    private void RenderEditPopup(IConversationContext context)
    {
        if ( _showEditPopup )
        {
            ImGui.OpenPopup("Edit Message");
            _showEditPopup = false;
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.Appearing);

        if ( ImGui.BeginPopupModal("Edit Message", ImGuiWindowFlags.NoResize) )
        {
            ImGui.InputTextMultiline("##edit_content", ref _editMessageContent, 1024 * 4, new Vector2(0, 150));

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var buttonWidth       = 120f;
            var totalButtonsWidth = buttonWidth * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalButtonsWidth) * 0.5f);

            if ( ImGui.Button("Save", new Vector2(buttonWidth, 0)) )
            {
                if ( _editMessageId != Guid.Empty && _editTurnId != Guid.Empty )
                {
                    context.TryUpdateMessage(_editTurnId, _editMessageId, _editMessageContent);

                    _editMessageId      = Guid.Empty;
                    _editTurnId         = Guid.Empty;
                    _editMessageContent = string.Empty;
                }

                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if ( ImGui.Button("Cancel", new Vector2(buttonWidth, 0)) )
            {
                _editMessageId      = Guid.Empty;
                _editTurnId         = Guid.Empty;
                _editMessageContent = string.Empty;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
        else
        {
            if ( _editMessageId == Guid.Empty )
            {
                return;
            }

            _editMessageId      = Guid.Empty;
            _editTurnId         = Guid.Empty;
            _editMessageContent = string.Empty;
        }
    }

    private void RenderConfirmClearPopup(IConversationContext context)
    {
        if ( _showConfirmClearPopup )
        {
            ImGui.OpenPopup("Confirm Clear");
            _showConfirmClearPopup = false;
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if ( ImGui.BeginPopupModal("Confirm Clear", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize) )
        {
            ImGui.TextWrapped("Are you sure you want to clear the entire conversation history?");
            ImGui.TextWrapped("This action cannot be undone.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            var buttonWidth       = 120f;
            var totalButtonsWidth = buttonWidth * 2 + ImGui.GetStyle().ItemSpacing.X;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - totalButtonsWidth) * 0.5f);

            if ( ImGui.Button("Yes, Clear", new Vector2(buttonWidth, 0)) )
            {
                _currentContext?.ClearHistory();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if ( ImGui.Button("No, Cancel", new Vector2(buttonWidth, 0)) )
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}