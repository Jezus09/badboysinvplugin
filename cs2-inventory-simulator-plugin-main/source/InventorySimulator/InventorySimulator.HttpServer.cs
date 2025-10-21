using System.Net;
using System.Text.Json;
using System.Threading;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace InventorySimulator;

public partial class InventorySimulator
{
	private HttpListener? _webhookListener;
	private Thread? _webhookThread;
	private bool _webhookRunning = false;

	public void StartWebhookListener(string prefix = "http://*:5005/")
	{
		if (_webhookRunning) return;
		_webhookListener = new HttpListener();
		_webhookListener.Prefixes.Add(prefix);
		_webhookListener.Start();
		_webhookRunning = true;
		_webhookThread = new Thread(WebhookListenLoop) { IsBackground = true };
		_webhookThread.Start();
	}

	private void WebhookListenLoop()
	{
		while (_webhookRunning && _webhookListener != null)
		{
			try
			{
				var context = _webhookListener.GetContext();
				ThreadPool.QueueUserWorkItem(_ => HandleWebhookRequest(context));
			}
			catch { /* Listener stopped or error */ }
		}
	}

	private void HandleWebhookRequest(HttpListenerContext context)
	{
		try
		{
			// Add CORS headers
			context.Response.AddHeader("Access-Control-Allow-Origin", "*");
			context.Response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
			context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

			// Handle OPTIONS preflight request
			if (context.Request.HttpMethod == "OPTIONS")
			{
				context.Response.StatusCode = 200;
				context.Response.Close();
				return;
			}

			if (context.Request.HttpMethod != "POST")
			{
				Logger.LogWarning($"[Webhook] Invalid method: {context.Request.HttpMethod}");
				context.Response.StatusCode = 404;
				context.Response.Close();
				return;
			}

			var path = context.Request.Url?.AbsolutePath;
			Logger.LogInformation($"[Webhook] Received request: {context.Request.HttpMethod} {path}");

			// Handle refresh-inventory endpoint
			if (path == "/api/plugin/refresh-inventory")
			{
				HandleRefreshInventory(context);
				return;
			}

			// Handle case-opened endpoint
			if (path == "/api/plugin/case-opened")
			{
				HandleCaseOpened(context);
				return;
			}

			Logger.LogWarning($"[Webhook] Unknown endpoint: {path}");
			context.Response.StatusCode = 404;
			context.Response.Close();
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] HandleWebhookRequest error: {ex.Message}");
			try
			{
				context.Response.StatusCode = 500;
				context.Response.Close();
			}
			catch { }
		}
	}

	private void HandleRefreshInventory(HttpListenerContext context)
	{
		try
		{
			using var reader = new System.IO.StreamReader(context.Request.InputStream);
			var body = reader.ReadToEnd();
			Logger.LogInformation($"[Webhook] RefreshInventory body: {body}");

			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var data = JsonSerializer.Deserialize<RefreshInventoryRequest>(body, options);

			if (data?.SteamId != null && ulong.TryParse(data.SteamId, out var steamId))
			{
				Logger.LogInformation($"[Webhook] Refreshing inventory for SteamID: {steamId}");
				// Schedule the refresh on the main server thread
				CounterStrikeSharp.API.Server.NextFrame(() =>
				{
					var player = InventorySimulator.GetPlayerFromSteamId(steamId);
					if (player != null && player.IsValid)
					{
						Logger.LogInformation($"[Webhook] Player found, refreshing inventory");
						RefreshPlayerInventory(player, true);
					}
					else
					{
						Logger.LogWarning($"[Webhook] Player not found for SteamID: {steamId}");
					}
				});
				context.Response.StatusCode = 200;
				context.Response.Close();
				return;
			}
			Logger.LogWarning($"[Webhook] Invalid RefreshInventory request data");
			context.Response.StatusCode = 400;
			context.Response.Close();
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] RefreshInventory error: {ex.Message}");
			context.Response.StatusCode = 500;
			context.Response.Close();
		}
	}

	private void HandleCaseOpened(HttpListenerContext context)
	{
		try
		{
			using var reader = new System.IO.StreamReader(context.Request.InputStream);
			var body = reader.ReadToEnd();
			Logger.LogInformation($"[Webhook] CaseOpened body: {body}");

			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var data = JsonSerializer.Deserialize<CaseOpenedRequest>(body, options);

			if (data?.PlayerName != null && data?.ItemName != null)
			{
				Logger.LogInformation($"[Webhook] Broadcasting case opening: {data.PlayerName} - {data.ItemName}");
				// Schedule the broadcast on the main server thread
				CounterStrikeSharp.API.Server.NextFrame(() =>
				{
					BroadcastCaseOpening(data.PlayerName, data.ItemName, data.Rarity, data.StatTrak);
				});
				context.Response.StatusCode = 200;
				context.Response.Close();
				return;
			}
			Logger.LogWarning($"[Webhook] Invalid CaseOpened request data");
			context.Response.StatusCode = 400;
			context.Response.Close();
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] CaseOpened error: {ex.Message}");
			context.Response.StatusCode = 500;
			context.Response.Close();
		}
	}

	private void BroadcastCaseOpening(string playerName, string itemName, string? rarity, bool statTrak)
	{
		try
		{
			// Build the message similar to CS2 case opening notifications
			var statTrakPrefix = statTrak ? "StatTrak™ " : "";
			var rarityColor = GetRarityColor(rarity);
			var message = $" {rarityColor}★ {playerName}\x01 unboxed a {rarityColor}{statTrakPrefix}{itemName}\x01!";

			Logger.LogInformation($"[Webhook] Broadcasting message: {message}");

			// Use Utilities.GetPlayers() directly instead of our custom GetPlayers()
			var players = CounterStrikeSharp.API.Utilities.GetPlayers().ToList();
			Logger.LogInformation($"[Webhook] Found {players.Count} total players");

			var playerCount = 0;
			foreach (var player in players)
			{
				if (player == null)
				{
					Logger.LogWarning($"[Webhook] Player is null");
					continue;
				}

				if (!player.IsValid)
				{
					Logger.LogWarning($"[Webhook] Player {player.PlayerName} is not valid");
					continue;
				}

				var isBot = player.IsBot || player.IsHLTV;
				Logger.LogInformation($"[Webhook] Player: {player.PlayerName}, IsBot: {isBot}, Connected: {player.Connected}, Team: {player.Team}");

				// Don't filter bots - send to everyone!
				player.PrintToChat(message);
				playerCount++;
			}
			Logger.LogInformation($"[Webhook] Broadcast sent to {playerCount} players");
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] BroadcastCaseOpening error: {ex.Message}");
		}
	}

	private string GetRarityColor(string? rarity)
	{
		return rarity?.ToLower() switch
		{
			"contraband" => "\x0B",    // Yellow/Gold
			"covert" => "\x10",         // Red
			"classified" => "\x0E",     // Pink/Magenta
			"restricted" => "\x0D",     // Purple
			"mil-spec" => "\x09",       // Blue
			"industrial" => "\x0C",     // Light Blue
			"consumer" => "\x01",       // White
			_ => "\x0A"                 // Gold (default for special items)
		};
	}

	private class RefreshInventoryRequest
	{
		public string? SteamId { get; set; }
	}

	private class CaseOpenedRequest
	{
		public string? PlayerName { get; set; }
		public string? ItemName { get; set; }
		public string? Rarity { get; set; }
		public bool StatTrak { get; set; }
	}

	public void StopWebhookListener()
	{
		_webhookRunning = false;
		_webhookListener?.Stop();
		_webhookListener = null;
		_webhookThread = null;
	}
}
