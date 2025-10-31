using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace InventorySimulator;

public partial class InventorySimulator
{
	private TcpListener? _webhookListener;
	private Thread? _webhookThread;
	private bool _webhookRunning = false;

	public void StartWebhookListener(int port = 5005)
	{
		if (_webhookRunning)
		{
			Logger.LogInformation("[Webhook] Listener already running");
			return;
		}

		try
		{
			_webhookListener = new TcpListener(IPAddress.Any, port);
			_webhookListener.Start();
			_webhookRunning = true;
			_webhookThread = new Thread(WebhookListenLoop) { IsBackground = true };
			_webhookThread.Start();
			Logger.LogInformation($"[Webhook] Cross-platform HTTP listener started on port {port}");
			Console.WriteLine($"[Webhook] Cross-platform HTTP listener started on port {port}");
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] Failed to start listener: {ex.Message}");
			Console.WriteLine($"[Webhook] Failed to start listener: {ex.Message}");
		}
	}

	private void WebhookListenLoop()
	{
		while (_webhookRunning && _webhookListener != null)
		{
			try
			{
				var client = _webhookListener.AcceptTcpClient();
				ThreadPool.QueueUserWorkItem(_ => HandleTcpClient(client));
			}
			catch (Exception ex)
			{
				if (_webhookRunning)
				{
					Logger.LogError($"[Webhook] Listen loop error: {ex.Message}");
				}
			}
		}
	}

	private void HandleTcpClient(TcpClient client)
	{
		try
		{
			using (client)
			using (var stream = client.GetStream())
			{
				var buffer = new byte[8192];
				var bytesRead = stream.Read(buffer, 0, buffer.Length);
				var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

				// Parse HTTP request
				var lines = request.Split('\n');
				if (lines.Length == 0)
				{
					SendResponse(stream, 400, "Bad Request");
					return;
				}

				var requestLine = lines[0].Split(' ');
				if (requestLine.Length < 3)
				{
					SendResponse(stream, 400, "Bad Request");
					return;
				}

				var method = requestLine[0];
				var path = requestLine[1];

				Logger.LogInformation($"[Webhook] Received {method} {path}");

				// Handle OPTIONS preflight
				if (method == "OPTIONS")
				{
					SendCorsResponse(stream, 200, "OK");
					return;
				}

				// Only accept POST
				if (method != "POST")
				{
					SendCorsResponse(stream, 405, "Method Not Allowed");
					return;
				}

				// Extract body
				var bodyIndex = request.IndexOf("\r\n\r\n");
				if (bodyIndex == -1)
				{
					bodyIndex = request.IndexOf("\n\n");
				}

				string body = "";
				if (bodyIndex != -1)
				{
					body = request.Substring(bodyIndex).Trim();
				}

				Logger.LogInformation($"[Webhook] Body: {body}");

				// Route to handlers
				if (path.StartsWith("/api/plugin/refresh-inventory"))
				{
					HandleRefreshInventoryTcp(stream, body);
				}
				else if (path.StartsWith("/api/plugin/case-opened"))
				{
					HandleCaseOpenedTcp(stream, body);
				}
				else
				{
					Logger.LogWarning($"[Webhook] Unknown endpoint: {path}");
					SendCorsResponse(stream, 404, "Not Found");
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] HandleTcpClient error: {ex.Message}");
		}
	}

	private void HandleRefreshInventoryTcp(NetworkStream stream, string body)
	{
		try
		{
			if (string.IsNullOrEmpty(body))
			{
				SendCorsResponse(stream, 400, "Empty body");
				return;
			}

			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var data = JsonSerializer.Deserialize<RefreshInventoryRequest>(body, options);

			if (data?.SteamId != null && ulong.TryParse(data.SteamId, out var steamId))
			{
				Logger.LogInformation($"[Webhook] Refreshing inventory for SteamID: {steamId}");

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

				SendCorsResponse(stream, 200, "OK");
				return;
			}

			Logger.LogWarning($"[Webhook] Invalid RefreshInventory request data");
			SendCorsResponse(stream, 400, "Invalid data");
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] RefreshInventory error: {ex.Message}");
			SendCorsResponse(stream, 500, "Internal Server Error");
		}
	}

	private void HandleCaseOpenedTcp(NetworkStream stream, string body)
	{
		try
		{
			if (string.IsNullOrEmpty(body))
			{
				SendCorsResponse(stream, 400, "Empty body");
				return;
			}

			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var data = JsonSerializer.Deserialize<CaseOpenedRequest>(body, options);

			if (data?.PlayerName != null && data?.ItemName != null)
			{
				Logger.LogInformation($"[Webhook] Broadcasting case opening: {data.PlayerName} - {data.ItemName}");

				Task.Delay(5000).ContinueWith(_ =>
				{
					CounterStrikeSharp.API.Server.NextFrame(() =>
					{
						BroadcastCaseOpening(data.PlayerName, data.ItemName, data.Rarity, data.StatTrak);
					});
				});

				SendCorsResponse(stream, 200, "OK");
				return;
			}

			Logger.LogWarning($"[Webhook] Invalid CaseOpened request data");
			SendCorsResponse(stream, 400, "Invalid data");
		}
		catch (Exception ex)
		{
			Logger.LogError($"[Webhook] CaseOpened error: {ex.Message}");
			SendCorsResponse(stream, 500, "Internal Server Error");
		}
	}

	private void SendResponse(NetworkStream stream, int statusCode, string statusMessage)
	{
		var response = $"HTTP/1.1 {statusCode} {statusMessage}\r\n" +
		               $"Content-Length: 0\r\n" +
		               $"Connection: close\r\n\r\n";

		var bytes = Encoding.UTF8.GetBytes(response);
		stream.Write(bytes, 0, bytes.Length);
	}

	private void SendCorsResponse(NetworkStream stream, int statusCode, string statusMessage)
	{
		var response = $"HTTP/1.1 {statusCode} {statusMessage}\r\n" +
		               $"Access-Control-Allow-Origin: *\r\n" +
		               $"Access-Control-Allow-Methods: POST, OPTIONS\r\n" +
		               $"Access-Control-Allow-Headers: Content-Type\r\n" +
		               $"Content-Length: 0\r\n" +
		               $"Connection: close\r\n\r\n";

		var bytes = Encoding.UTF8.GetBytes(response);
		stream.Write(bytes, 0, bytes.Length);
	}

	private void BroadcastCaseOpening(string playerName, string itemName, string? rarity, bool statTrak)
	{
		try
		{
			var statTrakPrefix = statTrak ? "StatTrak™ " : "";
			var rarityColor = GetRarityColor(rarity);
			var message = $" {rarityColor}★ {playerName}\x01 unboxed a {rarityColor}{statTrakPrefix}{itemName}\x01!";

			Logger.LogInformation($"[Webhook] Broadcasting message: {message}");

			var players = CounterStrikeSharp.API.Utilities.GetPlayers().ToList();
			Logger.LogInformation($"[Webhook] Found {players.Count} total players");

			var playerCount = 0;
			foreach (var player in players)
			{
				if (player == null || !player.IsValid) continue;

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
			"contraband" => "\x0B",
			"covert" => "\x10",
			"classified" => "\x0E",
			"restricted" => "\x0D",
			"mil-spec" => "\x09",
			"industrial" => "\x0C",
			"consumer" => "\x01",
			_ => "\x0A"
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
		Logger.LogInformation("[Webhook] Listener stopped");
	}
}
