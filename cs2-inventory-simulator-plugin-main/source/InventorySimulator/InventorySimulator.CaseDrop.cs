using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Concurrent;
using System.Drawing;

namespace InventorySimulator;

public partial class InventorySimulator
{
    private readonly Random _caseDropRandom = new Random();
    private readonly ConcurrentDictionary<int, DateTime> _droppedCases = new ConcurrentDictionary<int, DateTime>();
    private int _tickCounter = 0;
    private const float CASE_DROP_CHANCE = 0.10f; // 10% esély
    private const float PICKUP_DISTANCE = 75.0f; // Felvételi távolság

    // Használható CS2 prop modellek ládákhoz (egyszerűbb modellek)
    private readonly string[] _caseModels = new[]
    {
        "models/props/crates/crate_01.vmdl",
        "models/props_junk/wood_crate001a.vmdl",
        "models/props_junk/cardboard_box003a.vmdl"
    };

    public void OnPlayerKilledForCaseDrop(CCSPlayerController attacker, CCSPlayerController victim)
    {
        if (attacker == null || victim == null) return;
        if (!IsPlayerHumanAndValid(attacker) || !IsPlayerHumanAndValid(victim)) return;

        // 10% esély a láda dobásra
        if (_caseDropRandom.NextDouble() > CASE_DROP_CHANCE) return;

        // Halott pozíciója
        var victimPawn = victim.PlayerPawn.Value;
        if (victimPawn == null || victimPawn.AbsOrigin == null) return;

        var dropPosition = victimPawn.AbsOrigin;

        // Spawn case entity
        SpawnCaseAtPosition(dropPosition);

        // Értesítés a szervernek
        Server.PrintToChatAll($" \x10📦 {attacker.PlayerName}\x01 dropped a \x0Emystery case\x01 by killing {victim.PlayerName}!");
    }

    private void SpawnCaseAtPosition(Vector position)
    {
        try
        {
            // Prop fizikai entity létrehozása
            var caseEntity = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");

            if (caseEntity == null)
            {
                Console.WriteLine("[CaseDrop] Failed to create case entity!");
                return;
            }

            // Random modell kiválasztása
            var modelPath = _caseModels[_caseDropRandom.Next(_caseModels.Length)];

            // Pozíció beállítása (kicsit feljebb hogy ne a földben legyen)
            var spawnPos = new Vector(position.X, position.Y, position.Z + 15);

            // Entity beállítások
            caseEntity.SetModel(modelPath);
            caseEntity.Teleport(spawnPos, new QAngle(0, 0, 0), new Vector(0, 0, 0));

            // Spawn
            caseEntity.DispatchSpawn();

            // Glow effect (világítás) - beállítás spawn után
            Server.NextFrame(() =>
            {
                if (caseEntity != null && caseEntity.IsValid)
                {
                    caseEntity.Glow.GlowColorOverride = Color.Gold;
                    caseEntity.Glow.GlowRange = 800;
                    caseEntity.Glow.GlowType = 3;
                    caseEntity.Render = Color.FromArgb(255, 255, 215, 0);
                }
            });

            // Tárolás későbbi ellenőrzéshez
            _droppedCases.TryAdd(caseEntity.Index, DateTime.UtcNow);

            Console.WriteLine($"[CaseDrop] Case spawned at {spawnPos.X:F1}, {spawnPos.Y:F1}, {spawnPos.Z:F1} with model {modelPath}");

            // Auto-remove after 30 seconds
            AddTimer(30.0f, () =>
            {
                if (caseEntity != null && caseEntity.IsValid)
                {
                    caseEntity.Remove();
                    _droppedCases.TryRemove(caseEntity.Index, out _);
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CaseDrop] Error spawning case: {ex.Message}");
            Console.WriteLine($"[CaseDrop] Stack trace: {ex.StackTrace}");
        }
    }

    public void CheckCasePickupOnTick()
    {
        // Csak minden 30. ticken ellenőrizünk (performance)
        _tickCounter++;
        if (_tickCounter < 30) return;
        _tickCounter = 0;

        if (_droppedCases.IsEmpty) return;

        var players = Utilities.GetPlayers();
        foreach (var player in players)
        {
            if (player == null || !IsPlayerHumanAndValid(player)) continue;

            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null || playerPawn.AbsOrigin == null) continue;

            var playerPos = playerPawn.AbsOrigin;

            // Keressünk közeli ládákat
            foreach (var caseEntry in _droppedCases.ToArray())
            {
                var caseEntity = Utilities.GetEntityFromIndex<CDynamicProp>(caseEntry.Key);
                if (caseEntity == null || !caseEntity.IsValid)
                {
                    _droppedCases.TryRemove(caseEntry.Key, out _);
                    continue;
                }

                if (caseEntity.AbsOrigin == null) continue;

                var distance = CalculateDistance(playerPos, caseEntity.AbsOrigin);

                if (distance <= PICKUP_DISTANCE)
                {
                    // Játékos felvette a ládát!
                    OnCasePickedUp(player, caseEntity);
                    break;
                }
            }
        }
    }

    private void OnCasePickedUp(CCSPlayerController player, CDynamicProp caseEntity)
    {
        // Jutalom generálása
        var random = new Random();
        decimal reward = (decimal)(random.NextDouble() * 5.0 + 1.0); // €1.00 - €6.00 között

        // Coin hozzáadása
        if (_coinSystem != null)
        {
            var steamId = player.SteamID;
            var newTotal = _coinSystem.GetPlayerCoins(steamId) + reward;
            _coinSystem.UpdatePlayerCoins(steamId, newTotal);

            player.PrintToChat($" \x10📦 [Mystery Case]\x01 You found \x0E€{reward:F2}\x01! Total: \x0E€{newTotal:F2}");
            Server.PrintToChatAll($" \x10📦 {player.PlayerName}\x01 opened a mystery case and won \x0E€{reward:F2}\x01!");
        }

        // Láda eltávolítása
        _droppedCases.TryRemove(caseEntity.Index, out _);
        caseEntity.Remove();

        Console.WriteLine($"[CaseDrop] Player {player.PlayerName} picked up case, won €{reward:F2}");
    }

    private float CalculateDistance(Vector pos1, Vector pos2)
    {
        var dx = pos1.X - pos2.X;
        var dy = pos1.Y - pos2.Y;
        var dz = pos1.Z - pos2.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
