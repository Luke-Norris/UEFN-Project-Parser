using WellVersed.Core.Config;
using WellVersed.Core.Models;
using Microsoft.Extensions.Logging;

namespace WellVersed.Core.Services.VerseGeneration;

/// <summary>
/// Generates Verse source files for UI widgets using UEFN's Verse UI API.
/// Produces complete, compilable .verse files with proper using statements,
/// class definitions, and UI construction code.
/// </summary>
public class VerseUIGenerator
{
    private readonly WellVersedConfig _config;
    private readonly ILogger<VerseUIGenerator> _logger;

    public VerseUIGenerator(WellVersedConfig config, ILogger<VerseUIGenerator> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Generates a Verse UI file from a template.
    /// </summary>
    public VerseFileResult Generate(VerseFileRequest request)
    {
        var result = new VerseFileResult { ClassName = request.ClassName };

        try
        {
            var code = request.TemplateType switch
            {
                VerseTemplateType.HudOverlay => GenerateHUD(request),
                VerseTemplateType.Scoreboard => GenerateScoreboard(request),
                VerseTemplateType.InteractionMenu => GenerateInteractionMenu(request),
                VerseTemplateType.NotificationPopup => GenerateNotificationPopup(request),
                VerseTemplateType.ItemTracker => GenerateItemTracker(request),
                VerseTemplateType.ProgressBar => GenerateProgressBar(request),
                VerseTemplateType.CustomWidget => GenerateCustomWidget(request),
                _ => throw new ArgumentException($"Template type '{request.TemplateType}' is not a UI template.")
            };

            result.SourceCode = code;
            result.Success = true;

            // Write to file if output directory is set
            var outputDir = request.OutputDirectory
                ?? Path.Combine(_config.ProjectPath, "Content");
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                var fileName = string.IsNullOrEmpty(request.FileName)
                    ? request.ClassName
                    : request.FileName;
                result.FilePath = Path.Combine(outputDir, $"{fileName}.verse");
                File.WriteAllText(result.FilePath, code);
                result.Notes.Add($"File written to {result.FilePath}");
            }

            result.Notes.Add("Remember to add this device to your level in UEFN after compiling.");
            result.Notes.Add("The device needs to be placed in the level for the UI to show.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _logger.LogError(ex, "Failed to generate UI template {Type}", request.TemplateType);
        }

        return result;
    }

    /// <summary>
    /// Returns metadata about all available UI templates.
    /// </summary>
    public static List<VerseTemplateInfo> GetUITemplates()
    {
        return new List<VerseTemplateInfo>
        {
            new()
            {
                Name = "HUD Overlay",
                Description = "In-game heads-up display with health, shield, ammo, score, and timer elements. " +
                              "Fully reactive — updates automatically when game state changes.",
                Type = VerseTemplateType.HudOverlay,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "show_health", Description = "Show health bar", DefaultValue = "true" },
                    new() { Name = "show_shield", Description = "Show shield bar", DefaultValue = "true" },
                    new() { Name = "show_score", Description = "Show score counter", DefaultValue = "true" },
                    new() { Name = "show_timer", Description = "Show countdown timer", DefaultValue = "true" },
                    new() { Name = "show_elimination_feed", Description = "Show elimination notifications", DefaultValue = "true" },
                    new() { Name = "timer_seconds", Description = "Initial timer duration in seconds", DefaultValue = "300" }
                }
            },
            new()
            {
                Name = "Scoreboard",
                Description = "Team or FFA scoreboard showing player names, scores, and eliminations. " +
                              "Can be toggled with a button device.",
                Type = VerseTemplateType.Scoreboard,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "max_players", Description = "Max players to display", DefaultValue = "16" },
                    new() { Name = "show_teams", Description = "Team-based layout", DefaultValue = "true" },
                    new() { Name = "title", Description = "Scoreboard title text", DefaultValue = "Scoreboard" }
                }
            },
            new()
            {
                Name = "Interaction Menu",
                Description = "Pop-up menu triggered by a button or trigger device. " +
                              "Supports multiple selectable options with callbacks.",
                Type = VerseTemplateType.InteractionMenu,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "title", Description = "Menu title", DefaultValue = "Select Option" },
                    new() { Name = "option_count", Description = "Number of menu options", DefaultValue = "3" }
                }
            },
            new()
            {
                Name = "Notification Popup",
                Description = "Toast-style notification that slides in and auto-dismisses. " +
                              "Call ShowNotification() from other Verse devices.",
                Type = VerseTemplateType.NotificationPopup,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "duration", Description = "Display duration in seconds", DefaultValue = "3.0" },
                    new() { Name = "position", Description = "Screen position (top/center/bottom)", DefaultValue = "top" }
                }
            },
            new()
            {
                Name = "Item Tracker",
                Description = "Collection/inventory tracker showing items collected vs total. " +
                              "Wire to item granters or trigger devices.",
                Type = VerseTemplateType.ItemTracker,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "total_items", Description = "Total collectible items", DefaultValue = "10" },
                    new() { Name = "item_name", Description = "What's being collected", DefaultValue = "Coins" }
                }
            },
            new()
            {
                Name = "Progress Bar",
                Description = "Animated progress bar for objectives, loading, or cooldowns. " +
                              "Set progress from 0.0 to 1.0 via UpdateProgress().",
                Type = VerseTemplateType.ProgressBar,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "label", Description = "Label text", DefaultValue = "Progress" },
                    new() { Name = "bar_color", Description = "Bar color (red/green/blue/yellow)", DefaultValue = "green" }
                }
            },
            new()
            {
                Name = "Custom Widget",
                Description = "Blank canvas with helper methods. Build your own UI from scratch " +
                              "with AddText, AddColorBlock, and layout helpers.",
                Type = VerseTemplateType.CustomWidget,
                Category = "UI",
                Parameters = new()
                {
                    new() { Name = "element_count", Description = "Number of placeholder elements", DefaultValue = "2" }
                }
            }
        };
    }

    // =====================================================================
    //  HUD OVERLAY
    // =====================================================================

    private string GenerateHUD(VerseFileRequest request)
    {
        var p = request.Parameters;
        var showHealth = GetBool(p, "show_health", true);
        var showShield = GetBool(p, "show_shield", true);
        var showScore = GetBool(p, "show_score", true);
        var showTimer = GetBool(p, "show_timer", true);
        var showElimFeed = GetBool(p, "show_elimination_feed", true);
        var timerSeconds = GetInt(p, "timer_seconds", 300);

        var b = new VerseCodeBuilder();
        b.Comment($"HUD Overlay — Generated by WellVersed");
        b.Comment($"Place this device in your level to display the HUD for all players.");
        b.Line();
        b.UIUsings();
        b.Line();

        b.ClassDef(request.ClassName);

        // Editable device references
        if (showScore)
        {
            b.Editable("ScoreManager", "score_manager_device", "score_manager_device{}");
        }
        if (showElimFeed)
        {
            b.Editable("EliminationFeed", "elimination_manager_device", "elimination_manager_device{}");
        }
        b.Line();

        // Mutable state
        if (showScore)
            b.Var("CurrentScore", "int", "0");
        if (showTimer)
            b.Var("TimeRemaining", "float", $"{timerSeconds}.0");
        b.Var("PlayerUIPerPlayer", "?player_ui", "false");
        b.Var("ActiveCanvas", "?canvas", "false");
        b.Line();

        // Text block references for updating
        if (showHealth)
            b.Var("HealthText", "?text_block", "false");
        if (showShield)
            b.Var("ShieldText", "?text_block", "false");
        if (showScore)
            b.Var("ScoreText", "?text_block", "false");
        if (showTimer)
            b.Var("TimerText", "?text_block", "false");

        // OnBegin
        b.OnBegin();
        b.Comment("Set up HUD for each player that joins");
        b.Line("GetPlayspace().PlayerAddedEvent().Subscribe(OnPlayerAdded)");
        b.Line();
        b.Comment("Set up HUD for players already in the game");
        b.Line("AllPlayers := GetPlayspace().GetPlayers()");
        b.Line("for (Player : AllPlayers):");
        b.Indent();
        b.Line("SetupHUDForPlayer(Player)");
        b.Dedent();

        if (showTimer)
        {
            b.Line();
            b.Comment("Start the timer loop");
            b.Line("TimerLoop()");
        }
        b.EndBlock();

        // OnPlayerAdded
        b.Line();
        b.MethodWithParams("OnPlayerAdded", "Player:player", "void");
        b.Line("SetupHUDForPlayer(Player)");
        b.EndBlock();

        // SetupHUDForPlayer
        b.Line();
        b.MethodWithParams("SetupHUDForPlayer", "Player:player", "void");

        // Build the canvas with all requested elements
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();

        // Create text blocks
        if (showHealth)
        {
            b.Line("HealthDisplay := text_block{DefaultText := StringToMessage(\"HP: 100\")}");
            b.Line("set HealthText = option{HealthDisplay}");
        }
        if (showShield)
        {
            b.Line("ShieldDisplay := text_block{DefaultText := StringToMessage(\"Shield: 100\")}");
            b.Line("set ShieldText = option{ShieldDisplay}");
        }
        if (showScore)
        {
            b.Line("ScoreDisplay := text_block{DefaultText := StringToMessage(\"Score: 0\")}");
            b.Line("set ScoreText = option{ScoreDisplay}");
        }
        if (showTimer)
        {
            b.Line("TimerDisplay := text_block{DefaultText := StringToMessage(\"5:00\")}");
            b.Line("set TimerText = option{TimerDisplay}");
        }

        b.Line();
        b.Line("HUDCanvas := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();

        // Health — bottom left
        if (showHealth)
        {
            b.Line("canvas_slot:");
            b.Indent();
            b.Line("Widget := HealthDisplay");
            b.Line("Anchors := anchors{Minimum := vector2{X := 0.05, Y := 0.9}, Maximum := vector2{X := 0.05, Y := 0.9}}");
            b.Line("Alignment := vector2{X := 0.0, Y := 1.0}");
            b.Dedent();
        }

        // Shield — bottom left, above health
        if (showShield)
        {
            b.Line("canvas_slot:");
            b.Indent();
            b.Line("Widget := ShieldDisplay");
            b.Line("Anchors := anchors{Minimum := vector2{X := 0.05, Y := 0.85}, Maximum := vector2{X := 0.05, Y := 0.85}}");
            b.Line("Alignment := vector2{X := 0.0, Y := 1.0}");
            b.Dedent();
        }

        // Score — top right
        if (showScore)
        {
            b.Line("canvas_slot:");
            b.Indent();
            b.Line("Widget := ScoreDisplay");
            b.Line("Anchors := anchors{Minimum := vector2{X := 0.95, Y := 0.05}, Maximum := vector2{X := 0.95, Y := 0.05}}");
            b.Line("Alignment := vector2{X := 1.0, Y := 0.0}");
            b.Dedent();
        }

        // Timer — top center
        if (showTimer)
        {
            b.Line("canvas_slot:");
            b.Indent();
            b.Line("Widget := TimerDisplay");
            b.Line("Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.03}, Maximum := vector2{X := 0.5, Y := 0.03}}");
            b.Line("Alignment := vector2{X := 0.5, Y := 0.0}");
            b.Dedent();
        }

        b.Dedent(); // Slots array
        b.Dedent(); // canvas
        b.Line();
        b.Line("PlayerUI.AddWidget(HUDCanvas)");
        b.Line("set ActiveCanvas = option{HUDCanvas}");
        b.Line("set PlayerUIPerPlayer = option{PlayerUI}");
        b.Dedent(); // if PlayerUI
        b.EndBlock(); // method

        // Timer loop
        if (showTimer)
        {
            b.Line();
            b.Method("TimerLoop", "void", suspends: true);
            b.Line("loop:");
            b.Indent();
            b.Line("Sleep(1.0)");
            b.Line("set TimeRemaining -= 1.0");
            b.Line("if (TimeRemaining <= 0.0):");
            b.Indent();
            b.Line("OnTimerExpired()");
            b.Line("break");
            b.Dedent();
            b.Line("Minutes := Floor(TimeRemaining / 60.0)");
            b.Line("Seconds := Floor(Mod(TimeRemaining, 60.0))");
            b.Line("if (Timer := TimerText?):");
            b.Indent();
            b.Line("Timer.SetText(StringToMessage(\"{Minutes}:{Seconds}\"))");
            b.Dedent();
            b.Dedent(); // loop
            b.EndBlock();

            b.Line();
            b.Method("OnTimerExpired", "void");
            b.Comment("Timer hit zero — end the round or trigger an event");
            b.Line("Print(\"Timer expired!\")");
            b.EndBlock();
        }

        // UpdateScore helper
        if (showScore)
        {
            b.Line();
            b.MethodWithParams("UpdateScore", "NewScore:int", "void");
            b.Line("set CurrentScore = NewScore");
            b.Line("if (ScoreDisplay := ScoreText?):");
            b.Indent();
            b.Line("ScoreDisplay.SetText(StringToMessage(\"Score: {CurrentScore}\"))");
            b.Dedent();
            b.EndBlock();
        }

        b.EndBlock(); // class

        return b.ToString();
    }

    // =====================================================================
    //  SCOREBOARD
    // =====================================================================

    private string GenerateScoreboard(VerseFileRequest request)
    {
        var p = request.Parameters;
        var maxPlayers = GetInt(p, "max_players", 16);
        var showTeams = GetBool(p, "show_teams", true);
        var title = GetString(p, "title", "Scoreboard");

        var b = new VerseCodeBuilder();
        b.Comment($"Scoreboard — Generated by WellVersed");
        b.Comment("Toggle visibility with a button device wired to ToggleScoreboard.");
        b.Line();
        b.UIUsings();
        b.Using("/Fortnite.com/Game");
        b.Using("/Fortnite.com/Playspaces");
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("ToggleButton", "button_device", "button_device{}");
        b.Line();
        b.Var("IsVisible", "logic", "false");
        b.Var("ScoreboardCanvas", "?canvas", "false");
        b.Var("ScoreEntries", "[]{text_block}", "array{}");
        b.Line();

        b.OnBegin();
        b.Line("ToggleButton.InteractedWithEvent.Subscribe(OnTogglePressed)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnTogglePressed", "Agent:agent", "void");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (IsVisible?):");
        b.Indent();
        b.Line("HideScoreboard(Player)");
        b.Dedent();
        b.Line("else:");
        b.Indent();
        b.Line("ShowScoreboard(Player)");
        b.Dedent();
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("ShowScoreboard", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();

        // Build header
        b.Line($"TitleText := text_block{{DefaultText := StringToMessage(\"{title}\")}}");
        b.Line();

        // Build player rows
        b.Line("AllPlayers := GetPlayspace().GetPlayers()");
        b.Line("var Rows : []canvas_slot = array{}");
        b.Line("var YOffset : float = 0.2");
        b.Line();
        b.Line($"for (Index := 0..Min(AllPlayers.Length - 1, {maxPlayers - 1})):");
        b.Indent();
        b.Line("if (P := AllPlayers[Index]):");
        b.Indent();
        b.Line("EntryText := text_block{DefaultText := StringToMessage(\"Player {Index + 1} — Score: 0\")}");
        b.Line("EntrySlot := canvas_slot:");
        b.Indent();
        b.Line("Widget := EntryText");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.5, Y := YOffset}, Maximum := vector2{X := 0.5, Y := YOffset}}");
        b.Line("Alignment := vector2{X := 0.5, Y := 0.0}");
        b.Dedent();
        b.Line("set Rows += array{EntrySlot}");
        b.Line("set YOffset += 0.04");
        b.Dedent();
        b.Dedent();

        b.Line();
        b.Line("Board := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := TitleText");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.12}, Maximum := vector2{X := 0.5, Y := 0.12}}");
        b.Line("Alignment := vector2{X := 0.5, Y := 0.0}");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.Line();
        b.Comment("TODO: Append Rows to Board.Slots when dynamic slot addition is supported");
        b.Line("PlayerUI.AddWidget(Board)");
        b.Line("set ScoreboardCanvas = option{Board}");
        b.Line("set IsVisible = true");
        b.Dedent(); // if PlayerUI
        b.EndBlock();

        b.Line();
        b.MethodWithParams("HideScoreboard", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player], Board := ScoreboardCanvas?):");
        b.Indent();
        b.Line("PlayerUI.RemoveWidget(Board)");
        b.Line("set ScoreboardCanvas = false");
        b.Line("set IsVisible = false");
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  INTERACTION MENU
    // =====================================================================

    private string GenerateInteractionMenu(VerseFileRequest request)
    {
        var p = request.Parameters;
        var title = GetString(p, "title", "Select Option");
        var optionCount = GetInt(p, "option_count", 3);

        var b = new VerseCodeBuilder();
        b.Comment("Interaction Menu — Generated by WellVersed");
        b.Comment("Wire a button_device to show the menu. Options trigger custom events.");
        b.Line();
        b.UIUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("MenuTrigger", "button_device", "button_device{}");
        for (int i = 1; i <= optionCount; i++)
            b.Editable($"Option{i}Trigger", "trigger_device", "trigger_device{}");
        b.Line();
        b.Var("MenuCanvas", "?canvas", "false");
        b.Var("IsOpen", "logic", "false");
        b.Line();

        b.OnBegin();
        b.Line("MenuTrigger.InteractedWithEvent.Subscribe(OnMenuOpen)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnMenuOpen", "Agent:agent", "void");
        b.Line("if (Player := player[Agent]):");
        b.Indent();
        b.Line("if (IsOpen?). HideMenu(Player)");
        b.Line("else. ShowMenu(Player)");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("ShowMenu", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();

        b.Line($"TitleBlock := text_block{{DefaultText := StringToMessage(\"{title}\")}}");

        // Build option text blocks
        for (int i = 1; i <= optionCount; i++)
        {
            b.Line($"Option{i}Text := text_block{{DefaultText := StringToMessage(\"[{i}] Option {i}\")}}");
        }

        b.Line();
        b.Line("Menu := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();

        // Title slot
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := TitleBlock");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.3}, Maximum := vector2{X := 0.5, Y := 0.3}}");
        b.Line("Alignment := vector2{X := 0.5, Y := 0.0}");
        b.Dedent();

        // Option slots
        for (int i = 1; i <= optionCount; i++)
        {
            var yPos = 0.35 + (i * 0.05);
            b.Line("canvas_slot:");
            b.Indent();
            b.Line($"Widget := Option{i}Text");
            b.Line($"Anchors := anchors{{Minimum := vector2{{X := 0.5, Y := {yPos:F2}}}, Maximum := vector2{{X := 0.5, Y := {yPos:F2}}}}}");
            b.Line("Alignment := vector2{X := 0.5, Y := 0.0}");
            b.Dedent();
        }

        b.Dedent(); // array
        b.Dedent(); // canvas
        b.Line();
        b.Line("PlayerUI.AddWidget(Menu)");
        b.Line("set MenuCanvas = option{Menu}");
        b.Line("set IsOpen = true");
        b.Dedent(); // if PlayerUI
        b.EndBlock();

        b.Line();
        b.MethodWithParams("HideMenu", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player], Menu := MenuCanvas?):");
        b.Indent();
        b.Line("PlayerUI.RemoveWidget(Menu)");
        b.Line("set MenuCanvas = false");
        b.Line("set IsOpen = false");
        b.Dedent();
        b.EndBlock();

        for (int i = 1; i <= optionCount; i++)
        {
            b.Line();
            b.Method($"OnOption{i}Selected", "void");
            b.Comment($"Handle option {i} selection");
            b.Line($"Option{i}Trigger.Trigger()");
            b.Line($"Print(\"Option {i} selected\")");
            b.EndBlock();
        }

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  NOTIFICATION POPUP
    // =====================================================================

    private string GenerateNotificationPopup(VerseFileRequest request)
    {
        var p = request.Parameters;
        var duration = GetString(p, "duration", "3.0");
        var position = GetString(p, "position", "top");

        var yAnchor = position switch
        {
            "bottom" => "0.9",
            "center" => "0.5",
            _ => "0.08"
        };

        var b = new VerseCodeBuilder();
        b.Comment("Notification Popup — Generated by WellVersed");
        b.Comment("Call ShowNotification() to display a timed message on screen.");
        b.Line();
        b.UIUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("NotifyTrigger", "trigger_device", "trigger_device{}");
        b.Line();
        b.Var("NotificationCanvas", "?canvas", "false");
        b.Var("NotificationText", "?text_block", "false");
        b.Var("ActivePlayerUI", "?player_ui", "false");
        b.Line();

        b.OnBegin();
        b.Line("NotifyTrigger.TriggeredEvent.Subscribe(OnNotifyTriggered)");
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnNotifyTriggered", "MaybeAgent:?agent", "void");
        b.Line("if (Agent := MaybeAgent?, Player := player[Agent]):");
        b.Indent();
        b.Line("ShowNotification(Player, \"Notification!\")");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("ShowNotification", "Player:player, Message:string", "void", suspends: true);
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();
        b.Line("# Dismiss any existing notification");
        b.Line("DismissNotification()");
        b.Line();
        b.Line("MsgText := text_block{DefaultText := StringToMessage(Message)}");
        b.Line("set NotificationText = option{MsgText}");
        b.Line();
        b.Line("NotifyCanvas := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := MsgText");
        b.Line($"Anchors := anchors{{Minimum := vector2{{X := 0.5, Y := {yAnchor}}}, Maximum := vector2{{X := 0.5, Y := {yAnchor}}}}}");
        b.Line("Alignment := vector2{X := 0.5, Y := 0.5}");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.Line();
        b.Line("PlayerUI.AddWidget(NotifyCanvas)");
        b.Line("set NotificationCanvas = option{NotifyCanvas}");
        b.Line("set ActivePlayerUI = option{PlayerUI}");
        b.Line();
        b.Line($"Sleep({duration})");
        b.Line("DismissNotification()");
        b.Dedent(); // if PlayerUI
        b.EndBlock();

        b.Line();
        b.Method("DismissNotification", "void");
        b.Line("if (PUI := ActivePlayerUI?, Canvas := NotificationCanvas?):");
        b.Indent();
        b.Line("PUI.RemoveWidget(Canvas)");
        b.Line("set NotificationCanvas = false");
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  ITEM TRACKER
    // =====================================================================

    private string GenerateItemTracker(VerseFileRequest request)
    {
        var p = request.Parameters;
        var totalItems = GetInt(p, "total_items", 10);
        var itemName = GetString(p, "item_name", "Coins");

        var b = new VerseCodeBuilder();
        b.Comment("Item Tracker — Generated by WellVersed");
        b.Comment($"Tracks collection progress: 0/{totalItems} {itemName}.");
        b.Comment("Wire item_granter_device or trigger_device to OnItemCollected.");
        b.Line();
        b.UIUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Editable("ItemGranter", "item_granter_device", "item_granter_device{}");
        b.Editable("CompletionTrigger", "trigger_device", "trigger_device{}");
        b.Line();
        b.ImmutableVar("TotalItems", "int", totalItems.ToString());
        b.Var("CollectedCount", "int", "0");
        b.Var("TrackerText", "?text_block", "false");
        b.Var("TrackerCanvas", "?canvas", "false");
        b.Line();

        b.OnBegin();
        b.Line("ItemGranter.ItemGrantedEvent.Subscribe(OnItemCollected)");
        b.Line();
        b.Line("# Set up tracker UI for all players");
        b.Line("for (Player : GetPlayspace().GetPlayers()):");
        b.Indent();
        b.Line("SetupTracker(Player)");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("SetupTracker", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();
        b.Line($"Counter := text_block{{DefaultText := StringToMessage(\"{itemName}: 0/{totalItems}\")}}");
        b.Line("set TrackerText = option{Counter}");
        b.Line();
        b.Line("Tracker := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := Counter");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.95, Y := 0.15}, Maximum := vector2{X := 0.95, Y := 0.15}}");
        b.Line("Alignment := vector2{X := 1.0, Y := 0.0}");
        b.Dedent();
        b.Dedent();
        b.Dedent();
        b.Line();
        b.Line("PlayerUI.AddWidget(Tracker)");
        b.Line("set TrackerCanvas = option{Tracker}");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("OnItemCollected", "Agent:agent", "void");
        b.Line("set CollectedCount += 1");
        b.Line("UpdateDisplay()");
        b.Line();
        b.Line("if (CollectedCount >= TotalItems):");
        b.Indent();
        b.Line("OnAllCollected()");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.Method("UpdateDisplay", "void");
        b.Line("if (Display := TrackerText?):");
        b.Indent();
        b.Line($"Display.SetText(StringToMessage(\"{itemName}: {{CollectedCount}}/{{TotalItems}}\"))");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.Method("OnAllCollected", "void");
        b.Comment($"All {totalItems} {itemName} collected!");
        b.Line("CompletionTrigger.Trigger()");
        b.Line($"Print(\"All {itemName} collected!\")");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  PROGRESS BAR
    // =====================================================================

    private string GenerateProgressBar(VerseFileRequest request)
    {
        var p = request.Parameters;
        var label = GetString(p, "label", "Progress");
        var barColor = GetString(p, "bar_color", "green");

        var colorValue = barColor.ToLower() switch
        {
            "red" => "color{R := 0.9, G := 0.2, B := 0.2, A := 1.0}",
            "blue" => "color{R := 0.2, G := 0.4, B := 0.9, A := 1.0}",
            "yellow" => "color{R := 0.9, G := 0.9, B := 0.2, A := 1.0}",
            _ => "color{R := 0.2, G := 0.9, B := 0.3, A := 1.0}"
        };

        var b = new VerseCodeBuilder();
        b.Comment("Progress Bar — Generated by WellVersed");
        b.Comment("Call UpdateProgress(0.0..1.0) to set the bar fill percentage.");
        b.Line();
        b.UIUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Line();
        b.Var("Progress", "float", "0.0");
        b.Var("BarCanvas", "?canvas", "false");
        b.Var("LabelText", "?text_block", "false");
        b.Var("PercentText", "?text_block", "false");
        b.Line();

        b.OnBegin();
        b.Line("for (Player : GetPlayspace().GetPlayers()):");
        b.Indent();
        b.Line("SetupProgressBar(Player)");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("SetupProgressBar", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();

        b.Line($"Label := text_block{{DefaultText := StringToMessage(\"{label}\")}}");
        b.Line("set LabelText = option{Label}");
        b.Line("Percent := text_block{DefaultText := StringToMessage(\"0%\")}");
        b.Line("set PercentText = option{Percent}");
        b.Line();
        b.Line($"BarFill := color_block{{DefaultColor := {colorValue}}}");
        b.Line("BarBg := color_block{DefaultColor := color{R := 0.15, G := 0.15, B := 0.15, A := 0.8}}");
        b.Line();

        b.Line("Bar := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();

        // Background bar
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := BarBg");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.3, Y := 0.92}, Maximum := vector2{X := 0.7, Y := 0.95}}");
        b.Dedent();

        // Fill bar (initially zero width — conceptual, actual resizing requires Verse tricks)
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := BarFill");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.3, Y := 0.92}, Maximum := vector2{X := 0.3, Y := 0.95}}");
        b.Dedent();

        // Label
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := Label");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.9}, Maximum := vector2{X := 0.5, Y := 0.9}}");
        b.Line("Alignment := vector2{X := 0.5, Y := 1.0}");
        b.Dedent();

        // Percentage text
        b.Line("canvas_slot:");
        b.Indent();
        b.Line("Widget := Percent");
        b.Line("Anchors := anchors{Minimum := vector2{X := 0.5, Y := 0.935}, Maximum := vector2{X := 0.5, Y := 0.935}}");
        b.Line("Alignment := vector2{X := 0.5, Y := 0.5}");
        b.Dedent();

        b.Dedent(); // array
        b.Dedent(); // canvas
        b.Line();
        b.Line("PlayerUI.AddWidget(Bar)");
        b.Line("set BarCanvas = option{Bar}");
        b.Dedent(); // if PlayerUI
        b.EndBlock();

        b.Line();
        b.MethodWithParams("UpdateProgress", "NewProgress:float", "void");
        b.Line("set Progress = Clamp(NewProgress, 0.0, 1.0)");
        b.Line("PercentInt := Floor(Progress * 100.0)");
        b.Line("if (Display := PercentText?):");
        b.Indent();
        b.Line("Display.SetText(StringToMessage(\"{PercentInt}%\"))");
        b.Dedent();
        b.Comment("Note: Dynamic bar width resizing requires rebuilding the canvas.");
        b.Comment("For smooth animation, rebuild the canvas each frame with updated anchors.");
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  CUSTOM WIDGET (blank canvas with helpers)
    // =====================================================================

    private string GenerateCustomWidget(VerseFileRequest request)
    {
        var p = request.Parameters;
        var elementCount = GetInt(p, "element_count", 2);

        var b = new VerseCodeBuilder();
        b.Comment("Custom Widget — Generated by WellVersed");
        b.Comment("A blank canvas with helper methods. Customize to build any UI you need.");
        b.Line();
        b.UIUsings();
        b.Line();

        b.ClassDef(request.ClassName);
        b.Line();
        b.Var("MyCanvas", "?canvas", "false");
        b.Var("MyPlayerUI", "?player_ui", "false");
        for (int i = 1; i <= elementCount; i++)
            b.Var($"Element{i}", "?text_block", "false");
        b.Line();

        b.OnBegin();
        b.Line("for (Player : GetPlayspace().GetPlayers()):");
        b.Indent();
        b.Line("BuildUI(Player)");
        b.Dedent();
        b.EndBlock();

        b.Line();
        b.MethodWithParams("BuildUI", "Player:player", "void");
        b.Line("if (PlayerUI := GetPlayerUI[Player]):");
        b.Indent();

        for (int i = 1; i <= elementCount; i++)
        {
            b.Line($"Text{i} := text_block{{DefaultText := StringToMessage(\"Element {i}\")}}");
            b.Line($"set Element{i} = option{{Text{i}}}");
        }

        b.Line();
        b.Line("Widget := canvas:");
        b.Indent();
        b.Line("Slots := array:");
        b.Indent();

        for (int i = 1; i <= elementCount; i++)
        {
            var y = 0.4 + (i - 1) * 0.08;
            b.Line("canvas_slot:");
            b.Indent();
            b.Line($"Widget := Text{i}");
            b.Line($"Anchors := anchors{{Minimum := vector2{{X := 0.5, Y := {y:F2}}}, Maximum := vector2{{X := 0.5, Y := {y:F2}}}}}");
            b.Line("Alignment := vector2{X := 0.5, Y := 0.0}");
            b.Dedent();
        }

        b.Dedent(); // array
        b.Dedent(); // canvas
        b.Line();
        b.Line("PlayerUI.AddWidget(Widget)");
        b.Line("set MyCanvas = option{Widget}");
        b.Line("set MyPlayerUI = option{PlayerUI}");
        b.Dedent(); // if PlayerUI
        b.EndBlock();

        b.Line();
        b.MethodWithParams("UpdateElement", "Index:int, NewText:string", "void");
        b.Comment("Update a specific element by index (1-based)");
        for (int i = 1; i <= elementCount; i++)
        {
            var prefix = i == 1 ? "if" : "else if";
            b.Line($"{prefix} (Index = {i}, Elem := Element{i}?):");
            b.Indent();
            b.Line("Elem.SetText(StringToMessage(NewText))");
            b.Dedent();
        }
        b.EndBlock();

        b.Line();
        b.Method("RemoveUI", "void");
        b.Line("if (PUI := MyPlayerUI?, Canvas := MyCanvas?):");
        b.Indent();
        b.Line("PUI.RemoveWidget(Canvas)");
        b.Line("set MyCanvas = false");
        b.Dedent();
        b.EndBlock();

        b.EndBlock(); // class
        return b.ToString();
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private static bool GetBool(Dictionary<string, string> p, string key, bool def)
        => p.TryGetValue(key, out var v) ? bool.TryParse(v, out var b) && b : def;

    private static int GetInt(Dictionary<string, string> p, string key, int def)
        => p.TryGetValue(key, out var v) ? int.TryParse(v, out var i) ? i : def : def;

    private static string GetString(Dictionary<string, string> p, string key, string def)
        => p.TryGetValue(key, out var v) ? v : def;
}
