using System.Text.Json;
using WellVersed.Core.Models;
using WellVersed.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WellVersed.Tests;

/// <summary>
/// Tests for the UefnBridge HTTP client — connection lifecycle, command dispatch,
/// timeout handling, response model defaults, and convenience method serialization.
///
/// These tests do NOT require a running bridge; they verify behavior when no bridge
/// is available (the expected state in CI), plus model correctness.
/// </summary>
public class UefnBridgeTests
{
    // =====================================================================
    //  BridgeResponse model defaults
    // =====================================================================

    [Fact]
    public void BridgeResponse_DefaultStatus_IsEmptyString()
    {
        var response = new BridgeResponse();
        Assert.Equal("", response.Status);
        Assert.Null(response.Data);
        Assert.Null(response.Error);
    }

    [Fact]
    public void BridgeResponse_IsOk_TrueOnlyWhenStatusIsOk()
    {
        var ok = new BridgeResponse { Status = "ok" };
        var error = new BridgeResponse { Status = "error" };
        var empty = new BridgeResponse();

        Assert.True(ok.IsOk);
        Assert.False(error.IsOk);
        Assert.False(empty.IsOk);
    }

    [Fact]
    public void BridgeResponse_IsOk_CaseSensitive()
    {
        var upper = new BridgeResponse { Status = "OK" };
        var mixed = new BridgeResponse { Status = "Ok" };

        // "ok" is lowercase in the bridge protocol
        Assert.False(upper.IsOk);
        Assert.False(mixed.IsOk);
    }

    [Fact]
    public void BridgeResponse_DataProperty_HoldsJsonElement()
    {
        var json = JsonSerializer.Deserialize<JsonElement>("{\"key\": \"value\"}");
        var response = new BridgeResponse { Status = "ok", Data = json };

        Assert.True(response.Data.HasValue);
        Assert.Equal("value", response.Data.Value.GetProperty("key").GetString());
    }

    // =====================================================================
    //  IsConnected — returns false when no bridge running
    // =====================================================================

    [Fact]
    public async Task IsConnected_ReturnsFalse_WhenNeverConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var result = await bridge.IsConnected();
        Assert.False(result);
    }

    [Fact]
    public async Task IsConnected_ReturnsFalse_AfterDisconnect()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        bridge.Disconnect();
        var result = await bridge.IsConnected();
        Assert.False(result);
    }

    // =====================================================================
    //  Connect — fails gracefully when no bridge is running
    // =====================================================================

    [Fact]
    public async Task Connect_ReturnsError_WhenNoBridgeRunning()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        // Connect to a port where nothing is listening
        var response = await bridge.Connect(19999);

        Assert.False(response.IsOk);
        Assert.Equal("error", response.Status);
        Assert.NotNull(response.Error);
        Assert.Contains("Could not connect", response.Error);
    }

    [Fact]
    public async Task Connect_ScansPortRange()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        // This should try ports 9220, 9221, 9222 and fail on all
        var response = await bridge.Connect();

        Assert.False(response.IsOk);
        Assert.NotNull(response.Error);
        // The error message should mention the ports it tried
        Assert.Contains("9220", response.Error);
    }

    // =====================================================================
    //  SendCommand — not connected
    // =====================================================================

    [Fact]
    public async Task SendCommand_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.SendCommand("test_command");

        Assert.False(response.IsOk);
        Assert.Equal("error", response.Status);
        Assert.Contains("Not connected", response.Error);
    }

    [Fact]
    public async Task SendCommand_ReturnsError_WithParams_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.SendCommand("spawn_actor", new { classPath = "test", x = 0 });

        Assert.False(response.IsOk);
        Assert.Contains("Not connected", response.Error);
    }

    // =====================================================================
    //  Convenience methods — verify they delegate to SendCommand
    //  (they all return error because bridge is not connected)
    // =====================================================================

    [Fact]
    public async Task SpawnActor_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var location = new Vector3Info(100, 200, 300);

        var response = await bridge.SpawnActor("BP_TestDevice_C", location);

        Assert.False(response.IsOk);
        Assert.Contains("Not connected", response.Error);
    }

    [Fact]
    public async Task DeleteActors_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.DeleteActors(new List<string> { "Actor1", "Actor2" });

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task SetActorProperties_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var props = new Dictionary<string, string> { ["Health"] = "100" };

        var response = await bridge.SetActorProperties("TestActor", props);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task GetActorProperties_ReturnsEmptyDict_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var props = await bridge.GetActorProperties("TestActor");

        Assert.NotNull(props);
        Assert.Empty(props);
    }

    [Fact]
    public async Task GetAllActors_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.GetAllActors();

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task ExportWorldState_ReturnsErrorString_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var result = await bridge.ExportWorldState();

        Assert.Contains("Not connected", result);
    }

    [Fact]
    public async Task DeviceCatalogScan_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.DeviceCatalogScan();

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task StampSave_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.StampSave("test_stamp");

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task StampPlace_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var loc = new Vector3Info(0, 0, 0);

        var response = await bridge.StampPlace("test_stamp", loc, 90f, 2f);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task CreateFloor_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var loc = new Vector3Info(0, 0, 0);

        var response = await bridge.CreateFloor(1000, 1000, loc);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task CreateWall_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var loc = new Vector3Info(0, 0, 0);

        var response = await bridge.CreateWall(500, 300, loc, 45f);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task CreateBox_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var loc = new Vector3Info(0, 0, 0);

        var response = await bridge.CreateBox(200, 300, 400, loc);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task CreateArena_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.CreateArena("medium", 2);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task WriteVerseFile_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.WriteVerseFile("test.verse", "using { /Verse.org }");

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task TriggerBuild_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.TriggerBuild();

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task GetBuildErrors_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.GetBuildErrors();

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task RunPublishAudit_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.RunPublishAudit();

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task ExecuteGenerationPlan_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var plan = new GenerationPlan
        {
            SystemName = "test_system",
            Description = "A test plan"
        };

        var response = await bridge.ExecuteGenerationPlan(plan);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task GetViewportCamera_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var response = await bridge.GetViewportCamera();

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task SetViewportCamera_ReturnsError_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        var loc = new Vector3Info(100, 200, 300);
        var rot = new Vector3Info(0, 45, 0);

        var response = await bridge.SetViewportCamera(loc, rot);

        Assert.False(response.IsOk);
    }

    [Fact]
    public async Task TakeScreenshot_ReturnsErrorString_WhenNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);

        var result = await bridge.TakeScreenshot("/tmp/screenshot.png");

        Assert.Contains("Not connected", result);
    }

    // =====================================================================
    //  Disconnect resets state
    // =====================================================================

    [Fact]
    public void Disconnect_DoesNotThrow()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        bridge.Disconnect(); // should be safe even when never connected
    }

    [Fact]
    public async Task Disconnect_ThenSendCommand_ReturnsNotConnected()
    {
        var bridge = new UefnBridge(NullLogger<UefnBridge>.Instance);
        bridge.Disconnect();

        var response = await bridge.SendCommand("test");
        Assert.Contains("Not connected", response.Error);
    }
}
