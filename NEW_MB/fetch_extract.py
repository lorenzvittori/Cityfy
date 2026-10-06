r"""Scarica ed estrae le tabelle del dump PostgreSQL di MusicBrainz in CSV (data_raw/).

Uso:
    python NEW_MB\fetch_extract.py                         # ultimo snapshot, scarica in streaming
    python NEW_MB\fetch_extract.py --snapshot 20260926-002121
    python NEW_MB\fetch_extract.py --core mbdump.tar.bz2 --derived mbdump-derived.tar.bz2
    python NEW_MB\fetch_extract.py --schema-dir DIR        # DIR con CreateTables.sql, Constants.pm
                                                    # e DBDefs.pm.sample (lavoro offline)

Opzioni: --snapshot (default LATEST), --base-url, --core/--derived (archivi locali,
da passare insieme), --out (default data_raw), --schema-ref (default production),
--schema-dir.

Le tabelle da estrarre sono in TABLES (qui sotto): funziona per qualunque tabella,
perche' colonne e archivio si ricavano a runtime dai file di schema di
musicbrainz-server. Gli archivi non vengono salvati su disco: catena
HTTPS/file -> bz2 incrementale -> tarfile "r|", con arresto subito dopo l'ultima
tabella richiesta. Output in <out>.tmp/, poi sostituisce <out>.
Errori: "ERRORE: ..." su stderr, exit code 2.
"""

import argparse
import bz2
import hashlib
import http.client
import json
import re
import shutil
import sys
import tarfile
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import mbraw

TABLES = [
    # --- core ---
    "artist", "artist_alias", "genre", "l_genre_genre", "link", "link_type", "url", "l_artist_url",
    # "release_group", "artist_credit", "artist_credit_name",
    # "recording", "isrc",
    # --- derived ---
    "tag", "artist_tag",
]

GITHUB_REPO = "metabrainz/musicbrainz-server"
SCHEMA_FILES = {
    "CreateTables.sql": "admin/sql/CreateTables.sql",
    "Constants.pm": "lib/MusicBrainz/Server/Constants.pm",
    "DBDefs.pm.sample": "lib/DBDefs.pm.sample",
}
ARCHIVES = {"core": "mbdump.tar.bz2", "derived": "mbdump-derived.tar.bz2"}
LISTS = {"core": "CORE_TABLE_LIST", "derived": "DERIVED_TABLE_LIST"}
HEADER_FILES = {"TIMESTAMP", "SCHEMA_SEQUENCE", "REPLICATION_SEQUENCE"}
MIN_FREE = 6 * 2**30
LOG_EVERY = 30
CHUNK = 1 << 16
RETRIES = 5
UA = "cityfy-fetch-extract/1.0"


class FetchError(Exception):
    pass


def log(msg):
    print(msg, file=sys.stderr, flush=True)


# --------------------------------------------------------------------------- progresso

class Progress:
    """Log ogni 30 s e controllo dello spazio libero."""

    def __init__(self, path):
        self.path = path
        self.source = None
        self.kind = ""
        self.current = "-"
        self.last = time.monotonic()

    def check_space(self):
        free = shutil.disk_usage(self.path).free
        if free < MIN_FREE:
            raise FetchError(
                f"spazio libero insufficiente: {free / 2**30:.2f} GiB (minimo 6 GiB)"
            )

    def tick(self):
        now = time.monotonic()
        if now - self.last < LOG_EVERY:
            return
        self.last = now
        self.check_space()
        s = self.source
        if s is None:
            return
        pct = f"{100 * s.pos / s.total:.1f}%" if s.total else "n/d"
        log(f"[{self.kind}] letti {s.pos / 2**20:.0f} MiB ({pct}); tabella corrente: {self.current}")


# --------------------------------------------------------------------------- sorgenti

class FileSource:
    def __init__(self, path, progress):
        self.f = open(path, "rb")
        self.pos = 0
        self.total = Path(path).stat().st_size
        self.progress = progress

    def read(self, n):
        data = self.f.read(n)
        self.pos += len(data)
        self.progress.tick()
        return data

    def close(self):
        self.f.close()


class HttpSource:
    """Lettura HTTPS con ripresa (Range/If-Range), fino a RETRIES tentativi consecutivi."""

    def __init__(self, url, progress):
        self.url = url
        self.pos = 0
        self.total = None
        self.validator = None
        self.resp = None
        self.failures = 0
        self.progress = progress
        self._open()

    def _open(self):
        headers = {"User-Agent": UA}
        if self.pos:
            headers["Range"] = f"bytes={self.pos}-"
            if self.validator:
                headers["If-Range"] = self.validator
        resp = urllib.request.urlopen(urllib.request.Request(self.url, headers=headers), timeout=60)
        if self.pos:
            if resp.status != 206:
                resp.close()
                raise FetchError("il file sul server e' cambiato durante il download: ripresa impossibile")
        else:
            cl = resp.headers.get("Content-Length")
            self.total = int(cl) if cl else None
            etag = resp.headers.get("ETag")
            if etag and not etag.startswith("W/"):
                self.validator = etag
            else:
                self.validator = resp.headers.get("Last-Modified")
        self.resp = resp

    def read(self, n):
        while True:
            try:
                if self.resp is None:
                    self._open()
                data = self.resp.read(n)    # type: ignore
                if not data and self.total is not None and self.pos < self.total:
                    raise ConnectionError("connessione chiusa prima della fine del file")
                if data:
                    self.failures = 0
                self.pos += len(data)
                self.progress.tick()
                return data
            except FetchError:
                raise
            except (OSError, http.client.HTTPException) as e:
                if isinstance(e, urllib.error.HTTPError) and e.code < 500 and e.code not in (408, 429):
                    raise FetchError(f"HTTP {e.code} per {self.url}") from e
                self.failures += 1
                if self.failures > RETRIES:
                    raise FetchError(f"download interrotto dopo {RETRIES} tentativi: {e}") from e
                log(f"download interrotto ({e}); tentativo {self.failures}/{RETRIES} dal byte {self.pos}")
                self.close()
                time.sleep(min(2**self.failures, 30))

    def close(self):
        if self.resp is not None:
            try:
                self.resp.close()
            except Exception:
                pass
            self.resp = None


class Bz2Reader:
    """Decompressione bz2 incrementale (anche multi-stream) con read(n)."""

    def __init__(self, raw):
        self.raw = raw
        self.dec = bz2.BZ2Decompressor()
        self.pending = b""
        self.buf = bytearray()
        self.mid = False
        self.done = False

    def _fill(self):
        if self.pending:
            chunk, self.pending = self.pending, b""
        else:
            chunk = self.raw.read(CHUNK)
            if not chunk:
                if self.mid:
                    raise FetchError("archivio troncato: flusso bz2 incompleto")
                self.done = True
                return
        self.buf += self.dec.decompress(chunk)
        if self.dec.eof:
            self.pending = self.dec.unused_data
            self.dec = bz2.BZ2Decompressor()
            self.mid = False
        else:
            self.mid = True

    def read(self, n=-1):
        if n is None or n < 0:
            while not self.done:
                self._fill()
            out, self.buf = bytes(self.buf), bytearray()
            return out
        while len(self.buf) < n and not self.done:
            self._fill()
        out = bytes(self.buf[:n])
        del self.buf[:n]
        return out

    def close(self):
        pass


# --------------------------------------------------------------------------- schema

def http_get(url, accept=None):
    headers = {"User-Agent": UA}
    if accept:
        headers["Accept"] = accept
    last = None
    for attempt in range(3):
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code < 500:
                raise FetchError(f"HTTP {e.code} per {url}") from e
            last = e
        except (OSError, http.client.HTTPException) as e:
            last = e
        time.sleep(2**attempt)
    raise FetchError(f"download fallito: {url}: {last}")


def load_schema(args):
    if args.schema_dir:
        d = Path(args.schema_dir)
        return {n: (d / n).read_bytes() for n in SCHEMA_FILES}, "local"
    ref = args.schema_ref
    if re.fullmatch(r"[0-9a-f]{40}", ref):
        commit = ref
    else:
        body = http_get(
            f"https://api.github.com/repos/{GITHUB_REPO}/commits/{urllib.parse.quote(ref, safe='/')}",
            accept="application/vnd.github.sha",
        )
        commit = body.decode("ascii", "replace").strip()
        if not re.fullmatch(r"[0-9a-f]{40}", commit):
            raise FetchError(f"impossibile risolvere --schema-ref {ref!r} in un commit")
    files = {
        name: http_get(f"https://raw.githubusercontent.com/{GITHUB_REPO}/{commit}/{path}")
        for name, path in SCHEMA_FILES.items()
    }
    return files, commit


def parse_schema_sequence(text):
    m = re.search(r"sub\s+DB_SCHEMA_SEQUENCE\b[^{}]*\{\s*(?:return\s+)?(\d+)", text) or re.search(
        r"\bDB_SCHEMA_SEQUENCE\b\s*(?:=>|=)\s*(\d+)", text
    )
    if not m:
        raise FetchError("DB_SCHEMA_SEQUENCE non trovato in DBDefs.pm.sample")
    return int(m.group(1))


def parse_table_list(text, name):
    m = re.search("@" + name + r"\s*(?:=>|=)\s*(?:qw\s*)?([(\[{<])", text)
    if not m:
        raise FetchError(f"@{name} non trovato in Constants.pm")
    close = {"(": ")", "[": "]", "{": "}", "<": ">"}[m.group(1)]
    end = text.find(close, m.end())
    if end < 0:
        raise FetchError(f"@{name}: lista non chiusa in Constants.pm")
    body = re.sub(r"#[^\n]*", "", text[m.end():end])
    tokens = [t for t in re.findall(r"[A-Za-z_][\w.]*", body) if t != "qw"]
    if not tokens:
        raise FetchError(f"@{name}: lista vuota")
    return list(dict.fromkeys(tokens))


def strip_sql_comments(sql):
    out = []
    i, n = 0, len(sql)
    while i < n:
        c = sql[i]
        if c == "'":
            j = i + 1
            while j < n:
                if sql[j] == "'":
                    if j + 1 < n and sql[j + 1] == "'":
                        j += 2
                        continue
                    break
                j += 1
            out.append(sql[i:j + 1])
            i = j + 1
        elif sql.startswith("--", i):
            j = sql.find("\n", i)
            i = n if j < 0 else j
        elif sql.startswith("/*", i):
            j = sql.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
        else:
            out.append(c)
            i += 1
    return "".join(out)


def _scan_top(text, start, split):
    """Da text[start] (subito dopo la '(' di apertura) restituisce (elementi|fine, indice)."""
    depth, quote, cur, items = 0, None, [], []
    i = start
    while i < len(text):
        c = text[i]
        if quote:
            cur.append(c)
            if c == quote:
                quote = None
        elif c in "'\"":
            quote = c
            cur.append(c)
        elif c == "(":
            depth += 1
            cur.append(c)
        elif c == ")":
            if depth == 0:
                items.append("".join(cur))
                return items, i
            depth -= 1
            cur.append(c)
        elif c == "," and depth == 0 and split:
            items.append("".join(cur))
            cur = []
        else:
            cur.append(c)
        i += 1
    raise FetchError("CREATE TABLE non chiuso in CreateTables.sql")


NON_COLUMN = {"CONSTRAINT", "PRIMARY", "FOREIGN", "UNIQUE", "CHECK", "EXCLUDE", "LIKE"}


def parse_create_tables(sql):
    sql = strip_sql_comments(sql)
    tables = {}
    pat = re.compile(r"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?\"?([\w.]+)\"?\s*\(", re.I)
    for m in pat.finditer(sql):
        name = m.group(1).split(".")[-1]
        items, _ = _scan_top(sql, m.end(), True)
        cols = []
        for item in items:
            mm = re.match(r"\s*\"?([A-Za-z_][\w$]*)\"?", item)
            if mm and mm.group(1).upper() not in NON_COLUMN:
                cols.append(mm.group(1))
        tables.setdefault(name, cols)
    return tables


# --------------------------------------------------------------------------- COPY

_SIMPLE = {b"b": b"\b", b"f": b"\f", b"n": b"\n", b"r": b"\r", b"t": b"\t", b"v": b"\v"}
_ESC = re.compile(rb"\\(?:([0-7]{1,3})|x([0-9A-Fa-f]{1,2})|(.))", re.DOTALL)


def _esc_sub(m):
    if m.group(1):
        return bytes([int(m.group(1), 8) & 0xFF])
    if m.group(2):
        return bytes([int(m.group(2), 16)])
    c = m.group(3)
    return _SIMPLE.get(c, c)


def decode_field(b):
    if b == b"\\N":
        return None
    if b"\\" in b:
        b = _ESC.sub(_esc_sub, b)        # type: ignore
    return b.decode("utf-8")


def extract_table(tf, member, table, kind, cols, tmp, progress):
    progress.current = table
    progress.check_space()
    f = tf.extractfile(member)
    if f is None:
        raise FetchError(f"{table}: il membro del tar non e' un file regolare")
    ncol = len(cols)
    h = hashlib.sha256()
    size = count = 0
    ended = False

    def rows():
        nonlocal size, count, ended
        for lineno, raw in enumerate(f, 1):
            h.update(raw)
            size += len(raw)
            if ended:
                continue
            line = raw[:-1] if raw.endswith(b"\n") else raw
            if line == b"\\.":
                ended = True
                continue
            parts = line.split(b"\t")
            if len(parts) != ncol:
                raise FetchError(f"{table}, riga {lineno}: {len(parts)} colonne, attese {ncol}")
            try:
                row = [decode_field(p) for p in parts]
            except UnicodeDecodeError as e:
                raise FetchError(f"{table}, riga {lineno}: UTF-8 non valido ({e})") from e
            count += 1
            if count % 4096 == 0:
                progress.tick()
            yield row

    path = tmp / f"{table}.csv"
    mbraw.write_table(path, cols, rows())
    if size != member.size:
        raise FetchError(f"{table}: letti {size} byte, il tar ne dichiara {member.size}")
    log(f"[{kind}] {table}: {count} righe")
    return {
        "archive": kind, "rows": count, "columns": ncol, "copy_bytes": size,
        "copy_sha256": h.hexdigest(), "csv_bytes": path.stat().st_size,
    }


def empty_table(table, kind, cols, tmp):
    path = tmp / f"{table}.csv"
    mbraw.write_table(path, cols, [])
    log(f"[{kind}] {table}: vuota (assente dal tar)")
    return {
        "archive": kind, "rows": 0, "columns": len(cols), "copy_bytes": 0,
        "copy_sha256": hashlib.sha256(b"").hexdigest(), "csv_bytes": path.stat().st_size,
    }


def check_headers(kind, hdr, state, db_seq):
    for k in ("TIMESTAMP", "SCHEMA_SEQUENCE"):
        if k not in hdr:
            raise FetchError(f"archivio {kind}: manca il file {k}")
    try:
        seq = int(hdr["SCHEMA_SEQUENCE"])
    except ValueError:
        raise FetchError(f"archivio {kind}: SCHEMA_SEQUENCE non numerico") from None
    if seq != db_seq:
        raise FetchError(
            f"SCHEMA_SEQUENCE del dump ({seq}) diverso da DB_SCHEMA_SEQUENCE dello schema "
            f"({db_seq}): prova con --schema-ref (un altro branch, tag o commit SHA)"
        )
    for key, val in (("timestamp", hdr["TIMESTAMP"]), ("schema_sequence", seq)):
        if key in state and state[key] != val:
            raise FetchError(
                f"i due archivi non coincidono ({key}: {state[key]!r} contro {val!r})"
            )
        state[key] = val
    rep = hdr.get("REPLICATION_SEQUENCE", "")
    state.setdefault("replication_sequence", int(rep) if rep.isdigit() else (rep or None))


def extract_archive(kind, source, order, wanted, columns, tmp, progress, db_seq, state, tables):
    """Una passata sull'archivio; restituisce True se si e' fermata in anticipo."""
    pos_of = {t: i for i, t in enumerate(order)}
    pending = [t for t in order if t in wanted]
    hdr = {}
    checked = False
    early = False
    with tarfile.open(fileobj=Bz2Reader(source), mode="r|") as tf:  # type: ignore
        for member in tf:
            name = member.name.removeprefix("./")
            progress.current = f"(salto {name})"
            if name in HEADER_FILES:
                hdr[name] = tf.extractfile(member).read().decode("ascii", "replace").strip()
                continue
            if not name.startswith("mbdump/"):
                continue
            if not checked:
                check_headers(kind, hdr, state, db_seq)
                checked = True
            table = name[len("mbdump/"):]
            pos = pos_of.get(table)
            if pos is None:
                continue
            while pending and pos_of[pending[0]] < pos:
                t = pending.pop(0)
                tables[t] = empty_table(t, kind, columns[t], tmp)
            if pending and pending[0] == table:
                pending.pop(0)
                tables[table] = extract_table(tf, member, table, kind, columns[table], tmp, progress)
            if not pending:
                early = True
                break
    if not checked:
        check_headers(kind, hdr, state, db_seq)
    for t in pending:
        tables[t] = empty_table(t, kind, columns[t], tmp)
    return early


# --------------------------------------------------------------------------- main

def parse_args(argv):
    p = argparse.ArgumentParser(description="Scarica ed estrae tabelle MusicBrainz in CSV.")
    p.add_argument("--snapshot", default="LATEST")
    p.add_argument("--base-url", default="https://data.metabrainz.org/pub/musicbrainz/data/fullexport/")
    p.add_argument("--core", help="archivio core locale (mbdump.tar.bz2)")
    p.add_argument("--derived", help="archivio derived locale (mbdump-derived.tar.bz2)")
    p.add_argument("--out", default="NEW_MB/data_raw")
    p.add_argument("--schema-ref", default="production")
    p.add_argument("--schema-dir", help="cartella locale con i file di schema")
    return p.parse_args(argv)


def run(args):
    if bool(args.core) != bool(args.derived):
        raise FetchError("--core e --derived vanno passati insieme")
    wanted = list(dict.fromkeys(TABLES))
    if not wanted:
        raise FetchError("TABLES e' vuoto")
    out = Path(args.out)
    tmp = out.with_name(out.name + ".tmp")
    parent = out.resolve().parent
    parent.mkdir(parents=True, exist_ok=True)
    progress = Progress(parent)
    progress.check_space()
    if tmp.exists():
        log(f"{tmp} esiste gia' (estrazione interrotta?): lo cancello e ricomincio da zero")
        shutil.rmtree(tmp)
    (tmp / "_schema").mkdir(parents=True)

    files, commit = load_schema(args)
    for n, b in files.items():
        (tmp / "_schema" / n).write_bytes(b)
    sha = {n: hashlib.sha256(b).hexdigest() for n, b in files.items()}
    lists = {k: parse_table_list(files["Constants.pm"].decode("utf-8"), LISTS[k]) for k in ARCHIVES}
    columns = parse_create_tables(files["CreateTables.sql"].decode("utf-8"))
    db_seq = parse_schema_sequence(files["DBDefs.pm.sample"].decode("utf-8"))
    kind_of = {}
    for k, lst in lists.items():
        for t in lst:
            kind_of.setdefault(t, k)
    for t in wanted:
        if t not in kind_of:
            raise FetchError(f"tabella {t!r} assente da @CORE_TABLE_LIST/@DERIVED_TABLE_LIST")
        if t not in columns:
            raise FetchError(f"tabella {t!r} assente da CreateTables.sql")

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
    log(f"snapshot {snapshot}; schema commit {commit}; DB_SCHEMA_SEQUENCE {db_seq}")

    state, archives, tables = {}, {}, {}
    for kind in ("derived", "core"):
        names = {t for t in wanted if kind_of[t] == kind}
        if not names:
            continue
        progress.kind = kind
        t1 = time.monotonic()
        source = (FileSource if local else HttpSource)(locs[kind], progress)
        progress.source = source    # type: ignore
        try:
            early = extract_archive(
                kind, source, lists[kind], names, columns, tmp, progress, db_seq, state, tables
            )
        finally:
            source.close()
        archives[kind] = {
            "source": str(locs[kind]),
            "bytes_read": source.pos,
            "total_bytes": source.total,
            "percent": round(100 * source.pos / source.total, 2) if source.total else None,
            "early_stop": early,
            "seconds": round(time.monotonic() - t1, 1),
        }

    snap = {
        "snapshot": snapshot,
        "timestamp": state["timestamp"],
        "schema_sequence": state["schema_sequence"],
        "replication_sequence": state.get("replication_sequence"),
        "extracted_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "schema": {"commit": commit, "files": sha},
        "archives": archives,
        "tables": {t: tables[t] for t in wanted},
    }
    with open(tmp / "snapshot.json", "w", encoding="utf-8") as f:
        json.dump(snap, f, indent=2, ensure_ascii=False)
        f.write("\n")

    if out.exists():
        old = out.with_name(out.name + ".old")
        if old.exists():
            shutil.rmtree(old)
        out.replace(old)
        tmp.replace(out)
        shutil.rmtree(old)
    else:
        tmp.replace(out)
    log(f"fatto: {len(wanted)} tabelle in {out}")


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
