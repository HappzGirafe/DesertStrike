"""
Makes the neon wraps (1.20.1) for every weapon:
    python Tools/make_neon_wraps.py <project folder>
"""
# Makes the neon wraps: Assets/Resources/Skins/<weapon>/Neon <colour>/ main.png (a dark circuit pattern with glowing
# traces), detail.png (glowing stripes) and skin.json (the picture is also the glow's map; 500 Tokens).
import json, os, random, struct, sys, zlib

ROOT = sys.argv[1]
SIZE = 256
WEAPONS = ["Knife", "Glock-18", "USP", "TEC-DC9", "M1911", "Desert Eagle", "Uzi", "MP5", "Pump Shotgun", "AK-47", "M4A1",
           "AWP", "RPG", "SVD"]
COLOURS = {"Cyan": (0, 240, 255), "Pink": (255, 43, 214), "Green": (57, 255, 20), "Yellow": (255, 230, 0)}
BACK = (10, 12, 18)


def png(path, pixels):
    raw = b"".join(b"\x00" + bytes(v for p in pixels[y * SIZE:(y + 1) * SIZE] for v in p) for y in range(SIZE))
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def light(level, x, y, amount):
    i = (y % SIZE) * SIZE + (x % SIZE)   # wraps round: the picture tiles without seams
    level[i] = max(level[i], amount)


def line(level, x0, y0, x1, y1, width, amount):
    steps = max(abs(x1 - x0), abs(y1 - y0))
    for s in range(steps + 1):
        x = x0 + (x1 - x0) * s // max(1, steps)
        y = y0 + (y1 - y0) * s // max(1, steps)
        for dx in range(-(width // 2), width // 2 + 1):
            for dy in range(-(width // 2), width // 2 + 1):
                light(level, x + dx, y + dy, amount)


def glowing(level, segments, pads):
    for (x0, y0, x1, y1) in segments:
        line(level, x0, y0, x1, y1, 7, 0.22)   # the soft edge round a trace
    for (x0, y0, x1, y1) in segments:
        line(level, x0, y0, x1, y1, 3, 1.0)    # the trace
    for (x, y) in pads:
        for dx in range(-4, 5):
            for dy in range(-4, 5):
                light(level, x + dx, y + dy, 1.0 if max(abs(dx), abs(dy)) <= 3 else 0.3)


def circuit():
    level = [0.0] * (SIZE * SIZE)
    for k in range(0, SIZE, 32):   # a faint grid
        line(level, k, 0, k, SIZE - 1, 1, 0.16)
        line(level, 0, k, SIZE - 1, k, 1, 0.16)
    rng = random.Random(7)
    segments, pads = [], []
    for _ in range(9):              # traces: straight runs with 45-degree bends, a pad at each end
        x, y = rng.randrange(0, SIZE, 32), rng.randrange(0, SIZE, 32)
        pads.append((x, y))
        for _ in range(rng.randint(2, 4)):
            dx, dy = rng.choice([(1, 0), (0, 1), (1, 1), (1, -1), (-1, 0), (0, -1)])
            run = rng.choice([32, 48, 64, 96])
            segments.append((x, y, x + dx * run, y + dy * run))
            x, y = x + dx * run, y + dy * run
        pads.append((x, y))
    glowing(level, segments, pads)
    return level


def stripes():
    level = [0.0] * (SIZE * SIZE)
    glowing(level, [(0, 40, SIZE, 40), (0, 168, SIZE, 168)], [])
    for y in (104, 232):
        line(level, 0, y, SIZE - 1, y, 1, 0.5)
    return level


def paint(level, colour):
    return [tuple(int(BACK[c] + (colour[c] - BACK[c]) * a) for c in range(3)) for a in level]


patterns = {"main": circuit(), "detail": stripes()}
made = 0
for weapon in WEAPONS:
    for name, colour in COLOURS.items():
        folder = os.path.join(ROOT, "Assets", "Resources", "Skins", weapon, "Neon " + name)
        os.makedirs(folder, exist_ok=True)
        for part, level in patterns.items():
            png(os.path.join(folder, part + ".png"), paint(level, colour))
        info = {"name": "Neon " + name, "main": "#FFFFFF", "grip": "#16181E", "detail": "#FFFFFF",
                "mainGlow": "#FFFFFF", "detailGlow": "#FFFFFF", "smoothness": 0.55, "metallic": 0.2, "locked": True, "tokens": 500}
        with open(os.path.join(folder, "skin.json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(info) + "\n")
        made += 1
print("made", made, "neon wraps")
