# Desert Strike

A small Counter-Strike-style shooter made in Unity 6.3 (built-in render pipeline, no extra packages or assets).
Terrorists vs SWAT, round-based, with a buy menu and bots, on **de_dune**, a desert map with a Dust-2-style layout
(outside long, long doors, long A, catwalk/short A, mid doors, upper and lower tunnels to B).

## Download

| System | File | |
| --- | --- | --- |
| Windows | [**DesertStrike-Windows.exe**](https://github.com/HappzGirafe/DesertStrike/raw/main/Builds/DesertStrike-Windows.exe) | the whole game in one file |
| macOS | [**DesertStrike-macOS.zip**](https://github.com/HappzGirafe/DesertStrike/raw/main/Builds/DesertStrike-macOS.zip) | Intel and Apple Silicon |

Both files are in the [`Builds`](Builds) folder of this repository, and every version is also on the
[Releases](https://github.com/HappzGirafe/DesertStrike/releases) page.

## Play

- **Windows:** download `DesertStrike-Windows.exe` and double-click it.
  It is the whole game in one file: the first time (and after an update) it unpacks the game to
  `%LOCALAPPDATA%\DesertStrike`, then starts it. If Windows SmartScreen appears, click *More info* > *Run anyway*.
- **macOS** (Intel and Apple Silicon): download `DesertStrike-macOS.zip`, unzip it, and run this once in Terminal
  in the folder with the app (the app is not signed by Apple, so macOS blocks it until you do):
  ```
  xattr -cr DesertStrike.app && codesign --force --deep -s - DesertStrike.app
  ```
  Then open `DesertStrike.app` (the first time: right-click > Open).
- **In Unity:** open this folder in Unity Hub, open `Assets/Scenes/DesertStrike.unity` (it opens automatically), press Play.

The main menu lets you pick your team (Terrorists, SWAT, or watch the bots), team size (1v1 to 5v5),
bot difficulty, the number of rounds to win and friendly fire.

### LAN multiplayer (same WiFi)

1. On one PC: **LAN GAME > HOST A GAME**, choose your team and the match settings.
2. On the other PC: **LAN GAME**, pick a team, and click **JOIN** next to the game it finds
   (or type the host's IP address, shown on the host's screen).
3. The host clicks **START MATCH**. Bots fill the empty places on both teams.

The first time, Windows asks whether Desert Strike may use the network: allow it (at least on private networks).
The game uses UDP ports 27015 (game) and 27016 (finding games).

### Building

- Unity menu **Desert Strike > Build Windows Game** → `Builds/Windows/`
- Unity menu **Desert Strike > Build macOS Game** (needs Unity's Mac Build Support module) → `Builds/macOS/DesertStrike.app`
- One-file Windows exe: `powershell -ExecutionPolicy Bypass -File Tools/build_launcher.ps1` → `Builds/DesertStrike-Windows.exe`
- Mac zip that keeps the app runnable: `python Tools/zip_mac_app.py Builds/macOS/DesertStrike.app Builds/DesertStrike-macOS.zip`

## Controls

| Key | Action |
| --- | --- |
| WASD / Mouse | Move / aim |
| Left click / Right click | Shoot / scope (AWP), or switch semi/full-auto (TEC-DC9, M1911) |
| R | Reload |
| 1 / 2 / 3, Q, mouse wheel | Primary / pistol / knife, last weapon, cycle |
| Space / Ctrl / Shift | Jump / crouch / walk silently |
| B | Buy menu (in your team's spawn, first 25 s of a round) |
| E | Plant the bomb (Terrorists, on site A or B) / defuse it (SWAT) — hold |
| G | Drop the bomb |
| Tab | Scoreboard |
| Esc | Pause (menu only, in a LAN game) |

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
| Defuse Kit | SWAT | $400 | | Defuse in 5 s instead of 10 s |

## Inventory and skins

**INVENTORY** in the main menu lets you pick a skin for every weapon, with a turning 3D preview.
Hover a skin to preview it and click to equip it; the choice is saved. Bots carry random skins.

Every weapon has: Default, Desert Camo, Jungle, Urban Digital, Gold, Arctic.
Custom skins: Pink Scribble (Glock-18, USP), and skins with their own model: nogektestskin (knife),
digle (Desert Eagle) and AWPSkin (AWP).

### Skin folders

Each skin is its own folder: `Assets/Resources/Skins/<weapon name>/<skin name>/`

```
Assets/Resources/Skins/
  AK-47/
    Default/        skin.json
    Desert Camo/    main.png  skin.json
    Gold/           skin.json
    ...
  Knife/
    nogektestskin/  model.fbx  main.png  grip.png  detail.png
  ...
```

A skin folder can hold any of these:

| File | What it does |
| --- | --- |
| `main.png` | Texture for the main part: slide, body or blade |
| `grip.png` | Texture for the grip, stock or handle |
| `detail.png` | Texture for the rest: barrel, magazine, scope, guard, silencer |
| `model.fbx` | A model that replaces the weapon's own (like the knife skin) |
| `skin.json` | Optional settings, e.g. `{ "name": "Gold", "main": "#D4AF37", "grip": "#141414", "detail": "#8A6E22", "smoothness": 0.75, "metallic": 0.9 }` |

Colors in `skin.json` tint the texture of that part, or paint it when there is no texture. Parts with neither
keep their normal look. The weapon folder must be named like the weapon in the shop (`AK-47`, `Glock-18`,
`Desert Eagle`, `Knife`, ...). The `Default` folder is the weapon's default skin.

To add a skin, create a new folder: Unity updates `Skins/index.json` by itself, and the skin shows up in the inventory.
(If it does not, use the menu **Desert Strike > Rebuild Skin Index**.)

### Blender models

- Sources: `Art/Blender/*.blend`, with their textures in `Art/Textures`
- The Glock-18 and USP models: `Assets/Resources/Models/*.fbx`
- A skin's own model: `model.fbx` in its skin folder

After editing a model in Blender, re-export it (Blender must be installed):

```
blender -b Art/Blender/Glock18.blend --python Tools/export_gun_fbx.py -- Assets/Resources/Models/Glock18.fbx
```

The exporter names the parts by the texture they use: `peredr...` = guard, `ruchka`/`rychka` = grip,
`nogen` = knife blade, `skin` = pistol slide, and an untextured mesh (like the USP silencer) is a detail part.

## Rules

- One Terrorist carries the bomb (C4). Hold **E** on site A or B for 3 seconds to plant it; it explodes after
  40 seconds. SWAT defuse it by holding **E** next to it: 10 seconds, or 5 with a defuse kit. If the carrier dies,
  the bomb drops and any Terrorist can pick it up by walking over it (G drops it on purpose).
- Terrorists win by eliminating SWAT or when the bomb explodes. SWAT win by eliminating the Terrorists before
  the bomb is planted, by defusing it, or when the round time runs out without a plant.
- Buying is only possible in your own team's spawn area, during the first 25 seconds of a round.
- Friendly fire (menu option): when on, teammates' bullets and rockets hurt each other.
- Money: start $800, win $3250, loss $1400 (+$500 per loss in a row), kill $300 (AWP $100, knife $1500),
  planting or defusing $300, and +$800 for Terrorists who planted but lost the round.
- Survivors keep their weapons and armor for the next round.
- Free ammo at the start of every round (from round 2): pistols +20, MP5 / AK-47 / M4A1 +40, pump shotgun +8,
  AWP and RPG +5 reserve ammo (set per weapon by `RoundAmmoBonus` in `WeaponData.cs`).

## Code

All in `Assets/Scripts`:

| File | What it does |
| --- | --- |
| `GameManager.cs` | Match flow, rounds, money, buy zones, spawning, spectator camera |
| `MapBuilder.cs` | Builds the map from a grid (sites, buy zones) and bakes the bots' NavMesh |
| `PlayerController.cs` | First-person movement, shooting, recoil, scope, planting/defusing |
| `BotController.cs` | Bot AI: buying, round plans per team, sight, hearing, combat, the bomb |
| `BombManager.cs` | The C4: carrying, dropping, planting, defusing, timer and explosion |
| `CharacterBody.cs` | The third-person body: model, gun with skin, hit box, death, footsteps |
| `Combatant.cs` | Health, armor, money, weapons — shared by everyone |
| `NetSession.cs`, `NetProtocol.cs` | LAN multiplayer: hosting, finding games, snapshots and events over UDP |
| `RemotePlayerController.cs`, `PuppetController.cs` | Another PC's player on the host / everyone else on a client |
| `WeaponData.cs` | Weapon stats and prices |
| `GameUI.cs` | Menus, HUD, buy menu, scoreboard (IMGUI) |
| `Rocket.cs` | RPG rocket flight and explosion damage |
| `WeaponSkins.cs`, `SkinPreview.cs` | Pistol skins, the equipped choice, and the inventory's 3D preview |
| `Ballistics.cs`, `Effects.cs`, `SoundFX.cs`, `WeaponModels.cs` | Hitscan, visuals, generated sounds, gun models |
