using System.Net;
using System.Text.Json;
using System.Threading;
using CounterStrikeSharp.API.Core;

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
		if (context.Request.HttpMethod != "POST")
		{
			context.Response.StatusCode = 404;
			context.Response.Close();
			return;
		}

		var path = context.Request.Url?.AbsolutePath;

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

		context.Response.StatusCode = 404;
		context.Response.Close();
	}

	private void HandleRefreshInventory(HttpListenerContext context)
	{
		try
		{
			using var reader = new System.IO.StreamReader(context.Request.InputStream);
			var body = reader.ReadToEnd();
			var data = JsonSerializer.Deserialize<RefreshInventoryRequest>(body);
			if (data?.SteamId != null && ulong.TryParse(data.SteamId, out var steamId))
			{
				// Schedule the refresh on the main server thread
				CounterStrikeSharp.API.Server.NextFrame(() =>
				{
					var player = InventorySimulator.GetPlayerFromSteamId(steamId);
					if (player != null && player.IsValid)
					{
						RefreshPlayerInventory(player, true);
					}
				});
				context.Response.StatusCode = 200;
				context.Response.Close();
				return;
			}
			context.Response.StatusCode = 400;
			context.Response.Close();
		}
		catch
		{
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
			var data = JsonSerializer.Deserialize<CaseOpenedRequest>(body);

			if (data?.PlayerName != null && data?.ItemName != null)
			{
				// Schedule the broadcast on the main server thread
				CounterStrikeSharp.API.Server.NextFrame(() =>
				{
					BroadcastCaseOpening(data.PlayerName, data.ItemName, data.Rarity, data.StatTrak);
				});
				context.Response.StatusCode = 200;
				context.Response.Close();
				return;
			}
			context.Response.StatusCode = 400;
			context.Response.Close();
		}
		catch
		{
			context.Response.StatusCode = 500;
			context.Response.Close();
		}
	}

	private void BroadcastCaseOpening(string playerName, string itemName, string? rarity, bool statTrak)
	{
		// Build the message similar to CS2 case opening notifications
		var statTrakPrefix = statTrak ? "StatTrak™ " : "";
		var rarityColor = GetRarityColor(rarity);
		var message = $" {rarityColor}★ {playerName}\x01 unboxed a {rarityColor}{statTrakPrefix}{itemName}\x01!";

		// Broadcast to all players
		var players = GetPlayers();
		foreach (var player in players)
		{
			if (player != null && player.IsValid && !player.IsBot)
			{
				player.PrintToChat(message);
			}
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
