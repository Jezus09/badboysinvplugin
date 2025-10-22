/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json;
using System.Net.Http;
using CounterStrikeSharp.API.Modules.Entities;

namespace InventorySimulator;

public partial class InventorySimulator
{
    // Drop system state
    private static readonly Dictionary<nint, DropCrate> ActiveDropCrates = new();
    private static readonly Random DropRandom = new();
    private static readonly HttpClient DropHttpClient = new();

    // Drop crate data class
    private class DropCrate
    {
        public nint EntityHandle { get; set; }
        public Vector DeathPosition { get; set; }
        public ulong KillerSteamId { get; set; }
        public DateTime SpawnTime { get; set; }
    }

    /// <summary>
    /// Attempts to spawn a drop crate at the death location
    /// </summary>
    public void TrySpawnDropCrate(CCSPlayerController victim, CCSPlayerController? attacker)
    {
        if (!invsim_drop_enabled.Value)
            return;

        if (victim.PlayerPawn?.Value == null)
            return;

        // Random chance check
        var random = DropRandom.NextDouble() * 100.0;
        if (random > invsim_drop_chance.Value)
            return;

        var deathPos = victim.PlayerPawn.Value.AbsOrigin;
        if (deathPos == null)
            return;

        Server.NextFrame(() =>
        {
            try
            {
                // Create prop_physics entity
                var crate = CreateEntityByName<CPhysicsPropMultiplayer>("prop_physics_multiplayer");
                if (crate == null)
                {
                    if (invsim_debug.Value)
                        Server.PrintToConsole("[DropSystem] Failed to create prop_physics_multiplayer entity");
                    return;
                }

                // Set model
                crate.SetModel(invsim_drop_model.Value);

                // Set position (slightly above ground to prevent clipping)
                var spawnPos = new Vector(deathPos.X, deathPos.Y, deathPos.Z + 10.0f);
                crate.Teleport(spawnPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));

                // Spawn and activate
                crate.DispatchSpawn();

                // Add glow effect
                if (invsim_drop_glow.Value)
                {
                    crate.Glow.GlowColorOverride = Color.FromArgb(255, 255, 215, 0); // Gold color
                    crate.Glow.GlowRange = 1000;
                    crate.Glow.GlowRangeMin = 0;
                    crate.Glow.GlowType = 3;
                    crate.Glow.GlowTeam = -1;
                    SetStateChanged(crate, "CBaseModelEntity", "m_Glow");
                }

                // Store crate info
                var crateData = new DropCrate
                {
                    EntityHandle = crate.Handle,
                    DeathPosition = spawnPos,
                    KillerSteamId = attacker?.SteamID ?? 0,
                    SpawnTime = DateTime.UtcNow
                };

                ActiveDropCrates[crate.Handle] = crateData;

                // Announce drop
                if (invsim_drop_announce.Value)
                {
                    Server.PrintToChatAll(" \x06[Drop]\x01 A crate has dropped! Press \x04E\x01 to collect it!");
                }

                if (invsim_debug.Value)
                    Server.PrintToConsole($"[DropSystem] Spawned crate at {spawnPos.X}, {spawnPos.Y}, {spawnPos.Z}");

                // Auto-remove after timeout
                AddTimer(invsim_drop_timeout.Value, () =>
                {
                    if (crate.IsValid)
                    {
                        ActiveDropCrates.Remove(crate.Handle);
                        crate.Remove();
                        if (invsim_debug.Value)
                            Server.PrintToConsole($"[DropSystem] Crate timed out and removed");
                    }
                });
            }
            catch (Exception ex)
            {
                Server.PrintToConsole($"[DropSystem] Error spawning crate: {ex.Message}");
                if (invsim_debug.Value)
                    Server.PrintToConsole($"[DropSystem] Stack trace: {ex.StackTrace}");
            }
        });
    }

    /// <summary>
    /// Handle player USE button press on crate
    /// </summary>
    public void OnPlayerUseCrate(CCSPlayerController player, CPhysicsPropMultiplayer crate)
    {
        if (!ActiveDropCrates.TryGetValue(crate.Handle, out var crateData))
            return;

        if (!IsPlayerHumanAndValid(player))
            return;

        // Remove crate from tracking
        ActiveDropCrates.Remove(crate.Handle);

        // Visual feedback
        player.PrintToChat(" \x06[Drop]\x01 Opening crate...");

        // Remove the crate entity
        Server.NextFrame(() =>
        {
            if (crate.IsValid)
                crate.Remove();
        });

        // Call API to notify website
        Task.Run(async () =>
        {
            try
            {
                await NotifyWebsiteDropCollected(player.SteamID, crateData.KillerSteamId);
            }
            catch (Exception ex)
            {
                Server.NextFrame(() =>
                {
                    if (invsim_debug.Value)
                        Server.PrintToConsole($"[DropSystem] API Error: {ex.Message}");
                });
            }
        });

        if (invsim_debug.Value)
            Server.PrintToConsole($"[DropSystem] Player {player.PlayerName} (SteamID: {player.SteamID}) collected crate");
    }

    /// <summary>
    /// Notify website that a drop was collected
    /// </summary>
    private async Task NotifyWebsiteDropCollected(ulong collectorSteamId, ulong killerSteamId)
    {
        try
        {
            var apiUrl = $"{invsim_protocol.Value}://{invsim_hostname.Value}/api/plugin/drop-collected";

            var payload = new
            {
                collectorSteamId = collectorSteamId.ToString(),
                killerSteamId = killerSteamId.ToString(),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

            var response = await DropHttpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();

                Server.NextFrame(() =>
                {
                    var collector = GetPlayerFromSteamId(collectorSteamId);
                    if (collector != null && IsPlayerHumanAndValid(collector))
                    {
                        collector.PrintToChat(" \x06[Drop]\x01 \x04You received a reward!\x01 Check the website!");
                    }

                    if (invsim_debug.Value)
                    {
                        Server.PrintToConsole($"[DropSystem] API Response: {responseText}");
                    }
                });
            }
            else
            {
                Server.NextFrame(() =>
                {
                    if (invsim_debug.Value)
                        Server.PrintToConsole($"[DropSystem] API Error: {response.StatusCode}");
                });
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
            {
                if (invsim_debug.Value)
                    Server.PrintToConsole($"[DropSystem] Exception in NotifyWebsiteDropCollected: {ex.Message}");
            });
            throw;
        }
    }

    /// <summary>
    /// Check if player is looking at and close to a crate
    /// </summary>
    public CPhysicsPropMultiplayer? GetCratePlayerIsLookingAt(CCSPlayerController player)
    {
        if (player.PlayerPawn?.Value == null)
            return null;

        var playerPos = player.PlayerPawn.Value.AbsOrigin;
        if (playerPos == null)
            return null;

        var eyePos = new Vector(
            playerPos.X,
            playerPos.Y,
            playerPos.Z + 64.0f // Eye height
        );

        // Find nearby crates
        foreach (var crateHandle in ActiveDropCrates.Keys.ToList())
        {
            try
            {
                var crate = new CPhysicsPropMultiplayer(crateHandle);
                if (crate == null || !crate.IsValid)
                {
                    ActiveDropCrates.Remove(crateHandle);
                    continue;
                }

                var cratePos = crate.AbsOrigin;
                if (cratePos == null)
                    continue;

                // Calculate distance
                var distance = Math.Sqrt(
                    Math.Pow(playerPos.X - cratePos.X, 2) +
                    Math.Pow(playerPos.Y - cratePos.Y, 2) +
                    Math.Pow(playerPos.Z - cratePos.Z, 2)
                );

                // Check if within use range
                if (distance <= invsim_drop_use_range.Value)
                {
                    return crate;
                }
            }
            catch
            {
                ActiveDropCrates.Remove(crateHandle);
            }
        }

        return null;
    }

    /// <summary>
    /// Cleanup all active drop crates (called on map change/unload)
    /// </summary>
    public void CleanupAllDropCrates()
    {
        foreach (var crateHandle in ActiveDropCrates.Keys.ToList())
        {
            try
            {
                var crate = new CPhysicsPropMultiplayer(crateHandle);
                if (crate != null && crate.IsValid)
                {
                    crate.Remove();
                }
            }
            catch { }
        }
        ActiveDropCrates.Clear();

        if (invsim_debug.Value)
            Server.PrintToConsole("[DropSystem] All crates cleaned up");
    }
}
