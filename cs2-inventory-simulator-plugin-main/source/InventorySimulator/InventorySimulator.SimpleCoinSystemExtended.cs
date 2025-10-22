using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System;
using System.Linq;

namespace InventorySimulator;

// Extended methods for SimpleCoinSystem
public partial class SimpleCoinSystem
{
    // ===== KILL STREAK SYSTEM =====
    public void HandleKillForStreak(CCSPlayerController attacker, CCSPlayerController victim, bool isHeadshot, string weapon)
    {
        if (attacker?.SteamID == null) return;

        var steamId = attacker.SteamID;

        // Increment kill streak
        var streak = _playerKillStreaks.AddOrUpdate(steamId, 1, (key, current) => current + 1);

        // Update activity
        _lastActivity[steamId] = DateTime.UtcNow;

        // Track stats
        var stats = _playerStats.GetOrAdd(steamId, new PlayerStats());
        stats.TotalKills++;
        if (isHeadshot) stats.TotalHeadshots++;

        // Check for special kill bonuses
        decimal bonus = 0;
        string bonusMessage = "";

        // Knife kill
        if (weapon.Contains("knife") || weapon.Contains("bayonet"))
        {
            bonus += 0.5m;
            bonusMessage = "🔪 Knife Kill";
            stats.KnifeKills++;
        }

        // Check for kill streak bonuses
        if (streak == 3)
        {
            bonus += 0.1m;
            Server.PrintToChatAll($" \x10🔥 {attacker.PlayerName}\x01 has \x0E3 KILL STREAK\x01! +€0.10 bonus");
        }
        else if (streak == 5)
        {
            bonus += 0.3m;
            Server.PrintToChatAll($" \x10🔥🔥 {attacker.PlayerName}\x01 has \x0E5 KILL STREAK\x01! +€0.30 bonus");
        }
        else if (streak == 10)
        {
            bonus += 0.5m;
            Server.PrintToChatAll($" \x10🔥🔥🔥 {attacker.PlayerName}\x01 has \x0E10 KILL STREAK\x01! +€0.50 bonus");
        }

        // Apply bonus
        if (bonus > 0)
        {
            var newTotal = _playerCoins.AddOrUpdate(steamId, bonus, (key, current) => current + bonus);
            if (!string.IsNullOrEmpty(bonusMessage))
            {
                attacker.PrintToChat($" \x04[€]\x01 +€{bonus:F2} {bonusMessage}");
            }
            SavePlayerData(steamId, newTotal);
        }

        // Reset victim's streak
        if (victim?.SteamID != null)
        {
            _playerKillStreaks[victim.SteamID] = 0;
        }
    }

    // ===== OBJECTIVE REWARDS =====
    public void AddBombPlantReward(CCSPlayerController player)
    {
        if (player?.SteamID == null) return;

        decimal reward = 0.2m;
        var steamId = player.SteamID;
        var newTotal = _playerCoins.AddOrUpdate(steamId, reward, (key, current) => current + reward);

        player.PrintToChat($" \x04[€]\x01 +€{reward:F2} bomb plant");
        SavePlayerData(steamId, newTotal);

        var stats = _playerStats.GetOrAdd(steamId, new PlayerStats());
        stats.BombPlants++;
    }

    public void AddBombDefuseReward(CCSPlayerController player)
    {
        if (player?.SteamID == null) return;

        decimal reward = 0.3m;
        var steamId = player.SteamID;
        var newTotal = _playerCoins.AddOrUpdate(steamId, reward, (key, current) => current + reward);

        player.PrintToChat($" \x04[€]\x01 +€{reward:F2} bomb defuse");
        SavePlayerData(steamId, newTotal);

        var stats = _playerStats.GetOrAdd(steamId, new PlayerStats());
        stats.BombDefuses++;
    }

    public void AddAssistReward(CCSPlayerController player)
    {
        if (player?.SteamID == null) return;

        decimal reward = 0.05m;
        var steamId = player.SteamID;
        var newTotal = _playerCoins.AddOrUpdate(steamId, reward, (key, current) => current + reward);

        player.PrintToChat($" \x04[€]\x01 +€{reward:F2} assist");
        SavePlayerData(steamId, newTotal);

        var stats = _playerStats.GetOrAdd(steamId, new PlayerStats());
        stats.Assists++;
    }

    // ===== DAILY LOGIN REWARD =====
    public void CheckDailyLoginReward(CCSPlayerController player)
    {
        if (player?.SteamID == null) return;

        var steamId = player.SteamID;
        var now = DateTime.UtcNow;

        if (_lastDailyReward.TryGetValue(steamId, out var lastReward))
        {
            // Check if it's a new day
            if ((now - lastReward).TotalHours < 24)
            {
                return; // Already claimed today
            }
        }

        // Give daily reward
        decimal reward = 1.0m;
        var newTotal = _playerCoins.AddOrUpdate(steamId, reward, (key, current) => current + reward);
        _lastDailyReward[steamId] = now;

        player.PrintToChat($" \x10[Daily Reward]\x01 +€{reward:F2}! Come back tomorrow for more!");
        SavePlayerData(steamId, newTotal);

        Console.WriteLine($"[SimpleCoinSystem] Player {player.PlayerName} claimed daily reward: €{reward:F2}");
    }

    // ===== LEADERBOARD =====
    public void ShowTopPlayers(CCSPlayerController player, int count = 10)
    {
        if (player == null) return;

        var topPlayers = _playerCoins
            .OrderByDescending(x => x.Value)
            .Take(count)
            .ToList();

        player.PrintToChat($" \x10━━━━━━ TOP {count} RICHEST PLAYERS ━━━━━━");

        int rank = 1;
        foreach (var kvp in topPlayers)
        {
            var targetPlayer = Utilities.GetPlayers().FirstOrDefault(p => p?.SteamID == kvp.Key);
            string playerName = targetPlayer?.PlayerName ?? $"Player#{kvp.Key}";

            string medal = rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"{rank}."
            };

            player.PrintToChat($" {medal} \x0E{playerName}\x01 - \x10€{kvp.Value:F2}");
            rank++;
        }

        // Show player's own rank if not in top
        if (player.SteamID != 0 && !topPlayers.Any(x => x.Key == player.SteamID))
        {
            var allPlayers = _playerCoins.OrderByDescending(x => x.Value).ToList();
            var playerRank = allPlayers.FindIndex(x => x.Key == player.SteamID) + 1;
            var playerBalance = _playerCoins.GetValueOrDefault(player.SteamID, 0);

            player.PrintToChat($" \x08Your Rank:\x01 #{playerRank} - \x10€{playerBalance:F2}");
        }
    }

    // ===== ANTI-AFK SYSTEM =====
    public bool IsPlayerActive(ulong steamId)
    {
        if (!_lastActivity.TryGetValue(steamId, out var lastActive))
        {
            return true; // New player, consider active
        }

        // If no activity for 2 minutes, consider AFK
        return (DateTime.UtcNow - lastActive).TotalMinutes < 2;
    }

    public void UpdatePlayerActivity(ulong steamId)
    {
        _lastActivity[steamId] = DateTime.UtcNow;
    }

    // ===== ADMIN COMMANDS =====
    public void AdminAddCoins(ulong targetSteamId, decimal amount)
    {
        var newTotal = _playerCoins.AddOrUpdate(targetSteamId, amount, (key, current) => current + amount);
        SavePlayerData(targetSteamId, newTotal);
        Console.WriteLine($"[SimpleCoinSystem] Admin added €{amount:F2} to player {targetSteamId}. New total: €{newTotal:F2}");
    }

    public void AdminSetCoins(ulong targetSteamId, decimal amount)
    {
        _playerCoins[targetSteamId] = amount;
        SavePlayerData(targetSteamId, amount);
        Console.WriteLine($"[SimpleCoinSystem] Admin set player {targetSteamId} coins to €{amount:F2}");
    }

    public void AdminResetCoins(ulong targetSteamId)
    {
        _playerCoins[targetSteamId] = 0;
        SavePlayerData(targetSteamId, 0);
        Console.WriteLine($"[SimpleCoinSystem] Admin reset player {targetSteamId} coins to €0.00");
    }
}

// Player statistics tracking
public class PlayerStats
{
    public int TotalKills { get; set; }
    public int TotalHeadshots { get; set; }
    public int KnifeKills { get; set; }
    public int BombPlants { get; set; }
    public int BombDefuses { get; set; }
    public int Assists { get; set; }
}
