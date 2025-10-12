# Coin System Setup for CS2 Inventory Simulator Plugin

## Overview
This coin system has been added to the CS2 Inventory Simulator Plugin, allowing players to earn coins for kills, round wins, and MVP awards. The system uses PostgreSQL for data persistence.

## Features
- **Kill Rewards**: Players earn coins for each kill
- **Bonus Rewards**: Extra coins for headshots, knife kills, and grenade kills
- **Round Win Rewards**: Team members earn coins when their team wins a round
- **MVP Rewards**: MVP players get additional coin bonuses
- **Statistics Tracking**: Tracks kills, deaths, round wins, and MVP count
- **Admin Commands**: Server admins can manage player coins
- **Database Persistence**: All data is stored in PostgreSQL

## Database Setup

### 1. PostgreSQL Installation
Make sure you have PostgreSQL installed and running on your server.

### 2. Database Creation
```sql
CREATE DATABASE cs2_inventory;
```

### 3. User Setup (Optional)
```sql
CREATE USER cs2_user WITH PASSWORD 'your_password';
GRANT ALL PRIVILEGES ON DATABASE cs2_inventory TO cs2_user;
```

### 4. Table Creation
The plugin will automatically create the required table on first run:
```sql
CREATE TABLE IF NOT EXISTS player_coins (
    steam_id BIGINT PRIMARY KEY,
    coins INTEGER NOT NULL DEFAULT 0,
    total_kills INTEGER NOT NULL DEFAULT 0,
    total_deaths INTEGER NOT NULL DEFAULT 0,
    round_wins INTEGER NOT NULL DEFAULT 0,
    mvp_count INTEGER NOT NULL DEFAULT 0,
    last_updated TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
);
```

## Configuration

### Config File Location
The coin system configuration file `coinsystem_config.json` will be created in the plugin directory:
```
csgo/addons/counterstrikesharp/plugins/InventorySimulator/coinsystem_config.json
```

### Configuration Options
```json
{
  "database": {
    "connectionString": "Host=localhost;Database=cs2_inventory;Username=postgres;Password=password",
    "tableName": "player_coins"
  },
  "rewards": {
    "kill": 10,
    "roundWin": 50,
    "mvp": 100,
    "headshot": 5,
    "knife": 25,
    "grenade": 15
  },
  "settings": {
    "enableCoinSystem": true,
    "enableKillRewards": true,
    "enableRoundWinRewards": true,
    "enableMvpRewards": true,
    "enableBonus": true,
    "announceRewards": true,
    "saveInterval": 30
  }
}
```

### Configuration Explanation

#### Database Settings
- `connectionString`: PostgreSQL connection string
- `tableName`: Name of the table to store coin data

#### Reward Settings
- `kill`: Base coins awarded per kill
- `roundWin`: Coins awarded to each team member when their team wins
- `mvp`: Additional coins awarded to MVP player
- `headshot`: Bonus coins for headshot kills
- `knife`: Bonus coins for knife kills
- `grenade`: Bonus coins for grenade kills

#### System Settings
- `enableCoinSystem`: Master switch for the entire coin system
- `enableKillRewards`: Enable/disable kill rewards
- `enableRoundWinRewards`: Enable/disable round win rewards
- `enableMvpRewards`: Enable/disable MVP rewards
- `enableBonus`: Enable/disable bonus rewards (headshot, knife, grenade)
- `announceRewards`: Show coin reward messages to players
- `saveInterval`: How often to save data to database (in seconds)

## Player Commands

### Basic Commands
- `!coins` or `!balance` - Check your coin balance and stats
- `!top` or `!topcoins` - Show top players by coins (online players only)
- `!coinhelp` - Show help message with available commands

## Admin Commands

### Coin Management (Requires @css/admin permission)
- `!givecoins <player> <amount>` - Give coins to a player
- `!setcoins <player> <amount>` - Set a player's coin balance
- `!removecoins <player> <amount>` - Remove coins from a player

### Usage Examples
```
!givecoins Snoopy 1000
!setcoins "Player Name" 500
!removecoins Snoopy 250
```

## Installation

1. Build the plugin with the updated code
2. Copy the plugin files to your CounterStrikeSharp plugins directory
3. Configure your PostgreSQL connection in `coinsystem_config.json`
4. Restart your server
5. The plugin will automatically create the database table on first run

## Language Support

The coin system includes localized messages in the language files:
- English (`en.json`) - Fully supported
- Portuguese (`pt-BR.json`) - Can be extended
- Chinese (`zh-Hans.json`) - Can be extended

## Troubleshooting

### Common Issues

1. **Database Connection Failed**
   - Check your PostgreSQL connection string
   - Ensure PostgreSQL is running
   - Verify database permissions

2. **Plugin Not Loading**
   - Check server console for error messages
   - Ensure all dependencies (Npgsql, Newtonsoft.Json) are available
   - Verify configuration file syntax

3. **Coins Not Saving**
   - Check database permissions
   - Verify table exists and is accessible
   - Check server console for database errors

### Debug Information
The plugin logs important events to the server console:
- Coin system initialization
- Database connection status
- Admin actions (giving/setting/removing coins)
- Error messages for troubleshooting

## Performance Notes

- Player data is cached in memory for fast access
- Database saves occur at configurable intervals (default: 30 seconds)
- Player data is automatically saved when players disconnect
- The system is designed to handle multiple concurrent players efficiently

## Security Considerations

- Admin commands require proper CSS permissions
- Database credentials should be secured
- Consider using SSL connections for database communication in production
- Regular database backups are recommended

## Future Enhancements

The coin system is designed to be extensible. Potential future features:
- Shop system for spending coins
- Leaderboards and rankings
- Seasonal coin resets
- Integration with other plugins
- Web interface for coin management