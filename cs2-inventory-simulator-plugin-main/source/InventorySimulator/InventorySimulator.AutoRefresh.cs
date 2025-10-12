/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace InventorySimulator;

public partial class InventorySimulator
{
    private Timer? _autoRefreshTimer;
    private Dictionary<ulong, long> _lastUpdateTimestamps = new();

    private void InitializeAutoRefresh()
    {
        if (_autoRefreshTimer is not null)
        {
            _autoRefreshTimer.Kill();
            _autoRefreshTimer = null;
        }

        if (invsim_autorefresh.Value)
        {
            _autoRefreshTimer = AddTimer(invsim_recheck_interval.Value, () =>
            {
                try
                {
                    foreach (var player in Utilities.GetPlayers().Where(IsPlayerHumanAndValid))
                    {
                        var steamId = player.SteamID;
                        
                        // Ellenőrizzük, hogy van-e már timestamp-ünk
                        if (!_lastUpdateTimestamps.ContainsKey(steamId))
                        {
                            _lastUpdateTimestamps[steamId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            RefreshPlayerInventory(player);
                            continue;
                        }

                        // Ellenőrizzük a weboldalról, hogy van-e frissebb inventory
                        var currentTimestamp = _lastUpdateTimestamps[steamId];
                        CheckWebInventoryUpdate(player, currentTimestamp);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InventorySimulator] Error in auto-refresh timer: {ex.Message}");
                }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);

            Console.WriteLine("[InventorySimulator] Auto-refresh initialized");
        }
        else
        {
            Console.WriteLine("[InventorySimulator] Auto-refresh disabled");
        }
    }

    private async void CheckWebInventoryUpdate(CCSPlayerController player, long lastUpdateTime)
    {
        try
        {
            var url = GetAPIUrl($"/api/inventory-timestamp/{player.SteamID}");
            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            var serverTimestamp = long.Parse(content);

            if (serverTimestamp > lastUpdateTime)
            {
                Console.WriteLine($"[InventorySimulator] Web inventory newer than local for {player.SteamID} ({serverTimestamp} > {lastUpdateTime})");
                _lastUpdateTimestamps[player.SteamID] = serverTimestamp;
                RefreshPlayerInventory(player);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InventorySimulator] Error checking web inventory: {ex.Message}");
        }
    }

    private void OnAutoRefreshSettingChanged(object? sender, bool newValue)
    {
        InitializeAutoRefresh();
    }

    private void OnRecheckIntervalChanged(object? sender, int newValue)
    {
        if (invsim_autorefresh.Value)
        {
            InitializeAutoRefresh();
        }
    }
}