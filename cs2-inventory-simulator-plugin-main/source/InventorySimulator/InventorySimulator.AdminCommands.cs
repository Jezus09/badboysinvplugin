using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System;
using System.Linq;

namespace InventorySimulator;

public partial class InventorySimulator
{
    [ConsoleCommand("css_coins")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCoinsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !IsPlayerHumanAndValid(player)) return;

        if (_coinSystem == null)
        {
            player.PrintToChat(" \x07[Coins]\x01 Coin system is not available.");
            return;
        }

        var euros = _coinSystem.GetPlayerCoins(player.SteamID);
        player.PrintToChat(Localizer["coins.balance", $"{euros:F2}"]);
    }

    [ConsoleCommand("css_coinhelp")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnCoinHelpCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !IsPlayerHumanAndValid(player)) return;

        player.PrintToChat(" \x04[€ System]\x01 Available commands:");
        player.PrintToChat(" \x0E!coins\x01 - Check your balance");
        player.PrintToChat(" \x0E!top\x01 - Top 10 richest players");
        player.PrintToChat(" \x0E!coinhelp\x01 - Show this help");
        player.PrintToChat(" ");
        player.PrintToChat(" \x04[Rewards]\x01 Earn € by:");
        player.PrintToChat(" \x0E• Kills\x01 - €0.10-0.30 (random)");
        player.PrintToChat(" \x0E• Headshots\x01 - €0.30-0.50 (random)");
        player.PrintToChat(" \x0E• Kill Streaks\x01 - Extra bonuses!");
        player.PrintToChat(" \x0E• Knife kills\x01 - +€0.50");
        player.PrintToChat(" \x0E• Daily login\x01 - €1.00");
    }

    [ConsoleCommand("css_top")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTopCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !IsPlayerHumanAndValid(player)) return;

        if (_coinSystem == null)
        {
            player.PrintToChat(" \x07[€]\x01 Coin system is not available.");
            return;
        }

        _coinSystem.ShowTopPlayers(player, 10);
    }

    [ConsoleCommand("css_addcoins")]
    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 2, usage: "<player> <amount>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnAddCoinsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (_coinSystem == null)
        {
            commandInfo.ReplyToCommand("[€] Coin system is not available.");
            return;
        }

        var targetName = commandInfo.GetArg(1);
        if (!decimal.TryParse(commandInfo.GetArg(2), out var amount))
        {
            commandInfo.ReplyToCommand("[€] Invalid amount.");
            return;
        }

        var target = Utilities.GetPlayers().FirstOrDefault(p =>
            p?.PlayerName != null && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null || !target.IsValid || target.SteamID == 0)
        {
            commandInfo.ReplyToCommand($"[€] Player '{targetName}' not found.");
            return;
        }

        _coinSystem.AdminAddCoins(target.SteamID, amount);
        var newTotal = _coinSystem.GetPlayerCoins(target.SteamID);

        commandInfo.ReplyToCommand($"[€] Added €{amount:F2} to {target.PlayerName}. New balance: €{newTotal:F2}");
        target.PrintToChat($" \x04[Admin]\x01 You received \x0E€{amount:F2}\x01! Total: \x0E€{newTotal:F2}");
    }

    [ConsoleCommand("css_setcoins")]
    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 2, usage: "<player> <amount>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnSetCoinsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (_coinSystem == null)
        {
            commandInfo.ReplyToCommand("[€] Coin system is not available.");
            return;
        }

        var targetName = commandInfo.GetArg(1);
        if (!decimal.TryParse(commandInfo.GetArg(2), out var amount))
        {
            commandInfo.ReplyToCommand("[€] Invalid amount.");
            return;
        }

        var target = Utilities.GetPlayers().FirstOrDefault(p =>
            p?.PlayerName != null && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null || !target.IsValid || target.SteamID == 0)
        {
            commandInfo.ReplyToCommand($"[€] Player '{targetName}' not found.");
            return;
        }

        _coinSystem.AdminSetCoins(target.SteamID, amount);

        commandInfo.ReplyToCommand($"[€] Set {target.PlayerName}'s balance to €{amount:F2}");
        target.PrintToChat($" \x04[Admin]\x01 Your balance was set to \x0E€{amount:F2}");
    }

    [ConsoleCommand("css_resetcoins")]
    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 1, usage: "<player>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnResetCoinsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (_coinSystem == null)
        {
            commandInfo.ReplyToCommand("[€] Coin system is not available.");
            return;
        }

        var targetName = commandInfo.GetArg(1);
        var target = Utilities.GetPlayers().FirstOrDefault(p =>
            p?.PlayerName != null && p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase));

        if (target == null || !target.IsValid || target.SteamID == 0)
        {
            commandInfo.ReplyToCommand($"[€] Player '{targetName}' not found.");
            return;
        }

        _coinSystem.AdminResetCoins(target.SteamID);

        commandInfo.ReplyToCommand($"[€] Reset {target.PlayerName}'s balance to €0.00");
        target.PrintToChat($" \x04[Admin]\x01 Your balance was reset to \x0E€0.00");
    }
}