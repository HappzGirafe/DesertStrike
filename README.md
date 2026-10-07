# Low Strike

A small Counter-Strike-style shooter made in Unity 6.3 (built-in render pipeline, no extra packages or assets).
Terrorists vs SWAT, round-based, with a buy menu and bots, on four maps: **de_dune**, a desert map with a Dust-2-style
layout (outside long, long doors, long A, catwalk/short A, mid doors, upper and lower tunnels to B), and three maps
made in Blender: **Village**, **Halloween** (a haunted village at night) and **Industrial** (crane, containers, warehouse).

**Website: [happzgirafe.github.io/DesertStrike](https://happzgirafe.github.io/DesertStrike/)** (in English,
Ukrainian, German, French, Italian and Spanish). Its pages are built from
`Website/index.html` by `python Tools/build_site.py` into `docs/`, which GitHub Pages serves.

## Download

| System | File | |
| --- | --- | --- |
| Windows | [**LowStrike-Windows.exe**](https://github.com/HappzGirafe/DesertStrike/releases/latest/download/LowStrike-Windows.exe) | the whole game in one file |
| macOS | [**LowStrike-macOS.zip**](https://github.com/HappzGirafe/DesertStrike/releases/latest/download/LowStrike-macOS.zip) | Intel and Apple Silicon |

Both files are in the [`Builds`](Builds) folder of this repository, and every version is also on the
[Releases](https://github.com/HappzGirafe/DesertStrike/releases) page.

## Play

- **Windows:** download `LowStrike-Windows.exe` and double-click it.
  It is the whole game in one file: the first time (and after an update) it unpacks the game to
  `%LOCALAPPDATA%\LowStrike`, then starts it. If Windows SmartScreen appears, click *More info* > *Run anyway*.
  If it does not start, `%LOCALAPPDATA%\LowStrike-launcher.log` says why.
- **macOS** (Intel and Apple Silicon): download `LowStrike-macOS.zip` and unzip it. The game is not signed
  with a paid Apple Developer ID, so the first time macOS says it "cannot be opened" (unidentified developer).
  Allow it once:
  1. Double-click **Low Strike**, then click **Done** / **OK** on the warning.
  2. Open **System Settings > Privacy & Security**, scroll down, click **Open Anyway** next to
     *"Low Strike" was blocked*, and enter your Mac password.
  3. Click **Open**. From then on it opens normally. (The zip also contains these steps as a text file.)
- **In Unity:** open this folder in Unity Hub, open `Assets/Scenes/DesertStrike.unity` (it opens automatically), press Play.

The main menu lets you pick your team (Terrorists, SWAT, or watch the bots), team size (1v1 to 5v5),
bot difficulty, the map, the number of rounds to win and friendly fire.

### Accounts

At every start the game asks you to **log in or sign up** (or to play as a guest). An account is a nickname that no
other account on the computer has (3 to 16 letters, digits, `_` or `-`; bots' names are taken) and a password
(at least 4 characters). With an account you can play the **Halloween** map, use the **RPG's Web skin** and play
against **Extreme** bots; as a guest these show a padlock. The skins you equip are kept with your account, and a new
account starts with the skins you had as a guest.

Accounts are saved on the computer in `accounts.json` in the game's data folder, which updates do not touch
(Windows: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\Low Strike`, macOS:
`~/Library/Application Support/DefaultCompany/Low Strike`), so after an update you log in and have your skins again.
The password itself is never saved, only a salted PBKDF2-SHA256 hash of it. Accounts are per computer: there is no
server, so the same nickname can exist on another computer (in a LAN game the second one gets a number added).

### Settings and performance (laptops, MacBook Air)

**SETTINGS** (main menu, or Esc during a match) is saved between sessions:

| Setting | Options | |
| --- | --- | --- |
| Graphics | Low / Medium / High | Low: no shadows or lights, half-size textures. Medium: simple shadows close by. High: everything. Macs start on **Medium**, PCs on High. |
| Display | Fullscreen / Window | The window can be resized by dragging its edges; the game remembers the choice. |
| Frame limit | 30 / 60 / 120 FPS / Unlimited | Default 60. The game never draws more frames than this, so the computer is not at full load all the time. On a MacBook Air, 30 or 60 keeps it much cooler. |
| 3D resolution | 100% / 75% / 50% | The 3D view is drawn at fewer pixels and stretched to the screen; menus and the HUD stay sharp. The biggest help when the graphics chip is the limit. |
| Show FPS counter | Off / On | Shown under the radar, with the CPU and GPU time per frame (see below) |
| Mouse sensitivity | 0.3 – 8 | |

**Reading the FPS counter:** `CPU` is how many milliseconds the processor works on one frame, `GPU` how many the
graphics chip does. For 60 FPS both must stay under 16.7 ms. If GPU is the big one, lower Graphics or 3D resolution;
if CPU is the big one, fewer bots (players per team) helps most. The second line shows the resolution, the graphics
chip and whether the game runs natively (`arm64` on Apple Silicon Macs, `x64` on Intel).

What the game does to stay light:

- **Players behind walls are not drawn.** Right before every frame, a few line tests go from the camera to each
  player's head, shoulders, hips, feet and gun. Anyone fully behind a wall is skipped (body, gun and shadow); the
  moment any part comes into view they are drawn again in that same frame, so they never pop in late.
- All plain-coloured blocks (map, bodies, bullet holes, blood) share one material: every colour is a pixel of a
  small palette texture. The map merges into a few big batches and each body is a single mesh (one draw call).
- The HUD only does work when it is drawn, not for every mouse or keyboard event (a Mac sends many per frame).
- The camera draws straight to the screen (no HDR buffer that is copied over afterwards).
- Tracers, muzzle flashes, blood, bullet holes and sounds are reused instead of being created for every shot;
  sounds too far away to hear are not played at all.
- The Mac version draws at normal resolution instead of Retina (a quarter of the pixels).

Measured on a laptop with Intel UHD 620 graphics, 1280x720, 5v5 bots, no frame limit:

| Graphics | FPS | CPU / GPU per frame | Draw batches |
| --- | --- | --- | --- |
| High | ~160 | 3.8 / 5.6 ms | ~80 |
| Low | ~400 | 2.1 / 2.0 ms | ~55 |
| Low, 3D resolution 50% | ~480 | 1.5 / 1.6 ms | |

The gun models and skins make no measurable difference: the models have 24 to 1,424 triangles each.

### LAN multiplayer (same WiFi)

1. On one PC: **LAN GAME > HOST A GAME**, choose your team, the map and the match settings (everyone plays the
   host's map). Both computers need the same version of the game.
2. On the other PC: **LAN GAME**, pick a team, and click **JOIN** next to the game it finds
   (or type the host's IP address, shown on the host's screen).
3. The host clicks **START MATCH**. Bots fill the empty places on both teams.

The first time, Windows asks whether Low Strike may use the network: allow it (at least on private networks).
The game uses UDP ports 27015 (game) and 27016 (finding games).

**Voice chat:** during a LAN match, hold **V** to talk; everyone in the game hears you, and the names of the
people talking appear under the radar. Settings > *When the others hear you* > **Open mic** sends your voice
whenever you speak, without a key (use headphones). During a LAN match the HUD always shows the microphone:
"Hold V to talk", "You" with a level bar while talking, "Open mic", or what is wrong. The microphone starts the first time you press V (macOS asks for
permission once) and stops when the LAN game ends. Settings > *Voice chat in LAN games* turns it off.
**TEST MICROPHONE** in Settings shows a level bar that moves when you speak, without a second computer. If the
game cannot use the microphone, it says why (not allowed, no microphone, only silence) and where to turn it on;
on a Mac that is System Settings > Privacy & Security > Microphone > Low Strike (then restart the game).
While in a LAN game the game keeps running when its window is not in front, so the others are not affected.

### Building

- Unity menu **Low Strike > Build Windows Game** → `Builds/Windows/`
- Unity menu **Low Strike > Build macOS Game** (needs Unity's Mac Build Support module) → `Builds/macOS/Low Strike.app`
- One-file Windows exe: `powershell -ExecutionPolicy Bypass -File Tools/build_launcher.ps1` → `Builds/LowStrike-Windows.exe`
- Sign the Mac app (ad-hoc, so Macs do not call it "damaged") with the free
  [rcodesign](https://github.com/indygreg/apple-platform-rs/releases) tool: `rcodesign sign "Builds/macOS/Low Strike.app"`
- Mac zip that keeps the app runnable, with the opening steps:
  `python Tools/zip_mac_app.py "Builds/macOS/Low Strike.app" Builds/LowStrike-macOS.zip "Tools/How to open on Mac.txt"`
- Removing the "unidentified developer" warning completely needs an Apple Developer ID ($99/year) and notarization.

## Controls

| Key | Action |
| --- | --- |
| WASD / Mouse | Move / aim |
| Left click / Right click | Shoot / scope (AWP), or switch semi/full-auto (TEC-DC9, M1911) |
| R | Reload |
| 1 / 2 / 3, Q, mouse wheel | Primary / pistol / knife, last weapon, cycle |
| Space / Ctrl / Shift | Jump / crouch / walk silently |
| B | Buy menu (in your team's spawn: buying the first 25 s of a round, selling the first 30 s) |
| E | Plant the bomb (Terrorists, on site A or B) / defuse it (SWAT) — hold |
| G | Drop the bomb |
| Tab | Scoreboard |
| V | Voice chat: hold to talk (LAN games) |
| Esc | Pause (menu only, in a LAN game) and settings |

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

**Selling:** the bottom of the buy menu sells your own guns back (not the knife), in your spawn during the first
30 seconds of a round: the full price in the round you bought the gun, half the price after that. The free starting
pistol gives nothing.

## Inventory and skins

**INVENTORY** in the main menu lets you pick a skin for every weapon, with a turning 3D preview.
Hover a skin to preview it and click to equip it; the choice is saved. Bots carry random skins.

Every weapon has: Default, Desert Camo, Jungle, Urban Digital, Gold, Arctic.
Custom skins: Pink Scribble (Glock-18, USP), AWP_1skin (AWP), and skins with their own model:
nogektestskin and karambit (knife) and digle (Desert Eagle).

The Glock-18, USP, TEC-DC9, M1911, Desert Eagle (`digle_default.blend`), MP5, M4A1, AK-47 and AWP use Blender
models. Their **Default** skin is the model's own look from Blender; the other skins ("wraps") paint over it.
Weapons without a model (Pump Shotgun, RPG, knife) are built from boxes.

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
| `projectile.fbx` | RPG only: what the skin fires instead of the rocket (the Web skin's spinning spider-web net), with `projectile_parts.json` |
| `skin.json` | Optional settings, e.g. `{ "name": "Gold", "main": "#D4AF37", "grip": "#141414", "detail": "#8A6E22", "smoothness": 0.75, "metallic": 0.9 }` |

`skin.json` can also turn a skin's own model in the hand: `"rotation": [x, y, z]` in degrees, around the grip
(the karambit uses `[0, 0, 180]`). The game first points every model's blade or barrel forward by itself, then adds this turn.
A rifle or launcher turned this way still takes the weapon's usual place (the RPG's Web skin uses `[0, 180, 0]`).

Colors in `skin.json` tint the texture of that part, or paint it when there is no texture. Parts with neither
keep their normal look. The weapon folder must be named like the weapon in the shop (`AK-47`, `Glock-18`,
`Desert Eagle`, `Knife`, ...). The `Default` folder is the weapon's default skin.

To add a skin, create a new folder: Unity updates `Skins/index.json` by itself, and the skin shows up in the inventory.
(If it does not, use the menu **Low Strike > Rebuild Skin Index**.)

### Blender models

- Sources: `Art/Blender/*.blend`, with their textures in `Art/Textures`
- A weapon's default model: `Assets/Resources/Models/<weapon name>/model.fbx`, with `parts.json` and the textures
  that make up its own look
- A skin's own model: `model.fbx` in its skin folder

After adding or editing a model in Blender, re-export everything (Blender must be installed):

```
powershell -ExecutionPolicy Bypass -File Tools/export_models.ps1
```

`Tools/export_models.ps1` has one line per model. A skin paints a model by part: **Main** (body, slide, blade) gets
`main.png`, **Grip** (grip, stock) the grip colour, and **Detail** (barrel, magazine, sights) the detail colour.
`--roles "Main=Cube.002;Grip=Cube"` says which Blender objects are which (every other object is a Detail), and
`--copy-textures` makes the model's own textures and colours its Default look. Without `--roles` the texture names
decide (`ruchka`/`rychka`/`wood` = grip, `skin`/`stvol`/`steel` = main, `nogen` = knife blade, `peredr` = guard).
Flat reference pictures in a .blend are left out.

The Glock-18, USP, AK-47, Desert Eagle and Pump Shotgun use the newer models (`Glock18.blend`, `USP.blend`,
`ak47.blend`, `digle.blend`, `pumpshotgun.blend`), each with its own textures as its Default look.

A `.glb` model (like the RPG's Web skin and the maps) is first turned into a `.blend`, with its textures saved to
`Art/Textures/<prefix>_<material>.png`:

```
blender -b --factory-startup --python Tools/import_glb.py -- model.glb Art/Blender/<name>.blend <prefix>
```

### Blender maps

Village, Halloween and Industrial come from `Art/Blender/<map>_map.blend`. The `.glb` files they were made from
have some round parts built along the wrong axis (tree trunks and pine trees lying on their sides, cart wheels lying
flat, the well and the cauldron on their sides, ghosts, fence spikes and lamp caps sideways). After importing one of
them again, stand those parts up once (the meshes listed are the ones that need it):

```
blender -b Art/Blender/village_map.blend --python Tools/fix_map_rounds.py -- Tree_Bark Tree_Leaves Wood_Planks Wall_Stone Water
blender -b Art/Blender/industrial_map.blend --python Tools/fix_map_rounds.py -- Tree_Bark Tree_Leaves
blender -b Art/Blender/halloween_map.blend --python Tools/fix_map_rounds.py -- Iron_Black Ghost_Sheet Cauldron_Potion
```

Then the houses of Village and Halloween are remade (their roofs sat too low, so the walls cut through them, and
their gable triangles faced inwards, so the attics showed the roof's dark underside): every roof is rebuilt to rest
on its walls, every wall faces outwards, and the houses get trim boards, ridge caps, window frames and sills, door
frames, chimney caps, and shutters in the Village. On Industrial, bombsite B becomes a brick building of three rooms
(the bomb can be planted in all of them) with two entrances, one in the north wall from the middle of the map and
one in the east wall from the lane by the warehouse; and the map gets silos, fuel tanks, a water tower, a hangar to
walk into with 20 crates in piles, an office, a guard booth, a pipe rack, more containers and cover on site A.
The old brick warehouse can be walked into as well: its two big doors open onto a low loading dock and a door in its
south wall faces the SWAT spawn; inside are steel columns, lamps and 20 crates in five piles. Both scripts can be
run again; they rebuild what they added.

```
blender -b Art/Blender/village_map.blend --python Tools/remake_houses.py -- village
blender -b Art/Blender/halloween_map.blend --python Tools/remake_houses.py -- halloween
blender -b Art/Blender/industrial_map.blend --python Tools/industrial_structures.py
```

`Tools/industrial_structures.py` lists every structure with its place; before saving, it checks that none of them
touches a wall, a building, a spawn or a bombsite letter, and prints what is in the way if one does.

Export a map after editing it:

```
blender -b Art/Blender/village_map.blend --python Tools/export_map_fbx.py -- Assets/Resources/Maps/village
```

This writes `model.fbx`, `parts.json` (each object's texture, colour, glow and see-through look) and the textures.
The game reads the rest from objects with these names when the map loads:

| Object | Becomes |
| --- | --- |
| `TSpawn_Tiles`, `CTSpawn_Tiles` | Terrorist and SWAT spawn floors (players start there; they are also the buy zones) |
| `Site_Tiles` with `Decal_A`, `Decal_B` | The bombsites: the tiles nearer the painted A are site A, the others site B |

Everything else is solid, except objects whose names contain Leaves, Web, Glow, Ghost or Water, and the houses'
trim (House_Trim, Shutter_*, Roof_Ridge, Chimney_Cap), which would only narrow the gaps between houses. From these the game
also bakes the bots' NavMesh, plans their routes to the sites, and draws the radar from above, leaving out objects
named `...Ceiling...` (roofs over rooms people walk in, like site B's) so the radar shows the rooms. A new map also needs
a line in `MapCatalog` (`GameMap.cs`) with its name, description and lighting (Halloween is lit as night).

## Rules

- One Terrorist carries the bomb (C4). Hold **E** on site A or B (on the Blender maps: on the site's painted floor)
  for 3 seconds to plant it; it explodes after
  40 seconds. SWAT defuse it by holding **E** next to it: 10 seconds, or 5 with a defuse kit. If the carrier dies,
  the bomb drops and any Terrorist can pick it up by walking over it (G drops it on purpose).
- Terrorists win by eliminating SWAT or when the bomb explodes. SWAT win by eliminating the Terrorists before
  the bomb is planted, by defusing it, or when the round time runs out without a plant.
- Buying is only possible in your own team's spawn area, during the first 25 seconds of a round; selling your
  own guns there during the first 30 seconds.
- Friendly fire (menu option): when on, teammates' bullets and rockets hurt each other.
- Money: start $800, win $3250, loss $1400 (+$500 per loss in a row), kill $300 (AWP $100, knife $1500),
  planting or defusing $300, and +$800 for Terrorists who planted but lost the round.
- Survivors keep their weapons and armor for the next round.
- Everyone moves like a person: legs walk and run in step with their speed, bend into a squat when crouching
  (a smaller target, head included), tuck up in a jump, and on death the knees give way and the body falls away
  from whoever shot it, arms and gun over its head. Bots sometimes crouch while holding an angle.
- Free ammo at the start of every round (from round 2): pistols +20, MP5 / AK-47 / M4A1 +40, pump shotgun +8,
  AWP and RPG +5 reserve ammo (set per weapon by `RoundAmmoBonus` in `WeaponData.cs`).

## Code

All in `Assets/Scripts`:

| File | What it does |
| --- | --- |
| `GameManager.cs` | Match flow, rounds, money, buy zones, spawning, spectator camera |
| `GameMap.cs` | The list of maps (`MapCatalog`) and what every map gives the game: spawns, sites, bot routes, radar |
| `MapBuilder.cs` | Builds de_dune from a grid (sites, buy zones) and bakes the bots' NavMesh |
| `ModelMap.cs` | Loads a Blender map and finds its spawns, buy zones and bombsites, bot routes and radar |
| `PlayerController.cs` | First-person movement, shooting, recoil, scope, planting/defusing |
| `BotController.cs` | Bot AI: buying, round plans per team, sight, hearing, combat, the bomb |
| `BombManager.cs` | The C4: carrying, dropping, planting, defusing, timer and explosion |
| `CharacterBody.cs` | The third-person body: jointed model (one skinned mesh) posed by code for walking, crouching, jumping and dying; gun with skin, hit box, footsteps |
| `Combatant.cs` | Health, armor, money, weapons — shared by everyone |
| `NetSession.cs`, `NetProtocol.cs` | LAN multiplayer: hosting, finding games, snapshots and events over UDP |
| `RemotePlayerController.cs`, `PuppetController.cs` | Another PC's player on the host / everyone else on a client |
| `WeaponData.cs` | Weapon stats and prices |
| `GameUI.cs` | Menus, HUD, buy menu, scoreboard (IMGUI) |
| `Rocket.cs` | RPG rocket flight and explosion damage |
| `WeaponSkins.cs`, `SkinPreview.cs` | Pistol skins, the equipped choice, and the inventory's 3D preview |
| `Ballistics.cs`, `Effects.cs`, `SoundFX.cs`, `WeaponModels.cs` | Hitscan, visuals, generated sounds, gun models |
| `GameSettings.cs` | Graphics quality, frame limit, FPS counter, sensitivity (saved) |
| `Accounts.cs` | Accounts on this computer: sign up, log in (hashed passwords), each account's skins |
| `VisibilityCuller.cs` | Skips drawing players hidden behind walls, tested right before each frame |
| `EffectPool.cs` | Reuses effect and sound objects instead of creating new ones per shot |
| `RenderScaler.cs` | Draws the 3D view at the chosen 3D resolution and stretches it to the screen |
| `PerfStats.cs` | FPS, CPU and GPU time per frame, draw-call counts (FPS counter and `-ds-perf`) |

Test options for the built game: `-ds-perf` logs FPS, CPU/GPU time, draw batches and how many players in view were
drawn every 5 s, `-ds-nocull` turns the wall culling off for comparing, `-ds-quality low|medium|high`,
`-ds-fps <limit>` and `-ds-scale <0.25-1>` set graphics for one run without saving, `-ds-autostart -ds-side spectate`
starts a bots-only match, `-ds-map <dune|village|halloween|industrial>` plays that map (not saved),
`-ds-radar-png <file>` saves the map's radar picture, `-ds-view "x,y,z,tx,ty,tz;..."` shows the map from these
camera spots (looking at the second point, 2 s each, without the menu; one screenshot each with `-ds-capture`),
`-ds-pose-gallery` puts a row of figures in every pose (stand, walk, crouch, jump, dying, dead) in the sky over the
map and shows them from the side (with `-ds-capture`: a picture to check the animations), `-ds-sell-test`
(with `-ds-money 5000`, also on a LAN client) buys and sells in round 1 by itself and logs each step, and `-ds-give rpg -ds-equip rpg Web -ds-fire-test` gives the
player a weapon and skin and fires every 3 s, with `-ds-capture <folder>` (a full path) taking a screenshot just after.
Otherwise `-ds-capture <folder>` takes a screenshot every 8 s (`-ds-capture-every <seconds>` changes it).
`-ds-background` keeps a test running when its window is not in front. Tests skip the log-in screen
(`-ds-account-screen [signup]` shows it); `-ds-login <nickname> <password>` logs in (signing up when there is no such
account), and `-ds-accounts-file <file>` keeps test accounts out of the real `accounts.json`.
`-ds-difficulty <easy|normal|hard|extreme>` sets the bots, `-ds-players <1-5>` the players per team, and
`-ds-attack <a|b>` sends the Terrorist bots to that site every round. On a Blender map the log says how far it is to
walk from each spawn to each site ("no way" means the bots cannot get there), `-ds-navmesh-obj <file>` saves
where bots can walk as an `.obj`, and `-ds-hide <text>` stops drawing the map parts whose names contain it (with
`-ds-perf`, to find what costs frame time).

Profiling: **Low Strike > Build Windows Game** has a development twin, `DesertStrikeSetup.BuildWindowsProfiling`
(→ `Builds/WindowsProfiling`). Start that build with `-ds-profile Logs/prof.raw` to record 600 frames of a round,
then run `Unity -batchmode -quit -projectPath . -executeMethod ProfileReport.Analyze -profileFile Logs/prof.raw`
for a text report (`Logs/prof.txt`) of where each frame's time goes.
