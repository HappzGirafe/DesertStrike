"""Reviews on the website: anyone can read them; writing one takes a Low Strike online account and its password.

    GET  /reviews?limit=20&before=<time>    public: the newest first, with the count and the average rating
    POST /reviews                           {nick, password, rating 1-5, text, lang}: one review per account (a second
                                            one replaces the first)

The website calls these from the browser, so the answers carry CORS headers: reading from anywhere, writing only from
REVIEW_ORIGINS (the website's own addresses). The password is checked like a login (Argon2, the same lockout after
wrong guesses) and never kept; the review keeps the nickname, not the account's other data. The text is stored as
plain text (the website shows it as text, never as HTML); links are refused, to keep spam out.
"""
import os
import re
import time
import unicodedata
import uuid
from typing import Optional

from fastapi import HTTPException, Request
from fastapi.responses import JSONResponse, Response
from pydantic import BaseModel

SCHEMA = """
CREATE TABLE IF NOT EXISTS reviews (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    nick TEXT NOT NULL,
    rating INTEGER NOT NULL,
    text TEXT NOT NULL,
    lang TEXT NOT NULL,
    hidden INTEGER NOT NULL DEFAULT 0,
    created_at REAL NOT NULL,
    updated_at REAL NOT NULL
);
CREATE INDEX IF NOT EXISTS reviews_newest ON reviews (hidden, updated_at);
"""

DEFAULT_ORIGINS = "https://lowstrike.lokvetia.com,https://happzgirafe.github.io"
LANGS = ("en", "ua", "de", "fr", "it", "es")
LINK = re.compile(r"(https?://|www\.|\b[a-z0-9-]+\.(com|net|org|ru|io|xyz|ua|de|info|biz|top|link|click)\b)", re.I)


class ReviewBody(BaseModel):
    nick: str = ""
    password: str = ""
    rating: int = 0
    text: str = ""
    lang: str = "en"


def clean_text(text: str) -> str:
    """Plain text: no control characters (line breaks stay, at most two in a row), no runs of spaces."""
    text = unicodedata.normalize("NFC", text or "")
    text = "".join(c for c in text if c == "\n" or unicodedata.category(c)[0] != "C")
    text = re.sub(r"[ \t ]+", " ", text)
    text = re.sub(r" *\n *", "\n", text)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text.strip()


def install(app, s):
    origins = {o.strip().rstrip("/") for o in (s.CONFIG.review_origins or DEFAULT_ORIGINS).split(",") if o.strip()}

    def cors(request: Request, writing: bool) -> dict:
        origin = request.headers.get("origin", "")
        if not writing:
            return {"Access-Control-Allow-Origin": "*"}
        if origin in origins:
            return {"Access-Control-Allow-Origin": origin, "Vary": "Origin"}
        return {"Vary": "Origin"}

    @app.options("/reviews")
    def preflight(request: Request):
        headers = cors(request, writing=True)
        headers.update({"Access-Control-Allow-Methods": "GET, POST", "Access-Control-Allow-Headers": "Content-Type",
                        "Access-Control-Max-Age": "600"})
        return Response(status_code=204, headers=headers)

    @app.get("/reviews")
    def list_reviews(request: Request, limit: int = 20, before: Optional[float] = None):
        s.limit(request, "reviews-get", 60, 60)
        limit = max(1, min(50, limit))
        with s.DB_LOCK:
            stats = s.DB.execute("SELECT COUNT(*) AS n, AVG(rating) AS avg FROM reviews WHERE hidden = 0").fetchone()
            rows = s.DB.execute("SELECT nick, rating, text, lang, updated_at FROM reviews WHERE hidden = 0 AND updated_at < ? "
                                "ORDER BY updated_at DESC LIMIT ?", (before or 1e12, limit)).fetchall()
        body = {
            "count": stats["n"], "average": round(stats["avg"], 2) if stats["avg"] is not None else None,
            "reviews": [{"nick": r["nick"], "rating": r["rating"], "text": r["text"], "lang": r["lang"], "at": r["updated_at"],
                         "date": s.iso(r["updated_at"])[:10]} for r in rows],
        }
        return JSONResponse(body, headers={**cors(request, writing=False), "Cache-Control": "no-store"})

    @app.post("/reviews")
    def post_review(body: ReviewBody, request: Request):
        headers = cors(request, writing=True)

        def refuse(code: int, message: str):
            return JSONResponse({"detail": message}, status_code=code, headers=headers)

        if request.headers.get("origin") and "Access-Control-Allow-Origin" not in headers:
            return refuse(403, "Reviews are written on the Low Strike website.")
        nick = (body.nick or "").strip()
        key = nick.lower()[:32]
        if not 1 <= body.rating <= 5:
            return refuse(422, "Choose 1 to 5 stars.")
        text = clean_text(body.text)
        if len(text) < 10:
            return refuse(422, "Write a few words (10 characters at least).")
        if len(text) > 600:
            return refuse(422, "At most 600 characters, please.")
        if LINK.search(text):
            return refuse(422, "Links are not allowed in reviews.")
        # Password checks are slow on purpose: a few per address (and the nickname's own lockout, as for logins).
        try:
            s.limit(request, "reviews-post", 10, 600)
            s.limit(request, "reviews-day", 30, 86400)
        except HTTPException as e:
            return refuse(e.status_code, e.detail)
        if s.LIMITS.count(f"login-fail:{key}", 900) >= 8:
            return refuse(429, "Too many wrong passwords for this nickname. Wait 15 minutes.")
        with s.DB_LOCK:
            user = s.DB.execute("SELECT * FROM users WHERE nick_key = ?", (key,)).fetchone()
        ok = False
        if user is not None and body.password:
            try:
                ok = s.HASHER.verify(user["password_hash"], body.password)
            except (s.VerifyMismatchError, s.VerificationError, s.InvalidHashError):
                ok = False
        else:
            s.HASHER.hash("timing-equaliser")   # as slow as a real check: the answer does not tell which nicknames exist
        if not ok:
            s.LIMITS.allow(f"login-fail:{key}", 1000, 900)
            return refuse(401, "Wrong nickname or password.")
        t = s.now()
        lang = body.lang if body.lang in LANGS else "en"
        with s.Transaction() as con:
            old = con.execute("SELECT id FROM reviews WHERE user_id = ?", (user["id"],)).fetchone()
            if old:
                con.execute("UPDATE reviews SET nick = ?, rating = ?, text = ?, lang = ?, updated_at = ? WHERE id = ?",
                            (user["nick"], body.rating, text, lang, t, old["id"]))
            else:
                con.execute("INSERT INTO reviews (id, user_id, nick, rating, text, lang, created_at, updated_at) "
                            "VALUES (?, ?, ?, ?, ?, ?, ?, ?)", (str(uuid.uuid4()), user["id"], user["nick"], body.rating, text, lang, t, t))
        print(f"[reviews] {user['nick']} {'changed their' if old else 'wrote a'} review ({body.rating} stars)")
        return JSONResponse({"ok": True, "replaced": bool(old), "nick": user["nick"]}, headers=headers)

    def hide(nick: str, hidden: bool = True) -> bool:
        """For the server's owner (lowstrike_server.py --hide-review <nick>): takes a review off the website."""
        with s.Transaction() as con:
            done = con.execute("UPDATE reviews SET hidden = ? WHERE nick = ? COLLATE NOCASE", (1 if hidden else 0, nick)).rowcount
        return done > 0

    return {"hide": hide}
