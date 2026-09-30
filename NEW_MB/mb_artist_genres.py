#!/usr/bin/env python3
"""Associa agli artisti i generi MusicBrainz a partire dai tag (data_raw/).

Uso:
    python mb_artist_genres.py [--data-raw data_raw] [--out-dir output] [--genres-csv FILE]

Legge solo data_raw/{artist,genre,tag,artist_tag}.csv e snapshot.json.
Produce output/artist_genres.csv, output/artist_genres_report.md, output/ATTRIBUTION.txt.
Collegamento tag -> genere: uguaglianza esatta tag.name == genre.name (come
find_genres_for_entities in lib/MusicBrainz/Server/Data/EntityTag.pm, righe 131-161).
Exit code: 0 ok, 2 errore ("ERRORE: ..." su stderr, nessun file prodotto).
"""

import argparse
import csv
import os
import sys
from collections import Counter
from pathlib import Path

from mbraw import read_rows, read_snapshot


class Errore(Exception):
    pass


def kth(dist, k):
    """k-esimo elemento (da 0) della distribuzione {valore: frequenza} ordinata."""
    acc = 0
    for v, c in sorted(dist.items()):
        acc += c
        if k < acc:
            return v
    raise IndexError(k)


def run(args):
    raw, out = Path(args.data_raw), Path(args.out_dir)
    snap = read_snapshot(raw)

    # ---- generi
    gids, gnames = [], []
    idx_by_name = {}
    folded = set()
    for r in read_rows(raw / "genre.csv", required=("gid", "name")):
        name, gid = r["name"], r["gid"]
        if name is None or gid is None:
            raise Errore("genre.csv: gid o name mancanti")
        if name in idx_by_name:
            raise Errore(f"nomi di genere duplicati: {name!r}")
        if name.casefold() in folded:
            raise Errore(f"nomi di genere duplicati ignorando maiuscole/minuscole: {name!r}")
        folded.add(name.casefold())
        idx_by_name[name] = len(gids)
        gids.append(gid.lower())
        gnames.append(name)
    if not gids:
        raise Errore("genre.csv e' vuoto")
    if len(gids) >= 1 << 16:
        raise Errore("troppi generi per la codifica compatta")

    # ---- tag -> genere
    tag_to_genre = {}
    seen_genre = set()
    for r in read_rows(raw / "tag.csv", required=("id", "name")):
        gi = idx_by_name.get(r["name"])
        if gi is None:
            continue
        if gi in seen_genre:
            raise Errore(f"il genere {gnames[gi]!r} corrisponde a piu' di un tag")
        seen_genre.add(gi)
        tag_to_genre[r["id"]] = gi

    # ---- artist_tag: solo coppie con genere e count > 0
    pairs = {}           # artist.id -> [(count << 16) | indice genere]
    excluded = matched_rows = 0
    for r in read_rows(raw / "artist_tag.csv", required=("artist", "tag", "count")):
        gi = tag_to_genre.get(r["tag"])
        if gi is None:
            continue
        matched_rows += 1
        if r["artist"] is None or r["count"] is None:
            raise Errore("artist_tag.csv: artist o count mancanti")
        count = int(r["count"])
        if count <= 0:
            excluded += 1
            continue
        pairs.setdefault(int(r["artist"]), []).append((count << 16) | gi)
    n_pairs = sum(len(v) for v in pairs.values())
    if n_pairs == 0:
        raise Errore("nessuna coppia artista-genere trovata")

    # ---- artist in streaming -> CSV temporaneo
    out.mkdir(parents=True, exist_ok=True)
    final = out / "artist_genres.csv"
    tmp = out / "artist_genres.csv.tmp"
    dist = Counter()
    g_artists = [0] * len(gids)
    g_votes = [0] * len(gids)
    n_artists_total = n_artists = n_rows = 0
    try:
        with open(tmp, "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f, quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
            w.writerow(["artist_mbid", "artist_name", "genre_mbid", "genre_name", "votes"])
            for r in read_rows(raw / "artist.csv", required=("id", "gid", "name")):
                n_artists_total += 1
                lst = pairs.pop(int(r["id"]), None)
                if lst is None:
                    continue
                if len({p & 0xFFFF for p in lst}) != len(lst):
                    raise Errore(f"coppia (artista, genere) duplicata per l'artista {r['gid']}")
                lst.sort(key=lambda p: (-(p >> 16), gnames[p & 0xFFFF]))
                amb = r["gid"].lower()
                for p in lst:
                    gi, votes = p & 0xFFFF, p >> 16
                    w.writerow([amb, r["name"], gids[gi], gnames[gi], votes])
                    g_artists[gi] += 1
                    g_votes[gi] += votes
                dist[len(lst)] += 1
                n_artists += 1
                n_rows += len(lst)
        if pairs:
            sample = sorted(pairs)[:5]
            raise Errore(f"{len(pairs)} artist_tag.artist inesistenti in artist (es. id {sample})")
        os.replace(tmp, final)
    except BaseException:
        if tmp.exists():
            tmp.unlink()
        raise

    # ---- confronto facoltativo con genres.csv
    cmp_lines = []
    if args.genres_csv:
        with open(args.genres_csv, "r", encoding="utf-8", newline="") as f:
            rd = csv.DictReader(f)
            if rd.fieldnames is None or "genre_mbid" not in rd.fieldnames:
                raise Errore(f"{args.genres_csv}: colonna genre_mbid mancante")
            in_file = {row["genre_mbid"].lower() for row in rd}
        in_table = set(gids)
        only_t, only_f = sorted(in_table - in_file), sorted(in_file - in_table)
        cmp_lines = [
            f"- genre_mbid nella tabella genre: {len(in_table)}; nel file: {len(in_file)}",
            f"- solo nella tabella genre: {len(only_t)}",
            *[f"  - {x}" for x in only_t[:50]],
            f"- solo nel file: {len(only_f)}",
            *[f"  - {x}" for x in only_f[:50]],
        ]

    # ---- report
    n_dist = sum(dist.values())
    mean = n_rows / n_dist
    median = (kth(dist, (n_dist - 1) // 2) + kth(dist, n_dist // 2)) / 2
    top = sorted(range(len(gids)), key=lambda i: (-g_artists[i], gnames[i]))[:20]
    used = sum(1 for c in g_artists if c)
    lines = [
        "# Generi degli artisti MusicBrainz", "",
        f"- snapshot: {snap['snapshot']} (timestamp {snap['timestamp']}, schema_sequence {snap['schema_sequence']})",
        f"- artisti in artist.csv: {n_artists_total}",
        f"- artisti con almeno un genere: {n_artists}",
        f"- righe di artist_genres.csv: {n_rows}",
        f"- righe artist_tag su tag-genere escluse per count <= 0: {excluded} (su {matched_rows} righe su tag-genere)",
        f"- generi usati: {used} su {len(gids)}", "",
        "## Generi per artista", "",
        "| generi | artisti |", "|---:|---:|",
        *[f"| {k} | {dist[k]} |" for k in sorted(dist)], "",
        f"media {mean:.3f}; mediana {median:g}; minimo {min(dist)}; massimo {max(dist)}", "",
        "## I 20 generi piu' frequenti", "",
        "| genre_mbid | genere | artisti | somma voti |", "|---|---|---:|---:|",
        *[f"| {gids[i]} | {gnames[i]} | {g_artists[i]} | {g_votes[i]} |" for i in top], "",
        "## Controlli bloccanti", "",
        f"- coppie trovate: {n_pairs} (diverse da zero): OK",
        "- ogni artist_tag.artist selezionato esiste in artist: OK",
        "- ogni genre_mbid esiste in genre: OK (il genere proviene dalla tabella genre)",
        "- nessuna coppia (artist_mbid, genre_mbid) duplicata: OK",
        "- un genere corrisponde al piu' a un tag; nomi di genere unici anche ignorando maiuscole/minuscole: OK",
    ]
    if cmp_lines:
        lines += ["", "## Confronto con --genres-csv", "", *cmp_lines]
    lines += [
        "", "## Decisioni e assunzioni", "",
        "- Collegamento per uguaglianza esatta tag.name == genre.name; votes = artist_tag.count.",
        "- Le righe 'escluse per count <= 0' sono contate solo tra quelle su tag che sono generi.",
        "- Gli artisti senza generi non compaiono nel CSV. Ordine: fisico di artist.csv; per artista votes decrescenti, poi nome (code point).",
        "- Il confronto dei nomi di genere ignorando maiuscole/minuscole usa str.casefold().",
        "- Le coppie sono tenute in memoria in forma compatta ((votes << 16) | indice genere).",
    ]
    (out / "artist_genres_report.md").write_text("\n".join(lines) + "\n", encoding="utf-8")

    attribution = f"""Fonte: MusicBrainz (https://musicbrainz.org), dump PostgreSQL, snapshot {snap['snapshot']}
(timestamp {snap['timestamp']}).

Licenze:
- MBID e nomi (artisti e generi) sono dati core, rilasciati in CC0.
- Le associazioni artista-genere derivano dai tag (dati supplementari), rilasciati
  sotto CC BY-NC-SA 3.0. Il file artist_genres.csv nel suo insieme e' quindi
  distribuito sotto CC BY-NC-SA 3.0 (https://creativecommons.org/licenses/by-nc-sa/3.0/).

Modifiche apportate ai dati originali:
- estratte le tabelle artist, genre, tag, artist_tag e convertite in CSV;
- tenuti solo i tag il cui nome coincide esattamente con il nome di un genere;
- scartate le associazioni con count <= 0;
- votes = artist_tag.count; MBID scritti in minuscolo;
- per ogni artista, generi ordinati per votes decrescenti e poi per nome.
"""
    (out / "ATTRIBUTION.txt").write_text(attribution, encoding="utf-8")
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(description="Generi degli artisti MusicBrainz dai tag.")
    p.add_argument("--data-raw", default="data_raw")
    p.add_argument("--out-dir", default="output")
    p.add_argument("--genres-csv", default=None)
    args = p.parse_args(argv)
    try:
        return run(args)
    except (Errore, OSError, ValueError, KeyError) as e:
        print(f"ERRORE: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
