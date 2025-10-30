using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;

namespace GunMenu;

public class GunMenuPlugin : BasePlugin
{
    public override string ModuleName => "Gun Menu";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "BadBoys";
    public override string ModuleDescription => "Automatic gun menu at round start";

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        Console.WriteLine("[GunMenu] Plugin loaded!");
    }

    [ConsoleCommand("css_guns", "Open guns menu")]
    [ConsoleCommand("css_menu", "Open guns menu")]
    public void OnGunsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid || player.TeamNum < 2) return;
        OpenMainMenu(player);
    }

    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // Várunk 0.5 másodpercet hogy a játékosok spawnoljanak
        AddTimer(0.5f, () =>
        {
            var players = Utilities.GetPlayers();
            foreach (var player in players)
            {
                if (player == null || !player.IsValid || player.TeamNum < 2) continue;
                if (!player.PawnIsAlive) continue;

                OpenMainMenu(player);
            }
        });

        return HookResult.Continue;
    }

    private void OpenMainMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🔫 Gun Menu");

        menu.AddMenuOption("Rifles", (p, option) => OpenRiflesMenu(p));
        menu.AddMenuOption("SMGs", (p, option) => OpenSMGsMenu(p));
        menu.AddMenuOption("Snipers", (p, option) => OpenSnipersMenu(p));
        menu.AddMenuOption("Heavy Weapons", (p, option) => OpenHeavyMenu(p));
        menu.AddMenuOption("Pistols", (p, option) => OpenPistolsMenu(p));
        menu.AddMenuOption("Equipment", (p, option) => OpenEquipmentMenu(p));

        MenuManager.OpenChatMenu(player, menu);
    }

    private void OpenRiflesMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🔫 Rifles");
        var isCT = player.TeamNum == (int)CsTeam.CounterTerrorist;

        if (isCT)
        {
            menu.AddMenuOption("M4A4", (p, option) => GiveWeapon(p, "weapon_m4a1"));
            menu.AddMenuOption("M4A1-S", (p, option) => GiveWeapon(p, "weapon_m4a1_silencer"));
            menu.AddMenuOption("FAMAS", (p, option) => GiveWeapon(p, "weapon_famas"));
            menu.AddMenuOption("AUG", (p, option) => GiveWeapon(p, "weapon_aug"));
        }
        else
        {
            menu.AddMenuOption("AK-47", (p, option) => GiveWeapon(p, "weapon_ak47"));
            menu.AddMenuOption("Galil AR", (p, option) => GiveWeapon(p, "weapon_galilar"));
            menu.AddMenuOption("SG 553", (p, option) => GiveWeapon(p, "weapon_sg556"));
        }

        menu.AddMenuOption("← Back", (p, option) => OpenMainMenu(p));
        MenuManager.OpenChatMenu(player, menu);
    }

    private void OpenSMGsMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🔫 SMGs");
        var isCT = player.TeamNum == (int)CsTeam.CounterTerrorist;

        menu.AddMenuOption("MP9", (p, option) => GiveWeapon(p, "weapon_mp9"));
        menu.AddMenuOption("MP7", (p, option) => GiveWeapon(p, "weapon_mp7"));
        menu.AddMenuOption("MP5-SD", (p, option) => GiveWeapon(p, "weapon_mp5sd"));
        menu.AddMenuOption("UMP-45", (p, option) => GiveWeapon(p, "weapon_ump45"));
        menu.AddMenuOption("P90", (p, option) => GiveWeapon(p, "weapon_p90"));
        menu.AddMenuOption("PP-Bizon", (p, option) => GiveWeapon(p, "weapon_bizon"));

        if (!isCT)
        {
            menu.AddMenuOption("MAC-10", (p, option) => GiveWeapon(p, "weapon_mac10"));
        }

        menu.AddMenuOption("← Back", (p, option) => OpenMainMenu(p));
        MenuManager.OpenChatMenu(player, menu);
    }

    private void OpenSnipersMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🔫 Snipers");
        var isCT = player.TeamNum == (int)CsTeam.CounterTerrorist;

        menu.AddMenuOption("AWP", (p, option) => GiveWeapon(p, "weapon_awp"));
        menu.AddMenuOption("SSG 08 (Scout)", (p, option) => GiveWeapon(p, "weapon_ssg08"));

        if (isCT)
        {
            menu.AddMenuOption("SCAR-20", (p, option) => GiveWeapon(p, "weapon_scar20"));
        }
        else
        {
            menu.AddMenuOption("G3SG1", (p, option) => GiveWeapon(p, "weapon_g3sg1"));
        }

        menu.AddMenuOption("← Back", (p, option) => OpenMainMenu(p));
        MenuManager.OpenChatMenu(player, menu);
    }

    private void OpenHeavyMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🔫 Heavy Weapons");

        menu.AddMenuOption("Nova", (p, option) => GiveWeapon(p, "weapon_nova"));
        menu.AddMenuOption("XM1014", (p, option) => GiveWeapon(p, "weapon_xm1014"));
        menu.AddMenuOption("Sawed-Off", (p, option) => GiveWeapon(p, "weapon_sawedoff"));
        menu.AddMenuOption("MAG-7", (p, option) => GiveWeapon(p, "weapon_mag7"));
        menu.AddMenuOption("M249", (p, option) => GiveWeapon(p, "weapon_m249"));
        menu.AddMenuOption("Negev", (p, option) => GiveWeapon(p, "weapon_negev"));

        menu.AddMenuOption("← Back", (p, option) => OpenMainMenu(p));
        MenuManager.OpenChatMenu(player, menu);
    }

    private void OpenPistolsMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🔫 Pistols");
        var isCT = player.TeamNum == (int)CsTeam.CounterTerrorist;

        if (isCT)
        {
            menu.AddMenuOption("USP-S", (p, option) => GiveWeapon(p, "weapon_usp_silencer"));
            menu.AddMenuOption("P2000", (p, option) => GiveWeapon(p, "weapon_hkp2000"));
            menu.AddMenuOption("Five-SeveN", (p, option) => GiveWeapon(p, "weapon_fiveseven"));
        }
        else
        {
            menu.AddMenuOption("Glock-18", (p, option) => GiveWeapon(p, "weapon_glock"));
            menu.AddMenuOption("Tec-9", (p, option) => GiveWeapon(p, "weapon_tec9"));
        }

        menu.AddMenuOption("P250", (p, option) => GiveWeapon(p, "weapon_p250"));
        menu.AddMenuOption("Desert Eagle", (p, option) => GiveWeapon(p, "weapon_deagle"));
        menu.AddMenuOption("Dual Berettas", (p, option) => GiveWeapon(p, "weapon_elite"));
        menu.AddMenuOption("CZ75-Auto", (p, option) => GiveWeapon(p, "weapon_cz75a"));
        menu.AddMenuOption("R8 Revolver", (p, option) => GiveWeapon(p, "weapon_revolver"));

        menu.AddMenuOption("← Back", (p, option) => OpenMainMenu(p));
        MenuManager.OpenChatMenu(player, menu);
    }

    private void OpenEquipmentMenu(CCSPlayerController player)
    {
        var menu = new ChatMenu("🛡️ Equipment");

        menu.AddMenuOption("Kevlar Vest", (p, option) => GiveEquipment(p, "item_kevlar"));
        menu.AddMenuOption("Kevlar + Helmet", (p, option) => GiveEquipment(p, "item_assaultsuit"));
        menu.AddMenuOption("Defuse Kit (CT)", (p, option) => GiveEquipment(p, "item_defuser"));
        menu.AddMenuOption("HE Grenade", (p, option) => GiveEquipment(p, "weapon_hegrenade"));
        menu.AddMenuOption("Flashbang", (p, option) => GiveEquipment(p, "weapon_flashbang"));
        menu.AddMenuOption("Smoke Grenade", (p, option) => GiveEquipment(p, "weapon_smokegrenade"));
        menu.AddMenuOption("Molotov/Incendiary", (p, option) =>
        {
            var isCT = p.TeamNum == (int)CsTeam.CounterTerrorist;
            GiveEquipment(p, isCT ? "weapon_incgrenade" : "weapon_molotov");
        });
        menu.AddMenuOption("Decoy", (p, option) => GiveEquipment(p, "weapon_decoy"));

        menu.AddMenuOption("← Back", (p, option) => OpenMainMenu(p));
        MenuManager.OpenChatMenu(player, menu);
    }

    private void GiveWeapon(CCSPlayerController player, string weaponName)
    {
        if (!player.IsValid || !player.PawnIsAlive) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null) return;

        // Távolítsuk el a jelenlegi fegyvereket
        RemoveWeapons(player);

        // Add az új fegyvert
        player.GiveNamedItem(weaponName);

        player.PrintToChat($" \x04[Gun Menu]\x01 You received: \x0E{weaponName.Replace("weapon_", "")}");

        // Újra nyitjuk a menüt
        AddTimer(0.1f, () => OpenMainMenu(player));
    }

    private void GiveEquipment(CCSPlayerController player, string itemName)
    {
        if (!player.IsValid || !player.PawnIsAlive) return;

        player.GiveNamedItem(itemName);
        player.PrintToChat($" \x04[Gun Menu]\x01 You received: \x0E{itemName.Replace("weapon_", "").Replace("item_", "")}");

        // Újra nyitjuk a menüt
        AddTimer(0.1f, () => OpenEquipmentMenu(player));
    }

    private void RemoveWeapons(CCSPlayerController player)
    {
        if (!player.IsValid || !player.PawnIsAlive) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || pawn.WeaponServices == null) return;

        foreach (var weapon in pawn.WeaponServices.MyWeapons)
        {
            if (weapon == null || !weapon.IsValid || weapon.Value == null) continue;

            var weaponName = weapon.Value.DesignerName;

            // Ne távolítsuk el a kést és C4-et
            if (weaponName.Contains("knife") || weaponName.Contains("c4")) continue;

            weapon.Value.Remove();
        }
    }
}
