/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Ian Lucas. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

using System.Net;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Plugin;
using CounterStrikeSharp.API.Modules.Utils;

namespace InventorySimulator;

class SyncRequest
{
    public string SteamId { get; set; } = string.Empty;
    public long LastUpdateTimestamp { get; set; } = 0;
}

public partial class InventorySimulator
{
    private readonly HttpListener _httpListener = new();
    private Task? _listenTask;
    private readonly Dictionary<string, DateTimeOffset> _lastSyncTime = new();
    private readonly object _syncLock = new();
    private bool _isListening;

    private void StartHttpServer()
    {
        if (_isListening) return;

        try
        {
            _httpListener.Prefixes.Add("http://*:5005/");
            _httpListener.Start();
            _isListening = true;

            _listenTask = Task.Run(async () =>
            {
                while (_isListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _ = HandleRequestAsync(context);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling request: {ex.Message}");
                    }
                }
            });

            Console.WriteLine("[InventorySimulator] HTTP server started on port 5005");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InventorySimulator] Failed to start HTTP server: {ex.Message}");
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod == "POST" && context.Request.Url?.PathAndQuery == "/api/refresh-inventory")
            {
                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                var request = JsonSerializer.Deserialize<SyncRequest>(body);

                if (request?.SteamId != null)
                {
                    // Rate limit - csak 2 másodpercenként frissítünk egy adott SteamID-t
                    lock (_syncLock)
                    {
                        var now = DateTimeOffset.UtcNow;
                        if (_lastSyncTime.TryGetValue(request.SteamId, out var lastSync))
                        {
                            if ((now - lastSync).TotalSeconds < 2)
                            {
                                context.Response.StatusCode = 429; // Too Many Requests
                                context.Response.Close();
                                return;
                            }
                        }
                        _lastSyncTime[request.SteamId] = now;
                        if (_lastUpdateTimestamps.ContainsKey(ulong.Parse(request.SteamId)))
                        {
                            _lastUpdateTimestamps[ulong.Parse(request.SteamId)] = request.LastUpdateTimestamp;
                        }
                    }

                    // Inventory frissítése a szerveren
                    var steamId = ulong.Parse(request.SteamId);
                    var player = Utilities.GetPlayerFromSteamId(steamId);
                    if (player != null)
                    {
                        // Frissítjük a játékos inventory-ját
                        await Task.Run(() => RefreshPlayerInventory(player));
                        context.Response.StatusCode = 200;
                    }
                    else
                    {
                        context.Response.StatusCode = 404; // Player not found
                    }
                }
                else
                {
                    context.Response.StatusCode = 400; // Bad Request
                }
            }
            else
            {
                context.Response.StatusCode = 404; // Not Found
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InventorySimulator] Error processing request: {ex.Message}");
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private void StopHttpServer()
    {
        _isListening = false;
        _httpListener.Stop();
        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch { }
    }

    private void RefreshPlayerInventory(CCSPlayerController player)
    {
        try
        {
            if (player != null && player.IsValid)
            {
                Server.NextFrame(() => 
                {
                    // Egyszerűen csak hívjuk meg az API frissítést, de force=true nélkül
                    // hogy a változás detektálás működjön
                    RefreshPlayerInventory(player, false);
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[InventorySimulator] Error refreshing inventory: {ex.Message}");
        }
    }

    // A Load és Unload metódusok most az InventorySimulator.cs fájlban vannak kezelve
}