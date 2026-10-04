#!/usr/bin/env python3
"""Scarica da MusicBrainz le tabelle che servono e le salva in dumps/ (testo COPY grezzo).

Uso:
    python download_dumps.py                          # ultimo snapshot
    python download_dumps.py --snapshot 20260926-002121
    python download_dumps.py --core mbdump.tar.bz2 --derived mbdump-derived.tar.bz2

Opzioni: --snapshot (default LATEST), --out (default dumps), --base-url,
--core/--derived (archivi locali, da passare insieme), --core-tables/--derived-tables
(sovrascrivono le liste CORE_TABLES/DERIVED_TABLES qui sotto).

I tar non vengono salvati: catena HTTPS/file -> bz2 incrementale -> tarfile "r|", con
arresto subito dopo l'ultima tabella richiesta di ciascun archivio. Ogni tabella finisce in
dumps/<tabella>, byte per byte come nel dump (formato COPY di PostgreSQL: campi separati da
tab, \\N per NULL). Colonne e tipi sono in admin/sql/CreateTables.sql di musicbrainz-server.
dumps/SNAPSHOT.txt riporta snapshot, TIMESTAMP del dump, byte, righe e SHA256 di ogni file.
Scrittura atomica: tutto in dumps.tmp/, poi sostituisce dumps/ solo a successo.
Errori: "ERRORE: ..." su stderr, exit code 2.

Licenze: le tabelle core sono di pubblico dominio; quelle derived (tag, artist_tag) sono
CC BY-NC-SA 3.0 (vedi il file COPYING dell'archivio derived).
"""

import argparse
import hashlib
import http.client
import re
import shutil
import sys
import tarfile
import time
from pathlib import Path

from fetch_extract import (
    ARCHIVES,
    HEADER_FILES,
    Bz2Reader,
    FetchError,
    FileSource,
    HttpSource,
    Progress,
    http_get,
    log,
)

CORE_TABLES = ["artist", "genre", "genre_alias", "l_genre_genre", "link", "link_type"]
DERIVED_TABLES = ["tag", "artist_tag"]

BASE_URL = "https://data.metabrainz.org/pub/musicbrainz/data/fullexport/"
CHUNK = 1 << 20


def copy_member(tf, member, table, dest, progress):
    """Copia il membro del tar in dest a blocchi; restituisce byte, righe e SHA256."""
    progress.current = table
    progress.check_space()
    src = tf.extractfile(member)
    if src is None:
        raise FetchError(f"{table}: il membro del tar non e' un file regolare")
    h = hashlib.sha256()
    size = lines = 0
    last = b"\n"
    with open(dest, "wb") as out:
        while True:
            block = src.read(CHUNK)
            if not block:
                break
            out.write(block)
            h.update(block)
            size += len(block)
            lines += block.count(b"\n")
            last = block[-1:]
            progress.tick()
    if last != b"\n":
        lines += 1  # ultima riga senza fine riga
    if size != member.size:
        raise FetchError(f"{table}: letti {size} byte, il tar ne dichiara {member.size}")
    return {"bytes": size, "rows": lines, "sha256": h.hexdigest()}


def read_archive(kind, source, wanted, tmp, progress, header, tables):
    """Una passata sull'archivio; restituisce True se si e' fermata in anticipo."""
    pending = set(wanted)
    with tarfile.open(fileobj=Bz2Reader(source), mode="r|") as tf:
        for member in tf:
            name = member.name.removeprefix("./")
            progress.current = f"(salto {name})"
            if name in HEADER_FILES:
                header[name] = tf.extractfile(member).read().decode("ascii", "replace").strip()
                continue
            table = name.removeprefix("mbdump/") if name.startswith("mbdump/") else None
            if table not in pending:
                continue
            tables[table] = copy_member(tf, member, table, tmp / table, progress)
            tables[table]["archive"] = kind
            log(f"[{kind}] {table}: {tables[table]['rows']} righe, {tables[table]['bytes']} byte")
            pending.discard(table)
            if not pending:
                return True
    if pending:
        raise FetchError(f"archivio {kind}: tabelle assenti dal tar: {', '.join(sorted(pending))}")
    return False


def check_header(kind, header, state):
    """Controlla TIMESTAMP/SCHEMA_SEQUENCE e che i due archivi siano dello stesso dump."""
    for key in ("TIMESTAMP", "SCHEMA_SEQUENCE"):
        if key not in header:
            raise FetchError(f"archivio {kind}: manca il file {key}")
        if key in state and state[key] != header[key]:
            raise FetchError(
                f"i due archivi non coincidono ({key}: {state[key]!r} contro {header[key]!r})"
            )
        state[key] = header[key]
    state.setdefault("REPLICATION_SEQUENCE", header.get("REPLICATION_SEQUENCE", ""))


def write_snapshot(path, snapshot, state, tables):
    lines = [
        f"snapshot: {snapshot}",
        f"TIMESTAMP: {state['TIMESTAMP']}",
        f"SCHEMA_SEQUENCE: {state['SCHEMA_SEQUENCE']}",
        f"REPLICATION_SEQUENCE: {state['REPLICATION_SEQUENCE'] or '-'}",
        "",
        "tabella\tarchivio\tbyte\triga\tsha256",
    ]
    for t in sorted(tables):
        v = tables[t]
        lines.append(f"{t}\t{v['archive']}\t{v['bytes']}\t{v['rows']}\t{v['sha256']}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def parse_args(argv):
    p = argparse.ArgumentParser(description="Scarica tabelle MusicBrainz in dumps/ (COPY grezzo).")
    p.add_argument("--snapshot", default="LATEST")
    p.add_argument("--base-url", default=BASE_URL)
    p.add_argument("--core", help="archivio core locale (mbdump.tar.bz2)")
    p.add_argument("--derived", help="archivio derived locale (mbdump-derived.tar.bz2)")
    p.add_argument("--out", default="dumps")
    p.add_argument("--core-tables", nargs="+", metavar="TABELLA", default=CORE_TABLES)
    p.add_argument("--derived-tables", nargs="+", metavar="TABELLA", default=DERIVED_TABLES)
    return p.parse_args(argv)


def run(args):
    if bool(args.core) != bool(args.derived):
        raise FetchError("--core e --derived vanno passati insieme")
    wanted = {
        "core": list(dict.fromkeys(args.core_tables)),
        "derived": list(dict.fromkeys(args.derived_tables)),
    }
    both = set(wanted["core"]) & set(wanted["derived"])
    if both:
        raise FetchError(f"tabelle presenti sia in core sia in derived: {', '.join(sorted(both))}")
    if not wanted["core"] and not wanted["derived"]:
        raise FetchError("nessuna tabella richiesta")

    out = Path(args.out)
    tmp = out.with_name(out.name + ".tmp")
    parent = out.resolve().parent
    parent.mkdir(parents=True, exist_ok=True)
    progress = Progress(parent)
    progress.check_space()
    if tmp.exists():
        log(f"{tmp} esiste gia' (download interrotto?): lo cancello e ricomincio da zero")
        shutil.rmtree(tmp)
    tmp.mkdir()

    local = bool(args.core)
    if local:
        locs = {"core": args.core, "derived": args.derived}
        snapshot = args.snapshot
        if snapshot == "LATEST":
            parent_name = Path(args.core).resolve().parent.name
            snapshot = parent_name if re.fullmatch(r"\d{8}-\d{6}", parent_name) else "unknown"
    else:
        base = args.base_url if args.base_url.endswith("/") else args.base_url + "/"
        snapshot = args.snapshot
        if snapshot == "LATEST":
            snapshot = http_get(base + "LATEST").decode("ascii", "replace").strip()
        locs = {k: f"{base}{snapshot}/{fn}" for k, fn in ARCHIVES.items()}
    log(f"snapshot {snapshot}")

    state, tables = {}, {}
    for kind in ("derived", "core"):
        if not wanted[kind]:
            continue
        progress.kind = kind
        header = {}
        source = (FileSource if local else HttpSource)(locs[kind], progress)
        progress.source = source
        t1 = time.monotonic()
        try:
            early = read_archive(kind, source, wanted[kind], tmp, progress, header, tables)
        finally:
            source.close()
        check_header(kind, header, state)
        log(f"[{kind}] finito in {time.monotonic() - t1:.0f} s "
            f"({source.pos / 2**20:.0f} MiB letti{', arresto anticipato' if early else ''})")

    write_snapshot(tmp / "SNAPSHOT.txt", snapshot, state, tables)

    if out.exists():
        old = out.with_name(out.name + ".old")
        if old.exists():
            shutil.rmtree(old)
        out.replace(old)
        tmp.replace(out)
        shutil.rmtree(old)
    else:
        tmp.replace(out)
    log(f"fatto: {len(tables)} tabelle in {out}")


def main(argv=None):
    args = parse_args(argv)
    try:
        run(args)
    except (FetchError, OSError, ValueError, EOFError, tarfile.TarError, http.client.HTTPException) as e:
        print(f"ERRORE: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
