import csv
import json

from pathlib import Path


# ============================================================
# CONFIGURAZIONE
# ============================================================

REPO_FOLDER = Path(__file__).resolve().parent

# CSV di input: artisti seguiti su Spotify (colonna "name")
INPUT_CSV = REPO_FOLDER / (
    r"Data\Spotify Extended Streaming History\csv"
    r"\LorenzoVittori_followed_artists.csv"
)

# Database artista -> lista di generi (tag Last.fm), popolato da
# lastfm_genres.py
DB_FILE = REPO_FOLDER / "db_genre_by_artist.json"

# CSV di output
OUTPUT_CSV = REPO_FOLDER / (
    r"Data\Spotify Extended Streaming History\csv"
    r"\LorenzoVittori_followed_artists_genres.csv"
)


# ============================================================
# CARICAMENTO DATI
# ============================================================

def load_db():

    with open(DB_FILE, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def load_followed_artists():

    with open(INPUT_CSV, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        return [row["name"].strip() for row in reader if row.get("name")]


# ============================================================
# MAIN
# ============================================================

def main():

    db = load_db()

    # Indice case-insensitive, di riserva per quando il nome
    # nell'export "followed artists" non combacia esattamente
    # (maiuscole/minuscole diverse) con quello nello storico ascolti
    db_lower = {nome.lower(): nome for nome in db}

    artisti = load_followed_artists()

    print(f"Artisti seguiti      : {len(artisti)}")

    trovati = 0
    non_trovati = 0

    OUTPUT_CSV.parent.mkdir(parents=True, exist_ok=True)

    with open(OUTPUT_CSV, "w", newline="", encoding="utf-8-sig") as f:

        writer = csv.DictWriter(
            f, fieldnames=["ArtistName", "MainGenre", "SubGenre"]
        )
        writer.writeheader()

        for artista in artisti:

            tags = db.get(artista)

            if tags is None:
                nome_db = db_lower.get(artista.lower())
                tags = db.get(nome_db, []) if nome_db else []

            if tags:
                trovati += 1
            else:
                non_trovati += 1

            writer.writerow({
                "ArtistName": artista,
                "MainGenre": tags[0] if len(tags) > 0 else "",
                "SubGenre": tags[1] if len(tags) > 1 else "",
            })

    print("\n" + "=" * 60)
    print("ELABORAZIONE COMPLETATA")
    print("=" * 60)
    print(f"Con genere trovato nel DB Last.fm : {trovati}")
    print(f"Senza corrispondenza nel DB       : {non_trovati}")
    print(f"CSV di output                     : {OUTPUT_CSV}")
    print("=" * 60)


if __name__ == "__main__":
    main()
