using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;

namespace InventorySimulator;

public partial class InventorySimulator
{
    [ConsoleCommand("css_top")]
    [CommandHelper(minArgs: 0, usage: "", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTopCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !IsPlayerHumanAndValid(player)) return;

        if (_coinSystem == null)
        {
            player.PrintToChat(" \x07[Coins]\x01 Coin system is not available.");
            return;
        }

        player.PrintToChat(" \x04[Top Coins]\x01 Coming soon! Check the website for leaderboard.");
    }

    [ConsoleCommand("css_givecoins")]
    [RequiresPermissions("@css/generic")]
    [CommandHelper(minArgs: 2, usage: "<player> <amount>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnGiveCoinsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (_coinSystem == null)
        {
            commandInfo.ReplyToCommand("Coin system is not available.");
            return;
        }

        var targetName = commandInfo.GetArg(1);
        if (!int.TryParse(commandInfo.GetArg(2), out var amount))
        {
            commandInfo.ReplyToCommand("Invalid amount specified.");
            return;
        }

    var target = InventorySimulator.GetPlayers().FirstOrDefault(p => 
            p != null && IsPlayerHumanAndValid(p) && 
            (p.PlayerName.Contains(targetName, StringComparison.OrdinalIgnoreCase) || 
             p.SteamID.ToString().Contains(targetName)));

        if (target == null)
        {
            commandInfo.ReplyToCommand($"Player '{targetName}' not found.");
            return;
        }

        var currentCoins = _coinSystem.GetPlayerCoins(target.SteamID);
        var newTotal = currentCoins + amount;
        
        // Manual update in memory and database
        _coinSystem.UpdatePlayerCoins(target.SteamID, newTotal);
        
        commandInfo.ReplyToCommand($"Gave {amount} coins to {target.PlayerName}. Total: {newTotal}");
        target.PrintToChat($" \x04[Admin]\x01 You received \x0E{amount}\x01 coins! Total: \x0E{newTotal}\x01");
    }
}