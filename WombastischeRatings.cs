using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WombastischeRatings
{
    [MinimumApiVersion(100)]
    public class WombastischeRatingsPlugin : BasePlugin
    {
        public override string ModuleName => "Wombastische Ratings";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "wombastisch";
        public override string ModuleDescription => "ELO-based rating system for CS2";

        private Dictionary<string, PlayerStats> _playerStats = new();
        private string _jsonFilePath = "";
        private bool _roundInProgress = false;
        private Dictionary<int, string> _teamTerrorists = new();
        private Dictionary<int, string> _teamCounterTerrorists = new();
        private const int DEFAULT_ELO = 1000;
        private const float K_FACTOR = 30f;
        private CounterStrikeSharp.API.Modules.Timers.Timer? _autoSaveTimer;

        public override void Load(bool hotReload)
        {
            string? pluginDirectory = Path.GetDirectoryName(ModuleDirectory);
            _jsonFilePath = pluginDirectory != null
                ? Path.Combine(pluginDirectory, "wombastische_ratings.json")
                : "wombastische_ratings.json";

            LoadRatingsFromJson();

            RegisterEventHandler<EventRoundStart>(OnRoundStart);
            RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
            RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnect);
            RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
            RegisterEventHandler<EventGameEnd>(OnGameEnd);

            AddCommand("css_elo", "Show your current rating", CommandElo);
            AddCommand("css_top", "Show top players by rating", CommandTop);

            _autoSaveTimer = AddTimer(300.0f, () => SaveRatingsToJson(), TimerFlags.REPEAT);
        }

        public override void Unload(bool hotReload)
        {
            SaveRatingsToJson();
            _autoSaveTimer = null;
            base.Unload(hotReload);
        }

        private HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player == null || !player.IsValid) return HookResult.Continue;

            string steamId = player.SteamID.ToString();
            string playerName = player.PlayerName;
            LoadPlayerStats(steamId);

            if (_playerStats.TryGetValue(steamId, out var stats))
            {
                stats.PlayerName = playerName;
                _playerStats[steamId] = stats;
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            SaveRatingsToJson();
            return HookResult.Continue;
        }

        private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
        {
            _roundInProgress = true;
            _teamTerrorists.Clear();
            _teamCounterTerrorists.Clear();

            foreach (var player in Utilities.GetPlayers())
            {
                if (!player.IsValid || player.IsBot) continue;
                int? userId = player.UserId;
                if (!userId.HasValue) continue;

                string steamId = player.SteamID.ToString();
                int team = player.TeamNum;

                if (_playerStats.TryGetValue(steamId, out var stats))
                {
                    stats.PlayerName = player.PlayerName;
                    _playerStats[steamId] = stats;
                }

                if (team == 2) _teamTerrorists[userId.Value] = steamId;
                else if (team == 3) _teamCounterTerrorists[userId.Value] = steamId;
            }

            return HookResult.Continue;
        }

        private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            if (!_roundInProgress) return HookResult.Continue;
            _roundInProgress = false;

            int? winnerTeam = @event.Winner;
            if (winnerTeam.HasValue && winnerTeam.Value != 0)
            {
                int winner = winnerTeam.Value;
                float tAvgElo = CalculateAverageElo(_teamTerrorists.Values.ToList());
                float ctAvgElo = CalculateAverageElo(_teamCounterTerrorists.Values.ToList());

                if (winner == 2)
                {
                    UpdateTeamElo(_teamTerrorists.Values.ToList(), _teamCounterTerrorists.Values.ToList(), tAvgElo, ctAvgElo, true);
                    Server.PrintToChatAll($" \u0004[WR]\u0001 Terrorists win! Avg Rating: T={Math.Round(tAvgElo)} vs CT={Math.Round(ctAvgElo)}");
                }
                else if (winner == 3)
                {
                    UpdateTeamElo(_teamCounterTerrorists.Values.ToList(), _teamTerrorists.Values.ToList(), ctAvgElo, tAvgElo, true);
                    Server.PrintToChatAll($" \u0004[WR]\u0001 Counter-Terrorists win! Avg Rating: CT={Math.Round(ctAvgElo)} vs T={Math.Round(tAvgElo)}");
                }

                SaveRatingsToJson();
            }

            return HookResult.Continue;
        }

        private HookResult OnGameEnd(EventGameEnd @event, GameEventInfo info)
        {
            SaveRatingsToJson();
            return HookResult.Continue;
        }

        private void CommandElo(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;
            string steamId = player.SteamID.ToString();

            if (_playerStats.TryGetValue(steamId, out var stats))
            {
                player.PrintToChat($" \u0004[WR]\u0001 Your current rating: {Math.Round(stats.Elo)}");
                player.PrintToChat($" \u0004[WR]\u0001 Wins: {stats.Wins} | Losses: {stats.Losses}");
            }
            else
            {
                player.PrintToChat($" \u0004[WR]\u0001 No rating data found. Play some rounds!");
            }
        }

        private void CommandTop(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            var topPlayers = _playerStats.OrderByDescending(x => x.Value.Elo).Take(5).ToList();

            player.PrintToChat($" \u0004[WR]\u0001 Top 5 Players by Rating:");
            for (int i = 0; i < topPlayers.Count; i++)
            {
                string name = !string.IsNullOrEmpty(topPlayers[i].Value.PlayerName)
                            ? topPlayers[i].Value.PlayerName
                            : GetPlayerNameBySteamId(topPlayers[i].Key);

                player.PrintToChat($" \u0004[WR]\u0001 {i + 1}. {name}: {Math.Round(topPlayers[i].Value.Elo)}");
            }
        }

        private string GetPlayerNameBySteamId(string steamId)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (!player.IsValid) continue;
                if (player.SteamID.ToString() == steamId)
                    return player.PlayerName;
            }
            return "Unknown Player";
        }

        private float CalculateAverageElo(List<string> steamIds)
        {
            if (steamIds.Count == 0) return DEFAULT_ELO;
            float totalElo = steamIds.Sum(id => _playerStats.TryGetValue(id, out var stats) ? stats.Elo : DEFAULT_ELO);
            return totalElo / steamIds.Count;
        }

        private void UpdateTeamElo(List<string> winnerTeam, List<string> loserTeam, float winnerAvgElo, float loserAvgElo, bool isRoundEnd)
        {
            float expectedWinProbability = 1.0f / (1.0f + (float)Math.Pow(10, (loserAvgElo - winnerAvgElo) / 400.0f));
            float eloChange = K_FACTOR * (1 - expectedWinProbability);

            foreach (var steamId in winnerTeam)
            {
                if (!_playerStats.TryGetValue(steamId, out var stats))
                {
                    stats = new PlayerStats { SteamId = steamId, Elo = DEFAULT_ELO };
                    var playerOnline = GetPlayerBySteamId(steamId);
                    if (playerOnline?.IsValid == true) stats.PlayerName = playerOnline.PlayerName;
                    _playerStats[steamId] = stats;
                }

                float playerEloFactor = Math.Clamp(1.0f + (loserAvgElo - stats.Elo) / 1000.0f, 0.5f, 1.5f);
                float oldElo = stats.Elo;
                stats.Elo += eloChange * playerEloFactor;
                stats.Wins++;
                _playerStats[steamId] = stats;

                var playerObj = GetPlayerBySteamId(steamId);
                if (playerObj?.IsValid == true)
                    playerObj.PrintToChat($" \u0004[WR]\u0001 +{Math.Round(stats.Elo - oldElo)} Rating ({Math.Round(oldElo)} → {Math.Round(stats.Elo)})");
            }

            foreach (var steamId in loserTeam)
            {
                if (!_playerStats.TryGetValue(steamId, out var stats))
                {
                    stats = new PlayerStats { SteamId = steamId, Elo = DEFAULT_ELO };
                    var playerOnline = GetPlayerBySteamId(steamId);
                    if (playerOnline?.IsValid == true) stats.PlayerName = playerOnline.PlayerName;
                    _playerStats[steamId] = stats;
                }

                float playerEloFactor = Math.Clamp(1.0f + (stats.Elo - winnerAvgElo) / 1000.0f, 0.5f, 1.5f);
                float oldElo = stats.Elo;
                stats.Elo -= eloChange * playerEloFactor;
                stats.Losses++;
                _playerStats[steamId] = stats;

                var playerObj = GetPlayerBySteamId(steamId);
                if (playerObj?.IsValid == true)
                    playerObj.PrintToChat($" \u0004[WR]\u0001 -{Math.Round(oldElo - stats.Elo)} Rating ({Math.Round(oldElo)} → {Math.Round(stats.Elo)})");
            }
        }

        private CCSPlayerController? GetPlayerBySteamId(string steamId)
        {
            return Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.SteamID.ToString() == steamId);
        }

        private void LoadPlayerStats(string steamId)
        {
            if (!_playerStats.ContainsKey(steamId))
            {
                _playerStats[steamId] = new PlayerStats
                {
                    SteamId = steamId,
                    Elo = DEFAULT_ELO,
                    Wins = 0,
                    Losses = 0,
                    PlayerName = ""
                };
            }
        }

        private void LoadRatingsFromJson()
        {
            try
            {
                if (File.Exists(_jsonFilePath))
                {
                    string json = File.ReadAllText(_jsonFilePath);
                    var playerStatsList = JsonSerializer.Deserialize<List<PlayerStats>>(json);
                    if (playerStatsList != null)
                    {
                        _playerStats.Clear();
                        foreach (var stats in playerStatsList)
                            _playerStats[stats.SteamId] = stats;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ratings: {ex.Message}");
            }
        }

        private void SaveRatingsToJson()
        {
            try
            {
                var playerStatsList = _playerStats.Values.ToList();
                string json = JsonSerializer.Serialize(playerStatsList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_jsonFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ratings: {ex.Message}");
            }
        }
    }

    public class PlayerStats
    {
        public string SteamId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public float Elo { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }
}
