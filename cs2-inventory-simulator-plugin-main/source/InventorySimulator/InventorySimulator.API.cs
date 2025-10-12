/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace InventorySimulator;

public class SignInUserResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }
}

public partial class InventorySimulator
{
    public string GetAPIUrl(string pathname = "")
    {
        // Remove whitespace to avoid URI parse errors
        return $"{invsim_protocol.Value.Trim()}://{invsim_hostname.Value.Trim()}{pathname}";
    }

    public async Task<T?> Fetch<T>(string pathname, bool rethrow = false)
    {
        var url = GetAPIUrl(pathname);
        try
        {
            using HttpClient client = new();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string jsonContent = response.Content.ReadAsStringAsync().Result;
            T? data = JsonSerializer.Deserialize<T>(jsonContent);
            return data;
        }
        catch (Exception error)
        {
            Logger.LogError("GET {Url} failed: {Message}", url, error.Message);
            if (rethrow)
                throw;
            return default;
        }
    }

    public async Task<T?> Send<T>(string pathname, object data)
    {
        var url = GetAPIUrl(pathname);
        try
        {
            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpClient client = new();
            var response = await client.PostAsync(url, content);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logger.LogError("POST {Url} failed, check your invsim_apikey's value.", url);
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("POST {Url} failed with status code: {StatusCode}", url, response.StatusCode);
                return default;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseContent))
            {
                return default;
            }

            return JsonSerializer.Deserialize<T>(responseContent);
        }
        catch (Exception error)
        {
            Logger.LogError("POST {Url} failed: {Message}", url, error.Message);
            return default;
        }
    }

    public async Task FetchPlayerInventory(ulong steamId, bool force = false)
    {
        var existing = PlayerInventoryManager.TryGetValue(steamId, out var i) ? i : null;

        if (!force && existing != null)
            return;

        if (FetchingPlayerInventory.ContainsKey(steamId))
            return;

        FetchingPlayerInventory.TryAdd(steamId, true);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var playerInventory = await Fetch<PlayerInventory>($"/api/equipped/v3/{steamId}.json", true);

                if (playerInventory != null)
                {
                    if (existing != null)
                        playerInventory.CachedWeaponEconItems = existing.CachedWeaponEconItems;
                    PlayerCooldownManager[steamId] = Now();
                    AddPlayerInventory(steamId, playerInventory);
                }

                break;
            }
            catch
            {
                // Try again to fetch data (up to 3 times).
            }
        }

        FetchingPlayerInventory.Remove(steamId, out var _);
    }

    private bool HasInventoryChanged(PlayerInventory? oldInv, PlayerInventory? newInv)
    {
        if (oldInv == null || newInv == null) return true;

        // Debug log for comparison
        Console.WriteLine("[InventorySimulator] Comparing inventories:");
        Console.WriteLine($"Old CTWeapons count: {oldInv.CTWeapons.Count}, New: {newInv.CTWeapons.Count}");
        Console.WriteLine($"Old TWeapons count: {oldInv.TWeapons.Count}, New: {newInv.TWeapons.Count}");
        Console.WriteLine($"Old Knives count: {oldInv.Knives.Count}, New: {newInv.Knives.Count}");
        
        // Individual item comparison for each weapon type
        foreach (var pair in oldInv.CTWeapons)
        {
            if (!newInv.CTWeapons.ContainsKey(pair.Key) || 
                newInv.CTWeapons[pair.Key].Def != pair.Value.Def)
            {
                Console.WriteLine($"[InventorySimulator] CTWeapon changed: {pair.Key}");
                return true;
            }
        }
        foreach (var pair in oldInv.TWeapons)
        {
            if (!newInv.TWeapons.ContainsKey(pair.Key) || 
                newInv.TWeapons[pair.Key].Def != pair.Value.Def)
            {
                Console.WriteLine($"[InventorySimulator] TWeapon changed: {pair.Key}");
                return true;
            }
        }
        foreach (var pair in oldInv.Knives)
        {
            if (!newInv.Knives.ContainsKey(pair.Key) || 
                newInv.Knives[pair.Key].Def != pair.Value.Def)
            {
                Console.WriteLine($"[InventorySimulator] Knife changed: {pair.Key}");
                return true;
            }
        }
        foreach (var pair in oldInv.Gloves)
        {
            if (!newInv.Gloves.ContainsKey(pair.Key) || 
                newInv.Gloves[pair.Key].Def != pair.Value.Def)
            {
                Console.WriteLine($"[InventorySimulator] Glove changed: {pair.Key}");
                return true;
            }
        }
        foreach (var pair in oldInv.Agents)
        {
            if (!newInv.Agents.ContainsKey(pair.Key) || 
                newInv.Agents[pair.Key].Def != pair.Value.Def)
            {
                Console.WriteLine($"[InventorySimulator] Agent changed: {pair.Key}");
                return true;
            }
        }

        // Check MusicKit
        if ((oldInv.MusicKit == null) != (newInv.MusicKit == null))
        {
            Console.WriteLine("[InventorySimulator] MusicKit presence changed");
            return true;
        }
        if (oldInv.MusicKit != null && newInv.MusicKit != null && 
            oldInv.MusicKit.Def != newInv.MusicKit.Def)
        {
            Console.WriteLine("[InventorySimulator] MusicKit definition changed");
            return true;
        }

        // Check Graffiti
        if ((oldInv.Graffiti == null) != (newInv.Graffiti == null))
        {
            Console.WriteLine("[InventorySimulator] Graffiti presence changed");
            return true;
        }
        if (oldInv.Graffiti != null && newInv.Graffiti != null && 
            oldInv.Graffiti.Def != newInv.Graffiti.Def)
        {
            Console.WriteLine("[InventorySimulator] Graffiti definition changed");
            return true;
        }

        Console.WriteLine("[InventorySimulator] No changes detected");
        return false;
    }

    public async void RefreshPlayerInventory(CCSPlayerController player, bool force = false)
    {
        try 
        {
            var isFirstLoad = !PlayerInventoryManager.ContainsKey(player.SteamID);
            
            Console.WriteLine($"[InventorySimulator] Starting refresh for SteamID: {player.SteamID}");
            Console.WriteLine($"[InventorySimulator] IsFirstLoad: {isFirstLoad}, Force: {force}");
            
            var oldInventory = GetPlayerInventory(player);
            await FetchPlayerInventory(player.SteamID, force);
            
            Server.NextFrame(() =>
            {
                if (!player.IsValid) return;

                var newInventory = GetPlayerInventory(player);
                bool hasChanged = HasInventoryChanged(oldInventory, newInventory);
                
                Console.WriteLine($"[InventorySimulator] Refresh completed - HasChanged: {hasChanged}");
                
                // Mindig alkalmazzuk, ha bármi változás van
                if (isFirstLoad || hasChanged || force)
                {
                    GiveOnLoadPlayerInventory(player);
                    GiveOnRefreshPlayerInventory(player, newInventory);
                    
                    if (isFirstLoad)
                    {
                        player.PrintToChat(" \x04[Inventory] \x01Az inventory-d betöltve.");
                    }
                    else if (hasChanged)
                    {
                        player.PrintToChat(" \x04[Inventory] \x01Az inventory-d frissítve lett.");
                    }
                    else if (force)
                    {
                        player.PrintToChat(" \x04[Inventory] \x01Nincs változás az inventory-dban.");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InventorySimulator] Error in RefreshPlayerInventory: {ex}");
        }
    }

    public async Task Send(string pathname, object data)
    {
        await Task.Run(() => Send<object>(pathname, data));
    }

    public async void SendStatTrakIncrement(ulong userId, int targetUid)
    {
        if (invsim_apikey.Value == "")
            return;

        await Send(
            "/api/increment-item-stattrak",
            new
            {
                apiKey = invsim_apikey.Value,
                targetUid,
                userId = userId.ToString(),
            }
        );
    }

    public async void SendSignIn(ulong userId)
    {
        if (AuthenticatingPlayer.ContainsKey(userId))
            return;

        AuthenticatingPlayer.TryAdd(userId, true);
        var response = await Send<SignInUserResponse>("/api/sign-in", new { apiKey = invsim_apikey.Value, userId = userId.ToString() });
        AuthenticatingPlayer.TryRemove(userId, out var _);

        Server.NextFrame(() =>
        {
            var player = Utilities.GetPlayerFromSteamId(userId);
            if (response == null)
            {
                player?.PrintToChat(Localizer["invsim.login_failed"]);
                return;
            }

            player?.PrintToChat(Localizer["invsim.login", $"{GetAPIUrl("/api/sign-in/callback")}?token={response.Token}"]);
        });
    }
}
