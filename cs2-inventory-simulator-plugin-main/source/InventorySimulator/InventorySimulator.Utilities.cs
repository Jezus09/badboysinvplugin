// --- BEGIN: Local replacements for missing CounterStrikeSharp.API.Core.Utilities ---
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using NativeVector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace InventorySimulator
{
public partial class InventorySimulator
{
    // --- BEGIN: Local replacements for missing CounterStrikeSharp.API.Core.Utilities ---
    public static void SetStateChanged(object entity, string className, string propertyName, int arrayIndex = -1)
    {
        // Try to find and invoke a method named SetStateChanged via reflection
        var type = entity.GetType();
        var method = type.GetMethod("SetStateChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (method != null)
        {
            if (arrayIndex >= 0)
                method.Invoke(entity, new object[] { className, propertyName, arrayIndex });
            else
                method.Invoke(entity, new object[] { className, propertyName });
        }
        // else: No-op fallback
    }

    public static T? CreateEntityByName<T>(string name) where T : class
    {
        // Try to find a static method CreateEntityByName on T or a known type
        var type = typeof(T);
        var method = type.GetMethod("CreateEntityByName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if (method != null)
        {
            return method.Invoke(null, new object[] { name }) as T;
        }
        // else: No-op fallback
        return null;
    }

    public static IEnumerable<T> FindAllEntitiesByDesignerName<T>(string designerName) where T : class
    {
        // Try to find a static method FindAllEntitiesByDesignerName on T or a known type
        var type = typeof(T);
        var method = type.GetMethod("FindAllEntitiesByDesignerName", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if (method != null)
        {
            var result = method.Invoke(null, new object[] { designerName });
            if (result is IEnumerable<T> enumerable)
                return enumerable;
        }
        // else: No-op fallback
        return Enumerable.Empty<T>();
    }

    public static CCSPlayerController? GetPlayerFromUserid(int userid)
    {
        // Try to find a player with matching UserId
        return GetPlayers().FirstOrDefault(p => p != null && p.IsValid && p.UserId == userid);
    }
    // --- END: Local replacements for missing CounterStrikeSharp.API.Core.Utilities ---
    static CCSGameRulesProxy? GameRulesProxy;

    public static IEnumerable<CCSPlayerController> GetPlayers()
    {
        // Try to use a static GetPlayers() method if available (reflection fallback)
        try
        {
            var core = typeof(CCSPlayerController).Assembly;
            var utilType = core.GetType("CounterStrikeSharp.API.Core.Utilities");
            var method = utilType?.GetMethod("GetPlayers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (method != null)
            {
                var result = method.Invoke(null, null) as IEnumerable<CCSPlayerController>;
                if (result != null)
                    return result.Where(p => p != null && p.IsValid);
            }
        }
        catch { /* fallback below */ }

        // Fallback: try index-based search (1-64)
        var list = new List<CCSPlayerController>();
        for (int i = 1; i <= 64; i++)
        {
            try
            {
                var player = (CCSPlayerController?)typeof(CounterStrikeSharp.API.Core.CCSPlayerController)
                    .GetMethod("FindByIndex", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                    ?.Invoke(null, new object[] { i });
                if (player != null && player.IsValid)
                    list.Add(player);
            }
            catch { }
        }
        return list;
    }

    public static string GetAgentModelPath(string model) => $"characters/models/{model}.vmdl";

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static CsTeam ToggleTeam(CsTeam team) => team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;

    public static float ViewAsFloat<T>(T value)
        where T : struct
    {
        byte[] bytes = value switch
        {
            int intValue => BitConverter.GetBytes(intValue),
            uint uintValue => BitConverter.GetBytes(uintValue),
            _ => throw new ArgumentException("Unsupported type"),
        };
        return BitConverter.ToSingle(bytes, 0);
    }

    public static NativeVector Vector3toVector(Vector3 vec) => new(vec.X, vec.Y, vec.Z);

    public static CCSGameRules GetGameRules() =>
        (
            GameRulesProxy?.IsValid == true ? GameRulesProxy.GameRules
            : (GameRulesProxy = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First())?.IsValid == true ? GameRulesProxy?.GameRules
            : null
        ) ?? throw new Exception("Game rules not found.");
    /// <summary>
    /// Returns the CCSPlayerController for a given SteamID, or null if not found.
    /// </summary>
    public static CCSPlayerController? GetPlayerFromSteamId(ulong steamId)
    {
        return GetPlayers().FirstOrDefault(p => p != null && p.IsValid && p.SteamID == steamId);
    }
}
}
