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
		if (context.Request.HttpMethod != "POST" || context.Request.Url?.AbsolutePath != "/api/plugin/refresh-inventory")
		{
			context.Response.StatusCode = 404;
			context.Response.Close();
			return;
		}
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

	private class RefreshInventoryRequest
	{
		public string? SteamId { get; set; }
	}

	public void StopWebhookListener()
	{
		_webhookRunning = false;
		_webhookListener?.Stop();
		_webhookListener = null;
		_webhookThread = null;
	}
}
