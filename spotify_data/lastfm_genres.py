import csv
import json
import os
import time

from pathlib import Path

import requests


# ============================================================
# CONFIGURAZIONE
# ============================================================

REPO_FOLDER = Path(__file__).resolve().parent

# File con i segreti locali (non versionato, vedi .gitignore).
# Formato: una riga per variabile, CHIAVE=valore
ENV_FILE = REPO_FOLDER / ".env"


def load_dotenv(path):
    """Carica le variabili di ENV_FILE in os.environ, senza sovrascrivere
    variabili già impostate nella shell (che restano prioritarie)."""

    if not path.exists():
        return

    with open(path, "r", encoding="utf-8") as f:
        for line in f:

            line = line.strip()

            if not line or line.startswith("#") or "=" not in line:
                continue

            key, _, value = line.partition("=")
            key = key.strip()
            value = value.strip().strip('"').strip("'")

            os.environ.setdefault(key, value)


load_dotenv(ENV_FILE)


# CSV di input: deve avere una colonna "Artista"
# (es. quello generato da json_to_csv.py)
INPUT_CSV = REPO_FOLDER / (
    r"Data\Spotify Extended Streaming History\csv\LorenzoVittori_artist.csv"
)

# Database persistente artista -> lista di generi (tag Last.fm).
# Viene aggiornato in-place: gli artisti già presenti non vengono
# richiamati di nuovo su Last.fm.
DB_FILE = REPO_FOLDER / "db_genre_by_artist.json"

# Quanti tag (in ordine di peso) tenere per artista
TOP_TAGS = 5

# Lookup manuale genere -> macrogenere (lo popoli tu a mano).
# Usato solo per arricchire il CSV di riepilogo, non modifica DB_FILE.
GENRE_MACRO_MAP_FILE = REPO_FOLDER / "genre_macro_map.json"

# CSV di riepilogo (artista, genere, macrogenere) per revisione rapida
REVIEW_CSV = REPO_FOLDER / (
    r"Data\Spotify Extended Streaming History\csv"
    r"\LorenzoVittori_artist_genres.csv"
)

# Pausa fra una richiesta e l'altra (Last.fm consiglia max 5 req/s,
# stiamo larghi per non rischiare rate-limit)
REQUEST_DELAY_SEC = 0.25

# Ogni quanti artisti nuovi salvare DB_FILE su disco
SAVE_EVERY = 25

# Tag di Last.fm che non sono generi musicali e vanno scartati
TAG_BLACKLIST = {
    "seen live", "favorite", "favorite songs", "favourites", "favorites",
    "beautiful", "awesome", "love", "amazing", "male vocalist",
    "female vocalist", "male vocalists", "female vocalists",
    "under 2000 listeners", "spotify", "check out", "genius",
}

# API key Last.fm gratuita: https://www.last.fm/api/account/create
# Va messa in .env (LASTFM_API_KEY=...), oppure impostata come
# variabile d'ambiente nella shell.
LASTFM_API_KEY = os.environ.get(
    "LASTFM_API_KEY", "INSERISCI_QUI_LA_TUA_API_KEY"
)

LASTFM_ENDPOINT = "https://ws.audioscrobbler.com/2.0/"

# Session riutilizzata per tutte le chiamate: mantiene la connessione
# keep-alive invece di rifare l'handshake TCP/TLS ad ogni richiesta
session = requests.Session()


# ============================================================
# DATABASE artista -> genere
# ============================================================

def load_db():

    if DB_FILE.exists():
        with open(DB_FILE, "r", encoding="utf-8-sig") as f:
            return json.load(f)

    return {}


def save_db(db):

    DB_FILE.parent.mkdir(parents=True, exist_ok=True)

    with open(DB_FILE, "w", encoding="utf-8") as f:
        json.dump(db, f, ensure_ascii=False, indent=2, sort_keys=True)


# ============================================================
# LAST.FM API
# ============================================================

def get_top_tags(artist_name):
    """Ritorna fino a TOP_TAGS tag per l'artista (lista di stringhe,
    ordinata dal più pesato al meno pesato), oppure [] se Last.fm
    non ha tag utilizzabili."""

    params = {
        "method": "artist.gettoptags",
        "artist": artist_name,
        "api_key": LASTFM_API_KEY,
        "format": "json",
        "autocorrect": 1,
    }

    response = session.get(LASTFM_ENDPOINT, params=params, timeout=10)

    if not response.ok:
        raise Exception(
            f"Last.fm API error {response.status_code}: {response.text}"
        )

    data = response.json()

    # Es. artista non trovato / nome ambiguo
    if "error" in data:
        return []

    tags = data.get("toptags", {}).get("tag", [])

    result = []

    # I tag arrivano già ordinati per peso decrescente
    for tag in tags:

        name = tag.get("name", "").strip().lower()

        if not name or name in TAG_BLACKLIST:
            continue

        # Ignora tag che sono semplicemente il nome dell'artista
        if name == artist_name.strip().lower():
            continue

        result.append(name)

        if len(result) >= TOP_TAGS:
            break

    return result


# ============================================================
# MACROGENERE (lookup manuale, solo per il CSV di riepilogo)
# ============================================================

def load_genre_macro_map():

    if GENRE_MACRO_MAP_FILE.exists():
        with open(GENRE_MACRO_MAP_FILE, "r", encoding="utf-8") as f:
            return json.load(f)

    return {}


def resolve_macro_genere(tags, genre_macro_map):
    """Prende il primo tag (il più pesato) che trova una corrispondenza
    in genre_macro_map. tags è la lista completa di generi dell'artista."""

    for genere in tags:

        # Match esatto
        if genere in genre_macro_map:
            return genre_macro_map[genere]

        # Fallback: la chiave della mappa è contenuta nel genere
        # (es. "dance pop" -> chiave "pop")
        for key, macro in genre_macro_map.items():
            if key in genere:
                return macro

    return ""


# ============================================================
# MAIN
# ============================================================

def main():

    if LASTFM_API_KEY == "INSERISCI_QUI_LA_TUA_API_KEY":
        raise SystemExit(
            "Manca la API key di Last.fm.\n"
            "Registrane una gratis su "
            "https://www.last.fm/api/account/create\n"
            "e impostala nella variabile LASTFM_API_KEY in questo file, "
            "oppure nell'env var LASTFM_API_KEY."
        )

    db = load_db()

    with open(INPUT_CSV, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        artisti = [row["Artista"] for row in reader]

    nuovi = [a for a in artisti if a not in db]

    print(f"Artisti nel CSV       : {len(artisti)}")
    print(f"Già nel database       : {len(artisti) - len(nuovi)}")
    print(f"Nuovi da interrogare   : {len(nuovi)}")
    print()

    trovati = 0
    non_trovati = 0

    for i, artista in enumerate(nuovi, 1):

        try:
            tags = get_top_tags(artista)
        except Exception as e:
            print(f"  Errore su '{artista}': {e}")
            tags = []

        db[artista] = tags

        if tags:
            trovati += 1
        else:
            non_trovati += 1

        time.sleep(REQUEST_DELAY_SEC)

        if i % SAVE_EVERY == 0:
            save_db(db)
            print(f"  ...{i}/{len(nuovi)} nuovi artisti (db salvato)")

    save_db(db)

    # --------------------------------------------------------
    # CSV di riepilogo: artista, genere (da db), macrogenere
    # (da genre_macro_map.json)
    # --------------------------------------------------------

    genre_macro_map = load_genre_macro_map()

    REVIEW_CSV.parent.mkdir(parents=True, exist_ok=True)

    with open(REVIEW_CSV, "w", newline="", encoding="utf-8-sig") as f:

        writer = csv.DictWriter(
            f, fieldnames=["Artista", "Genere", "TuttiITag", "GenereMacro"]
        )
        writer.writeheader()

        for artista in artisti:

            tags = db.get(artista, [])
            macro = resolve_macro_genere(tags, genre_macro_map)

            writer.writerow({
                "Artista": artista,
                "Genere": tags[0] if tags else "",
                "TuttiITag": ", ".join(tags),
                "GenereMacro": macro,
            })

    print("\n" + "=" * 60)
    print("ELABORAZIONE COMPLETATA")
    print("=" * 60)
    print(f"Nuovi artisti interrogati : {len(nuovi)}")
    print(f"  con genere trovato      : {trovati}")
    print(f"  senza genere            : {non_trovati}")
    print(f"Database artisti totale   : {len(db)}")
    print(f"DB (artista -> generi)    : {DB_FILE}")
    print(f"CSV di riepilogo          : {REVIEW_CSV}")
    print("=" * 60)


if __name__ == "__main__":
    main()
