# Desert Strike

A small Counter-Strike-style shooter made in Unity 6.3 (built-in render pipeline, no extra packages or assets).
Terrorists vs SWAT, round-based, with a buy menu and bots, on **de_dune**, a desert map with a Dust-2-style layout
(outside long, long doors, long A, catwalk/short A, mid doors, upper and lower tunnels to B).

## Play

- **In Unity:** open this folder in Unity Hub, open `Assets/Scenes/DesertStrike.unity` (it opens automatically), press Play.
- **Without Unity:** run `Builds/Windows/DesertStrike.exe`. Rebuild it with the menu **Desert Strike > Build Windows Game**.

The main menu lets you pick your team (Terrorists, SWAT, or watch the bots), team size (1v1 to 5v5),
bot difficulty and the number of rounds to win.

## Controls

| Key | Action |
| --- | --- |
| WASD / Mouse | Move / aim |
| Left click / Right click | Shoot / scope (AWP), or switch semi/full-auto (TEC-DC9, M1911) |
| R | Reload |
| 1 / 2 / 3, Q, mouse wheel | Primary / pistol / knife, last weapon, cycle |
| Space / Ctrl / Shift | Jump / crouch / walk silently |
| B | Buy menu (first 25 s of a round) |
| Tab | Scoreboard |
| Esc | Pause |

## Shop

| Weapon | Team | Price | Ammo | Notes |
| --- | --- | --- | --- | --- |
| Glock-18 / USP | T / SWAT | $200 | 20/120, 12/100 | Starting pistols |
| TEC-DC9 | T | $500 | 12/24 | Right click: semi-auto or full-auto |
| M1911 | SWAT | $500 | 10/30 | Right click: semi-auto or full-auto |
| Desert Eagle | Both | $700 | 7/35 | |
| MP5 / Pump Shotgun | Both | $1500 / $1700 | 30/120, 8/32 | |
| AK-47 / M4A1 | T / SWAT | $2500 / $3100 | 30/90 | |
| AWP | Both | $4750 | 10/30 | Scope with right click; no-scope shots work too |
| RPG | Both | $10000 | 1/14 | Rocket with splash damage (your own rocket hurts you at half damage) |
| Kevlar / + Helmet | Both | $650 / $1000 | | |

## Rules

- Win a round by eliminating the other team. If time runs out, SWAT wins.
- Money: start $800, win $3250, loss $1400 (+$500 per loss in a row), kill $300 (AWP $100, knife $1500).
- Survivors keep their weapons and armor for the next round.

## Code

All in `Assets/Scripts`:

| File | What it does |
| --- | --- |
| `GameManager.cs` | Match flow, rounds, money, spawning, spectator camera |
| `MapBuilder.cs` | Builds the map from a grid and bakes the bots' NavMesh |
| `PlayerController.cs` | First-person movement, shooting, recoil, scope |
| `BotController.cs` | Bot AI: buying, round plans per team, sight, hearing, combat |
| `Combatant.cs` | Health, armor, money, weapons — shared by player and bots |
| `WeaponData.cs` | Weapon stats and prices |
| `GameUI.cs` | Menus, HUD, buy menu, scoreboard (IMGUI) |
| `Rocket.cs` | RPG rocket flight and explosion damage |
| `Ballistics.cs`, `Effects.cs`, `SoundFX.cs`, `WeaponModels.cs` | Hitscan, visuals, generated sounds, gun models |
