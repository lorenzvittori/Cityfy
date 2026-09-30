#!/usr/bin/env python3
"""
fetch_extract.py -- acquisizione ed estrazione delle tabelle MusicBrainz in CSV.

Legge in streaming gli archivi del dump PostgreSQL di MusicBrainz
(mbdump.tar.bz2 = "core", mbdump-derived.tar.bz2 = "derived"), scaricandoli in
HTTPS dal mirror ufficiale oppure da file locali, e converte le tabelle elencate
in TABLES dal formato COPY di PostgreSQL in CSV (convenzione in mbraw.py).

OUTPUT (cartella --out, default: data_raw/ accanto allo script; sovrascritta a
ogni esecuzione)
  <tabella>.csv    una per tabella attiva in TABLES
  snapshot.json    snapshot, TIMESTAMP, SCHEMA_SEQUENCE, REPLICATION_SEQUENCE,
                   data di estrazione, righe per tabella, byte letti per archivio
  _schema/         file di musicbrainz-server usati per ricavare archivi e colonne

USO
  python fetch_extract.py                               # ultimo snapshot (LATEST)
  python fetch_extract.py --snapshot 20260926-002121    # snapshot indicato
  python fetch_extract.py --core  C:\\dump\\20260926-002121\\mbdump.tar.bz2 \\
                          --derived C:\\dump\\20260926-002121\\mbdump-derived.tar.bz2

Requisiti: Python >= 3.12, solo libreria standard. Dettagli nel README.
"""

from __future__ import annotations

import argparse
import bz2
import csv
import hashlib
import http.client
import io
import json
import os
import re
import shutil
import sys
import tarfile
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import mbraw

# --------------------------------------------------------------------------- #
# Tabelle da estrarre. Togliere il "#" per attivarne altre: archivio e colonne
# sono ricavati automaticamente dallo schema di musicbrainz-server.
# --------------------------------------------------------------------------- #
TABLES = [
    # --- core ---
    "artist", "artist_alias", "genre", "l_genre_genre", "link", "link_type", "url", "l_artist_url",
    # "release_group", "artist_credit", "artist_credit_name",  # disambiguazione tramite titoli degli album
    # "recording", "isrc",                                       # matching via ISRC
    # --- derived ---
    "tag", "artist_tag",
]

SCRIPT_VERSION = "1.0"
DEFAULT_BASE_URL = "https://data.metabrainz.org/pub/musicbrainz/data/fullexport"
USER_AGENT = f"fetch_extract/{SCRIPT_VERSION} (progetto personale di ricerca)"
SNAPSHOT_RE = re.compile(r"^\d{8}-\d{6}$")

# Schema: repository metabrainz/musicbrainz-server.
SCHEMA_REPO = "metabrainz/musicbrainz-server"
DEFAULT_SCHEMA_REF = "production"
SCHEMA_FILES = {
    "Constants.pm": "lib/MusicBrainz/Server/Constants.pm",   # @CORE_TABLE_LIST, @DERIVED_TABLE_LIST
    "CreateTables.sql": "admin/sql/CreateTables.sql",         # colonne di ogni tabella
    "DBDefs.pm.sample": "lib/DBDefs.pm.sample",               # DB_SCHEMA_SEQUENCE
}

# Archivio -> (nome del file sul mirror, lista di tabelle in Constants.pm).
# admin/ExportAllTables: make_tar('mbdump.tar.bz2', @CORE_TABLE_LIST) e
# make_tar('mbdump-derived.tar.bz2', @DERIVED_TABLE_LIST).
ARCHIVES = {
    "core": ("mbdump.tar.bz2", "CORE_TABLE_LIST"),
    "derived": ("mbdump-derived.tar.bz2", "DERIVED_TABLE_LIST"),
}
# Ordine di lettura: prima il derived (piccolo), cosi' un eventuale disaccordo
# di TIMESTAMP/SCHEMA_SEQUENCE emerge prima della lunga lettura del core.
ARCHIVE_ORDER = ("derived", "core")
META_FILES = ("TIMESTAMP", "SCHEMA_SEQUENCE", "REPLICATION_SEQUENCE")

DEFAULT_MIN_FREE_GIB = 6.0
PROGRESS_SECONDS = 30
NET_RETRIES = 5


class FatalError(Exception):
    """Errore che impedisce di produrre una data_raw/ corretta."""


def log(msg: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", file=sys.stderr, flush=True)


def sha256_bytes(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


# --------------------------------------------------------------------------- #
# Formato COPY testuale di PostgreSQL
#   campi separati da TAB, una riga per record terminata da \n;
#   \N (campo intero) = NULL;
#   escape: \\ \b \f \n \r \t \v, \ooo (ottale), \xhh (esadecimale); ogni altro
#   \c vale c. COPY TO non emette mai sequenze ottali o esadecimali.
#   (https://www.postgresql.org/docs/current/sql-copy.html, "Text Format")
# --------------------------------------------------------------------------- #
_ESC_RE = re.compile(rb"\\(x[0-9A-Fa-f]{1,2}|[0-7]{1,3}|.)", re.S)
_SIMPLE_ESC = {b"b": b"\x08", b"f": b"\x0c", b"n": b"\n", b"r": b"\r", b"t": b"\t", b"v": b"\x0b"}
_ENCODE_TABLE = str.maketrans({"\\": "\\\\", "\x08": "\\b", "\x0c": "\\f", "\n": "\\n",
                               "\r": "\\r", "\t": "\\t", "\x0b": "\\v"})


def _esc_sub(m: re.Match) -> bytes:
    s = m.group(1)
    if len(s) > 1 and s[:1] == b"x":
        return bytes([int(s[1:], 16)])
    if s[:1] in b"01234567":
        return bytes([int(s, 8) & 0xFF])
    return _SIMPLE_ESC.get(s, s)


def decode_copy_line(line: bytes) -> list[str | None]:
    """Decodifica una riga COPY (senza il \\n finale): NULL -> None, altrimenti str UTF-8."""
    if b"\\" not in line:
        return line.decode("utf-8").split("\t")
    out: list[str | None] = []
    for raw in line.split(b"\t"):
        if raw == b"\\N":
            out.append(None)
        elif b"\\" in raw:
            out.append(_ESC_RE.sub(_esc_sub, raw).decode("utf-8"))
        else:
            out.append(raw.decode("utf-8"))
    return out


def encode_copy_line(fields) -> bytes:
    """Inverso canonico di decode_copy_line, come lo scrive COPY TO (con il \\n finale)."""
    return ("\t".join("\\N" if f is None else f.translate(_ENCODE_TABLE) for f in fields)
            + "\n").encode("utf-8")


# --------------------------------------------------------------------------- #
# Schema da musicbrainz-server
# --------------------------------------------------------------------------- #

def _http_get(url: str, accept: str | None = None) -> bytes:
    headers = {"User-Agent": USER_AGENT}
    if accept:
        headers["Accept"] = accept
    req = urllib.request.Request(url, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.read()
    except urllib.error.HTTPError as e:
        raise FatalError(f"HTTP {e.code} per {url}") from e
    except (urllib.error.URLError, OSError) as e:
        raise FatalError(f"Errore di rete per {url}: {e}") from e


def load_schema(ref: str, schema_dir: Path | None, dest: Path) -> dict:
    """Carica Constants.pm, CreateTables.sql e DBDefs.pm.sample e ne salva copia in dest."""
    dest.mkdir(parents=True, exist_ok=True)
    texts: dict[str, bytes] = {}
    if schema_dir is not None:
        for name in SCHEMA_FILES:
            p = schema_dir / name
            if not p.is_file():
                raise FatalError(f"--schema-dir: file {p} mancante.")
            texts[name] = p.read_bytes()
        source = {"schema_dir": str(schema_dir.resolve()), "repo": None, "ref": None, "commit": None}
        log(f"Schema: file locali in {schema_dir}")
    else:
        if re.fullmatch(r"[0-9a-f]{40}", ref):
            commit = ref
        else:
            commit = _http_get(f"https://api.github.com/repos/{SCHEMA_REPO}/commits/{ref}",
                               accept="application/vnd.github.sha").decode("ascii").strip()
            if not re.fullmatch(r"[0-9a-f]{40}", commit):
                raise FatalError(f"Risoluzione di {ref!r} non riuscita: {commit[:80]!r}")
        for name, rel in SCHEMA_FILES.items():
            texts[name] = _http_get(f"https://raw.githubusercontent.com/{SCHEMA_REPO}/{commit}/{rel}")
        source = {"schema_dir": None, "repo": SCHEMA_REPO, "ref": ref, "commit": commit}
        log(f"Schema: {SCHEMA_REPO} ref {ref} = commit {commit}")
    for name, data in texts.items():
        (dest / name).write_bytes(data)
    source["files"] = {name: {"path": SCHEMA_FILES[name], "sha256": sha256_bytes(data)}
                       for name, data in texts.items()}

    constants = texts["Constants.pm"].decode("utf-8")
    lists = {}
    for m in re.finditer(r"Readonly\s+our\s+@(\w+_TABLE_LIST)\s*=>\s*qw\((.*?)\);", constants, re.S):
        lists[m.group(1)] = m.group(2).split()
    for kind, (_, list_name) in ARCHIVES.items():
        if not lists.get(list_name):
            raise FatalError(f"Constants.pm: @{list_name} non trovata o vuota.")

    m = re.search(r"sub\s+DB_SCHEMA_SEQUENCE\s*\{\s*(\d+)\s*\}", texts["DBDefs.pm.sample"].decode("utf-8"))
    if not m:
        raise FatalError("DBDefs.pm.sample: DB_SCHEMA_SEQUENCE non trovato.")

    return {"source": source, "lists": lists, "schema_sequence": int(m.group(1)),
            "columns": parse_create_tables(texts["CreateTables.sql"].decode("utf-8"))}


_NOT_COLUMN = {"CONSTRAINT", "CHECK", "PRIMARY", "UNIQUE", "FOREIGN", "EXCLUDE", "LIKE"}


def parse_create_tables(sql: str) -> dict[str, list[str]]:
    """Colonne di ogni tabella da CreateTables.sql, nell'ordine di definizione.

    Gestisce commenti "--", vincoli di tabella e CHECK su piu' righe; aggiunge in
    coda le colonne di eventuali "ALTER TABLE t ADD [COLUMN] c".
    """
    sql = re.sub(r"--[^\n]*", "", sql)
    tables: dict[str, list[str]] = {}
    for m in re.finditer(r"\bCREATE\s+TABLE\s+(\w+)\s*\(", sql, re.I):
        depth, j = 1, m.end()
        while depth:
            if j >= len(sql):
                raise FatalError(f"CreateTables.sql: parentesi non chiusa in {m.group(1)}.")
            depth += {"(": 1, ")": -1}.get(sql[j], 0)
            j += 1
        items, depth, cur = [], 0, []
        for ch in sql[m.end():j - 1]:
            if ch == "(":
                depth += 1
            elif ch == ")":
                depth -= 1
            if ch == "," and depth == 0:
                items.append("".join(cur))
                cur = []
            else:
                cur.append(ch)
        items.append("".join(cur))
        cols = [it.split()[0].strip('"') for it in items
                if it.strip() and it.split()[0].upper() not in _NOT_COLUMN]
        tables[m.group(1)] = cols
    for m in re.finditer(r"\bALTER\s+TABLE\s+(\w+)\s+ADD\s+(?:COLUMN\s+)?(\w+)", sql, re.I):
        if m.group(2).upper() not in _NOT_COLUMN and m.group(1) in tables:
            tables[m.group(1)].append(m.group(2))
    return tables


# --------------------------------------------------------------------------- #
# Sorgenti in streaming (HTTPS o file locale) con conteggio dei byte
# --------------------------------------------------------------------------- #

class Progress:
    """Stato condiviso per il log periodico e il controllo dello spazio libero."""

    def __init__(self, kind: str, total: int | None, out_dir: Path, min_free: float):
        self.kind, self.total, self.out_dir, self.min_free = kind, total, out_dir, min_free
        self.member = "-"
        self.rows = 0
        self._next = time.monotonic() + PROGRESS_SECONDS

    def describe(self, pos: int) -> str:
        pct = f" ({100 * pos / self.total:.1f}% di {self.total:,} B)" if self.total else ""
        return f"letti {pos:,} B{pct}"

    def tick(self, pos: int) -> None:
        now = time.monotonic()
        if now < self._next:
            return
        self._next = now + PROGRESS_SECONDS
        extra = f", righe {self.rows:,}" if self.rows else ""
        log(f"  {self.kind}: {self.describe(pos)}; membro {self.member}{extra}")
        check_free_space(self.out_dir, self.min_free)


class HttpStream(io.RawIOBase):
    """Download HTTPS in streaming; in caso di errore di rete riprende con Range/If-Range."""

    def __init__(self, url: str, progress: Progress):
        self.url, self.progress = url, progress
        self.pos = 0
        self.total: int | None = None
        self.etag: str | None = None
        self.resp = None
        self._open()

    def _open(self) -> None:
        headers = {"User-Agent": USER_AGENT}
        if self.pos:
            headers["Range"] = f"bytes={self.pos}-"
            if self.etag:
                headers["If-Range"] = self.etag
        req = urllib.request.Request(self.url, headers=headers)
        try:
            resp = urllib.request.urlopen(req, timeout=60)
        except urllib.error.HTTPError as e:
            if e.code == 404:
                raise FatalError(f"{self.url}: 404, snapshot non (piu') disponibile sul mirror.") from e
            raise FatalError(f"HTTP {e.code} per {self.url}") from e
        if self.pos:
            cr = resp.headers.get("Content-Range", "")
            if resp.status != 206 or not cr.startswith(f"bytes {self.pos}-"):
                resp.close()
                raise FatalError(f"Ripresa dal byte {self.pos} non riuscita (HTTP {resp.status}, "
                                 f"Content-Range {cr!r}): il file remoto potrebbe essere cambiato.")
        else:
            self.total = int(resp.headers.get("Content-Length") or 0) or None
            self.etag = resp.headers.get("ETag")
            self.progress.total = self.total
        self.resp = resp

    def readable(self) -> bool:
        return True

    def readinto(self, b) -> int:
        attempt = 0
        while True:
            try:
                n = self.resp.readinto(b)
                if n == 0 and self.total is not None and self.pos < self.total:
                    raise ConnectionError("connessione chiusa prima della fine del file")
                self.pos += n
                self.progress.tick(self.pos)
                return n
            except (OSError, http.client.HTTPException) as e:
                attempt += 1
                if attempt > NET_RETRIES:
                    raise FatalError(f"Download interrotto al byte {self.pos:,}: {e}") from e
                log(f"  errore di rete ({e!r}); riprendo dal byte {self.pos:,} "
                    f"(tentativo {attempt}/{NET_RETRIES})")
                try:
                    self.resp.close()
                except Exception:
                    pass
                time.sleep(10 * attempt)
                try:
                    self._open()
                except (OSError, http.client.HTTPException) as e2:
                    log(f"  riapertura non riuscita: {e2!r}")

    def close(self) -> None:
        if self.resp is not None:
            self.resp.close()
        super().close()


class LocalStream(io.RawIOBase):
    """Lettura di un archivio locale con conteggio dei byte letti."""

    def __init__(self, path: Path, progress: Progress):
        self.f = open(path, "rb", buffering=0)
        self.pos = 0
        self.total = path.stat().st_size
        self.progress = progress
        progress.total = self.total

    def readable(self) -> bool:
        return True

    def readinto(self, b) -> int:
        n = self.f.readinto(b)
        self.pos += n
        self.progress.tick(self.pos)
        return n

    def close(self) -> None:
        self.f.close()
        super().close()


def check_free_space(path: Path, min_free_gib: float) -> None:
    free = shutil.disk_usage(path).free / 2**30
    if free < min_free_gib:
        raise FatalError(f"Spazio libero insufficiente su {path}: {free:.1f} GiB "
                         f"(minimo {min_free_gib:g} GiB). Liberare spazio e rieseguire.")


# --------------------------------------------------------------------------- #
# Estrazione di un archivio
# --------------------------------------------------------------------------- #

def extract_table(kind: str, table: str, fobj, columns: list[str], out_dir: Path,
                  prog: Progress) -> dict:
    """Converte un membro COPY in <table>.csv riga per riga (memoria costante)."""
    ncol = len(columns)
    path = out_dir / f"{table}.csv"
    h = hashlib.sha256()
    nbytes = rows = 0
    prog.rows = 0
    fh, writer = mbraw.open_csv_writer(path)
    try:
        writer.writerow(columns)
        for line in fobj:
            h.update(line)
            nbytes += len(line)
            rows += 1
            if not line.endswith(b"\n"):
                raise FatalError(f"{kind}/mbdump/{table}, riga {rows}: manca il fine riga finale "
                                 "(membro troncato?).")
            body = line[:-1]
            if body == b"\\.":
                raise FatalError(f"{kind}/mbdump/{table}, riga {rows}: marcatore di fine dati "
                                 "'\\.' inatteso.")
            try:
                fields = decode_copy_line(body)
            except UnicodeDecodeError as e:
                raise FatalError(f"{kind}/mbdump/{table}, riga {rows}: UTF-8 non valido ({e}).") from e
            if len(fields) != ncol:
                raise FatalError(f"{kind}/mbdump/{table}, riga {rows}: {len(fields)} colonne, "
                                 f"attese {ncol} secondo lo schema ({', '.join(columns)}).")
            try:
                writer.writerow(fields)
            except csv.Error as e:  # solo tabelle a una colonna con valore NULL
                raise FatalError(f"{table}, riga {rows}: impossibile scrivere in CSV ({e}).") from e
            if not rows & 0xFFFF:
                prog.rows = rows
    finally:
        fh.close()
    prog.rows = 0
    return {"archive": kind, "file": path.name, "columns": columns, "rows": rows,
            "present_in_archive": True, "copy_bytes": nbytes, "copy_sha256": h.hexdigest(),
            "csv_bytes": path.stat().st_size}


def write_empty_table(kind: str, table: str, columns: list[str], out_dir: Path) -> dict:
    path = out_dir / f"{table}.csv"
    fh, writer = mbraw.open_csv_writer(path)
    with fh:
        writer.writerow(columns)
    return {"archive": kind, "file": path.name, "columns": columns, "rows": 0,
            "present_in_archive": False, "copy_bytes": 0, "copy_sha256": sha256_bytes(b""),
            "csv_bytes": path.stat().st_size}


def extract_archive(kind: str, source: str, is_local: bool, wanted: list[str],
                    table_list: list[str], schema: dict, out_dir: Path, min_free: float,
                    check_meta) -> tuple[dict, dict]:
    """Una sola passata sull'archivio: legge i metadati, estrae `wanted`, si ferma dopo l'ultima."""
    order = {t: i for i, t in enumerate(table_list)}
    last_idx = max(order[t] for t in wanted)
    prog = Progress(kind, None, out_dir, min_free)
    t0 = time.time()
    log(f"{kind}: apro {source}; tabelle richieste (in ordine d'archivio): {', '.join(wanted)}")
    stream = LocalStream(Path(source), prog) if is_local else HttpStream(source, prog)
    meta: dict[str, str] = {}
    results: dict[str, dict] = {}
    prev_idx = -1
    meta_checked = False
    stopped_early = False
    last_member = None
    try:
        buffered = io.BufferedReader(stream, buffer_size=1 << 20)
        with bz2.BZ2File(buffered) as bz, tarfile.open(fileobj=bz, mode="r|") as tf:
            for member in tf:
                name = member.name[2:] if member.name.startswith("./") else member.name
                last_member = name
                prog.member = name
                if name in META_FILES:
                    f = tf.extractfile(member)
                    meta[name] = f.read().decode("utf-8").strip() if f else ""
                    continue
                if not name.startswith("mbdump/") or not member.isfile():
                    continue
                if not meta_checked:
                    check_meta(kind, meta)
                    meta_checked = True
                table = name[len("mbdump/"):]
                if table not in order:
                    log(f"  {kind}: membro {name} non presente in @{ARCHIVES[kind][1]}: ignorato")
                    continue
                if order[table] < prev_idx:
                    raise FatalError(f"{kind}: ordine dei membri diverso da @{ARCHIVES[kind][1]} "
                                     f"({name} dopo {table_list[prev_idx]}).")
                prev_idx = order[table]
                if table in wanted:
                    check_free_space(out_dir, min_free)
                    log(f"  {kind}: estraggo {table} ({member.size:,} B non compressi; "
                        f"{prog.describe(stream.pos)})")
                    results[table] = extract_table(kind, table, tf.extractfile(member),
                                                   schema["columns"][table], out_dir, prog)
                    log(f"  {kind}: {table}: {results[table]['rows']:,} righe")
                if prev_idx >= last_idx:
                    stopped_early = True
                    break
    except EOFError as e:
        raise FatalError(f"{kind}: archivio troncato al byte {stream.pos:,} ({e}).") from e
    except (OSError, tarfile.TarError) as e:
        raise FatalError(f"{kind}: errore al byte {stream.pos:,} dell'archivio (CRC o "
                         f"decompressione bzip2, tar corrotto, oppure scrittura su disco): {e!r}") from e
    finally:
        stream.close()
    if not meta_checked:
        check_meta(kind, meta)
    for t in wanted:
        if t not in results:  # tabella vuota: make_tar non la include (DatabaseDump.pm)
            log(f"  {kind}: {t} assente dall'archivio (tabella vuota): CSV con sola intestazione")
            results[t] = write_empty_table(kind, t, schema["columns"][t], out_dir)
    info = {
        "file": ARCHIVES[kind][0],
        "mode": "local" if is_local else "remote",
        "source": source,
        "size_bytes": stream.total,
        "bytes_read": stream.pos,
        "percent_read": round(100 * stream.pos / stream.total, 2) if stream.total else None,
        "stopped_early": stopped_early,
        "last_member": last_member,
        "seconds": round(time.time() - t0, 1),
        "TIMESTAMP": meta.get("TIMESTAMP"),
        "SCHEMA_SEQUENCE": meta.get("SCHEMA_SEQUENCE"),
        "REPLICATION_SEQUENCE": meta.get("REPLICATION_SEQUENCE", ""),
    }
    how = "interrotto dopo l'ultima tabella richiesta" if stopped_early else "letto fino in fondo"
    log(f"{kind}: {how}; {prog.describe(stream.pos)}; ultimo membro {last_member}; "
        f"{info['seconds']:.0f} s")
    return info, results


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #

def prepare_out_dirs(out: Path) -> tuple[Path, Path]:
    tmp = out.with_name(out.name + ".tmp")
    old = out.with_name(out.name + ".old")
    if tmp.exists():
        log(f"ATTENZIONE: trovata {tmp} (esecuzione precedente interrotta): "
            "non la riuso, la elimino e ricomincio da zero.")
        shutil.rmtree(tmp)
    if old.exists():
        if out.exists():
            log(f"ATTENZIONE: trovata {old} residua: la elimino.")
            shutil.rmtree(old)
        else:
            log(f"ATTENZIONE: sostituzione precedente interrotta: ripristino {old} come {out}.")
            old.rename(out)
    tmp.mkdir(parents=True)
    return tmp, old


def main(argv: list[str] | None = None) -> int:
    here = Path(__file__).resolve().parent
    ap = argparse.ArgumentParser(description="Estrae le tabelle MusicBrainz di TABLES in CSV (data_raw/).")
    ap.add_argument("--snapshot", help="snapshot remoto, es. 20260926-002121 (default: LATEST)")
    ap.add_argument("--core", type=Path, help="mbdump.tar.bz2 locale")
    ap.add_argument("--derived", type=Path, help="mbdump-derived.tar.bz2 locale")
    ap.add_argument("--out", type=Path, default=here / "data_raw", help="default: data_raw/ accanto allo script")
    ap.add_argument("--base-url", default=DEFAULT_BASE_URL, help=f"default: {DEFAULT_BASE_URL}")
    ap.add_argument("--schema-ref", default=DEFAULT_SCHEMA_REF,
                    help=f"branch, tag o commit di {SCHEMA_REPO} (default: {DEFAULT_SCHEMA_REF})")
    ap.add_argument("--schema-dir", type=Path,
                    help="cartella con Constants.pm, CreateTables.sql, DBDefs.pm.sample (uso offline)")
    ap.add_argument("--min-free-gib", type=float, default=DEFAULT_MIN_FREE_GIB,
                    help=f"spazio libero minimo (default: {DEFAULT_MIN_FREE_GIB:g} GiB)")
    args = ap.parse_args(argv)

    t0 = time.time()
    out: Path = args.out.resolve()
    try:
        if len(set(TABLES)) != len(TABLES):
            raise FatalError("TABLES contiene tabelle duplicate.")
        if args.snapshot and not SNAPSHOT_RE.fullmatch(args.snapshot):
            raise FatalError(f"--snapshot {args.snapshot!r}: forma attesa AAAAMMGG-hhmmss.")
        for p in (args.core, args.derived):
            if p is not None and not p.is_file():
                raise FatalError(f"File non trovato: {p}")
        out.parent.mkdir(parents=True, exist_ok=True)
        check_free_space(out.parent, args.min_free_gib)
        tmp, old = prepare_out_dirs(out)

        schema = load_schema(args.schema_ref, args.schema_dir, tmp / "_schema")

        # (a) archivio di ogni tabella, (b) colonne
        by_archive: dict[str, list[str]] = {k: [] for k in ARCHIVES}
        for t in TABLES:
            kinds = [k for k, (_, ln) in ARCHIVES.items() if t in schema["lists"][ln]]
            if not kinds:
                other = [ln for ln, lst in schema["lists"].items() if t in lst]
                raise FatalError(f"Tabella {t!r} non presente in core/derived" +
                                 (f" (si trova in @{', @'.join(other)}: archivio non gestito)." if other
                                  else " ne' in altre liste di Constants.pm."))
            if t not in schema["columns"]:
                raise FatalError(f"Tabella {t!r} non definita in CreateTables.sql.")
            by_archive[kinds[0]].append(t)
        for k in by_archive:
            lst = schema["lists"][ARCHIVES[k][1]]
            by_archive[k].sort(key=lst.index)
        needed = [k for k in ARCHIVE_ORDER if by_archive[k]]

        # sorgenti e nome dello snapshot
        local = {"core": args.core, "derived": args.derived}
        remote_needed = [k for k in needed if local[k] is None]
        if remote_needed:
            base = args.base_url.rstrip("/")
            if args.snapshot:
                snapshot, snap_src = args.snapshot, "--snapshot"
            else:
                snapshot = _http_get(f"{base}/LATEST").decode("ascii").strip()
                snap_src = "LATEST"
                if not SNAPSHOT_RE.fullmatch(snapshot):
                    raise FatalError(f"Contenuto inatteso di LATEST: {snapshot!r}")
            if not base.lower().startswith("https://"):
                log(f"ATTENZIONE: --base-url non HTTPS: {base}")
        else:
            snapshot, snap_src = args.snapshot, "--snapshot" if args.snapshot else None
            if snapshot is None:
                for k in ("core", "derived"):
                    if local[k] is not None and SNAPSHOT_RE.fullmatch(local[k].resolve().parent.name):
                        snapshot, snap_src = local[k].resolve().parent.name, f"cartella di --{k}"
                        break
            if snapshot is None:
                log("ATTENZIONE: nome dello snapshot non determinato (usare --snapshot).")
        log(f"Snapshot: {snapshot or 'non determinato'} ({snap_src or '-'}); "
            f"archivi: {', '.join(f'{k}={len(by_archive[k])} tabelle' for k in needed)}")

        first: dict = {}

        def check_meta(kind: str, meta: dict) -> None:
            for key in ("TIMESTAMP", "SCHEMA_SEQUENCE"):
                if not meta.get(key):
                    raise FatalError(f"{kind}: file {key} assente prima delle tabelle.")
            if int(meta["SCHEMA_SEQUENCE"]) != schema["schema_sequence"]:
                raise FatalError(
                    f"{kind}: SCHEMA_SEQUENCE {meta['SCHEMA_SEQUENCE']}, ma lo schema caricato "
                    f"(DB_SCHEMA_SEQUENCE) e' {schema['schema_sequence']}: indicare con --schema-ref "
                    f"un ref di {SCHEMA_REPO} con lo schema {meta['SCHEMA_SEQUENCE']}.")
            if first:
                for key in ("TIMESTAMP", "SCHEMA_SEQUENCE"):
                    if meta[key] != first["meta"][key]:
                        raise FatalError(f"Snapshot diversi: {key} {first['kind']}={first['meta'][key]!r}, "
                                         f"{kind}={meta[key]!r}. Mi fermo senza scrivere data_raw/.")
            else:
                first.update(kind=kind, meta=dict(meta))
            log(f"  {kind}: TIMESTAMP {meta['TIMESTAMP']}, SCHEMA_SEQUENCE {meta['SCHEMA_SEQUENCE']}, "
                f"REPLICATION_SEQUENCE {meta.get('REPLICATION_SEQUENCE', '')}")

        archives: dict[str, dict] = {}
        tables: dict[str, dict] = {}
        for k in needed:
            if local[k] is not None:
                src, is_local = str(local[k].resolve()), True
            else:
                src, is_local = f"{args.base_url.rstrip('/')}/{snapshot}/{ARCHIVES[k][0]}", False
            info, res = extract_archive(k, src, is_local, by_archive[k], schema["lists"][ARCHIVES[k][1]],
                                        schema, tmp, args.min_free_gib, check_meta)
            archives[k] = info
            tables.update(res)

        ref = archives.get("core") or archives[needed[0]]
        repl = {k: a["REPLICATION_SEQUENCE"] for k, a in archives.items()}
        if len(set(repl.values())) > 1:
            log(f"ATTENZIONE: REPLICATION_SEQUENCE diverse tra gli archivi: {repl}")
        snap = {
            "format_version": mbraw.SNAPSHOT_FORMAT_VERSION,
            "snapshot": snapshot,
            "snapshot_source": snap_src,
            "TIMESTAMP": ref["TIMESTAMP"],
            "SCHEMA_SEQUENCE": ref["SCHEMA_SEQUENCE"],
            "REPLICATION_SEQUENCE": ref["REPLICATION_SEQUENCE"],
            "extracted_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "generated_by": f"fetch_extract.py v{SCRIPT_VERSION}",
            "csv_convention": "UTF-8, RFC 4180, CRLF, intestazione; NULL = campo vuoto senza "
                              "virgolette, ogni altro valore tra virgolette (stringa vuota = \"\"); "
                              "Python csv.QUOTE_NOTNULL",
            "schema": {**schema["source"], "db_schema_sequence": schema["schema_sequence"]},
            "archives": {k: archives[k] for k in ARCHIVES if k in archives},
            "tables": {t: tables[t] for t in TABLES},
        }
        with open(tmp / mbraw.SNAPSHOT_FILE, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(snap, fh, ensure_ascii=False, indent=2)
            fh.write("\n")

        # sostituzione di data_raw/: mai snapshot mescolati
        try:
            if out.exists():
                out.rename(old)
            tmp.rename(out)
            if old.exists():
                shutil.rmtree(old)
        except OSError as e:
            raise FatalError(f"Sostituzione di {out} non riuscita ({e}); un file e' forse aperto "
                             f"in un altro programma. Dati completi in {tmp}.") from e
    except FatalError as e:
        log(f"ERRORE: {e}")
        log("data_raw/ non modificata; la cartella .tmp viene eliminata alla prossima esecuzione.")
        return 2

    log(f"Fatto in {time.time() - t0:.0f} s -> {out}")
    for t in TABLES:
        i = snap["tables"][t]
        print(f"{t:<22} {i['archive']:<8} {i['rows']:>12,} righe  {i['csv_bytes']:>14,} B")
    for k, a in snap["archives"].items():
        print(f"{k}: {a['mode']}, letti {a['bytes_read']:,} B su {a['size_bytes']:,} "
              f"({a['percent_read']}%), interrotto in anticipo: {a['stopped_early']}, {a['seconds']} s")
    return 0


if __name__ == "__main__":
    sys.exit(main())
