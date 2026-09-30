#!/usr/bin/env python3
"""
mb_artist_genres.py -- associazione artista -> generi ufficiali MusicBrainz.

Legge i CSV di data_raw/ prodotti da fetch_extract.py dai dump PostgreSQL di
MusicBrainz (senza server PostgreSQL):
  mbdump.tar.bz2          -> artist.csv, genre.csv
  mbdump-derived.tar.bz2  -> tag.csv, artist_tag.csv
dello STESSO snapshot (data_raw/snapshot.json), e produce in --out-dir
(default: output/ accanto allo script; file sovrascritti):
  artist_genres.csv          CSV lungo, UTF-8, RFC 4180
                             colonne: artist_mbid, artist_name, genre_mbid, genre_name, votes
  artist_genres_report.md    report con versione dello snapshot, logica replicata,
                             assunzioni/decisioni, statistiche e controlli
  ATTRIBUTION.txt            attribuzione e licenza (CC BY-NC-SA 3.0)

Logica replicata (musicbrainz-server, commit 3468ea32dc46795799b48bc16cef475205bb30d5):
  lib/MusicBrainz/Server/Data/EntityTag.pm, find_genres_for_entities (righe 131-161):
      FROM artist_tag entity_tag
      JOIN tag   ON tag.id = entity_tag.tag
      JOIN genre ON tag.name = genre.name
  lib/MusicBrainz/Server/WebService/Serializer/JSON/2/Utils.pm, righe 332-338:
      solo le righe con count > 0; id = genre.gid, name = genre.name.

Solo libreria standard. Python >= 3.12.


USO

python fetch_extract.py                     # una volta: scarica/estrae in data_raw/
python mb_artist_genres.py [--genres-csv output/genres.csv]
"""

from __future__ import annotations

import argparse
import csv
import datetime as _dt
import os
import re
import statistics
import sys
import time
from collections import Counter
from pathlib import Path

import mbraw

# --------------------------------------------------------------------------
# Costanti di schema (admin/sql/CreateTables.sql, commit 3468ea3; schema 31)
# --------------------------------------------------------------------------
SOURCE_COMMIT = "3468ea32dc46795799b48bc16cef475205bb30d5"
VERIFIED_SCHEMA_SEQUENCE = "31"  # lib/DBDefs.pm.sample: sub DB_SCHEMA_SEQUENCE { 31 }

# Colonne lette da data_raw/, per nome (il numero di colonne di ogni riga e'
# verificato da fetch_extract.py rispetto a admin/sql/CreateTables.sql).
TABLE_COLUMNS = {
    "artist": ["id", "gid", "name"],
    "genre": ["id", "gid", "name"],
    "tag": ["id", "name"],
    "artist_tag": ["artist", "tag", "count"],
}
UUID_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")

CSV_NAME = "artist_genres.csv"
REPORT_NAME = "artist_genres_report.md"
ATTRIBUTION_NAME = "ATTRIBUTION.txt"
CSV_HEADER = ["artist_mbid", "artist_name", "genre_mbid", "genre_name", "votes"]


class DumpError(RuntimeError):
    """Errore bloccante: lo script si ferma senza produrre output definitivi."""


# --------------------------------------------------------------------------
# Log
# --------------------------------------------------------------------------
_T0 = time.monotonic()


def log(msg: str) -> None:
    el = time.monotonic() - _T0
    print(f"[{int(el // 60):4d}m{el % 60:04.1f}s] {msg}", file=sys.stderr, flush=True)


# --------------------------------------------------------------------------
# Lettura di data_raw/
# --------------------------------------------------------------------------
def parse_int(raw: str | None, table: str, col: str) -> int:
    try:
        return int(raw)
    except (TypeError, ValueError):
        raise DumpError(f"{table}.csv: valore non intero in colonna {col}: {str(raw)[:50]!r}")


def parse_uuid(val: str | None, table: str) -> str:
    if val is None or not UUID_RE.match(val):
        raise DumpError(f"{table}.csv: gid non valido: {str(val)[:50]!r}")
    return val


def table_rows(data_raw: Path, snap: dict, table: str):
    log(f"leggo {table}.csv")
    return mbraw.read_rows(data_raw, snap, table, TABLE_COLUMNS[table])


# --------------------------------------------------------------------------
# Passi di elaborazione
# --------------------------------------------------------------------------
class State:
    def __init__(self) -> None:
        # genre.id -> (gid, name) e name -> genre.id  (tabella genre: poche migliaia di righe)
        self.genres: dict[int, tuple[str, str]] = {}
        self.genre_by_name: dict[str, int] = {}
        # tag.id -> genre.id, solo per i tag il cui nome coincide con un genere
        self.tag_to_genre: dict[int, int] = {}
        self.tags_total = 0
        # artist.id -> lista di (genre.id, count) con count > 0
        self.by_artist: dict[int, list[tuple[int, int]]] = {}
        self.artist_tag_rows = 0
        self.genre_rows_total = 0
        self.genre_rows_nonpositive = 0
        self.artist_rows_total = 0


def read_genre(st: State):
    def handler(rows):
        for p in rows:
            gid_int = parse_int(p[0], "genre", "id")
            gid = parse_uuid(p[1], "genre")
            name = p[2]
            if name is None:
                raise DumpError("mbdump/genre: name NULL.")
            if name in st.genre_by_name:
                raise DumpError(f"mbdump/genre: nome duplicato {name!r}.")
            st.genres[gid_int] = (gid, name)
            st.genre_by_name[name] = gid_int
        lower = Counter(n.lower() for n in st.genre_by_name)
        dup = [n for n, c in lower.items() if c > 1]
        if dup:  # impossibile con l'indice UNIQUE genre_idx_name ON genre (LOWER(name))
            raise DumpError(f"mbdump/genre: nomi duplicati a meno di maiuscole: {dup[:10]}")
        if not st.genres:
            raise DumpError("mbdump/genre: tabella vuota.")
        log(f"genre: {len(st.genres)} generi ufficiali.")
    return handler


def read_tag(st: State):
    def handler(rows):
        genre_seen: dict[int, int] = {}
        for p in rows:
            st.tags_total += 1
            name = p[1]
            g = st.genre_by_name.get(name)  # uguaglianza esatta: JOIN genre ON tag.name = genre.name
            if g is None:
                continue
            tid = parse_int(p[0], "tag", "id")
            if g in genre_seen:  # impossibile con l'indice UNIQUE tag_idx_name ON tag (name)
                raise DumpError(f"Il genere {name!r} corrisponde a piu' tag "
                                f"({genre_seen[g]}, {tid}).")
            genre_seen[g] = tid
            st.tag_to_genre[tid] = g
        log(f"tag: {st.tags_total} tag, di cui {len(st.tag_to_genre)} con nome di un genere.")
    return handler


def read_artist_tag(st: State):
    def handler(rows):
        by_artist = st.by_artist
        t2g = st.tag_to_genre
        for p in rows:
            st.artist_tag_rows += 1
            g = t2g.get(int(p[1]))
            if g is None:
                continue
            st.genre_rows_total += 1
            c = int(p[2])
            if c <= 0:
                st.genre_rows_nonpositive += 1
                continue
            a = int(p[0])
            lst = by_artist.get(a)
            if lst is None:
                by_artist[a] = [(g, c)]
            else:
                lst.append((g, c))
        log(f"artist_tag: {st.artist_tag_rows} righe; {st.genre_rows_total} con un genere, "
            f"{st.genre_rows_nonpositive} escluse (count <= 0); "
            f"{len(st.by_artist)} artisti con almeno un genere.")
    return handler


class Stats:
    def __init__(self) -> None:
        self.rows = 0
        self.artists = 0
        self.per_artist = Counter()          # k -> numero di artisti con k generi
        self.genre_artists = Counter()       # genre.id -> numero di artisti
        self.genre_votes = Counter()         # genre.id -> somma dei voti
        self.duplicates = 0
        self.bad_genre_mbid = 0


def write_artists(st: State, writer, stats: Stats, valid_genre_gids: set[str]):
    """Legge mbdump/artist e scrive il CSV nell'ordine delle righe del dump."""
    def handler(rows):
        by_artist = st.by_artist
        genres = st.genres
        for p in rows:
            st.artist_rows_total += 1
            aid = int(p[0])
            pairs = by_artist.pop(aid, None)  # pop: libera memoria mentre si scrive
            if pairs is None:
                continue
            mbid = parse_uuid(p[1], "artist")
            name = p[2]
            if name is None:
                raise DumpError(f"mbdump/artist: name NULL per l'artista {mbid}.")
            gids_here = [g for g, _ in pairs]
            if len(set(gids_here)) != len(gids_here):
                stats.duplicates += len(gids_here) - len(set(gids_here))
            # ordinamento: votes decrescente, poi nome del genere (ordine dei code point)
            pairs.sort(key=lambda gc: (-gc[1], genres[gc[0]][1]))
            for g, c in pairs:
                ggid, gname = genres[g]
                if ggid not in valid_genre_gids:
                    stats.bad_genre_mbid += 1
                writer.writerow((mbid, name, ggid, gname, c))
                stats.genre_artists[g] += 1
                stats.genre_votes[g] += c
            stats.rows += len(pairs)
            stats.artists += 1
            stats.per_artist[len(pairs)] += 1
        log(f"artist: {st.artist_rows_total} artisti letti, {stats.artists} scritti.")
    return handler


# --------------------------------------------------------------------------
# Confronto con genres.csv
# --------------------------------------------------------------------------
def load_genres_csv(path: str) -> set[str]:
    with open(path, newline="", encoding="utf-8-sig") as fh:
        reader = csv.DictReader(fh)
        if reader.fieldnames is None or "genre_mbid" not in reader.fieldnames:
            raise DumpError(f"{path}: colonna 'genre_mbid' assente "
                            f"(intestazione: {reader.fieldnames}).")
        out = set()
        for row in reader:
            v = (row["genre_mbid"] or "").strip().lower()
            if v:
                out.add(v)
    return out


# --------------------------------------------------------------------------
# Report e attribuzione
# --------------------------------------------------------------------------
def md_escape(s: str) -> str:
    return s.replace("|", "\\|")


def write_report(path, args, core_meta, derived_meta, st: State, stats: Stats,
                 csv_cmp, schema_warning):
    ks = sorted(stats.per_artist)
    values = []
    for k in ks:
        values.extend([k] * stats.per_artist[k])  # lista di interi: memoria O(artisti)
    mean = statistics.fmean(values)
    median = statistics.median(values)
    del values
    top = sorted(stats.genre_artists.items(),
                 key=lambda kv: (-kv[1], st.genres[kv[0]][1]))[:20]
    genres_used = len(stats.genre_artists)
    genres_with_tag = len(st.tag_to_genre)

    L = []
    w = L.append
    w("# artist_genres — report\n")
    w(f"Generato il {_dt.datetime.now(_dt.timezone.utc):%Y-%m-%d %H:%M:%S} UTC "
      f"da `mb_artist_genres.py`.\n")

    w("## Snapshot\n")
    w("| Archivio | File | TIMESTAMP | SCHEMA_SEQUENCE | REPLICATION_SEQUENCE |")
    w("|---|---|---|---|---|")
    w(f"| core | `{os.path.basename(core_meta['source'])}` | {core_meta['TIMESTAMP']} | "
      f"{core_meta['SCHEMA_SEQUENCE']} | {core_meta['REPLICATION_SEQUENCE']} |")
    w(f"| derived | `{os.path.basename(derived_meta['source'])}` | {derived_meta['TIMESTAMP']} | "
      f"{derived_meta['SCHEMA_SEQUENCE']} | {derived_meta['REPLICATION_SEQUENCE']} |")
    w("")
    w("TIMESTAMP e SCHEMA_SEQUENCE coincidono tra i due archivi (verificato; in caso "
      "contrario lo script si ferma).\n")
    if schema_warning:
        w(f"**Attenzione:** {schema_warning}\n")

    w("## Logica replicata (tag → genere)\n")
    w(f"Fonte: repository `metabrainz/musicbrainz-server`, commit `{SOURCE_COMMIT}`.\n")
    w("1. Il campo `genres` del web service per gli artisti è prodotto da "
      "`find_genres_for_entities` in `lib/MusicBrainz/Server/Data/EntityTag.pm` "
      "(righe 131-161), chiamato da `_tags` in "
      "`lib/MusicBrainz/Server/ControllerBase/WS/2.pm` (righe 227-248). La query è "
      "`FROM artist_tag entity_tag JOIN tag ON tag.id = entity_tag.tag "
      "JOIN genre ON tag.name = genre.name` (riga 141). Per gli artisti la tabella è "
      "`artist_tag` (`lib/MusicBrainz/Server/Data/Role/Tag.pm`, default "
      "`type . '_tag'`, righe 12-16).")
    w("2. Il serializzatore JSON tiene solo gli elementi con `count > 0` ed espone "
      "`id = genre.gid`, `name = genre.name`, `count` "
      "(`lib/MusicBrainz/Server/WebService/Serializer/JSON/2/Utils.pm`, righe 332-338).")
    w("3. Un genere ha un solo tag associato. Il collegamento è un'uguaglianza esatta "
      "di nomi. `tag.name` è unico (`CREATE UNIQUE INDEX tag_idx_name ON tag (name)`, "
      "`admin/sql/CreateIndexes.sql` riga 667). `genre.name` è unico anche a meno di "
      "maiuscole (`genre_idx_name ON genre (LOWER(name))`, riga 117). La pagina del "
      "genere mostra, sotto \"Associated tags\", il solo \"Primary tag\" uguale a "
      "`genre.name` (`root/genre/GenreIndex.js`, righe 31-36).")
    w("4. Gli alias dei generi (`genre_alias`) non sono usati per i generi delle "
      "entità. Compaiono solo in `lib/MusicBrainz/Server/Data/Tag.pm` (righe 17-35: "
      "`coalesce(genre.id, genre_alias.genre)`), che collega un tag al genere nelle "
      "pagine dei tag, ma non in `find_genres_for_entities`. Qui quindi non sono "
      "replicati.")
    w("5. Replica in Python: il nome del tag, dopo l'unescape COPY, è confrontato con "
      "`==` con i nomi di `genre`. In PostgreSQL, con una collazione deterministica "
      "(il default), stringhe non uguali byte per byte non sono mai uguali. Assumo "
      "quindi che la collazione di default del database MusicBrainz sia "
      "deterministica.\n")
    w(f"Tag totali: {st.tags_total:,}; tag con nome di un genere: {genres_with_tag:,} "
      f"su {len(st.genres):,} generi; generi senza alcun tag omonimo: "
      f"{len(st.genres) - genres_with_tag:,}.\n")

    w("## Assunzioni e decisioni\n")
    w("- `votes = artist_tag.count`. Significato (verificato, non assunto): è la "
      "somma dei voti, +1 per ogni upvote e −1 per ogni downvote "
      "(`admin/sql/CreateFunctions.sql`: `update_aggregate_tag_count` righe "
      "1580-1594; trigger righe 1619-1668; il commento alle righe 1602-1604 precisa "
      "che un conteggio 0 può corrispondere a un downvote per ogni upvote). Filtro: "
      "`votes > 0`, come nel web service.")
    w("- Colonne lette per nome dai CSV di `data_raw/`. `fetch_extract.py` le ricava da "
      "`admin/sql/CreateTables.sql` dello schema corrispondente a SCHEMA_SEQUENCE (il dump "
      "è prodotto con `COPY tabella TO stdout`, quindi segue l'ordine delle colonne della "
      "tabella) e si ferma se una riga ha un numero di colonne diverso.")
    w("- Il formato COPY testuale è decodificato da `fetch_extract.py`: TAB come "
      "separatore, `\\N` = NULL, escape `\\b \\f \\n \\r \\t \\v \\ooo \\xhh`, "
      "qualsiasi altro `\\c` = `c`. Codifica: UTF-8. Nei CSV il NULL è un campo vuoto "
      "senza virgolette, la stringa vuota `\"\"`.")
    w("- Artisti nell'ordine delle righe di `artist.csv`, uguale all'ordine fisico di "
      "`mbdump/artist` (il dump non ha `ORDER BY`). Per ogni artista, generi per `votes` "
      "decrescente e poi per nome (ordine dei code point Unicode, non la collazione "
      "`musicbrainz`).")
    w("- `artist_name` è `artist.name`, non `sort_name`. Gli MBID sono in minuscolo.")
    w("- I \"generi più frequenti\" sono ordinati per numero di artisti; a parità, "
      "per nome.")
    w("- Ordine di lettura dei CSV, scelto per tenere in RAM solo il necessario: "
      "(1) `genre`; (2) `tag`; (3) `artist_tag`; (4) `artist`, scrivendo il CSV "
      "durante la lettura. Gli archivi sono letti una sola volta da `fetch_extract.py`, "
      "che si ferma dopo l'ultima tabella richiesta.")
    w("- Il CSV è scritto su un file temporaneo e rinominato solo se tutti i "
      "controlli bloccanti passano. In caso di errore non si produce nessun output.")
    w("- Acquisizione ed estrazione affidate a `fetch_extract.py` (download HTTPS o "
      "archivi locali); questo script legge solo `data_raw/`.")
    w("- Se `genres.csv` differisce dalla tabella `genre`, le differenze sono "
      "elencate qui sotto e segnalate su stderr, ma lo script non si ferma.\n")

    w("## Risultati\n")
    w(f"- Artisti con almeno un genere: **{stats.artists:,}** "
      f"(su {st.artist_rows_total:,} righe di `artist`)")
    w(f"- Righe (coppie artista–genere): **{stats.rows:,}**")
    w(f"- Righe di `artist_tag` con tag di genere escluse perché `count <= 0`: "
      f"{st.genre_rows_nonpositive:,}")
    w(f"- Generi ufficiali nella tabella `genre`: {len(st.genres):,}; generi usati "
      f"almeno una volta nel CSV: {genres_used:,}\n")

    w("### Distribuzione del numero di generi per artista\n")
    w(f"Media {mean:.3f}; mediana {median:g}; minimo {ks[0]}; massimo {ks[-1]}.\n")
    w("| generi per artista | artisti | % |")
    w("|---:|---:|---:|")
    for k in ks:
        n = stats.per_artist[k]
        w(f"| {k} | {n:,} | {100 * n / stats.artists:.2f} |")
    w("")

    w("### 20 generi più frequenti (per numero di artisti)\n")
    w("| # | genere | genre_mbid | artisti | somma voti |")
    w("|---:|---|---|---:|---:|")
    for i, (g, n) in enumerate(top, 1):
        gid, name = st.genres[g]
        w(f"| {i} | {md_escape(name)} | `{gid}` | {n:,} | {stats.genre_votes[g]:,} |")
    w("")

    w("## Controlli\n")
    w(f"- Ogni `genre_mbid` del CSV esiste nella tabella `genre` dello stesso dump: "
      f"**{'OK' if stats.bad_genre_mbid == 0 else 'FALLITO'}** "
      f"({stats.bad_genre_mbid} valori non validi)")
    w(f"- Coppie (artist_mbid, genre_mbid) duplicate: "
      f"**{'nessuna' if stats.duplicates == 0 else stats.duplicates}**")
    w("- Ogni `artist_tag.artist` selezionato esiste in `artist`: **OK**")
    w("- Numero di coppie > 0: **OK**")
    if csv_cmp is None:
        w("- Confronto con `genres.csv`: non eseguito (`--genres-csv` non fornito)\n")
    else:
        csv_path, only_dump, only_csv = csv_cmp
        ok = not only_dump and not only_csv
        w(f"- Confronto con `{csv_path}` (colonna `genre_mbid`) rispetto alla tabella "
          f"`genre`: **{'insiemi identici' if ok else 'DIFFERENZE'}**\n")
        if not ok:
            gid_to_name = {gid: name for gid, name in st.genres.values()}
            w(f"#### Solo nella tabella genre del dump ({len(only_dump)})\n")
            for gid in sorted(only_dump, key=lambda x: gid_to_name[x]):
                w(f"- `{gid}` — {md_escape(gid_to_name[gid])}")
            w("")
            w(f"#### Solo in genres.csv ({len(only_csv)})\n")
            for gid in sorted(only_csv):
                w(f"- `{gid}`")
            w("")

    w("## Licenza\n")
    w("Vedi `ATTRIBUTION.txt`. Le associazioni artista–genere derivano da tag "
      "(dati supplementari, CC BY-NC-SA 3.0); il file nel suo insieme è distribuito "
      "sotto CC BY-NC-SA 3.0.")
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(L) + "\n")


def write_attribution(path, core_meta):
    text = f"""artist_genres.csv — attribuzione e licenza

Dati: MusicBrainz (https://musicbrainz.org), MetaBrainz Foundation.

Fonte: dump PostgreSQL di MusicBrainz, snapshot con
  TIMESTAMP        {core_meta['TIMESTAMP']}
  SCHEMA_SEQUENCE  {core_meta['SCHEMA_SEQUENCE']}
  archivi          mbdump.tar.bz2 (tabelle artist, genre)
                   mbdump-derived.tar.bz2 (tabelle tag, artist_tag)

Licenze:
  - MBID e nomi di artisti e generi: dati core MusicBrainz, CC0 1.0
    (https://creativecommons.org/publicdomain/zero/1.0/).
  - Associazioni artista-genere e conteggi dei voti (tag): dati supplementari
    MusicBrainz, CC BY-NC-SA 3.0
    (https://creativecommons.org/licenses/by-nc-sa/3.0/).
  Questo file, che combina le due parti, è distribuito sotto
  CC BY-NC-SA 3.0: solo uso non commerciale, con attribuzione, e le opere
  derivate devono usare la stessa licenza.

Modifiche rispetto alla fonte: estratti i soli tag degli artisti che
corrispondono a generi ufficiali MusicBrainz, con conteggio netto dei
voti > 0; dati convertiti in CSV (una riga per coppia artista-genere).
"""
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


# --------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------
def main(argv=None) -> int:
    here = Path(__file__).resolve().parent
    ap = argparse.ArgumentParser(
        description="Associa gli artisti MusicBrainz ai generi ufficiali "
                    "(CSV di data_raw/ prodotti da fetch_extract.py).")
    ap.add_argument("--data-raw", default=str(here / "data_raw"),
                    help="cartella prodotta da fetch_extract.py (default: data_raw/ accanto allo script)")
    ap.add_argument("--out-dir", default=str(here / "output"),
                    help="cartella di output (default: output/ accanto allo script)")
    ap.add_argument("--genres-csv", default=None,
                    help="genres.csv opzionale (colonna genre_mbid) per il controllo incrociato")
    args = ap.parse_args(argv)

    if args.genres_csv and not os.path.isfile(args.genres_csv):
        raise DumpError(f"file non trovato: {args.genres_csv}")
    data_raw = Path(args.data_raw)
    try:
        snap = mbraw.load_snapshot(data_raw)
    except mbraw.RawDataError as e:
        raise DumpError(str(e))
    for kind in ("core", "derived"):
        if kind not in snap["archives"]:
            raise DumpError(f"snapshot.json: archivio {kind} assente.")
    core_meta = snap["archives"]["core"]
    derived_meta = snap["archives"]["derived"]
    os.makedirs(args.out_dir, exist_ok=True)

    st = State()

    # (1) genre  (2) tag  -> controllo dello snapshot prima di proseguire
    read_genre(st)(table_rows(data_raw, snap, "genre"))
    for key in ("TIMESTAMP", "SCHEMA_SEQUENCE"):
        if core_meta[key] != derived_meta[key]:
            raise DumpError(
                f"Snapshot diversi: {key} core={core_meta[key]!r} "
                f"derived={derived_meta[key]!r}. Rieseguire fetch_extract.py.")
    read_tag(st)(table_rows(data_raw, snap, "tag"))
    schema_warning = None
    if core_meta["SCHEMA_SEQUENCE"] != VERIFIED_SCHEMA_SEQUENCE:
        schema_warning = (
            f"SCHEMA_SEQUENCE del dump = {core_meta['SCHEMA_SEQUENCE']}, "
            f"la logica è stata verificata sullo schema {VERIFIED_SCHEMA_SEQUENCE}. "
            f"Il numero di colonne è stato controllato riga per riga, ma conviene "
            f"ricontrollare la logica tag → genere sul codice corrispondente.")
        log("ATTENZIONE: " + schema_warning)
    if not st.tag_to_genre:
        raise DumpError("Nessun tag corrisponde a un genere: impossibile proseguire.")

    # (3) artist_tag
    read_artist_tag(st)(table_rows(data_raw, snap, "artist_tag"))
    if not st.by_artist:
        raise DumpError("Zero coppie artista-genere con votes > 0: mi fermo.")

    # (4) artist -> scrittura CSV
    valid_genre_gids = {gid for gid, _ in st.genres.values()}
    stats = Stats()
    csv_path = os.path.join(args.out_dir, CSV_NAME)
    tmp_path = csv_path + ".tmp"
    try:
        with open(tmp_path, "w", encoding="utf-8", newline="") as fh:
            writer = csv.writer(fh, quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
            writer.writerow(CSV_HEADER)
            write_artists(st, writer, stats, valid_genre_gids)(table_rows(data_raw, snap, "artist"))
        # controlli bloccanti
        if st.by_artist:
            missing = list(st.by_artist)[:10]
            raise DumpError(f"{len(st.by_artist)} artisti di artist_tag assenti da artist "
                            f"(es. id {missing}).")
        if stats.rows == 0:
            raise DumpError("Zero coppie artista-genere: mi fermo.")
        if stats.bad_genre_mbid:
            raise DumpError(f"{stats.bad_genre_mbid} genre_mbid non presenti in genre.")
        if stats.duplicates:
            raise DumpError(f"{stats.duplicates} coppie (artista, genere) duplicate.")
        os.replace(tmp_path, csv_path)
    except BaseException:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)
        raise

    # controllo incrociato opzionale con genres.csv
    csv_cmp = None
    if args.genres_csv:
        other = load_genres_csv(args.genres_csv)
        only_dump = valid_genre_gids - other
        only_csv = other - valid_genre_gids
        csv_cmp = (args.genres_csv, only_dump, only_csv)
        if only_dump or only_csv:
            log(f"ATTENZIONE: genres.csv differisce dalla tabella genre "
                f"({len(only_dump)} solo nel dump, {len(only_csv)} solo nel CSV); "
                f"dettagli nel report.")
        else:
            log("genres.csv: insieme dei genre_mbid identico alla tabella genre.")

    write_report(os.path.join(args.out_dir, REPORT_NAME), args, core_meta, derived_meta,
                 st, stats, csv_cmp, schema_warning)
    write_attribution(os.path.join(args.out_dir, ATTRIBUTION_NAME), core_meta)
    log(f"Fatto: {stats.artists:,} artisti, {stats.rows:,} righe -> {csv_path}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (DumpError, mbraw.RawDataError) as e:
        log(f"ERRORE: {e}")
        sys.exit(1)