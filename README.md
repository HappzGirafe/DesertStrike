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
- **Android** phones and tablets: copy `LowStrike-Android.apk` to the phone and open it. Android asks to allow installing
  apps from that source (the file manager or browser): allow it, then **Install**. If Play Protect warns about an
  unknown app, choose to install anyway.
- **Linux** (64-bit x86): download `LowStrike-Linux.tar.gz`, unpack it (`tar -xzf LowStrike-Linux.tar.gz`) and run
  `./LowStrike-Linux/LowStrike.x86_64` (if it does not start: `chmod +x LowStrike-Linux/LowStrike.x86_64` once).
- **iPhone / iPad:** Apple only lets apps onto an iPhone through Xcode on a Mac or the App Store. On a Mac with
  Xcode: open `Builds/iOS/Unity-iPhone.xcodeproj`, under **Signing & Capabilities** tick *Automatically manage
  signing* and pick your Apple ID as the team, plug in the iPhone (Developer Mode on: Settings > Privacy & Security),
  choose it at the top and press **Run**. With a free Apple ID the app works for 7 days, then press Run again; the App
  Store needs the Apple Developer Program ($99/year).
- **In Unity:** open this folder in Unity Hub, open `Assets/Scenes/DesertStrike.unity` (it opens automatically), press Play.

**On a phone** the game is played with on-screen controls: the left thumb moves (a stick appears where you touch;
pushing it a little walks quietly), dragging on the right looks around, and the buttons are **FIRE** (drag it to
aim while shooting), **JUMP**, **CROUCH** (on/off), **R** reload, **GUN** next weapon, **SCOPE** / **AUTO** when the
weapon has it, **PLANT** / **DEFUSE** (hold) and **DROP** when the bomb matters, **BUY** in your spawn, **MIC** to talk
in LAN games, **TAB** scores and **II** pause; **<** and **>** switch players while spectating. **Settings >
TOUCH CONTROLS** moves the buttons: drag one where you want it, tap one to change its size with the slider, RESET
puts them all back (saved on the phone). Text fields (nickname, password, friends, IP) open the phone's keyboard;
its Done key logs in or sends the request. Phones start on Low
graphics with the 3D at 75%; LAN games work over WiFi. On iPhone, finding LAN games and friends by themselves needs
an extra permission from Apple (the multicast networking entitlement), so there you join a LAN game by its IP address.

The main menu lets you pick your team (Terrorists, SWAT, or watch the bots), how many players each team has (typed
in, or - and +: 1 to 10 per team, and the teams may differ, e.g. 2 against 5; bots fill the places people do not),
bot difficulty, the map, the number of rounds to win and friendly fire. **Site in play** is for small matches (no
team over 3): **Site A** or **Site B** plays on that site with the other site's half of the map shut, **Whole map**
keeps both sites open (bigger teams always have the whole map).

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

**Online account:** the log-in screen's **ONLINE ACCOUNT** makes an account on the Low Strike server instead (password
at least 8 characters, kept there only as an Argon2id hash). It works on every computer and phone, its nickname is
unique everywhere, and its skins and crosshair and voice settings are saved on the server. The first time, the game
offers to bring this computer's skins and settings along. **ACCOUNT** (main menu, when logged in online) changes the
password or deletes the account and everything the server keeps about it.

### Tutorial

A new account is offered a **tutorial**: a short practice round on Village as a Terrorist, with a training bot that
stands in front of you, does not shoot and cannot die. Seven steps on screen, each ticked off as you do it: walk, look
around, jump, crouch, buy a rifle (the shop opens anywhere during the tutorial), hit the bot 3 times, and plant the
bomb on a site. The round clock does not run out meanwhile; **FINISH** (or Esc / pause: **SKIP TUTORIAL**) goes back
to the menu with your own match settings. **Settings > TUTORIAL** plays it again at any time.

### Tokens

**Tokens** are the game's money for skins, kept on your **online account** (the balance shows next to EXIT in
the main menu and in the inventory). You earn **1 for every kill** and **5 for every match you win**: online-room
matches pay everyone in them (the room's game server reports the result), and matches against bots on your own game
pay up to **40 tokens a day** (the server also checks that the match could have happened). They buy these skins in
the **INVENTORY**:

| Skin | Price |
| --- | --- |
| AK-47 Halloween | 100 |
| M4A1 Halloween | 150 |
| Knife: Classic Halloween | 500 |
| Knife: karambit | 400 |
| Knife: nogektestskin | 1000 |
| Desert Eagle: digle | 750 |
| RPG: Web | 400 |
| RPG: RPG-7 | 400 |

A skin bought is yours for good, on every computer and phone you log in on. Until then it shows its price and a
padlock. A skin is sold for tokens when its `skin.json` has `"tokens": <price>` (the price shown until the server's is
known); what it really costs is set by `SHOP` in `online-server/tokens.py`, which must list it too.

### Admin panel

Admin accounts (online: **001**, **pups183** and **pups183mobile**) have an **ADMIN** button next to ACCOUNT in the
main menu. The panel finds any player by nickname (**ME** shows your own account) and shows their Tokens and
Halloween skins. **GIVE** / **TAKE AWAY** gives or takes the amount typed (or **+10** ... **+1000**; never below 0), and
each skin can be given or taken back. The right side lists what admins did lately. The server decides who is an admin
(`ADMIN_NICKS` in its `.env`), checks every request, and logs everything done in the panel.

**Console and cheats (admins):** in a match, **`** (the key left of 1; on a phone **CONSOLE** in the pause menu) opens
the console. **/cheats on** turns the cheats on and shows their switches: **AIMBOT** (the view locks onto the head of
the nearest enemy in sight), **FLY** (no gravity; Space or JUMP up, Ctrl or CROUCH down), **INFINITE AMMO** (full
magazines, 999 in reserve), **WALL HACK** (a box, name, health and distance round every enemy, through walls),
**INFINITE MONEY** ($16000) and **INFINITE HEALTH** (hit, never hurt). **/cheats off** turns them all off; **/help**
lists the commands. They work only for admin accounts, in every match but the tutorial: against bots, in LAN games
(hosting or joining) and in online rooms. Whoever runs the match carries out fly, ammo, money and health, and only for a
player the online server vouches for: in an online room by the join ticket's admin mark, in a LAN game by an admin's
pass (`/admin/lan-pass`) that the LAN host checks online (so a LAN host needs internet for a guest's cheats). Aimbot
and wall hack happen in the player's own game.

**Cheats for everyone (admins switch it):** **/cheats everyone on** (or the console's **CHEATS FOR EVERYONE** button)
lets every player in that match use the console's cheats, admin or not; **/cheats everyone off** (or the button
again) takes them away at once from everyone but the admins. It works in online rooms, LAN games and games against
bots; whoever runs the match keeps it, checks that the one switching it is an admin, and tells every player who turned
it on or off. Each new match starts with it off.

### Friends

**FRIENDS** in the main menu (it needs an account): type a friend's nickname and click **SEND REQUEST**; on their
side the request shows up with **ACCEPT** / **DECLINE**, and once accepted you are on each other's list. A friend
shows as **Online** (green) while their game is open on the same WiFi or network as yours (like LAN games: there is
no server), otherwise **Offline**. The menu button shows how many friends are online, or that a request is waiting.
**REMOVE** (click twice) takes a friend off your list, and they stop seeing you online too.

**Settings > Online status (friends): Hidden** - nobody sees when you are online, and while it is on you cannot
see whether your friends are online either (they show as "Hidden"). The choice is saved with the account.

The friends list is kept with the account in `accounts.json`; each account also gets a random id there, so two
players with the same nickname on different computers are told apart. A request only reaches a friend whose game
is open on the network at the time.

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
chip and whether the game runs natively (`arm64` on Apple Silicon Macs, `x64` on Intel). Admin accounts (see
**Admin panel**) get a third line once they joined a game: the ping to it, measured every second (green under 80 ms,
yellow under 150, red above), and which game server it is (`room-1`, `room-2`... and the room's name; a LAN game: the
host's name).

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

### Online multiplayer

**PLAY ONLINE** (needs an online account): the list of rooms, with their map, players, and whether they are waiting
or playing. **JOIN** one, **QUICK JOIN** the first with a free place, join a private one with its **code**, or
**CREATE ROOM** (name, map, players in each team, bots, rounds, friendly fire, everyone or with the code). In the room you pick
Terrorists, SWAT or Watch, invite friends, and its owner presses **START MATCH**; bots fill the free places. After the
match everyone is back in the room. If the connection drops, the game joins again by itself and gets its player back
(within a minute). Voice chat works as in LAN games; the pause menu and the room's player list can **MUTE** a player.

**Time in the game:** the server counts how long each online account has the game open and logged in (menus and
matches), for as long as the account exists (counted from 1.20 on). **ACCOUNT** shows your own; the friends list,
the scoreboard (Tab) and the admin panel show other players'.

**FRIENDS** with an online account are friends over the internet: find players by nickname, requests, invitations to
rooms (kept on the server while a friend is offline), and Online / In match / Offline. The LAN friends are still there.

The rooms run on a Low Strike server on a home PC, reached through Cloudflare Tunnel (accounts, rooms:
`https://lowstrike-api.lokvetia.com`) and playit.gg (the game itself, over UDP); players need no IP addresses, VPN or
router settings. The game finds the server through `online.json` on the website. The server, its setup and its scripts
are in [`online-server/`](online-server/README.md).

### LAN multiplayer (same WiFi)

1. On one PC: **LAN GAME > HOST A GAME**, choose your team, the map and the match settings (everyone plays the
   host's map). Both computers need the same version of the game.
2. On the other PC: **LAN GAME**, pick a team, and click **JOIN** next to the game it finds
   (or type the host's IP address, shown on the host's screen).
3. The host clicks **START MATCH**. Bots fill the empty places on both teams.

The first time, Windows asks whether Low Strike may use the network: allow it (at least on private networks).
The game uses UDP ports 27015 (game), 27016 (finding games) and 27017 (friends: who is online, friend requests).

**Voice chat:** during a LAN match, hold **V** to talk; everyone in the game hears you, and the names of the
people talking appear under the radar. Settings > *When the others hear you* > **Open mic** sends your voice
whenever you speak, without a key (use headphones). During a LAN match the HUD always shows the microphone:
"Hold V to talk", "You" with a level bar while talking, "Open mic", or what is wrong. The microphone starts the first time you press V (macOS asks for
permission once) and stops when the LAN game ends. Settings > *Voice chat (LAN and online games)* turns it off.
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
- Unity menu **Low Strike > Build Android Game** (needs Unity's Android Build Support module with SDK, NDK and
  OpenJDK) → `Builds/Android/LowStrike-Android.apk`
- Unity menu **Low Strike > Build Linux Game** (needs Unity's Linux Build Support (Mono) module) →
  `Builds/Linux/LowStrike.x86_64`; **Low Strike > Build Linux Server** (Linux Dedicated Server Build Support) →
  `Builds/LinuxServer/`, the headless game host for a Linux online server (`Tools/make_server_package.ps1 -Linux`)
- Unity menu **Low Strike > Build iOS Xcode Project** (needs Unity's iOS Build Support module) → `Builds/iOS/`,
  which Xcode on a Mac builds and installs (see Play above)

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
| V | Voice chat: hold to talk (LAN and online games) |
| Esc | Pause (menu only, in a LAN game) and settings |

**Crosshair** (Settings > **CROSSHAIR**), like Counter-Strike 2: color (Green, Yellow, Red, Blue, Cyan, White, or
Custom with red / green / blue sliders), length, thickness, gap, opacity, outline, center dot, cross or T shape,
and **Static** or **Dynamic** (opens up with the spread, and closes when the first shot is dead on). The preview
shows it twice the size; **RESET** goes back to the default. Saved on the computer (or phone).

## Shop

| Weapon | Team | Price | Ammo | Notes |
| --- | --- | --- | --- | --- |
| Glock-18 / USP | T / SWAT | $200 | 20/120, 12/100 | Starting pistols |
| TEC-DC9 | T | $500 | 12/24 | Right click: semi-auto or full-auto |
| M1911 | SWAT | $500 | 10/30 | Right click: semi-auto or full-auto |
| Desert Eagle | Both | $700 | 7/35 | |
| Uzi | Both | $1200 | 24/96 | Very fast fire, but a big spread (model: `Art/Blender/Usi.blend`) |
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
nogektestskin and karambit (knife), digle (Desert Eagle), and Web and RPG-7 (RPG; RPG-7 is `RPG.blend`, a classic
launcher with wooden grips).

The Glock-18, USP, TEC-DC9, M1911, Desert Eagle (`digle_default.blend`), Uzi, MP5, Pump Shotgun, M4A1, AK-47, SVD,
AWP (since 1.20 the green one with scope and bipod) and knife (since 1.20 the Classic knife, `klasiknogen.blend`)
use Blender models. Their **Default** skin is the model's own look from Blender; the other skins ("wraps") paint
over it. The RPG is built from boxes.

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

Players whose games have different skins still see each other's. Unity also packs every skin (pictures at most
512 px, model and parts) into `Assets/Resources/SkinPacks/<weapon name>/<skin name>.bytes`, made again whenever its
folder changes (**Low Strike > Rebuild Skin Packs** makes them all). In a LAN or online game, a game that meets a
skin it does not have asks for its pack (the host sends it, getting it first from the player whose game has it); the
default look shows until it arrives, usually within a few seconds. A skin from another game only shows on the guns
that carry it; it does not go into the inventory.

Each gun keeps its own skin: a gun picked up from the ground looks the way it did in its owner's hands.

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

Export the maps after editing them (all of them, or the ones named):

```
powershell -ExecutionPolicy Bypass -File Tools/export_maps.ps1 dune bone
```

`Tools/export_maps.ps1` lists which `.blend` each map comes from: Village `village_map.blend`, Halloween
`halloween_map.blend`, Industrial `industrialnorm.blend`, Dune `dunenorm.blend` (de_dune made in Blender, 2.57 times
the size of the old one that the game built from blocks) and Bone `bone_map.blend`. It writes `model.fbx`,
`parts.json` (each object's texture, colour, glow and see-through look) and the textures. Only what shows in
Blender's viewport is exported (a collection or object hidden with its eye stays out), nothing lying wholly outside the
map (a spare copy parked beside it), an object with several materials becomes one per material, and a solid object
whose faces point inwards (Blender draws both sides, the game only the front) is turned outwards. Pictures the
`.blend` cannot find (made on another computer, with a "текстури" folder) are looked for by name in `Art/Textures`.

**Closed versions.** A map can have its own version for small matches with one site's half shut, made by hand:
`<map>_closedA.blend` / `<map>_closedB.blend` (Dune, Halloween; Industrial has B), exported to
`Maps/<map>/closedA` and `closedB` (they share the map's textures). When a small match closes that site the game
loads that version instead of building walls, checks that no spawn can walk onto the closed site (if one can, it
says where the way in goes, in Blender's coordinates, and adds its own walls too), and marks the half nobody can
reach for the bots. Maps and sites without one get the game's walls as before.

**A map without the marked objects** below (Dune and Bone were made without them) gets them from
`Tools/maps/<map>.json` while it is exported (`Tools/map_markers.py`): the spawn and bombsite rectangles and the
letters, in the `.blend`'s coordinates, as tiles 2 cm under the floor; and ramps where bots must climb a ledge (Bone's
bases stand 1.2 m above the ground; bots climb 0.75 m). Bone: the Terrorists start in the fenced base in the
south-west, SWAT in the north-east; site A is the open ground in the north-west corner, B in the south-east.
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
- **First shot:** a gun that has not fired for 3 seconds fires its next shot exactly where it is aimed, with no
  spread at all (the crosshair closes up to show it); not the pump shotgun's pellets. Bots get it too, but their
  own aim is still not perfect.
- Each round every Terrorist bot goes for site A or B, each with a 50% chance; the bomb goes where its carrier
  went.
- Everyone moves like a person: legs walk and run in step with their speed, bend into a squat when crouching
  (a smaller target, head included), tuck up in a jump, and on death the knees give way and the body falls away
  from whoever shot it, arms and gun over its head. Bots sometimes crouch while holding an angle.
- Free ammo at the start of every round (from round 2): pistols +20, Uzi +48, MP5 / AK-47 / M4A1 +40, pump shotgun +8,
  AWP +10, SVD +15, RPG +5 reserve ammo (set per weapon by `RoundAmmoBonus` in `WeaponData.cs`).

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
| `GameUI.Admin.cs` | The admin panel (admin accounts only; the server's side is `online-server/admin.py`) |
| `Tutorial.cs`, `GameUI.Halloween.cs` | The tutorial's steps and training bot; its panel, the tutorial offer, buying skins with Tokens |
| `Ballistics.cs`, `Effects.cs`, `SoundFX.cs`, `WeaponModels.cs` | Hitscan, visuals, generated sounds, gun models |
| `GameSettings.cs` | Graphics quality, frame limit, FPS counter, sensitivity (saved) |
| `Accounts.cs` | Accounts on this computer: sign up, log in (hashed passwords), each account's skins and friends |
| `Friends.cs` | Friend requests and who is online on the network (its own thread, so it runs in the background too) |
| `GameInput.cs`, `TouchControls.cs` | All player input in one place: keyboard and mouse, or the on-screen controls on a phone |
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
`-ds-touch` shows the phone controls on a PC (the mouse is one finger; `-ds-touch-layout` opens the button editor,
`-ds-touch-demo` moves FIRE and enlarges JUMP for one run without saving), `-ds-crosshair` opens the crosshair page and
`-ds-crosshair-demo` shows a different crosshair for one run without saving it, `-ds-friends` opens the friends screen, `-ds-friend-request <nickname>` sends a request, `-ds-accept-friends`
accepts every request and `-ds-hide-status on|off` sets the online status (two games on one PC with their own
`-ds-accounts-file` can test friends), `-ds-hidefps` hides the FPS counter for one run (clean screenshots), and `-ds-background` keeps a test running
when its window is not in front. Tests skip the log-in screen
(`-ds-account-screen [signup]` shows it); `-ds-login <nickname> <password>` logs in (signing up when there is no such
account), and `-ds-accounts-file <file>` keeps test accounts out of the real `accounts.json`.
`-ds-difficulty <easy|normal|hard|extreme>` sets the bots, `-ds-players <1-5>` the players per team, and
`-ds-attack <a|b>` sends the Terrorist bots to that site every round. `-ds-tutorial` starts the tutorial at once, and
`-ds-cheats-test` (logged in as an admin) turns every cheat on with the console, flies up and
shoots with the aimbot, and logs what each cheat did; `-ds-cheats-everyone [seconds]` (an admin host) turns on cheats
for everyone, and off again after that long. `-ds-tutorial-test` plays it through by itself with pretend controls (logging each step, a screenshot of each with
`-ds-capture`) and presses FINISH. On a Blender map the log says how far it is to
walk from each spawn to each site ("no way" means the bots cannot get there), `-ds-navmesh-obj <file>` saves
where bots can walk as an `.obj`, and `-ds-hide <text>` stops drawing the map parts whose names contain it (with
`-ds-perf`, to find what costs frame time).

Profiling: **Low Strike > Build Windows Game** has a development twin, `DesertStrikeSetup.BuildWindowsProfiling`
(→ `Builds/WindowsProfiling`). Start that build with `-ds-profile Logs/prof.raw` to record 600 frames of a round,
then run `Unity -batchmode -quit -projectPath . -executeMethod ProfileReport.Analyze -profileFile Logs/prof.raw`
for a text report (`Logs/prof.txt`) of where each frame's time goes.
