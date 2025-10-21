using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using Npgsql;

namespace InventorySimulator;

public class SimpleCoinSystem : IDisposable
{
    private readonly ConcurrentDictionary<ulong, decimal> _playerCoins = new();
    private readonly SimpleCoinConfig _config;
    private readonly Timer? _saveTimer;
    private readonly string? _connectionString;
    private readonly IStringLocalizer _localizer;
    private bool _databaseAvailable = false;

    public SimpleCoinSystem(string configPath, IStringLocalizer localizer)
    {
        _localizer = localizer;
        _config = LoadConfig(configPath);
        
        if (_config.Database.Enabled && !string.IsNullOrEmpty(_config.Database.ConnectionString))
        {
            _connectionString = _config.Database.ConnectionString;
            
            // Próbáljuk meg inicializálni az adatbázist
            try
            {
                InitializeDatabase();
                LoadAllPlayerData();
                _databaseAvailable = true;
                
                // Périodique mentés timer (30 másodpercenként)
                _saveTimer = new Timer(SaveAllPlayerData, null, TimeSpan.FromSeconds(_config.Settings.SaveInterval), 
                                      TimeSpan.FromSeconds(_config.Settings.SaveInterval));
                Console.WriteLine("[SimpleCoinSystem] Database mode enabled successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimpleCoinSystem] Database connection failed: {ex.Message}");
                Console.WriteLine($"[SimpleCoinSystem] Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[SimpleCoinSystem] Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine("[SimpleCoinSystem] Falling back to memory-only mode.");
                _databaseAvailable = false;
            }
        }
        else
        {
            Console.WriteLine("[SimpleCoinSystem] Database disabled in config, using memory-only mode.");
        }
        
        Console.WriteLine($"[SimpleCoinSystem] Initialized successfully! Mode: {(_databaseAvailable ? "Database" : "Memory-only")}");
    }

    private SimpleCoinConfig LoadConfig(string configPath)
    {
        try
        {
            Console.WriteLine($"[SimpleCoinSystem] Checking config file existence: {configPath}");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                Console.WriteLine($"[SimpleCoinSystem] Config file content length: {json.Length}");
                var config = JsonSerializer.Deserialize<SimpleCoinConfig>(json);
                return config ?? new SimpleCoinConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimpleCoinSystem] Error loading config: {ex.Message}");
        }

        Console.WriteLine("[SimpleCoinSystem] Using default config.");
        return new SimpleCoinConfig();
    }

    private void InitializeDatabase()
    {
        if (_connectionString == null) return;
        
        try
        {
            Console.WriteLine("[SimpleCoinSystem] Testing database connection...");
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            
            Console.WriteLine($"[SimpleCoinSystem] Connected successfully! Database: {connection.Database}");
            Console.WriteLine($"[SimpleCoinSystem] Server version: {connection.ServerVersion}");
            
            // Ellenőrizzük hogy létezik-e a User tábla
            var checkUserTableQuery = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.tables 
                    WHERE table_schema = 'public' 
                    AND table_name = 'User'
                )";
            
            using var checkCommand = new NpgsqlCommand(checkUserTableQuery, connection);
            var userTableExists = (bool)checkCommand.ExecuteScalar()!;
            
            Console.WriteLine($"[SimpleCoinSystem] User table exists: {userTableExists}");
            
            if (userTableExists)
            {
                Console.WriteLine("[SimpleCoinSystem] User table found, checking for coins column...");
                
                // Ellenőrizzük hogy létezik-e a coins oszlop a User táblában
                var checkCoinsColumnQuery = @"
                    SELECT EXISTS (
                        SELECT FROM information_schema.columns 
                        WHERE table_name = 'User' 
                        AND column_name = 'coins'
                    )";
                
                using var checkColumnCommand = new NpgsqlCommand(checkCoinsColumnQuery, connection);
                var coinsColumnExists = (bool)checkColumnCommand.ExecuteScalar()!;
                
                Console.WriteLine($"[SimpleCoinSystem] Coins column exists: {coinsColumnExists}");
                
                if (!coinsColumnExists)
                {
                    Console.WriteLine("[SimpleCoinSystem] Adding coins column to User table...");
                    var addColumnQuery = "ALTER TABLE \"User\" ADD COLUMN coins DECIMAL(10,2) DEFAULT 0.00";
                    using var addColumnCommand = new NpgsqlCommand(addColumnQuery, connection);
                    addColumnCommand.ExecuteNonQuery();
                    Console.WriteLine("[SimpleCoinSystem] Coins column added successfully.");
                }
                else
                {
                    Console.WriteLine("[SimpleCoinSystem] Coins column already exists.");
                    
                    // Ellenőrizzük a coins oszlop típusát
                    var checkColumnTypeQuery = @"
                        SELECT data_type 
                        FROM information_schema.columns 
                        WHERE table_name = 'User' 
                        AND column_name = 'coins'";
                    
                    using var checkTypeCommand = new NpgsqlCommand(checkColumnTypeQuery, connection);
                    var columnType = checkTypeCommand.ExecuteScalar()?.ToString();
                    
                    Console.WriteLine($"[SimpleCoinSystem] Current coins column type: {columnType}");
                    
                    // Ha INTEGER típusú, akkor módosítsuk DECIMAL típusra
                    if (columnType == "integer")
                    {
                        Console.WriteLine("[SimpleCoinSystem] Converting coins column from INTEGER to DECIMAL...");
                        var alterColumnQuery = "ALTER TABLE \"User\" ALTER COLUMN coins TYPE DECIMAL(10,2)";
                        using var alterCommand = new NpgsqlCommand(alterColumnQuery, connection);
                        alterCommand.ExecuteNonQuery();
                        Console.WriteLine("[SimpleCoinSystem] Coins column converted to DECIMAL successfully.");
                    }
                }
            }
            else
            {
                Console.WriteLine("[SimpleCoinSystem] User table not found, creating player_coins table...");
                // Ha nincs User tábla, hozza létre a player_coins táblát
                var createTableQuery = $@"
                    CREATE TABLE IF NOT EXISTS {_config.Database.TableName} (
                        steam_id BIGINT PRIMARY KEY,
                        coins DECIMAL(10,2) NOT NULL DEFAULT 0.00,
                        kills INTEGER NOT NULL DEFAULT 0,
                        deaths INTEGER NOT NULL DEFAULT 0,
                        round_wins INTEGER NOT NULL DEFAULT 0,
                        mvp_count INTEGER NOT NULL DEFAULT 0,
                        last_updated TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
                    )";
                
                using var createCommand = new NpgsqlCommand(createTableQuery, connection);
                createCommand.ExecuteNonQuery();
            }
            
            Console.WriteLine("[SimpleCoinSystem] Database initialization completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimpleCoinSystem] Database initialization failed: {ex.Message}");
            Console.WriteLine($"[SimpleCoinSystem] Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[SimpleCoinSystem] Inner exception: {ex.InnerException.Message}");
            }
            Console.WriteLine($"[SimpleCoinSystem] Will continue in memory-only mode.");
            throw; // Re-throw hogy a hívó is lássa a hibát
        }
    }

    private void LoadAllPlayerData()
    {
        if (_connectionString == null) return;
        
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            
            // Először próbáljuk a User táblából
            var userTableQuery = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.columns 
                    WHERE table_name = 'User' 
                    AND column_name = 'coins'
                )";
            
            using var checkCommand = new NpgsqlCommand(userTableQuery, connection);
            var useUserTable = (bool)checkCommand.ExecuteScalar()!;
            
            string query;
            if (useUserTable)
            {
                query = "SELECT id::BIGINT, COALESCE(coins, 0) FROM \"User\" WHERE coins IS NOT NULL";
                Console.WriteLine("[SimpleCoinSystem] Loading data from User table...");
            }
            else
            {
                query = $"SELECT steam_id, coins FROM {_config.Database.TableName}";
                Console.WriteLine("[SimpleCoinSystem] Loading data from player_coins table...");
            }
            
            using var command = new NpgsqlCommand(query, connection);
            using var reader = command.ExecuteReader();
            
            while (reader.Read())
            {
                var steamId = reader.GetInt64(0);
                var coins = reader.GetInt32(1);
                
                // CRITICAL FIX: Ne írjuk felül a meglévő értékeket!
                // Csak akkor töltsük be, ha még nincs a memóriában
                if (!_playerCoins.ContainsKey((ulong)steamId))
                {
                    _playerCoins.TryAdd((ulong)steamId, coins);
                }
                else
                {
                    // Ha már van érték a memóriában, akkor az az aktuális
                    Console.WriteLine($"[SimpleCoinSystem] Player {steamId} already in memory, keeping current value: €{_playerCoins[(ulong)steamId]:F2}");
                }
            }
            
            Console.WriteLine($"[SimpleCoinSystem] Loaded {_playerCoins.Count} player records from database.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimpleCoinSystem] Failed to load player data: {ex.Message}");
            Console.WriteLine("[SimpleCoinSystem] Continuing in memory-only mode.");
        }
    }

    private void SavePlayerData(ulong steamId, decimal coins)
    {
        if (_connectionString == null || !_databaseAvailable) return;
        
        Task.Run(async () =>
        {
            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                
                // Ellenőrizzük melyik táblát használjuk
                var checkUserTableQuery = @"
                    SELECT EXISTS (
                        SELECT FROM information_schema.columns 
                        WHERE table_name = 'User' 
                        AND column_name = 'coins'
                    )";
                
                using var checkCommand = new NpgsqlCommand(checkUserTableQuery, connection);
                var useUserTable = (bool)(await checkCommand.ExecuteScalarAsync())!;
                
                string query;
                if (useUserTable)
                {
                    // User táblába mentés - először ellenőrizzük hogy létezik-e a user
                    var userExistsQuery = "SELECT EXISTS(SELECT 1 FROM \"User\" WHERE id = @steamId)";
                    using var existsCommand = new NpgsqlCommand(userExistsQuery, connection);
                    existsCommand.Parameters.AddWithValue("steamId", steamId.ToString());
                    var userExists = (bool)(await existsCommand.ExecuteScalarAsync())!;
                    
                    if (userExists)
                    {
                        query = "UPDATE \"User\" SET coins = @coins WHERE id = @steamId";
                    }
                    else
                    {
                        // Ha nincs user rekord, hozzuk létre alapértelmezett értékekkel
                        query = @"INSERT INTO ""User"" (id, coins, name, avatar, inventory, ""createdAt"", ""updatedAt"", ""syncedAt"") 
                                 VALUES (@steamId, @coins, 'Unknown', '', '{}', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)";
                    }
                    
                    using var command = new NpgsqlCommand(query, connection);
                    command.Parameters.AddWithValue("steamId", steamId.ToString());
                    command.Parameters.AddWithValue("coins", coins);
                    
                    await command.ExecuteNonQueryAsync();
                }
                else
                {
                    // player_coins táblába mentés
                    query = $@"
                        INSERT INTO {_config.Database.TableName} (steam_id, coins, last_updated) 
                        VALUES (@steamId, @coins, CURRENT_TIMESTAMP)
                        ON CONFLICT (steam_id) 
                        DO UPDATE SET coins = @coins, last_updated = CURRENT_TIMESTAMP";
                    
                    using var command = new NpgsqlCommand(query, connection);
                    command.Parameters.AddWithValue("steamId", (long)steamId);
                    command.Parameters.AddWithValue("coins", coins);
                    
                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimpleCoinSystem] Failed to save player {steamId}: {ex.Message}");
                // Ha a mentés sikertelen, jelöljük az adatbázist elérhetetlen ként
                _databaseAvailable = false;
            }
        });
    }

    private void SaveAllPlayerData(object? state)
    {
        if (_connectionString == null || !_databaseAvailable) return;
        
        // CRITICAL FIX: Először frissítsük az adatokat az adatbázisból
        // hogy ne írjuk felül a webalkalmazás által végzett változtatásokat
        RefreshFromDatabase();
        
        // Csak akkor mentsük a memória tartalmát, ha biztosan friss
        foreach (var kvp in _playerCoins)
        {
            SavePlayerData(kvp.Key, kvp.Value);
        }
    }

    private void RefreshFromDatabase()
    {
        if (_connectionString == null) return;
        
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            
            // Ellenőrizzük, hogy melyik táblát használjuk
            var userTableQuery = @"
                SELECT EXISTS (
                    SELECT FROM information_schema.columns 
                    WHERE table_name = 'User' 
                    AND column_name = 'coins'
                )";
            
            using var checkCommand = new NpgsqlCommand(userTableQuery, connection);
            var useUserTable = (bool)checkCommand.ExecuteScalar()!;
            
            string query;
            if (useUserTable)
            {
                query = "SELECT id::BIGINT, COALESCE(coins, 0) FROM \"User\" WHERE coins IS NOT NULL";
            }
            else
            {
                query = $"SELECT steam_id, coins FROM {_config.Database.TableName}";
            }
            
            using var command = new NpgsqlCommand(query, connection);
            using var reader = command.ExecuteReader();
            
            var dbValues = new Dictionary<ulong, decimal>();
            while (reader.Read())
            {
                var steamId = reader.GetInt64(0);
                var coins = reader.GetInt32(1);
                dbValues[(ulong)steamId] = coins;
            }
            
            // Frissítsük a memória értékeket az adatbázis értékeivel
            foreach (var kvp in dbValues)
            {
                _playerCoins.AddOrUpdate(kvp.Key, kvp.Value, (key, current) => kvp.Value);
            }
            
            Console.WriteLine($"[SimpleCoinSystem] Refreshed {dbValues.Count} player records from database.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimpleCoinSystem] Failed to refresh from database: {ex.Message}");
        }
    }


    public void AddKillReward(CCSPlayerController player, bool isHeadshot = false)
    {
        if (!_config.Settings.EnableKillRewards || player?.SteamID == null) return;

        var steamId = player.SteamID;

        // Random összeg generálása
        var random = new Random();
        double reward;

        if (isHeadshot)
        {
            // Headshot: 0.3-0.5 euró között
            reward = 0.3 + (random.NextDouble() * 0.2); // 0.3 + (0.0-0.2)
        }
        else
        {
            // Sima ölés: 0.1-0.3 euró között
            reward = 0.1 + (random.NextDouble() * 0.2); // 0.1 + (0.0-0.2)
        }

        var newTotal = _playerCoins.AddOrUpdate(steamId, reward, (key, current) => current + reward);

        if (_config.Settings.AnnounceRewards)
        {
            var rewardType = isHeadshot ? "headshot" : "kill";
            player.PrintToChat($" \x04[€]\x01 +€{reward:F2} {rewardType}");
        }

        // Azonnali mentés adatbázisba
        SavePlayerData(steamId, newTotal);
        Console.WriteLine($"[SimpleCoinSystem] Player {player.PlayerName} earned €{reward:F2} for {(isHeadshot ? "headshot" : "kill")}. Total: €{newTotal:F2}");
    }

    public void AddRoundWinReward(CCSPlayerController player)
    {
        if (!_config.Settings.EnableRoundWinRewards || player?.SteamID == null) return;

        var steamId = player.SteamID;
        var newTotal = _playerCoins.AddOrUpdate(steamId, _config.Rewards.RoundWin, (key, current) => current + _config.Rewards.RoundWin);

        // Ne jelenítse meg a chatben
        // if (_config.Settings.AnnounceRewards)
        // {
        //     player.PrintToChat(_localizer["coins.round_win", $"{_config.Rewards.RoundWin:F2}", $"{newTotal:F2}"]);
        // }

        // Azonnali mentés adatbázisba
        SavePlayerData(steamId, newTotal);
        Console.WriteLine($"[SimpleCoinSystem] Player {player.PlayerName} earned €{_config.Rewards.RoundWin:F2} for round win. Total: €{newTotal:F2}");
    }

    public void AddRoundWinReward(List<CCSPlayerController> players)
    {
        foreach (var player in players)
        {
            AddRoundWinReward(player);
        }
    }

    public void AddMvpReward(CCSPlayerController player)
    {
        if (!_config.Settings.EnableMvpRewards || player?.SteamID == null) return;

        var steamId = player.SteamID;
        var newTotal = _playerCoins.AddOrUpdate(steamId, _config.Rewards.Mvp, (key, current) => current + _config.Rewards.Mvp);

        // Ne jelenítse meg a chatben
        // if (_config.Settings.AnnounceRewards)
        // {
        //     player.PrintToChat(_localizer["coins.mvp_reward", $"{_config.Rewards.Mvp:F2}", $"{newTotal:F2}"]);
        // }

        // Azonnali mentés adatbázisba
        SavePlayerData(steamId, newTotal);
        Console.WriteLine($"[SimpleCoinSystem] Player {player.PlayerName} earned €{_config.Rewards.Mvp:F2} for MVP. Total: €{newTotal:F2}");
    }

    public decimal GetPlayerCoins(ulong steamId)
    {
        return _playerCoins.GetValueOrDefault(steamId, 0m);
    }

    public void UpdatePlayerCoins(ulong steamId, decimal newAmount)
    {
        _playerCoins.AddOrUpdate(steamId, newAmount, (key, current) => newAmount);
        SavePlayerData(steamId, newAmount);
        Console.WriteLine($"[SimpleCoinSystem] Updated player {steamId} coins to {newAmount}");
    }

    public void Dispose()
    {
        // Mentés az összes pending adatról kilépéskor (csak ha adatbázis elérhető)
        if (_databaseAvailable)
        {
            SaveAllPlayerData(null);
        }
        _saveTimer?.Dispose();
        Console.WriteLine($"[SimpleCoinSystem] Disposed. Final mode: {(_databaseAvailable ? "Database" : "Memory-only")}");
    }
}

public class SimpleCoinConfig
{
    public SimpleCoinDatabase Database { get; set; } = new();
    public SimpleCoinRewards Rewards { get; set; } = new();
    public SimpleCoinSettings Settings { get; set; } = new();
}

public class SimpleCoinDatabase
{
    public string ConnectionString { get; set; } = "";
    public string TableName { get; set; } = "player_coins";
    public bool Enabled { get; set; } = true;
}

public class SimpleCoinRewards
{
    public decimal Kill { get; set; } = 0.20m;
    public decimal RoundWin { get; set; } = 1.00m;
    public decimal Mvp { get; set; } = 2.50m;
}

public class SimpleCoinSettings
{
    public bool EnableCoinSystem { get; set; } = true;
    public bool EnableKillRewards { get; set; } = true;
    public bool EnableRoundWinRewards { get; set; } = true;
    public bool EnableMvpRewards { get; set; } = true;
    public bool AnnounceRewards { get; set; } = true;
    public int SaveInterval { get; set; } = 30;
}