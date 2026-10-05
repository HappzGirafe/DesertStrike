"""Builds the public website (GitHub Pages, docs/) from Website/index.html.

Website/index.html is the one source: a single page that switches language with JavaScript (that version is the
preview on claude.ai). Search engines read each language best as its own page, so this script writes one static
page per language, with the translated text already in the HTML:

    docs/index.html       English (also the default page)
    docs/ua/index.html    Ukrainian
    docs/de/ fr/ it/ es/  German, French, Italian, Spanish

plus sitemap.xml and a favicon. The video and pictures live in docs/media (the preview page uses the same files). (A robots.txt would have to sit at the root of
happzgirafe.github.io, so the sitemap is handed to Google in Search Console instead.)

    python Tools/build_site.py

Google Search Console verification: put the file Google gives you (googleXXXX.html) in Website/ and run this again;
it is copied to docs/. A <meta name="google-site-verification"> code can go in Website/google-verification.txt.
"""
import html
import json
import os
import re
import shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "Website", "index.html")
OUT = os.path.join(ROOT, "docs")
SITE = "https://happzgirafe.github.io/DesertStrike/"

# folder, html lang, Open Graph locale, language name, switcher label
LANGUAGES = [
    ("en", "", "en-GB", "en_GB", "English", "UK"),
    ("ua", "ua/", "uk", "uk_UA", "Українська", "UA"),
    ("de", "de/", "de", "de_DE", "Deutsch", "DEU"),
    ("fr", "fr/", "fr", "fr_FR", "Français", "FR"),
    ("it", "it/", "it", "it_IT", "Italiano", "IT"),
    ("es", "es/", "es", "es_ES", "Español", "ES"),
]
SWITCHER_ORDER = ["ua", "en", "de", "fr", "it", "es"]   # as on the page: UA, UK, DEU, FR, IT, ES

TITLES = {
    "en": "Low Strike: free shooter with bots and LAN for Windows and macOS",
    "ua": "Low Strike: безкоштовний шутер з ботами та LAN для Windows і macOS",
    "de": "Low Strike: kostenloser Shooter mit Bots und LAN für Windows und macOS",
    "fr": "Low Strike : jeu de tir gratuit avec bots et LAN pour Windows et macOS",
    "it": "Low Strike: sparatutto gratuito con bot e LAN per Windows e macOS",
    "es": "Low Strike: shooter gratis con bots y LAN para Windows y macOS",
}

# Extra styles for the static pages: the language switcher is made of links there.
STATIC_CSS = """
  .langs a {
    font: 700 12px/1 var(--font-mono); letter-spacing: 0.06em; text-decoration: none; text-align: center;
    background: var(--tile); color: var(--muted);
    border: 1px solid var(--line); border-radius: var(--radius);
    padding: 7px 8px; min-width: 38px;
  }
  .langs a:hover { color: var(--text); background: var(--tile-hi); }
  .langs a[aria-current="page"] { background: var(--orange); border-color: var(--orange); color: #1b130a; }
"""

# Screenshot viewer and round clock, without the language switching of the preview page.
STATIC_SCRIPT = """<script>
(function () {
  const shots = Array.from(document.querySelectorAll(".shot"));
  const box = document.getElementById("lightbox");
  const boxImg = document.getElementById("lb-img");
  const boxCap = document.getElementById("lb-caption");
  let current = 0;
  function showShot(i) {
    current = (i + shots.length) % shots.length;
    const img = shots[current].querySelector("img");
    boxImg.src = img.getAttribute("src");
    boxImg.alt = img.alt;
    boxCap.textContent = shots[current].closest("figure").querySelector("figcaption").textContent;
  }
  shots.forEach((s, i) => s.addEventListener("click", () => {
    showShot(i);
    if (typeof box.showModal === "function") box.showModal(); else box.setAttribute("open", "");
  }));
  document.getElementById("lb-prev").addEventListener("click", () => showShot(current - 1));
  document.getElementById("lb-next").addEventListener("click", () => showShot(current + 1));
  document.getElementById("lb-close").addEventListener("click", () => box.close());
  box.addEventListener("click", e => { if (e.target === box) box.close(); });
  box.addEventListener("keydown", e => {
    if (e.key === "ArrowLeft") showShot(current - 1);
    if (e.key === "ArrowRight") showShot(current + 1);
  });
  // How many times the game was downloaded: the download counts of every release on GitHub (Windows + macOS),
  // kept for 10 minutes so reloading the page does not ask GitHub again.
  (function loadDownloadCount() {
    const counter = document.getElementById("dl-counter");
    if (!counter) return;
    const show = data => {
      const fmt = n => Number(n).toLocaleString(document.documentElement.lang || "en");
      document.getElementById("dl-total").textContent = fmt(data.win + data.mac);
      document.getElementById("dl-split").textContent = "Windows " + fmt(data.win) + " · macOS " + fmt(data.mac);
      counter.hidden = false;
    };
    try {
      const cached = JSON.parse(sessionStorage.getItem("lowstrike.downloads") || "null");
      if (cached && Date.now() - cached.at < 10 * 60 * 1000) { show(cached); return; }
    } catch (e) {}
    fetch("https://api.github.com/repos/HappzGirafe/DesertStrike/releases?per_page=100")
      .then(r => r.ok ? r.json() : Promise.reject(r.status))
      .then(releases => {
        const data = { win: 0, mac: 0, at: Date.now() };
        for (const release of releases)
          for (const asset of release.assets || []) {
            if (/windows/i.test(asset.name)) data.win += asset.download_count;
            else if (/mac/i.test(asset.name)) data.mac += asset.download_count;
          }
        try { sessionStorage.setItem("lowstrike.downloads", JSON.stringify(data)); } catch (e) {}
        show(data);
      })
      .catch(() => {});
  })();
  const clock = document.getElementById("clock");
  if (!window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    let left = 115;
    setInterval(() => {
      left = left <= 0 ? 115 : left - 1;
      clock.textContent = Math.floor(left / 60) + ":" + String(left % 60).padStart(2, "0");
    }, 1000);
  }
})();
</script>"""


def js_object_to_json(text):
    """Quotes the bare keys of the page's translation object (outside strings) so json can read it."""
    out, i, in_string = [], 0, False
    while i < len(text):
        c = text[i]
        if in_string:
            out.append(c)
            if c == "\\":
                out.append(text[i + 1])
                i += 1
            elif c == '"':
                in_string = False
        elif c == '"':
            in_string = True
            out.append(c)
        else:
            m = re.match(r"([A-Za-z_]\w*)(\s*):", text[i:])
            if m and (not out or not re.match(r"\w", out[-1])):
                out.append('"' + m.group(1) + '"' + m.group(2) + ":")
                i += m.end()
                continue
            out.append(c)
        i += 1
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", "".join(out)))


def translations(source):
    start = source.index("const T = {") + len("const T = ")
    depth, end = 0, start
    for end in range(start, len(source)):
        if source[end] == "{":
            depth += 1
        elif source[end] == "}":
            depth -= 1
            if depth == 0:
                break
    return js_object_to_json(source[start:end + 1])


def translate(body, t):
    def text(match):
        key = match.group(3)
        if key not in t:
            raise KeyError(f"No translation for {key}")
        return match.group(1) + html.escape(t[key], quote=False) + match.group(5)

    body = re.sub(r'(<(\w+)\b[^>]*\sdata-i18n="(\w+)"[^>]*>)([^<]*)(</\2>)', text, body)

    def alt(match):
        tag = match.group(0)
        key = re.search(r'data-i18n-alt="(\w+)"', tag).group(1)
        return re.sub(r'(?<![\w-])alt="[^"]*"', 'alt="' + html.escape(t[key]) + '"', tag)

    body = re.sub(r"<img\b[^>]*data-i18n-alt=\"\w+\"[^>]*>", alt, body)

    def aria(match):
        tag = match.group(0)
        key = re.search(r'data-i18n-aria="(\w+)"', tag).group(1)
        return re.sub(r'aria-label="[^"]*"', 'aria-label="' + html.escape(t[key]) + '"', tag)

    body = re.sub(r"<[^>]*data-i18n-aria=\"\w+\"[^>]*>", aria, body)
    # The static pages carry their text directly; the data attributes are only for the preview page.
    return re.sub(r'\sdata-i18n(-alt|-aria)?="\w+"', "", body)


def switcher(code, base, label):
    links = []
    for other in SWITCHER_ORDER:
        _, folder, lang, _, name, short = next(l for l in LANGUAGES if l[0] == other)
        current = ' aria-current="page"' if other == code else ""
        links.append(f'      <a href="{base}{folder}" hreflang="{lang}" lang="{lang}" title="{name}"{current}>{short}</a>')
    return f'<nav class="langs" aria-label="{html.escape(label)}">\n' + "\n".join(links) + "\n    </nav>"


def head(code, t, verification):
    _, folder, lang, locale, _, _ = next(l for l in LANGUAGES if l[0] == code)
    url = SITE + folder
    alternates = "\n".join(
        f'<link rel="alternate" hreflang="{l[2]}" href="{SITE}{l[1]}">' for l in LANGUAGES
    ) + f'\n<link rel="alternate" hreflang="x-default" href="{SITE}">'
    description = t["hero_lead"]
    game = {
        "@context": "https://schema.org",
        "@type": "VideoGame",
        "name": "Low Strike",
        "url": url,
        "description": description,
        "inLanguage": lang,
        "image": SITE + "media/poster.jpg",
        "genre": ["Shooter", "Tactical shooter"],
        "gamePlatform": ["Windows", "macOS"],
        "operatingSystem": "Windows 10, Windows 11, macOS 12 or newer",
        "applicationCategory": "GameApplication",
        "playMode": ["SinglePlayer", "MultiPlayer"],
        "numberOfPlayers": {"@type": "QuantitativeValue", "minValue": 1, "maxValue": 10},
        "author": {"@type": "Person", "name": "HappzGirafe", "url": "https://github.com/HappzGirafe"},
        "offers": {"@type": "Offer", "price": "0", "priceCurrency": "USD"},
        "trailer": {
            "@type": "VideoObject",
            "name": "Low Strike gameplay",
            "description": t["video_text"],
            "thumbnailUrl": SITE + "media/poster.jpg",
            "contentUrl": SITE + "media/gameplay.mp4",
            "uploadDate": "2026-10-03",
            "duration": "PT1M3S",
        },
    }
    meta_verification = f'\n<meta name="google-site-verification" content="{html.escape(verification)}">' if verification else ""
    return f"""<!doctype html>
<html lang="{lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{html.escape(TITLES[code])}</title>
<meta name="description" content="{html.escape(description)}">
<link rel="canonical" href="{url}">
{alternates}
<meta property="og:type" content="website">
<meta property="og:site_name" content="Low Strike">
<meta property="og:title" content="{html.escape(TITLES[code])}">
<meta property="og:description" content="{html.escape(description)}">
<meta property="og:url" content="{url}">
<meta property="og:locale" content="{locale}">
<meta property="og:image" content="{SITE}media/poster.jpg">
<meta property="og:image:width" content="1280">
<meta property="og:image:height" content="720">
<meta property="og:video" content="{SITE}media/gameplay.mp4">
<meta name="twitter:card" content="summary_large_image">
<meta name="theme-color" content="#15110d">
<link rel="icon" type="image/png" sizes="192x192" href="{SITE}favicon.png">
<link rel="apple-touch-icon" href="{SITE}favicon.png">{meta_verification}
<script type="application/ld+json">{json.dumps(game, ensure_ascii=False)}</script>
"""


def build():
    source = open(SOURCE, encoding="utf-8").read()
    t_all = translations(source)
    styles_end = source.index("</style>")
    styles = source[source.index("<link rel=\"preconnect\""):styles_end] + STATIC_CSS + "</style>\n"
    body = source[styles_end + len("</style>"):source.index("<script>")]
    body = re.sub(r'<div class="langs"[^>]*>.*?</div>', "{{SWITCHER}}", body, count=1, flags=re.S)

    verification_file = os.path.join(ROOT, "Website", "google-verification.txt")
    verification = open(verification_file, encoding="utf-8").read().strip() if os.path.exists(verification_file) else ""

    # docs/media (video and pictures) is kept as it is; everything else in docs/ is written fresh.
    os.makedirs(OUT, exist_ok=True)
    for name in os.listdir(OUT):
        path = os.path.join(OUT, name)
        if name == "media":
            continue
        if os.path.isdir(path):
            shutil.rmtree(path)
        else:
            os.remove(path)
    for name in os.listdir(os.path.join(ROOT, "Website")):
        if re.fullmatch(r"google[0-9a-f]+\.html", name):
            shutil.copy(os.path.join(ROOT, "Website", name), OUT)
    shutil.copy(os.path.join(ROOT, "Website", "favicon.png"), OUT)
    open(os.path.join(OUT, ".nojekyll"), "w").close()

    for code, folder, _, _, _, _ in LANGUAGES:
        t = t_all[code]
        base = "../" if folder else ""
        page = translate(body, t)
        page = page.replace("{{SWITCHER}}", switcher(code, base, t["lang_label"]))
        page = page.replace('src="media/', f'src="{base}media/').replace('poster="media/', f'poster="{base}media/')
        document = head(code, t, verification) + styles + "</head>\n<body>\n" + page.strip() + "\n" + STATIC_SCRIPT + "\n</body>\n</html>\n"
        os.makedirs(os.path.join(OUT, folder), exist_ok=True)
        with open(os.path.join(OUT, folder, "index.html"), "w", encoding="utf-8", newline="\n") as file:
            file.write(document)
        print("Wrote", os.path.join("docs", folder, "index.html"))

    urls = []
    for _, folder, _, _, _, _ in LANGUAGES:
        links = "".join(f'\n    <xhtml:link rel="alternate" hreflang="{l[2]}" href="{SITE}{l[1]}"/>' for l in LANGUAGES)
        urls.append(f"  <url>\n    <loc>{SITE}{folder}</loc>{links}\n  </url>")
    with open(os.path.join(OUT, "sitemap.xml"), "w", encoding="utf-8", newline="\n") as file:
        file.write('<?xml version="1.0" encoding="UTF-8"?>\n'
                   '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n'
                   + "\n".join(urls) + "\n</urlset>\n")
    print("Wrote docs/sitemap.xml")


if __name__ == "__main__":
    build()
