using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for SmartTemplateService — verifies template listing, content quality,
/// unique IDs, Verse code correctness, device counts, wiring, categories, and
/// generation plan output.
/// </summary>
public class SmartTemplateTests
{
    private readonly SmartTemplateService _service = new(NullLogger<SmartTemplateService>.Instance);

    // =====================================================================
    //  ListTemplates — counts and completeness
    // =====================================================================

    [Fact]
    public void ListTemplates_ReturnsExpectedCount()
    {
        var templates = _service.ListTemplates();

        // The service defines 19 templates in _builders
        Assert.True(templates.Count >= 19,
            $"Expected at least 19 templates, got {templates.Count}");
    }

    [Fact]
    public void ListTemplates_AllHaveUniqueIds()
    {
        var templates = _service.ListTemplates();
        var ids = templates.Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ListTemplates_AllHaveNonEmptyFields()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            Assert.False(string.IsNullOrEmpty(summary.Id), "Template missing Id");
            Assert.False(string.IsNullOrEmpty(summary.Name), $"Template {summary.Id} missing Name");
            Assert.False(string.IsNullOrEmpty(summary.Category), $"Template {summary.Id} missing Category");
            Assert.False(string.IsNullOrEmpty(summary.Description), $"Template {summary.Id} missing Description");
            Assert.False(string.IsNullOrEmpty(summary.Difficulty), $"Template {summary.Id} missing Difficulty");
        }
    }

    // =====================================================================
    //  GetTemplate — by ID
    // =====================================================================

    [Fact]
    public void GetTemplate_ReturnsTemplate_ForAllKnownIds()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);
            Assert.Equal(summary.Id, template!.Id);
        }
    }

    [Fact]
    public void GetTemplate_ReturnsNull_ForUnknownId()
    {
        var template = _service.GetTemplate("totally_nonexistent_template_xyz");
        Assert.Null(template);
    }

    // =====================================================================
    //  Template content quality — Verse code
    // =====================================================================

    [Fact]
    public void AllTemplates_HaveNonEmptyVerseCode()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);
            Assert.False(string.IsNullOrEmpty(template!.VerseCode),
                $"Template '{summary.Id}' has empty VerseCode");
        }
    }

    [Fact]
    public void AllTemplates_VerseCode_ContainsCreativeDeviceClass()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);

            Assert.Contains("creative_device", template!.VerseCode,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AllTemplates_VerseCode_ContainsOnBegin()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);

            Assert.Contains("OnBegin", template!.VerseCode);
        }
    }

    // =====================================================================
    //  Template content quality — Devices
    // =====================================================================

    [Fact]
    public void AllTemplates_HaveAtLeast2Devices()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);
            Assert.True(template!.Devices.Count >= 2,
                $"Template '{summary.Id}' has only {template.Devices.Count} devices (need >= 2)");
        }
    }

    [Fact]
    public void AllTemplates_DevicesHaveRoleAndClass()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);

            foreach (var device in template!.Devices)
            {
                Assert.False(string.IsNullOrEmpty(device.Role),
                    $"Template '{summary.Id}' has device with empty Role");
                Assert.False(string.IsNullOrEmpty(device.DeviceClass),
                    $"Template '{summary.Id}' has device with empty DeviceClass");
            }
        }
    }

    // =====================================================================
    //  Template content quality — Wiring
    // =====================================================================

    [Fact]
    public void MostTemplates_HaveAtLeast1WiringConnection()
    {
        var templates = _service.ListTemplates();
        var templatesWithWiring = 0;

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);
            if (template!.Wiring.Count >= 1)
                templatesWithWiring++;
        }

        // Most templates should have wiring — some (like voting_system) handle
        // everything through Verse code and may have no device-level wiring.
        var ratio = (double)templatesWithWiring / templates.Count;
        Assert.True(ratio >= 0.85,
            $"Expected >= 85% of templates to have wiring, got {ratio:P0} ({templatesWithWiring}/{templates.Count})");
    }

    [Fact]
    public void AllTemplates_WiringHasRequiredFields()
    {
        var templates = _service.ListTemplates();

        foreach (var summary in templates)
        {
            var template = _service.GetTemplate(summary.Id);
            Assert.NotNull(template);

            foreach (var wire in template!.Wiring)
            {
                Assert.False(string.IsNullOrEmpty(wire.SourceRole),
                    $"Template '{summary.Id}' wiring missing SourceRole");
                Assert.False(string.IsNullOrEmpty(wire.OutputEvent),
                    $"Template '{summary.Id}' wiring missing OutputEvent");
                Assert.False(string.IsNullOrEmpty(wire.TargetRole),
                    $"Template '{summary.Id}' wiring missing TargetRole");
                Assert.False(string.IsNullOrEmpty(wire.InputAction),
                    $"Template '{summary.Id}' wiring missing InputAction");
            }
        }
    }

    // =====================================================================
    //  Categories — coverage
    // =====================================================================

    [Fact]
    public void Templates_CoverAllExpectedCategories()
    {
        var templates = _service.ListTemplates();
        var categories = templates.Select(t => t.Category.ToLowerInvariant()).Distinct().ToHashSet();

        Assert.Contains("combat", categories);
        Assert.Contains("economy", categories);
        Assert.Contains("objective", categories);
        Assert.Contains("progression", categories);
        Assert.Contains("meta", categories);
        Assert.Contains("utility", categories);
    }

    [Fact]
    public void ListTemplates_FilterByCategory_Works()
    {
        var combatTemplates = _service.ListTemplates("combat");

        Assert.NotEmpty(combatTemplates);
        Assert.All(combatTemplates, t =>
            Assert.Equal("combat", t.Category, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListTemplates_FilterByNonexistentCategory_ReturnsEmpty()
    {
        var templates = _service.ListTemplates("nonexistent_category");
        Assert.Empty(templates);
    }

    // =====================================================================
    //  GenerateFromTemplate — produces valid GenerationPlan
    // =====================================================================

    [Fact]
    public void GenerateFromTemplate_ProducesValidPlan()
    {
        var plan = _service.GenerateFromTemplate("class_selector");

        Assert.NotNull(plan);
        Assert.False(string.IsNullOrEmpty(plan.SystemName));
        Assert.False(string.IsNullOrEmpty(plan.Description));
        Assert.NotEmpty(plan.Devices);
        Assert.NotEmpty(plan.Wiring);
        Assert.False(string.IsNullOrEmpty(plan.VerseCode));
        Assert.NotEmpty(plan.Steps);
    }

    [Fact]
    public void GenerateFromTemplate_UnknownTemplate_ReturnsErrorPlan()
    {
        var plan = _service.GenerateFromTemplate("nonexistent_template");

        Assert.NotNull(plan);
        Assert.Equal("Error", plan.SystemName);
        Assert.Contains("not found", plan.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GenerateFromTemplate_AcceptsCustomParameters()
    {
        var plan = _service.GenerateFromTemplate("class_selector", new Dictionary<string, string>
        {
            ["class_count"] = "2",
            ["selection_time"] = "10"
        });

        Assert.NotNull(plan);
        Assert.NotEmpty(plan.Devices);
    }

    [Fact]
    public void GenerateFromTemplate_StepsIncludeCloneAndWire()
    {
        var plan = _service.GenerateFromTemplate("gun_game");

        Assert.NotNull(plan);

        var hasCloneStep = plan.Steps.Any(s => s.Tool.Contains("clone_actor"));
        var hasWireStep = plan.Steps.Any(s => s.Tool.Contains("wire"));

        Assert.True(hasCloneStep, "Plan should include clone_actor steps for placing devices");
        Assert.True(hasWireStep, "Plan should include wire steps for connecting devices");
    }

    // =====================================================================
    //  Specific template checks
    // =====================================================================

    [Theory]
    [InlineData("class_selector")]
    [InlineData("gun_game")]
    [InlineData("last_man_standing")]
    [InlineData("team_elimination")]
    [InlineData("currency_system")]
    [InlineData("upgrade_station")]
    [InlineData("trading_post")]
    [InlineData("king_of_the_hill")]
    [InlineData("domination")]
    [InlineData("search_and_destroy")]
    [InlineData("capture_the_flag")]
    [InlineData("payload")]
    [InlineData("wave_defense")]
    [InlineData("parkour")]
    [InlineData("deathrun")]
    [InlineData("voting_system")]
    [InlineData("matchmaking")]
    [InlineData("achievement_system")]
    [InlineData("spectator_mode")]
    public void GetTemplate_KnownId_ReturnsNonNull(string templateId)
    {
        var template = _service.GetTemplate(templateId);
        Assert.NotNull(template);
        Assert.Equal(templateId, template!.Id);
    }

    // =====================================================================
    //  SmartTemplate model
    // =====================================================================

    [Fact]
    public void SmartTemplate_DefaultValues()
    {
        var template = new SmartTemplate();
        Assert.Equal("", template.Id);
        Assert.Equal("", template.Name);
        Assert.Equal("", template.Category);
        Assert.Equal("", template.Description);
        Assert.Empty(template.Parameters);
        Assert.Empty(template.Devices);
        Assert.Empty(template.Wiring);
        Assert.Equal("", template.VerseCode);
        Assert.Null(template.WidgetSpecJson);
        Assert.Empty(template.Tags);
        Assert.Equal(0, template.EstimatedDeviceCount);
        Assert.Equal("intermediate", template.Difficulty);
    }

    [Fact]
    public void TemplateParameter_DefaultValues()
    {
        var param = new TemplateParameter();
        Assert.Equal("", param.Name);
        Assert.Equal("", param.Description);
        Assert.Equal("string", param.Type);
        Assert.Equal("", param.DefaultValue);
        Assert.False(param.Required);
    }

    [Fact]
    public void SmartTemplateSummary_DefaultValues()
    {
        var summary = new SmartTemplateSummary();
        Assert.Equal("", summary.Id);
        Assert.Equal("", summary.Name);
        Assert.Equal("", summary.Category);
        Assert.Equal("", summary.Description);
        Assert.Empty(summary.Tags);
        Assert.Equal(0, summary.EstimatedDeviceCount);
        Assert.Equal("", summary.Difficulty);
        Assert.Equal(0, summary.ParameterCount);
    }
}
