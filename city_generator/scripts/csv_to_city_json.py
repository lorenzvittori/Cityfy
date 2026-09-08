#!/usr/bin/env python3
"""
Genera il JSON della struttura della citta' (formato manual_city.json)
a partire dal CSV degli artisti seguiti su Spotify, raggruppati per genere.

Mappatura:
  - Quartiere  <- MainGenre (macro-genere)
  - Building   <- SubGenre (sotto-genere), unico all'interno del quartiere
  - Popolazione di un building <- numero di artisti che hanno quella
                                   combinazione (MainGenre, SubGenre)

Uso:
    python scripts/csv_to_city_json.py
    python scripts/csv_to_city_json.py --input percorso\al\file.csv --output manual_city.json

Se non specificato, --input punta al percorso indicato dall'utente:
    spotify_data\\Data\\Spotify Extended Streaming History\\csv\\LorenzoVittori_followed_artists_genres.csv
--output di default e' "spotify_city.json" nella root del progetto, per non
sovrascrivere il manual_city.json di esempio gia' presente.
"""

from __future__ import annotations

import argparse
import colorsys
import csv
import hashlib
import json
import sys
from collections import defaultdict
from pathlib import Path

DEFAULT_INPUT = Path(
    "../spotify_data/Data/Spotify Extended Streaming History/csv/"
    "LorenzoVittori_followed_artists_genres.csv"
)
DEFAULT_OUTPUT = Path("spotify_city.json")

# Possibili nomi (case/spazi/underscore-insensitive) delle colonne nel CSV.
MAIN_GENRE_CANDIDATES = ["maingenre", "main_genre", "macrogenere", "macro_genere", "genremain"]
SUB_GENRE_CANDIDATES = ["subgenre", "sub_genre", "sottogenere", "sotto_genere", "genresub"]
ARTIST_CANDIDATES = ["artist", "artistname", "artist_name", "nomeartista", "artista", "name"]


def normalize(header: str) -> str:
    return "".join(ch for ch in header.lower().strip() if ch.isalnum())


def find_column(fieldnames: list[str], candidates: list[str]) -> str | None:
    normalized = {normalize(f): f for f in fieldnames}
    for candidate in candidates:
        if candidate in normalized:
            return normalized[candidate]
    return None


def genre_to_hex_color(name: str) -> str:
    """Colore deterministico e leggibile a partire dal nome del macro-genere."""
    digest = hashlib.sha1(name.encode("utf-8")).hexdigest()
    hue = (int(digest[:8], 16) % 360) / 360.0
    r, g, b = colorsys.hls_to_rgb(hue, 0.55, 0.55)
    return "#{:02x}{:02x}{:02x}".format(round(r * 255), round(g * 255), round(b * 255))


def slugify(name: str) -> str:
    """Rende il nome del genere un id compatto e leggibile (usato come chiave JSON)."""
    cleaned = "".join(ch if ch.isalnum() else "_" for ch in name.strip())
    while "__" in cleaned:
        cleaned = cleaned.replace("__", "_")
    return cleaned.strip("_") or "unknown"


def build_city(rows: list[dict], main_col: str, sub_col: str, artist_col: str | None) -> dict:
    # (main_genre, sub_genre) -> set di artisti (o contatore righe se manca la colonna artista)
    groups: dict[tuple[str, str], set[str] | int] = {}

    for row in rows:
        main_genre = (row.get(main_col) or "").strip()
        sub_genre = (row.get(sub_col) or "").strip()
        if not main_genre or not sub_genre:
            continue

        key = (main_genre, sub_genre)
        if artist_col:
            artist = (row.get(artist_col) or "").strip()
            if not artist:
                continue
            groups.setdefault(key, set()).add(artist)
        else:
            groups[key] = groups.get(key, 0) + 1

    city: dict[str, dict] = {}
    for (main_genre, sub_genre), value in groups.items():
        population = len(value) if isinstance(value, set) else value
        if population <= 0:
            continue

        quartiere_id = slugify(main_genre)
        building_id = f"{quartiere_id}__{slugify(sub_genre)}"

        if quartiere_id not in city:
            city[quartiere_id] = {
                "color": genre_to_hex_color(main_genre),
                "buildings": {},
            }
        city[quartiere_id]["buildings"][building_id] = population

    return city


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT, help="Percorso del CSV di input.")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="Percorso del JSON di output.")
    args = parser.parse_args()

    if not args.input.exists():
        print(f"Errore: file CSV non trovato: {args.input}", file=sys.stderr)
        return 1

    with args.input.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if not reader.fieldnames:
            print("Errore: il CSV non ha un header leggibile.", file=sys.stderr)
            return 1

        main_col = find_column(reader.fieldnames, MAIN_GENRE_CANDIDATES)
        sub_col = find_column(reader.fieldnames, SUB_GENRE_CANDIDATES)
        artist_col = find_column(reader.fieldnames, ARTIST_CANDIDATES)

        if not main_col or not sub_col:
            print(
                "Errore: non trovo le colonne MainGenre/SubGenre nel CSV.\n"
                f"Colonne disponibili: {reader.fieldnames}",
                file=sys.stderr,
            )
            return 1

        rows = list(reader)

    if artist_col is None:
        print(
            "Attenzione: nessuna colonna artista riconosciuta, la popolazione sara' "
            "il numero di righe per combinazione MainGenre/SubGenre (nessuna deduplica)."
        )

    city = build_city(rows, main_col, sub_col, artist_col)

    if not city:
        print("Attenzione: nessun quartiere generato (dati vuoti o colonne non valide).", file=sys.stderr)
        return 1

    args.output.write_text(json.dumps(city, indent=4, ensure_ascii=False), encoding="utf-8")

    n_buildings = sum(len(q["buildings"]) for q in city.values())
    n_population = sum(sum(q["buildings"].values()) for q in city.values())
    print(f"Colonne usate -> MainGenre: '{main_col}', SubGenre: '{sub_col}', Artista: '{artist_col}'")
    print(f"Generati {len(city)} quartieri, {n_buildings} building, popolazione totale {n_population}.")
    print(f"Scritto: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
