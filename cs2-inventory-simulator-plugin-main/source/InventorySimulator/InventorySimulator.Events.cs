/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace InventorySimulator;

public partial class InventorySimulator
{
    public HookResult OnPlayerConnect(EventPlayerConnect @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player))
            OnPlayerConnect(player);
        return HookResult.Continue;
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player))
            OnPlayerConnect(player);
        return HookResult.Continue;
    }

    public void OnPlayerConnect(CCSPlayerController player)
    {
        if (PlayerOnTickInventoryManager.TryGetValue(player.SteamID, out var tuple))
            PlayerOnTickInventoryManager[player.SteamID] = (player, tuple.Item2);
        // Only fetch inventory if not already present
        if (!PlayerInventoryManager.ContainsKey(player.SteamID))
            RefreshPlayerInventory(player);

        // Check for daily login reward
        _coinSystem?.CheckDailyLoginReward(player);
    }

    public HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo _)
    {
        Server.NextFrame(() =>
        {
            if (GetGameRules().TeamIntroPeriod)
                GiveTeamPreviewItems("team_intro");
        });
        return HookResult.Continue;
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player) && IsPlayerPawnValid(player))
        {
            GiveOnPlayerSpawn(player);
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDeathPre(EventPlayerDeath @event, GameEventInfo _)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        var assister = @event.Assister;

        if (attacker != null && victim != null)
        {
            var isValidAttacker = (IsPlayerHumanAndValid(attacker) && IsPlayerPawnValid(attacker));
            var isValidVictim = (invsim_stattrak_ignore_bots.Value ? IsPlayerHumanAndValid(victim) : IsPlayerValid(victim)) && IsPlayerPawnValid(victim);

            // Coin system esetében BOT-ok is validek
            var isValidVictimForCoins = IsPlayerValid(victim) && IsPlayerPawnValid(victim);

            if (isValidAttacker && isValidVictim)
            {
                GivePlayerWeaponStatTrakIncrement(attacker, @event.Weapon, @event.WeaponItemid);
            }

            // Coin reward külön logikával (BOT-ok is számítanak)
            if (isValidAttacker && isValidVictimForCoins)
            {
                // Headshot ellenőrzés
                bool isHeadshot = @event.Headshot;
                _coinSystem?.AddKillReward(attacker, isHeadshot);

                // Kill streak és speciális ölés tracking
                _coinSystem?.HandleKillForStreak(attacker, victim, isHeadshot, @event.Weapon);
            }

            // Assist reward
            if (assister != null && IsPlayerHumanAndValid(assister) && IsPlayerPawnValid(assister) && _coinSystem != null)
            {
                _coinSystem.AddAssistReward(assister);
            }

            // Case drop system (10% chance)
            if (isValidAttacker && isValidVictimForCoins)
            {
                OnPlayerKilledForCaseDrop(attacker, victim);
            }
        }

        return HookResult.Continue;
    }

    public HookResult OnRoundMvpPre(EventRoundMvp @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player) && IsPlayerPawnValid(player))
        {
            GivePlayerMusicKitStatTrakIncrement(player);
            
            // Coin system - award MVP coins
            _coinSystem?.AddMvpReward(player);
        }

        return HookResult.Continue;
    }

    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo _)
    {
        if (_coinSystem is null) return HookResult.Continue;

        // Get winning team players
        var winningTeam = @event.Winner;
    var winners = InventorySimulator.GetPlayers()
            .Where(p => IsPlayerHumanAndValid(p) && p.TeamNum == winningTeam)
            .ToList();

        if (winners.Any())
        {
            _coinSystem.AddRoundWinReward(winners);
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player))
        {
            ClearPlayerUseCmd(player.SteamID);
            ClearPlayerServerSideClient(player.UserId);
            RemovePlayerInventory(player.SteamID);
            ClearInventoryManager();

            // Coin system - player disconnecting (simple system doesn't need to save)
        }

        return HookResult.Continue;
    }

    public HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player) && _coinSystem != null)
        {
            _coinSystem.AddBombPlantReward(player);
        }

        return HookResult.Continue;
    }

    public HookResult OnBombDefused(EventBombDefused @event, GameEventInfo _)
    {
        var player = @event.Userid;
        if (player != null && IsPlayerHumanAndValid(player) && _coinSystem != null)
        {
            _coinSystem.AddBombDefuseReward(player);
        }

        return HookResult.Continue;
    }

    public HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo _)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        // Track assists - if damage dealt but not killing blow
        if (attacker != null && victim != null && attacker != victim &&
            IsPlayerHumanAndValid(attacker) && IsPlayerHumanAndValid(victim) && _coinSystem != null)
        {
            _coinSystem.UpdatePlayerActivity(attacker.SteamID);
        }

        return HookResult.Continue;
    }
}
