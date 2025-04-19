# Wombastische Ratings

A lightweight ELO-based rating system for CS2 servers using [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp).

## 📦 Features

- [x] Calculates ELO rating for each player
- [x] Tracks wins and losses
- [x] Auto-saves ratings to JSON file
- [x] Shows top 5 players via command
- [x] Clean in-game feedback after rounds
- [ ] In-game menu for ELO stats and leaderboard
- [ ] Optional database integration (MySQL/SQLite)
- [ ] Configurable rating mode: round-based or full match-based
- [ ] Simple Web interface for player ratings

## 🛠 Commands

| Command       | Description                     |
|---------------|---------------------------------|
| `!css_elo`    | Shows your current ELO rating   |
| `!css_top`    | Shows top 5 players by rating   |

## 📂 Data Storage

Player stats are stored in a local `wombastische_ratings.json` file inside the plugin directory.

## 📥 Installation

1. Install [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
2. Drop this plugin into your `cs2/addons/counterstrikesharp/plugins` folder
3. Restart your server

## 🔧 Config

No additional config is required. ELO starts at 1000 by default and adjusts after each round using a simplified ELO formula.

## 📃 License

MIT License – free to use, modify, and share.  
See [`LICENSE`](LICENSE) for more info.
