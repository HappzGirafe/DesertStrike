# Онлайн-сервер LOW STRIKE

Сервер акаунтів, друзів і кімнат для гри через інтернет. Працює на звичайному домашньому ПК з Windows (навіть через
5G без статичної IP-адреси): гравцям не потрібні VPN, налаштування роутера чи відкриті порти.

```text
Гравці ── HTTPS ──> api.<домен> ── Cloudflare Tunnel ──> API на цьому ПК (127.0.0.1:8000) ── SQLite (data\)
   │                                                        │ запускає й стежить
   └──── UDP (гра й голос) ──> адреса playit.gg ── playit ──> Unity game host (127.0.0.1:27115), одна кімната
Сайт (GitHub Pages) і гра беруть адресу API з https://happzgirafe.github.io/DesertStrike/online.json
```

- `lowstrike_server.py`: публічний API (тільки 127.0.0.1, з інтернету лише через тунель) і внутрішній
  API на 127.0.0.1:8001, куди Unity-хости щосекунди звітують. Він же запускає по одному Unity-хосту на кожен слот
  (`-batchmode -nographics`: без вікна) і перезапускає їх, якщо вони впали.
- Паролі зберігаються лише як хеш Argon2id; сесії керовані (вихід їх завершує). Квиток входу в кімнату підписаний
  `GAME_JOIN_SECRET`, діє 2 хвилини й один раз.
- `tests\smoke_test.py` перевіряє весь API: `.venv\Scripts\python.exe tests\smoke_test.py`.

## 1. Встановлення й `.env`

1. Розпакуйте `LowStrike-Server.zip`, наприклад у `C:\LowStrike-Server` (у ньому `game\` — гра, `online-server\` — сервер).
2. Якщо Python ще немає: `winget install -e --id Python.Python.3.13`
3. У PowerShell:
   ```
   powershell -ExecutionPolicy Bypass -File C:\LowStrike-Server\online-server\scripts\setup.ps1
   ```
   Він створює `.venv`, ставить залежності і пише `online-server\.env` з двома новими випадковими секретами, версією
   гри та шляхом до `game\LowStrike.exe`. Наприкінці показує, що ще треба заповнити (кроки 2 і 3).
   `.env` — секретний файл: не надсилайте його й не кладіть у Git.
4. Щоб ПК не засинав (від мережі): `powercfg /change standby-timeout-ac 0`

## 2. playit.gg: UDP-тунель для гри

1. Встановіть playit з https://playit.gg/download (Windows), запустіть і прив'яжіть агент до свого акаунта за
   посиланням, яке він покаже.
2. На playit.gg: **Tunnels → Add tunnel**: тип **UDP**, Local address `127.0.0.1`, Local port `27115`,
   Proxy Protocol — **None** (якщо увімкнете, сервер усе одно його розбере).
3. playit покаже зовнішню адресу, наприклад `xyz.gl.at.ply.gg:12345` (або `147.185.x.x:12345`). Впишіть її в `.env`:
   ```
   ROOM_SLOTS_JSON=[{"id":"room-1","local_udp_port":27115,"public_udp_host":"xyz.gl.at.ply.gg","public_udp_port":12345}]
   ```
   Адреса може не збігатися з вашим доменом — так і має бути. Якщо адреса зміниться, просто змініть її тут і
   перезапустіть сервер: гравцям нову гру ставити не треба.
   На безкоштовному тарифі до 4 портів. Якщо UDP-тунель не пропонують, потрібен тариф із ним
   (https://playit.gg/support/playit-premium/); TCP замість UDP не підходить.
4. Інсталятор playit для Windows ставить його службою (процес `playitd`), і він запускається разом із Windows сам.
   Якщо у вас інакше — впишіть у `.env` `PLAYIT_EXE=<шлях до playit.exe>`, і крок 5 зробить для нього автозапуск.

## 3. Cloudflare Tunnel: HTTPS-адреса для API

Потрібен домен (наприклад, з Infomaniak), доданий у Cloudflare.

1. Cloudflare → **Add a domain** → тариф Free. Перевірте, що Cloudflare переніс усі наявні DNS-записи (пошта: MX,
   TXT тощо), і лише тоді в Infomaniak (Manager → домен → DNS → змінити DNS-сервери) поставте два nameserver-и
   Cloudflare.
2. Встановіть cloudflared: `winget install --id Cloudflare.cloudflared`
3. Cloudflare → **Networking → Tunnels → Create Tunnel**, назва `lowstrike`, Windows. Скопіюйте команду з розділу
   Install and Run (`cloudflared.exe service install <токен>`) і виконайте в PowerShell **від адміністратора**:
   cloudflared стане службою Windows і запускатиметься сам.
4. У тунелі: **Routes → Add route → Published application**: Hostname `api` + ваш домен, Service URL
   `http://127.0.0.1:8000`. Порт 8001 у тунель **не додавайте**.
5. У `.env`: `LOWSTRIKE_DOMAIN=<домен>` і `PUBLIC_API_URL=https://api.<домен>`.

Ще без домену: можна тимчасово взяти Tailscale Funnel (безкоштовно, своє HTTPS-ім'я `https://<ПК>.<tailnet>.ts.net`):
встановіть Tailscale, увійдіть і виконайте `tailscale funnel --bg 8000`; цю адресу впишіть у `PUBLIC_API_URL` і в
`online.json` (крок 4).

## 4. Сайт і `online.json`

1. У репозиторії впишіть адресу API у `Website/online.json`: `{"api": "https://api.<домен>"}`, потім
   `python Tools/build_site.py`, commit і push. Тепер гра (PLAY ONLINE) і блок Play Online на сайті знаходять сервер.
   Поки там `""`, гра пише «Online play is not open yet», а сайт — «Opening soon».
2. Сайт лишається на GitHub Pages і працює, навіть коли сервер вимкнений. Якщо хочете сайт на своєму домені: GitHub →
   Settings → Pages → Custom domain `www.<домен>`; у Cloudflare DNS: `CNAME www → happzgirafe.github.io` і для
   кореневого домену A-записи `185.199.108.153`, `185.199.109.153`, `185.199.110.153`, `185.199.111.153`
   (усі «DNS only», сіра хмарка). До того сайт лишається на https://happzgirafe.github.io/DesertStrike/.

## 5. Запуск і автозапуск

```
cd C:\LowStrike-Server\online-server
powershell -ExecutionPolicy Bypass -File scripts\start.ps1               # запустити (без вікон)
powershell -ExecutionPolicy Bypass -File scripts\status.ps1              # що працює, чи відповідає з інтернету
powershell -ExecutionPolicy Bypass -File scripts\install-autostart.ps1   # автозапуск (у PowerShell від адміністратора:
                                                                          # разом із Windows, ще до входу)
powershell -ExecutionPolicy Bypass -File scripts\stop.ps1                # зупинити
powershell -ExecutionPolicy Bypass -File scripts\uninstall-autostart.ps1 # прибрати автозапуск
```

Автозапуск — це завдання Windows: стартує з ПК, піднімає сервер за хвилину, якщо той упав, і перевіряє кожні
5 хвилин. Після обриву 5G чи рестарту тунелів усе відновлюється само; кімнати, хост яких мовчить 45 с, закриваються.
Журнали: `logs\api.log`, `logs\unity-room-1.log`. Базу `data\lowstrike.sqlite3` час від часу копіюйте (бекап).

## 6. Перевірка через інший інтернет

На другому ПК або телефоні **не в домашній Wi-Fi** (наприклад, через мобільний інтернет) встановіть Low Strike тієї ж
версії, натисніть **PLAY ONLINE → LOG IN / SIGN UP ONLINE**, зареєструйтеся і створіть кімнату. Другий гравець (інший
акаунт) заходить у неї (JOIN, QUICK JOIN або за кодом), власник тисне **START MATCH**. Голос: V у матчі.

## Більше кімнат одночасно

Один Unity-хост = один матч. Для другої кімнати: ще один UDP-тунель у playit на `127.0.0.1:27125` і ще один запис у
`ROOM_SLOTS_JSON` (`"id":"room-2","local_udp_port":27125,...`), потім `stop.ps1` і `start.ps1`. Код міняти не треба.

## Відгуки на сайті й навчання ботів (з 1.20)

**Відгуки** (`reviews.py`). Сайт показує відгуки з `GET /reviews`, а форма на сайті надсилає `POST /reviews` з ніком і
паролем онлайн-акаунта: сервер перевіряє пароль так само, як при вході (Argon2, блокування після 8 помилок на 15
хвилин), і не зберігає його. Один відгук на акаунт (новий замінює старий); посилання заборонені; текст показується
лише як текст. Писати можна тільки зі сторінок сайту (`REVIEW_ORIGINS` у `.env`, за замовчуванням
`https://lowstrike.lokvetia.com,https://happzgirafe.github.io`). Прибрати відгук із сайту:

    .venv\Scripts\python.exe lowstrike_server.py --hide-review <нік>     (повернути: --show-review <нік>)

Коли акаунт видаляють, його відгук зникає разом із ним.

**Знання ботів** (`bot_knowledge.py`). Кожен матч, який рахує гра (ігрові хости онлайн-кімнат, LAN-хост, одиночна гра
з онлайн-акаунтом), записує, де стоять люди кожної команди і звідки вони вбивають (клітинки 3 м). Після матчу це йде на
API: хости онлайн-кімнат через внутрішній порт, гравці зі своїм входом (їхні дані важать удвічі менше, не частіше
разу на хвилину). Усе складається в одну таблицю `bot_knowledge`; вага клітинки зменшується вдвічі за 14 днів, тож
важить те, що люди роблять зараз. Ігри завантажують `GET /bot-knowledge/<мапа>` і тримають копію в себе. Боти
користуються цим в онлайн-кімнатах і на складності Hard та Extreme: цілять туди, звідки люди стріляють, швидше
реагують на ворога саме там, займають позиції, які люди люблять тримати. Спочатку база порожня: боти вчаться з
перших же матчів.

## Halloween Tokens (з 1.20)

`tokens.py`: ігрова валюта на онлайн-акаунті (`users.halloween_tokens`, міграція 8), куплені скіни (`owned_skins`) і
денний облік (`token_days`). 1 токен за вбивство, 5 за виграний матч. Матчі онлайн-кімнат платить хост через
внутрішній heartbeat (кожен гравець зі своїми вбивствами, один раз на матч, без денної межі). Одиночні матчі з ботами гра
надсилає сама (`POST /tokens/match`): сервер перевіряє, що такий матч міг бути (раунди, вбивства, тривалість), не
частіше 12 на годину, і не більше 40 токенів на день. `GET /tokens` — баланс, куплені скіни й ціни; `POST /tokens/buy`
купує скін (ціни в `SHOP` у `tokens.py`: AK-47 і M4A1 Halloween по 40, ніж Classic Halloween 60). Перевірка:
`tests/smoke_test.py`.

## Адмін-панель (з 1.20)

`admin.py`. Адміни — акаунти з `ADMIN_NICKS` у `.env` (через кому; за замовчуванням `001,pups183,pups183mobile`). У грі
в них є кнопка **ADMIN** у головному меню: знайти гравця за ніком, дати чи забрати Halloween Tokens, дати чи забрати
скіни магазину. Сервер сам перевіряє кожен запит (`GET /admin/player`, `POST /admin/tokens`, `POST /admin/skins`,
`GET /admin/log`; не адмін отримує 403) і записує кожну дію в таблицю `admin_log` (міграція 9) та в лог сервера
(`[admin] хто -> кому: що`). Ніхто не може зареєструвати нік зі списку, якого ще немає: щоб створити такий акаунт,
приберіть нік зі списку, зареєструйтеся, поверніть нік і перезапустіть сервер.

## Нова версія гри

`stop.ps1` → замініть папку `game\` новою збіркою Windows → `GAME_BUILD_VERSION` у `.env` → `start.ps1`. Гравцям зі
старою версією гра покаже «Update Low Strike to version …».

## Якщо щось не так

| Що видно | Що перевірити |
| --- | --- |
| У грі «Server unavailable» | cloudflared працює? `status.ps1` → рядок Internet; адреса в `online.json` |
| «The game servers are offline right now» | `UNITY_SERVER_EXECUTABLE` у `.env`, журнал `logs\unity-room-1.log` |
| Кімната є, але «The game server does not answer» | playit запущений? адреса й порт у `ROOM_SLOTS_JSON` — саме ті, що дав playit |
| «All rooms are busy» | усі слоти зайняті: додайте слот (див. вище) |
