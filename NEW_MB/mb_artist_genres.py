#!/usr/bin/env python3
"""
mb_artist_genres.py -- associazione artista -> generi ufficiali MusicBrainz.

Legge in streaming i dump PostgreSQL di MusicBrainz (senza server PostgreSQL):
  --core     mbdump.tar.bz2          (tabelle artist, genre)
  --derived  mbdump-derived.tar.bz2  (tabelle tag, artist_tag)
dello STESSO snapshot, e produce in --out-dir:
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

Solo libreria standard. Python >= 3.8.


USO

python3 mb_artist_genres.py --core mbdump.tar.bz2 --derived mbdump-derived.tar.bz2 \
        --out-dir out [--genres-csv genres.csv]
"""

from __future__ import annotations

import argparse
import bz2
import csv
import datetime as _dt
import os
import re
import statistics
import sys
import tarfile
import time
from collections import Counter

# --------------------------------------------------------------------------
# Costanti di schema (admin/sql/CreateTables.sql, commit 3468ea3; schema 31)
# --------------------------------------------------------------------------
SOURCE_COMMIT = "3468ea32dc46795799b48bc16cef475205bb30d5"
VERIFIED_SCHEMA_SEQUENCE = "31"  # lib/DBDefs.pm.sample: sub DB_SCHEMA_SEQUENCE { 31 }

# Numero esatto di colonne atteso per tabella; posizioni usate:
#   artist:     0=id, 1=gid, 2=name                 (19 colonne)
#   genre:      0=id, 1=gid, 2=name                 (6 colonne)
#   tag:        0=id, 1=name                        (3 colonne)
#   artist_tag: 0=artist, 1=tag, 2=count            (4 colonne)
EXPECTED_COLUMNS = {"artist": 19, "genre": 6, "tag": 3, "artist_tag": 4}

META_FILES = ("TIMESTAMP", "SCHEMA_SEQUENCE", "REPLICATION_SEQUENCE")
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
# Formato COPY testuale di PostgreSQL
#   - campi separati da TAB, righe da \n
#   - \N (campo intero) = NULL, riconosciuto prima di ogni altro escape
#   - escape: \b \f \n \r \t \v, \ooo (1-3 cifre ottali), \xhh (1-2 cifre hex),
#     qualsiasi altro \c rappresenta c stesso
#   - la riga "\." e' il marcatore di fine dati
# --------------------------------------------------------------------------
_ESC_RE = re.compile(rb"\\(x[0-9A-Fa-f]{1,2}|[0-7]{1,3}|.)", re.S)
_SIMPLE_ESC = {
    b"b": b"\x08",
    b"f": b"\x0c",
    b"n": b"\n",
    b"r": b"\r",
    b"t": b"\t",
    b"v": b"\x0b",
}


def _esc_sub(m: re.Match) -> bytes:
    s = m.group(1)
    if len(s) > 1 and s[:1] == b"x":
        return bytes([int(s[1:], 16)])
    if s[0] in b"01234567":
        return bytes([int(s, 8) & 0xFF])
    return _SIMPLE_ESC.get(s, s)


def copy_field(raw: bytes) -> str | None:
    """Decodifica un campo COPY: NULL -> None, altrimenti str UTF-8 senza escape."""
    if raw == b"\\N":
        return None
    if b"\\" in raw:
        raw = _ESC_RE.sub(_esc_sub, raw)
    return raw.decode("utf-8")


def copy_rows(fileobj, table: str):
    """Itera le righe di un file COPY restituendo la lista di campi grezzi (bytes).

    Controlla che ogni riga abbia esattamente il numero di colonne atteso.
    """
    ncols = EXPECTED_COLUMNS[table]
    for lineno, line in enumerate(fileobj, 1):
        if line.endswith(b"\n"):
            line = line[:-1]
        if line == b"\\.":
            break
        parts = line.split(b"\t")
        if len(parts) != ncols:
            raise DumpError(
                f"mbdump/{table}, riga {lineno}: {len(parts)} colonne, attese {ncols}. "
                f"Lo schema del dump non corrisponde a quello verificato "
                f"(CreateTables.sql, commit {SOURCE_COMMIT[:7]})."
            )
        yield parts


def parse_int(raw: bytes, table: str, col: str) -> int:
    try:
        return int(raw)
    except ValueError:
        raise DumpError(f"mbdump/{table}: valore non intero in colonna {col}: {raw[:50]!r}")


def parse_uuid(raw: bytes, table: str) -> str:
    val = copy_field(raw)
    if val is None or not UUID_RE.match(val):
        raise DumpError(f"mbdump/{table}: gid non valido: {raw[:50]!r}")
    return val


# --------------------------------------------------------------------------
# Lettura in streaming di un archivio .tar.bz2 con arresto anticipato
# --------------------------------------------------------------------------
def scan_archive(path: str, handlers: dict, label: str) -> dict:
    """Scorre l'archivio in ordine e passa ai gestori i membri richiesti.

    handlers: {nome_tabella: funzione(fileobj)}. La lettura si ferma appena
    tutti i gestori hanno lavorato: i membri successivi non vengono decompressi.
    Restituisce i metadati dello snapshot (TIMESTAMP, SCHEMA_SEQUENCE,
    REPLICATION_SEQUENCE), che nel dump precedono tutte le tabelle.
    """
    size = os.path.getsize(path)
    pending = {f"mbdump/{t}": fn for t, fn in handlers.items()}
    meta: dict[str, str] = {}
    log(f"{label}: apro {path} ({size / 1e9:.2f} GB); tabelle richieste: "
        f"{', '.join(handlers)}")
    with open(path, "rb") as raw:
        # bz2.BZ2File gestisce anche gli stream bzip2 concatenati (multi-stream).
        with bz2.BZ2File(raw) as bz, tarfile.open(fileobj=bz, mode="r|") as tf:
            for member in tf:
                name = member.name[2:] if member.name.startswith("./") else member.name
                if name in META_FILES:
                    f = tf.extractfile(member)
                    meta[name] = f.read().decode("utf-8").strip() if f else ""
                    continue
                if name in pending:
                    for m in META_FILES[:2]:
                        if m not in meta:
                            raise DumpError(f"{label}: {m} non trovato prima delle tabelle.")
                    log(f"{label}: leggo {name} "
                        f"(posizione compressa {raw.tell() / size:6.1%})")
                    f = tf.extractfile(member)
                    pending.pop(name)(f)
                    if not pending:
                        log(f"{label}: tabelle lette, interrompo la decompressione "
                            f"al {raw.tell() / size:6.1%} del file compresso.")
                        break
    if pending:
        raise DumpError(f"{label}: membri non trovati nell'archivio: {', '.join(pending)}")
    for m in META_FILES[:2]:
        if m not in meta:
            raise DumpError(f"{label}: file {m} assente.")
    meta.setdefault("REPLICATION_SEQUENCE", "")
    return meta


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
    def handler(f):
        for p in copy_rows(f, "genre"):
            gid_int = parse_int(p[0], "genre", "id")
            gid = parse_uuid(p[1], "genre")
            name = copy_field(p[2])
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
    def handler(f):
        genre_seen: dict[int, int] = {}
        for p in copy_rows(f, "tag"):
            st.tags_total += 1
            name = copy_field(p[1])
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
    def handler(f):
        by_artist = st.by_artist
        t2g = st.tag_to_genre
        for p in copy_rows(f, "artist_tag"):
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
    def handler(f):
        by_artist = st.by_artist
        genres = st.genres
        for p in copy_rows(f, "artist"):
            st.artist_rows_total += 1
            aid = int(p[0])
            pairs = by_artist.pop(aid, None)  # pop: libera memoria mentre si scrive
            if pairs is None:
                continue
            mbid = parse_uuid(p[1], "artist")
            name = copy_field(p[2])
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
    w(f"| core | `{os.path.basename(args.core)}` | {core_meta['TIMESTAMP']} | "
      f"{core_meta['SCHEMA_SEQUENCE']} | {core_meta['REPLICATION_SEQUENCE']} |")
    w(f"| derived | `{os.path.basename(args.derived)}` | {derived_meta['TIMESTAMP']} | "
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
    w("- Posizioni delle colonne prese da `admin/sql/CreateTables.sql` (artist 19 "
      "colonne, genre 6, tag 3, artist_tag 4). Il dump è prodotto con "
      "`COPY (SELECT * FROM tabella)`, quindi segue l'ordine delle colonne della "
      "tabella. Se il numero di colonne differisce, lo script si ferma.")
    w("- Il formato COPY testuale è decodificato così: TAB come separatore, `\\N` = "
      "NULL, escape `\\b \\f \\n \\r \\t \\v \\ooo \\xhh`, qualsiasi altro `\\c` = "
      "`c`, riga `\\.` = fine dati. Codifica: UTF-8.")
    w("- Artisti nell'ordine fisico delle righe di `mbdump/artist` (il dump non ha "
      "`ORDER BY`). Per ogni artista, generi per `votes` decrescente e poi per nome "
      "(ordine dei code point Unicode, non la collazione `musicbrainz`).")
    w("- `artist_name` è `artist.name`, non `sort_name`. Gli MBID sono in minuscolo.")
    w("- I \"generi più frequenti\" sono ordinati per numero di artisti; a parità, "
      "per nome.")
    w("- Ordine di lettura, scelto per tenere in RAM solo il necessario: "
      "(1) core fino a `genre`; (2) derived fino a `tag`; (3) derived fino a "
      "`artist_tag`; (4) core fino a `artist`, scrivendo il CSV durante la lettura. "
      "Ogni lettura si ferma dopo l'ultima tabella necessaria. Nel dump `artist` "
      "precede `genre` e `artist_tag` precede `tag` (ordine di `@CORE_TABLE_LIST` e "
      "`@DERIVED_TABLE_LIST` in `lib/MusicBrainz/Server/Constants.pm`), da cui le due "
      "letture parziali per archivio.")
    w("- Il CSV è scritto su un file temporaneo e rinominato solo se tutti i "
      "controlli bloccanti passano. In caso di errore non si produce nessun output.")
    w("- Il download automatico non è implementato: gli archivi sono passati con "
      "`--core` e `--derived`.")
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
    ap = argparse.ArgumentParser(
        description="Associa gli artisti MusicBrainz ai generi ufficiali "
                    "(dump PostgreSQL, senza server).")
    ap.add_argument("--core", required=True, help="percorso di mbdump.tar.bz2")
    ap.add_argument("--derived", required=True, help="percorso di mbdump-derived.tar.bz2")
    ap.add_argument("--out-dir", default=".", help="cartella di output (default: .)")
    ap.add_argument("--genres-csv", default=None,
                    help="genres.csv opzionale (colonna genre_mbid) per il controllo incrociato")
    args = ap.parse_args(argv)

    for p in (args.core, args.derived) + ((args.genres_csv,) if args.genres_csv else ()):
        if not os.path.isfile(p):
            raise DumpError(f"file non trovato: {p}")
    os.makedirs(args.out_dir, exist_ok=True)

    st = State()

    # (1) core: metadati + genre
    core_meta = scan_archive(args.core, {"genre": read_genre(st)}, "core[1/2]")
    # (2) derived: metadati + tag  -> controllo dello snapshot prima di proseguire
    derived_meta = scan_archive(args.derived, {"tag": read_tag(st)}, "derived[1/2]")
    for key in ("TIMESTAMP", "SCHEMA_SEQUENCE"):
        if core_meta[key] != derived_meta[key]:
            raise DumpError(
                f"Snapshot diversi: {key} core={core_meta[key]!r} "
                f"derived={derived_meta[key]!r}. Usa archivi della stessa cartella di snapshot.")
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

    # (3) derived: artist_tag
    meta3 = scan_archive(args.derived, {"artist_tag": read_artist_tag(st)}, "derived[2/2]")
    if meta3 != derived_meta:
        raise DumpError("Il file derived è cambiato tra le due letture.")
    if not st.by_artist:
        raise DumpError("Zero coppie artista-genere con votes > 0: mi fermo.")

    # (4) core: artist -> scrittura CSV
    valid_genre_gids = {gid for gid, _ in st.genres.values()}
    stats = Stats()
    csv_path = os.path.join(args.out_dir, CSV_NAME)
    tmp_path = csv_path + ".tmp"
    try:
        with open(tmp_path, "w", encoding="utf-8", newline="") as fh:
            writer = csv.writer(fh, quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
            writer.writerow(CSV_HEADER)
            meta4 = scan_archive(args.core,
                                 {"artist": write_artists(st, writer, stats, valid_genre_gids)},
                                 "core[2/2]")
        if meta4 != core_meta:
            raise DumpError("Il file core è cambiato tra le due letture.")
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
    except DumpError as e:
        log(f"ERRORE: {e}")
        sys.exit(1)