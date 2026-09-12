import json
import csv

from pathlib import Path
from dataclasses import dataclass


# ============================================================
# CONFIGURAZIONE
# ============================================================

REPO_FOLDER = Path(__file__).resolve().parent

JSON_FILES_FOLDER = Path(
    r"Data\Spotify Extended Streaming History\LorenzoVittori"
)

OUTPUT_FOLDER = Path(
    r"Data\Spotify Extended Streaming History\csv"
)

OUTPUT_TRACK = "LorenzoVittori_track.csv"
OUTPUT_ARTIST = "LorenzoVittori_artist.csv"


# ------------------------------------------------------------
# INTERVALLO ANNI
# ------------------------------------------------------------

ANNI = [2018, 2026]


# ------------------------------------------------------------
# CONFIGURAZIONE ASCOLTO
# ------------------------------------------------------------

# Tempo minimo di ascolto considerato come "ascolto filtrato"
FILTER_SEC = 60


# ============================================================
# VALIDAZIONE ANNI
# ============================================================

ANNO_START = ANNI[0]
ANNO_END = ANNI[1]

if ANNO_START > ANNO_END:
    raise ValueError(
        f"Intervallo anni non valido: {ANNO_START} > {ANNO_END}"
    )


# ============================================================
# CHIAVI DEL JSON SPOTIFY
# ============================================================

@dataclass(frozen=True)
class DATA_JSON:
    TIMESTAMP: str = "ts"
    MILLISEC: str = "ms_played"
    TRACK: str = "master_metadata_track_name"
    ARTIST: str = "master_metadata_album_artist_name"
    ALBUM: str = "master_metadata_album_album_name"
    TRACK_ID: str = "spotify_track_uri"


# ============================================================
# PERCORSI
# ============================================================

json_folder = REPO_FOLDER / JSON_FILES_FOLDER

output_track_path = REPO_FOLDER / OUTPUT_FOLDER / OUTPUT_TRACK
output_artist_path = REPO_FOLDER / OUTPUT_FOLDER / OUTPUT_ARTIST


# ============================================================
# LETTURA FILE JSON
# ============================================================

all_json_files = sorted(
    json_folder.glob("Streaming_History_Audio_*.json")
)


if not all_json_files:
    raise FileNotFoundError(
        f"Nessun file JSON Audio trovato nella cartella:\n"
        f"{json_folder}"
    )


# ============================================================
# SELEZIONE FILE IN BASE ALL'INTERVALLO DI ANNI
# ============================================================

json_files = []

for json_file in all_json_files:

    # Esempio:
    # Streaming_History_Audio_2025.json
    # Streaming_History_Audio_2025_1.json

    filename = json_file.stem

    try:
        # Prende il primo numero dopo "Streaming_History_Audio_"
        year_part = filename.replace(
            "Streaming_History_Audio_",
            ""
        ).split("_")[0]

        year = int(year_part)

    except ValueError:
        continue

    if ANNO_START <= year <= ANNO_END:
        json_files.append(json_file)


# ============================================================
# CONTROLLO FILE SELEZIONATI
# ============================================================

if not json_files:
    raise FileNotFoundError(
        f"Nessun file Audio trovato per l'intervallo "
        f"{ANNO_START}-{ANNO_END}"
    )


print("\n" + "=" * 60)
print("FILE SELEZIONATI")
print("=" * 60)

print(f"Intervallo anni: {ANNO_START} - {ANNO_END}")
print(f"File trovati: {len(json_files)}")

for json_file in json_files:
    print(f"  - {json_file.name}")


# ============================================================
# ELABORAZIONE
# ============================================================

rows_track = {}
rows_artist = {}

total_records = 0
valid_records = 0
ignored_records = 0


for json_file in json_files:

    print(f"\nElaborazione: {json_file.name}")

    with open(
        json_file,
        "r",
        encoding="utf-8"
    ) as f:

        data = json.load(f)


    for entry in data:

        total_records += 1


        # ====================================================
        # LETTURA DATI
        # ====================================================

        track_name = entry.get(DATA_JSON.TRACK)
        artist = entry.get(DATA_JSON.ARTIST)
        album = entry.get(DATA_JSON.ALBUM)
        track_uri = entry.get(DATA_JSON.TRACK_ID)

        # Se ms_played è None consideriamo 0
        milli_sec = entry.get(DATA_JSON.MILLISEC) or 0


        # ====================================================
        # IGNORA RECORD NON MUSICALI / INCOMPLETI
        # ====================================================

        if (
            not track_uri
            or not track_name
            or not artist
        ):
            ignored_records += 1
            continue


        valid_records += 1


        # ====================================================
        # ASCOLTO FILTRATO
        # ====================================================

        is_filtered = (
            milli_sec >= FILTER_SEC * 1000
        )


        # ====================================================
        # AGGREGAZIONE PER TRACCIA
        # ====================================================

        if track_uri not in rows_track:

            rows_track[track_uri] = {

                "artist": artist,

                "track_name": track_name,

                "album": album,

                "milli_sec": milli_sec,

                "ascolti": 1,

                "ascolti_filter": int(
                    is_filtered
                ),

            }

        else:

            rows_track[track_uri][
                "milli_sec"
            ] += milli_sec

            rows_track[track_uri][
                "ascolti"
            ] += 1

            rows_track[track_uri][
                "ascolti_filter"
            ] += int(is_filtered)


        # ====================================================
        # AGGREGAZIONE PER ARTISTA
        # ====================================================

        if artist not in rows_artist:

            rows_artist[artist] = {

                "artist": artist,

                # Numero totale di eventi di ascolto
                "ascolti": 1,

                # Numero totale di eventi >= FILTER_SEC
                "ascolti_filter": int(
                    is_filtered
                ),

                # Tracce uniche ascoltate
                "tracce_uniche": {
                    track_uri
                },

                # Tracce uniche con almeno un ascolto
                # >= FILTER_SEC
                "tracce_uniche_filter": (
                    {track_uri}
                    if is_filtered
                    else set()
                ),

            }

        else:

            # ------------------------------------------------
            # ASCOLTI
            # ------------------------------------------------

            rows_artist[artist][
                "ascolti"
            ] += 1


            # ------------------------------------------------
            # ASCOLTI FILTER
            # ------------------------------------------------

            rows_artist[artist][
                "ascolti_filter"
            ] += int(is_filtered)


            # ------------------------------------------------
            # TRACCE UNICHE
            # ------------------------------------------------

            rows_artist[artist][
                "tracce_uniche"
            ].add(track_uri)


            # ------------------------------------------------
            # TRACCE UNICHE FILTER
            # ------------------------------------------------

            if is_filtered:

                rows_artist[artist][
                    "tracce_uniche_filter"
                ].add(track_uri)


# ============================================================
# CONVERSIONE SET → NUMERO
# ============================================================

for artist_data in rows_artist.values():

    artist_data["tracce_uniche"] = len(
        artist_data["tracce_uniche"]
    )

    artist_data["tracce_uniche_filter"] = len(
        artist_data["tracce_uniche_filter"]
    )


# ============================================================
# ORDINAMENTO
# ============================================================

sorted_rows_tracks = sorted(
    rows_track.values(),
    key=lambda row: row["ascolti"],
    reverse=True
)


sorted_rows_artist = sorted(
    rows_artist.values(),
    key=lambda row: row["ascolti"],
    reverse=True
)


# ============================================================
# CREAZIONE CARTELLA OUTPUT
# ============================================================

output_track_path.parent.mkdir(
    parents=True,
    exist_ok=True
)


# ============================================================
# CREAZIONE CSV TRACK
# ============================================================

with open(
    output_track_path,
    "w",
    newline="",
    encoding="utf-8-sig"
) as f:

    writer = csv.writer(f)

    writer.writerow([
        "Artista",
        "Traccia",
        "Album",
        "MilliSec",
        "Ascolti",
        "AscoltiFilter"
    ])


    for row in sorted_rows_tracks:

        writer.writerow([
            row["artist"],
            row["track_name"],
            row["album"],
            row["milli_sec"],
            row["ascolti"],
            row["ascolti_filter"]
        ])


# ============================================================
# CREAZIONE CSV ARTIST
# ============================================================

with open(
    output_artist_path,
    "w",
    newline="",
    encoding="utf-8-sig"
) as f:

    writer = csv.writer(f)

    writer.writerow([
        "Artista",
        "Ascolti",
        "AscoltiFilter",
        "TracceUniche",
        "TracceUnicheFilter"
    ])


    for row in sorted_rows_artist:

        writer.writerow([
            row["artist"],
            row["ascolti"],
            row["ascolti_filter"],
            row["tracce_uniche"],
            row["tracce_uniche_filter"]
        ])


# ============================================================
# RIEPILOGO
# ============================================================

print("\n" + "=" * 60)
print("ELABORAZIONE COMPLETATA")
print("=" * 60)

print(f"Intervallo anni     : {ANNO_START} - {ANNO_END}")
print(f"File JSON elaborati : {len(json_files)}")
print(f"Record totali       : {total_records}")
print(f"Record validi       : {valid_records}")
print(f"Record ignorati     : {ignored_records}")
print(f"Tracce uniche       : {len(rows_track)}")
print(f"Artisti unici       : {len(rows_artist)}")
print(f"CSV tracce          : {output_track_path}")
print(f"CSV artisti         : {output_artist_path}")

print("=" * 60)