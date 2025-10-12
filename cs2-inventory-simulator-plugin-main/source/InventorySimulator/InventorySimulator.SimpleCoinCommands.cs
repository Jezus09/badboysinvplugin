using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

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

        player.PrintToChat(" \x04[Euros]\x01 Available commands:");
        player.PrintToChat(" \x0E!coins\x01 - Check your euro balance");
        player.PrintToChat(" \x0E!coinhelp\x01 - Show this help message");
        player.PrintToChat(" ");
        player.PrintToChat(" \x04[Rewards]\x01 Earn euros by:");
        player.PrintToChat(" \x0E• Killing enemies\x01 - €0.20");
        player.PrintToChat(" \x0E• Winning rounds\x01 - €1.00");
        player.PrintToChat(" \x0E• Getting MVP\x01 - €2.50");
    }
}