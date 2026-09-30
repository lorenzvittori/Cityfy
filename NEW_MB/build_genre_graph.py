#!/usr/bin/env python3
"""
build_genre_graph.py -- Grafo dei generi musicali di MusicBrainz (GraphML + CSV).

Progetto "Categorizzazione Generi musicali e Artisti". Fonte unica: dump
PostgreSQL ufficiale di MusicBrainz, file mbdump.tar.bz2 (licenza CC0).
Tabelle lette: genre, l_genre_genre, link, link_type (+ TIMESTAMP, SCHEMA_SEQUENCE).

OUTPUT (in --out-dir, default: cartella corrente)
  genres.graphml     multigrafo orientato (networkx), archi entity0 -> entity1
  genres.csv         lista di adiacenza, una riga per genere
  genres_report.md   versione del dump, conteggi, controlli, decisioni e assunzioni

REQUISITI
  Python >= 3.10;  pip install "networkx>=3.0"

USO
  Modalita' principale (dump gia' scaricato):
    python build_genre_graph.py --dump /percorso/20260923-002121/mbdump.tar.bz2
  Il nome dello snapshot (es. 20260923-002121) e' ricavato dalla cartella che
  contiene il file, se ha la forma AAAAMMGG-hhmmss; altrimenti indicarlo con
    --snapshot 20260923-002121

  Opzione: download automatico dell'ultimo snapshot (solo mbdump.tar.bz2,
  con verifica SHA256):
    python build_genre_graph.py --download --download-dir ./dumps

DOWNLOAD MANUALE DEL DUMP (mbdump.tar.bz2 + mbdump-derived.tar.bz2)
  Mirror ufficiale HTTPS (EU, Germania):
    https://data.metabrainz.org/pub/musicbrainz/data/fullexport/
  Il file LATEST contiene il nome dell'ultimo snapshot. Sul server restano solo
  gli snapshot piu' recenti (nuovi dump il mercoledi' e il sabato): scaricare
  subito dopo aver letto LATEST e tenere i file nella cartella dello snapshot.

  Linux / macOS (bash):
    BASE=https://data.metabrainz.org/pub/musicbrainz/data/fullexport
    SNAP=$(curl -fsSL "$BASE/LATEST")
    mkdir -p "$SNAP" && cd "$SNAP"
    curl -fL -C - -O "$BASE/$SNAP/mbdump.tar.bz2"
    curl -fL -C - -O "$BASE/$SNAP/mbdump-derived.tar.bz2"
    curl -fL -O "$BASE/$SNAP/SHA256SUMS"
    # verifica SHA256 -- Linux:
    grep -E ' [*]?mbdump(-derived)?\\.tar\\.bz2$' SHA256SUMS | sha256sum -c -
    # verifica SHA256 -- macOS:
    grep -E ' [*]?mbdump(-derived)?\\.tar\\.bz2$' SHA256SUMS | shasum -a 256 -c -
    # atteso:  mbdump.tar.bz2: OK   mbdump-derived.tar.bz2: OK

  Windows (PowerShell, curl.exe incluso in Windows 10+):
    $BASE = "https://data.metabrainz.org/pub/musicbrainz/data/fullexport"
    $SNAP = (curl.exe -fsSL "$BASE/LATEST").Trim()
    mkdir $SNAP; cd $SNAP
    curl.exe -fL -C - -O "$BASE/$SNAP/mbdump.tar.bz2"
    curl.exe -fL -C - -O "$BASE/$SNAP/mbdump-derived.tar.bz2"
    curl.exe -fL -O "$BASE/$SNAP/SHA256SUMS"
    Get-FileHash mbdump.tar.bz2 -Algorithm SHA256
    Select-String "mbdump.tar.bz2" SHA256SUMS     # confrontare i due hash

STIME (indicative)
  Disco: ~7 GB (mbdump.tar.bz2) + ~0.5 GB (mbdump-derived.tar.bz2), dimensioni
    dello snapshot 20260923-002121. Lo script non estrae nulla su disco
    (lettura in streaming); gli output occupano pochi MB. RAM: poche centinaia di MB.
  Download di 7 GB: ~10 min a 100 Mbit/s, ~30 min a 30 Mbit/s.
  Elaborazione: dominata dalla decompressione bzip2 (single-thread) della parte
    di archivio che precede link_type; lo script si ferma appena ha letto le
    tabelle necessarie. Stima: 10-40 min su un PC recente (vedi report per il
    tempo effettivo).
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import itertools
import re
import sys
import tarfile
import time
import urllib.parse
import urllib.request
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path

import networkx as nx

# --------------------------------------------------------------------------- #
# Costanti
# --------------------------------------------------------------------------- #

SCRIPT_VERSION = "1.0"
DEFAULT_BASE_URL = "https://data.metabrainz.org/pub/musicbrainz/data/fullexport"
DUMP_FILENAME = "mbdump.tar.bz2"
USER_AGENT = f"build_genre_graph/{SCRIPT_VERSION} (progetto personale di ricerca)"
SNAPSHOT_RE = re.compile(r"^\d{8}-\d{6}$")

# Colonne attese per tabella (schema v31, admin/sql/CreateTables.sql).
EXPECTED_SCHEMA_SEQUENCE = 31
TABLE_COLUMNS = {"genre": 6, "l_genre_genre": 9, "link": 11, "link_type": 16}
META_FILES = ("TIMESTAMP", "SCHEMA_SEQUENCE")

# link_type.gid -> etichetta dell'arco (https://musicbrainz.org/relationships/genre-genre)
UUID_SUBGENRE = "9d61bc67-fa39-4719-8025-ea056a5bd7e6"
UUID_INFLUENCED_BY = "59117855-52db-4371-8dd3-87a16f285499"
UUID_FUSION_OF = "723732ec-762c-4cb3-a2d0-e7e797c51915"
REL_TYPES = {
    UUID_SUBGENRE: "subgenre",
    UUID_INFLUENCED_BY: "influenced_by",
    UUID_FUSION_OF: "fusion_of",
}
TYPE_ORDER = ("subgenre", "influenced_by", "fusion_of")
TYPE_UUID = {v: k for k, v in REL_TYPES.items()}

CSV_HEADER = ["genre_mbid", "genre_name", "rel_sub", "rel_inf", "rel_fus"]
CSV_COLUMN_OF_TYPE = {"subgenre": "rel_sub", "influenced_by": "rel_inf", "fusion_of": "rel_fus"}

SEP = "|"
ALT_SEPARATORS = [";", "¦", "/", "#", "~", "^"]
CYCLE_CAP = 10_000

GRAPH_METADATA_STATIC = {
    "description": (
        "Grafo dei generi musicali ufficiali di MusicBrainz. Multigrafo orientato: "
        "nodi = generi (tabella genre), archi = relazioni genre-genre (tabella l_genre_genre)."
    ),
    "source": "MusicBrainz database dump, mbdump.tar.bz2 (licenza CC0)",
    "node_id": "MBID del genere (genre.gid)",
    "node_attr_name": "nome del genere (genre.name)",
    "node_attr_disambiguation": "disambiguazione (genre.comment); stringa vuota se assente",
    "edge_attr_type": "tipo di relazione: subgenre | influenced_by | fusion_of",
    "edge_orientation": (
        "Ogni arco X -> Y riproduce l'orientamento di MusicBrainz: X = entity0, Y = entity1 "
        "di l_genre_genre. Attenzione: subgenre va dal padre al figlio; influenced_by e "
        "fusion_of vanno dal genere derivato alla sua fonte."
    ),
    "edge_type_subgenre": (
        f"MusicBrainz 'subgenre' (link_type {UUID_SUBGENRE}; forward 'subgenres', reverse "
        "'subgenre of'). X -> Y: Y e' un sottogenere di X."
    ),
    "edge_type_influenced_by": (
        f"MusicBrainz 'influenced by' (link_type {UUID_INFLUENCED_BY}; forward 'influenced by', "
        "reverse 'influenced genres'). X -> Y: X ha influenze di Y, senza esserne sottogenere."
    ),
    "edge_type_fusion_of": (
        f"MusicBrainz 'fusion of' (link_type {UUID_FUSION_OF}; sottotipo di 'influenced by'; "
        "forward 'fusion of', reverse 'has fusion genres'). X -> Y: X e' nato come fusione "
        "(ibrido) di Y e di almeno un altro genere."
    ),
    "edge_multiplicity": (
        "Ogni relazione MusicBrainz ha un solo tipo; archi identici (stessi X, Y, type) "
        "sono collassati in un unico arco."
    ),
}


class FatalError(Exception):
    """Errore che impedisce di produrre output corretti."""


def log(msg: str) -> None:
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", file=sys.stderr, flush=True)


# --------------------------------------------------------------------------- #
# Download (opzionale)
# --------------------------------------------------------------------------- #

def _urlopen(url: str):
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    return urllib.request.urlopen(req, timeout=120)


def _sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(1 << 20):
            h.update(chunk)
    return h.hexdigest()


def download_latest(base_url: str, dest_root: Path) -> tuple[Path, str, str]:
    """Scarica mbdump.tar.bz2 dell'ultimo snapshot e ne verifica lo SHA256.

    Restituisce (percorso, nome snapshot, URL del file)."""
    base = base_url.rstrip("/")
    with _urlopen(f"{base}/LATEST") as r:
        snap = r.read().decode("ascii").strip()
    if not SNAPSHOT_RE.fullmatch(snap):
        raise FatalError(f"Contenuto inatteso di LATEST: {snap!r}")
    with _urlopen(f"{base}/{snap}/SHA256SUMS") as r:
        sums = r.read().decode("utf-8")
    expected = None
    for line in sums.splitlines():
        m = re.match(r"^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$", line)
        if m and m.group(2) == DUMP_FILENAME:
            expected = m.group(1).lower()
    if expected is None:
        raise FatalError(f"{DUMP_FILENAME} non presente in SHA256SUMS dello snapshot {snap}")

    dest = dest_root / snap / DUMP_FILENAME
    dest.parent.mkdir(parents=True, exist_ok=True)
    url = f"{base}/{snap}/{DUMP_FILENAME}"
    if dest.exists():
        log(f"{dest} esiste: verifico lo SHA256...")
        if _sha256_file(dest) == expected:
            log("SHA256 corretto, riuso il file esistente.")
            return dest, snap, url
        log("SHA256 diverso: riscarico.")

    part = dest.with_name(dest.name + ".part")
    h = hashlib.sha256()
    done = 0
    next_report = 0
    log(f"Download {url}")
    with _urlopen(url) as r, open(part, "wb") as f:
        total = int(r.headers.get("Content-Length") or 0)
        while chunk := r.read(1 << 20):
            f.write(chunk)
            h.update(chunk)
            done += len(chunk)
            if done >= next_report:
                pct = f" ({100 * done / total:.1f}%)" if total else ""
                log(f"  scaricati {done / 1e9:.2f} GB{pct}")
                next_report += 256 * 1024 * 1024
    if h.hexdigest() != expected:
        part.unlink(missing_ok=True)
        raise FatalError("SHA256 del file scaricato non corrisponde a SHA256SUMS: download corrotto.")
    part.replace(dest)
    log("Download completato, SHA256 verificato.")
    return dest, snap, url


# --------------------------------------------------------------------------- #
# Lettura del dump (formato COPY testuale di PostgreSQL)
# --------------------------------------------------------------------------- #

_ESCAPE_RE = re.compile(r"\\(?:([0-7]{1,3})|x([0-9A-Fa-f]{1,2})|(.))", re.S)
_SIMPLE_ESCAPES = {"b": "\b", "f": "\f", "n": "\n", "r": "\r", "t": "\t", "v": "\v"}


def _unescape(s: str) -> str:
    if "\\" not in s:
        return s

    def rep(m: re.Match) -> str:
        if m.group(1):
            return chr(int(m.group(1), 8))
        if m.group(2):
            return chr(int(m.group(2), 16))
        c = m.group(3)
        return _SIMPLE_ESCAPES.get(c, c)

    return _ESCAPE_RE.sub(rep, s)


def parse_copy_line(line: str) -> list[str | None]:
    fields = line.rstrip("\n").split("\t")
    return [None if f == r"\N" else _unescape(f) for f in fields]


@dataclass
class DumpData:
    meta: dict[str, str] = field(default_factory=dict)
    genres: dict[int, tuple[str, str, str]] = field(default_factory=dict)  # id -> (gid, name, comment)
    lgg: list[tuple[int, int, int]] = field(default_factory=list)          # (link, entity0, entity1)
    links: dict[int, int] = field(default_factory=dict)                     # link.id -> link_type.id
    link_types: dict[int, dict] = field(default_factory=dict)               # link_type.id -> riga
    stopped_early: bool = False


def read_dump(path: Path) -> DumpData:
    data = DumpData()
    wanted = set(TABLE_COLUMNS) | set(META_FILES)
    seen: set[str] = set()
    needed_links: set[int] | None = None

    log(f"Lettura in streaming di {path}")
    with tarfile.open(path, mode="r|bz2") as tf:
        for member in tf:
            name = member.name
            while name.startswith("./"):
                name = name[2:]
            if not member.isfile():
                continue

            if name in META_FILES:
                fobj = tf.extractfile(member)
                data.meta[name] = fobj.read().decode("utf-8").strip()
                seen.add(name)

            elif name.startswith("mbdump/") and name[len("mbdump/"):] in TABLE_COLUMNS:
                table = name[len("mbdump/"):]
                ncol = TABLE_COLUMNS[table]
                log(f"  tabella {table}")
                # In modalita' streaming l'oggetto non e' seekable: niente TextIOWrapper,
                # si itera sulle righe in byte (terminate da b"\n") e si decodifica UTF-8.
                for raw_line in tf.extractfile(member):
                    row = parse_copy_line(raw_line.decode("utf-8"))
                    if len(row) != ncol:
                        raise FatalError(
                            f"Tabella {table}: {len(row)} colonne invece di {ncol}. "
                            f"Lo schema del dump non corrisponde alla v{EXPECTED_SCHEMA_SEQUENCE} "
                            "per cui lo script e' scritto."
                        )
                    if table == "genre":
                        # id, gid, name, comment, edits_pending, last_updated
                        data.genres[int(row[0])] = (row[1], row[2], row[3] or "")
                    elif table == "l_genre_genre":
                        # id, link, entity0, entity1, ...
                        data.lgg.append((int(row[1]), int(row[2]), int(row[3])))
                    elif table == "link":
                        # id, link_type, ...
                        lid = int(row[0])
                        if needed_links is None or lid in needed_links:
                            data.links[lid] = int(row[1])
                    elif table == "link_type":
                        # id, parent, child_order, gid, entity_type0, entity_type1, name, ...
                        data.link_types[int(row[0])] = {
                            "parent": int(row[1]) if row[1] is not None else None,
                            "gid": row[3],
                            "entity_type0": row[4],
                            "entity_type1": row[5],
                            "name": row[6],
                        }
                if table == "l_genre_genre":
                    needed_links = {lnk for lnk, _, _ in data.lgg}
                    if "link" in seen:  # ordine inatteso: filtro a posteriori
                        data.links = {k: v for k, v in data.links.items() if k in needed_links}
                seen.add(table)

            if wanted <= seen:
                data.stopped_early = True
                log("  tutte le tabelle necessarie lette: interrompo la lettura dell'archivio")
                break

    missing = wanted - seen
    if missing:
        raise FatalError(
            "Nel dump mancano: " + ", ".join(sorted(missing)) +
            " (una tabella vuota non viene inclusa nel tar)."
        )
    return data


# --------------------------------------------------------------------------- #
# Costruzione della struttura in memoria
# --------------------------------------------------------------------------- #

@dataclass
class GenreGraph:
    nodes: dict[str, dict]                      # mbid -> {name, disambiguation}
    edges: list[tuple[str, str, str]]           # (mbid0, mbid1, type), senza duplicati, ordinati
    label: dict[str, str]                       # mbid -> etichetta univoca usata nel CSV
    raw_edge_count: int
    duplicate_edges: dict[tuple[str, str, str], int]
    homonyms: dict[str, list[str]]              # nome -> [mbid...]
    mbid_fallback_labels: list[str]             # mbid con etichetta "nome [mbid]"
    fusion_parent_ok: bool
    link_type_ids: dict[str, int]               # etichetta tipo -> link_type.id


def _sort_key(s: str) -> tuple[str, str]:
    return (s.casefold(), s)


def build_labels(nodes: dict[str, dict]) -> tuple[dict[str, str], dict[str, list[str]], list[str]]:
    by_name: dict[str, list[str]] = defaultdict(list)
    for mbid, d in nodes.items():
        by_name[d["name"]].append(mbid)
    homonyms = {n: sorted(ids) for n, ids in by_name.items() if len(ids) > 1}

    label: dict[str, str] = {}
    for mbid, d in nodes.items():
        name, dis = d["name"], d["disambiguation"]
        if name not in homonyms:
            label[mbid] = name
        elif dis:
            label[mbid] = f"{name} ({dis})"
        else:
            label[mbid] = f"{name} [{mbid}]"

    counts = Counter(label.values())
    for mbid, d in nodes.items():
        if counts[label[mbid]] > 1 and d["name"] in homonyms:
            label[mbid] = f"{d['name']} [{mbid}]"
    counts = Counter(label.values())
    clashes = [lab for lab, c in counts.items() if c > 1]
    if clashes:
        raise FatalError(f"Etichette non univoche anche dopo la disambiguazione: {clashes[:10]}")
    fallback = sorted(m for m, lab in label.items() if lab.endswith(f"[{m}]"))
    return label, homonyms, fallback


def build_graph(data: DumpData) -> GenreGraph:
    nodes = {gid: {"name": name, "disambiguation": comment}
             for gid, name, comment in data.genres.values()}
    if len(nodes) != len(data.genres):
        raise FatalError("MBID duplicati nella tabella genre.")

    gg_types = {ltid: lt for ltid, lt in data.link_types.items()
                if lt["entity_type0"] == "genre" and lt["entity_type1"] == "genre"}
    unknown = [f"{lt['name']} ({lt['gid']})" for lt in gg_types.values() if lt["gid"] not in REL_TYPES]
    if unknown:
        raise FatalError("Tipi di relazione genre-genre non previsti: " + "; ".join(unknown))
    present = {lt["gid"] for lt in gg_types.values()}
    absent = [REL_TYPES[u] for u in REL_TYPES if u not in present]
    if absent:
        raise FatalError("Tipi di relazione attesi assenti da link_type: " + ", ".join(absent))
    ltid_to_type = {ltid: REL_TYPES[lt["gid"]] for ltid, lt in gg_types.items()}
    link_type_ids = {t: ltid for ltid, t in ltid_to_type.items()}
    fusion_parent_ok = (
        data.link_types[link_type_ids["fusion_of"]]["parent"] == link_type_ids["influenced_by"]
    )

    raw: list[tuple[str, str, str]] = []
    for link_id, e0, e1 in data.lgg:
        if link_id not in data.links:
            raise FatalError(f"l_genre_genre: link {link_id} assente dalla tabella link.")
        t = ltid_to_type.get(data.links[link_id])
        if t is None:
            raise FatalError(f"l_genre_genre: link {link_id} con tipo non genre-genre.")
        if e0 not in data.genres or e1 not in data.genres:
            raise FatalError(f"l_genre_genre: riferimento a genere inesistente ({e0}, {e1}).")
        raw.append((data.genres[e0][0], data.genres[e1][0], t))

    counts = Counter(raw)
    duplicates = {e: c for e, c in counts.items() if c > 1}
    label, homonyms, fallback = build_labels(nodes)
    edges = sorted(counts, key=lambda e: (_sort_key(label[e[0]]), TYPE_ORDER.index(e[2]),
                                          _sort_key(label[e[1]])))
    return GenreGraph(nodes, edges, label, len(raw), duplicates, homonyms, fallback,
                      fusion_parent_ok, link_type_ids)


def check_separator(gg: GenreGraph) -> None:
    bad = sorted({lab for lab in gg.label.values() if SEP in lab}, key=_sort_key)
    if not bad:
        return
    alt = next((s for s in ALT_SEPARATORS if all(s not in lab for lab in gg.label.values())), None)
    msg = f"Il separatore '{SEP}' compare in {len(bad)} etichette di genere, ad es.: {bad[:10]}.\n"
    msg += (f"Separatore alternativo proposto (assente da tutte le etichette): '{alt}'. "
            "Nessun file e' stato scritto: confermare il nuovo separatore e modificare SEP."
            if alt else "Nessuno dei separatori alternativi candidati e' libero.")
    raise FatalError(msg)


# --------------------------------------------------------------------------- #
# Scrittura dei file
# --------------------------------------------------------------------------- #

def write_graphml(gg: GenreGraph, path: Path, version_meta: dict) -> None:
    G = nx.MultiDiGraph()
    G.graph.update(GRAPH_METADATA_STATIC)
    G.graph.update(version_meta)
    for mbid in sorted(gg.nodes, key=lambda m: (_sort_key(gg.label[m]), m)):
        d = gg.nodes[mbid]
        G.add_node(mbid, name=d["name"], disambiguation=d["disambiguation"])
    for i, (u, v, t) in enumerate(gg.edges):
        G.add_edge(u, v, key=f"e{i}", type=t)
    nx.write_graphml(G, path, encoding="utf-8", prettyprint=True, named_key_ids=True)


def write_csv(gg: GenreGraph, path: Path) -> None:
    adj: dict[str, dict[str, list[str]]] = defaultdict(lambda: defaultdict(list))
    for u, v, t in gg.edges:
        adj[u][t].append(gg.label[v])
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, delimiter=",", quotechar='"', doublequote=True,
                       quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
        w.writerow(CSV_HEADER)
        for mbid in sorted(gg.nodes, key=lambda m: (_sort_key(gg.label[m]), m)):
            row = [mbid, gg.label[mbid]]
            for t in TYPE_ORDER:
                row.append(SEP.join(sorted(adj[mbid][t], key=_sort_key)))
            w.writerow(row)


# --------------------------------------------------------------------------- #
# Controlli
# --------------------------------------------------------------------------- #

def edges_from_csv(path: Path) -> tuple[set[str], list[tuple[str, str, str]], list[str]]:
    problems: list[str] = []
    with open(path, encoding="utf-8", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        rows = list(reader)
    if header != CSV_HEADER:
        problems.append(f"intestazione CSV inattesa: {header}")
    label_to_mbid: dict[str, str] = {}
    for row in rows:
        if row[1] in label_to_mbid:
            problems.append(f"etichetta duplicata nel CSV: {row[1]!r}")
        label_to_mbid[row[1]] = row[0]
    edges: list[tuple[str, str, str]] = []
    for row in rows:
        for t in TYPE_ORDER:
            cell = row[CSV_HEADER.index(CSV_COLUMN_OF_TYPE[t])]
            if cell == "":
                continue
            for lab in cell.split(SEP):
                if lab not in label_to_mbid:
                    problems.append(f"etichetta non risolta nel CSV: {lab!r}")
                    continue
                edges.append((row[0], label_to_mbid[lab], t))
    mbids = [row[0] for row in rows]
    if len(set(mbids)) != len(mbids):
        problems.append("genre_mbid duplicati nel CSV")
    return set(mbids), edges, problems


def coherence_check(graphml_path: Path, csv_path: Path) -> dict:
    H = nx.read_graphml(graphml_path, force_multigraph=True)
    gm_edges = [(u, v, d["type"]) for u, v, d in H.edges(data=True)]
    gm_nodes = set(H.nodes)
    csv_nodes, csv_edges, problems = edges_from_csv(csv_path)
    cg, cc = Counter(gm_edges), Counter(csv_edges)
    return {
        "nodes_equal": gm_nodes == csv_nodes,
        "edge_sets_equal": set(gm_edges) == set(csv_edges),
        "edge_multisets_equal": cg == cc,
        "n_graphml_edges": len(gm_edges),
        "n_csv_edges": len(csv_edges),
        "only_graphml": sorted((cg - cc).elements())[:50],
        "only_csv": sorted((cc - cg).elements())[:50],
        "problems": problems[:50],
    }


def analyse(gg: GenreGraph) -> dict:
    S = nx.DiGraph()
    S.add_edges_from((u, v) for u, v, t in gg.edges if t == "subgenre")
    acyclic = nx.is_directed_acyclic_graph(S)
    cycles, truncated, sccs = [], False, []
    if not acyclic:
        sccs = [sorted(c) for c in nx.strongly_connected_components(S)
                if len(c) > 1 or S.has_edge(next(iter(c)), next(iter(c)))]
        for c in itertools.islice(nx.simple_cycles(S), CYCLE_CAP + 1):
            cycles.append(c)
        if len(cycles) > CYCLE_CAP:
            cycles, truncated = cycles[:CYCLE_CAP], True

    degree = Counter()
    for u, v, _ in gg.edges:
        degree[u] += 1
        degree[v] += 1
    isolated = [m for m in gg.nodes if degree[m] == 0]
    multi_parents = {n: sorted(S.predecessors(n)) for n in S if S.in_degree(n) > 1}
    roots = [n for n in S if S.in_degree(n) == 0]
    return {
        "per_type": Counter(t for _, _, t in gg.edges),
        "self_loops": [e for e in gg.edges if e[0] == e[1]],
        "sub_nodes": S.number_of_nodes(),
        "acyclic": acyclic,
        "cycles": cycles,
        "cycles_truncated": truncated,
        "sccs": sccs,
        "isolated": isolated,
        "multi_parents": multi_parents,
        "roots": roots,
        "no_subgenre_edges": len(gg.nodes) - S.number_of_nodes(),
    }


# --------------------------------------------------------------------------- #
# Versione del dump
# --------------------------------------------------------------------------- #

def _parse_pg_timestamp(ts: str) -> datetime | None:
    m = re.match(r"^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?"
                 r"([+-])(\d{2})(?::?(\d{2}))?$", ts.strip())
    if not m:
        return None
    y, mo, d, h, mi, s = (int(m.group(i)) for i in range(1, 7))
    us = int((m.group(7) or "0")[:6].ljust(6, "0"))
    off = timedelta(hours=int(m.group(9)), minutes=int(m.group(10) or 0))
    off = off if m.group(8) == "+" else -off
    return datetime(y, mo, d, h, mi, s, us, tzinfo=timezone(off))


def snapshot_timestamp_delta(snapshot: str | None, ts: str) -> float | None:
    if not snapshot or not SNAPSHOT_RE.fullmatch(snapshot):
        return None
    t = _parse_pg_timestamp(ts)
    if t is None:
        return None
    snap_dt = datetime.strptime(snapshot, "%Y%m%d-%H%M%S").replace(tzinfo=timezone.utc)
    return (t - snap_dt).total_seconds()


# --------------------------------------------------------------------------- #
# Report
# --------------------------------------------------------------------------- #

DECISIONS = [
    "Fonte: solo mbdump.tar.bz2 (tabelle genre, l_genre_genre, link, link_type). L'API /ws/2 non "
    "espone le relazioni tra generi; nodi e archi provengono dallo stesso snapshot.",
    "Tipi di relazione identificati dall'UUID di link_type, non dal nome. Un tipo genre-genre non "
    "previsto, o l'assenza di uno dei tre attesi, interrompe lo script con errore.",
    "Relazioni con entita' non-genere escluse per costruzione: stanno in altre tabelle l_* "
    "(l_area_genre, l_genre_label, l_genre_url, ...), non lette.",
    "Archi identici (stessi entity0, entity1, type) collassati in un solo arco; il numero di "
    "righe collassate e gli eventuali auto-anelli sono riportati.",
    "Id del nodo GraphML = MBID; attributi di nodo: name, disambiguation (stringa vuota se "
    "assente). L'MBID non e' duplicato come attributo.",
    "Id degli archi GraphML = 'e0', 'e1', ... (univoci, in ordine deterministico); attributo type.",
    "Metadati del grafo in italiano; includono semantica e orientamento di ogni tipo, UUID dei "
    "tipi, TIMESTAMP, SCHEMA_SEQUENCE e nome dello snapshot.",
    "Etichette nel CSV: nome del genere; per gli omonimi 'nome (disambiguazione)'; se manca la "
    "disambiguazione o l'etichetta collide, 'nome [mbid]'. Per gli omonimi anche genre_name usa "
    "l'etichetta disambiguata.",
    "Controllo del separatore '|' eseguito sulle etichette effettive (inclusa la disambiguazione); "
    "se compare, lo script si ferma e propone un'alternativa senza scrivere file.",
    "CSV: UTF-8 senza BOM, separatore virgola, quoting RFC 4180 (QUOTE_MINIMAL, virgolette "
    "raddoppiate), fine riga CRLF come da RFC 4180. Righe e valori ordinati per etichetta "
    "(casefold); celle vuote se non ci sono relazioni.",
    "Coerenza: gli archi ricostruiti dal CSV sono confrontati con quelli riletti da genres.graphml "
    "su disco (nx.read_graphml), sia come insieme sia come multinsieme; si confrontano anche i nodi.",
    "Radici del sottografo subgenre: nodi incidenti ad almeno un arco subgenre con grado entrante "
    "subgenre 0. I generi senza archi subgenre sono contati a parte.",
    f"Cicli subgenre: elencati tutti i cicli semplici fino a {CYCLE_CAP:,}; oltre, elenco troncato. "
    "Sono elencate anche le componenti fortemente connesse non banali.",
    "Liste complete nel report (isolati, radici, nodi con piu' padri): possono essere lunghe.",
    f"Formato delle tabelle verificato contando le colonne rispetto allo schema "
    f"v{EXPECTED_SCHEMA_SEQUENCE} (admin/sql/CreateTables.sql); se il conteggio non torna lo "
    "script si ferma. SCHEMA_SEQUENCE diversa ma colonne compatibili: solo avviso.",
    "Nome dello snapshot: da --snapshot, altrimenti dal nome della cartella che contiene il dump "
    "(forma AAAAMMGG-hhmmss). Viene confrontato con TIMESTAMP (solo informativo).",
    "Lettura in streaming del tar (nessuna estrazione su disco); interruzione anticipata appena "
    "lette le tabelle necessarie. Parsing del formato COPY testuale di PostgreSQL (\\N = NULL, "
    "escape con backslash).",
    "Verifica aggiuntiva: che 'fusion of' abbia come padre 'influenced by' in link_type.",
    "Download automatico (opzionale): solo mbdump.tar.bz2, mirror HTTPS data.metabrainz.org, "
    "verifica SHA256 con SHA256SUMS dello snapshot.",
]


def _fmt(gg: GenreGraph, mbid: str) -> str:
    return f"{gg.label[mbid]} (`{mbid}`)"


def write_report(path: Path, gg: GenreGraph, an: dict, coh: dict, ctx: dict) -> None:
    L: list[str] = []
    a = L.append
    a("# Report: grafo dei generi MusicBrainz\n")
    a(f"Generato il {ctx['generated']} da build_genre_graph.py v{SCRIPT_VERSION}.\n")

    a("## Versione del dump\n")
    a(f"- Snapshot (cartella): `{ctx['snapshot'] or 'non determinato'}`")
    a(f"- TIMESTAMP: `{ctx['timestamp']}`")
    a(f"- SCHEMA_SEQUENCE: `{ctx['schema_sequence']}`")
    a(f"- File: `{ctx['dump_path']}`")
    d = ctx["snap_ts_delta"]
    a("- Confronto snapshot/TIMESTAMP: " +
      ("non eseguibile" if d is None else f"differenza {d:+.0f} s"))
    a(f"- Acquisizione: {ctx['acquisition']}\n")

    a("## Conteggi\n")
    a(f"- Nodi (generi): **{len(gg.nodes)}**")
    a(f"- Archi totali: **{len(gg.edges)}**")
    for t in TYPE_ORDER:
        a(f"  - {t}: {an['per_type'][t]}")
    a(f"- Righe l_genre_genre lette: {gg.raw_edge_count}; archi duplicati collassati: "
      f"{gg.raw_edge_count - len(gg.edges)}")
    for (u, v, t), c in sorted(gg.duplicate_edges.items()):
        a(f"  - {_fmt(gg, u)} → {_fmt(gg, v)} [{t}] ×{c}")
    a(f"- Auto-anelli: {len(an['self_loops'])}")
    for u, _, t in an["self_loops"]:
        a(f"  - {_fmt(gg, u)} [{t}]")
    a(f"- 'fusion of' figlio di 'influenced by' in link_type: "
      f"{'sì' if gg.fusion_parent_ok else 'NO'}\n")

    a("## Coerenza GraphML ↔ CSV\n")
    ok = coh["nodes_equal"] and coh["edge_multisets_equal"] and not coh["problems"]
    a(f"- Esito: **{'OK' if ok else 'DIFFERENZE'}**")
    a(f"- Nodi uguali: {coh['nodes_equal']}")
    a(f"- Insiemi di archi uguali: {coh['edge_sets_equal']}")
    a(f"- Multinsiemi di archi uguali: {coh['edge_multisets_equal']} "
      f"(GraphML {coh['n_graphml_edges']}, CSV {coh['n_csv_edges']})")
    for e in coh["only_graphml"]:
        a(f"  - solo GraphML: {e}")
    for e in coh["only_csv"]:
        a(f"  - solo CSV: {e}")
    for p in coh["problems"]:
        a(f"  - problema CSV: {p}")
    a("")

    a("## Aciclicità del sottografo subgenre\n")
    a(f"- Nodi del sottografo subgenre: {an['sub_nodes']}")
    a(f"- Aciclico: **{'sì' if an['acyclic'] else 'NO'}**")
    if not an["acyclic"]:
        a(f"- Componenti fortemente connesse non banali: {len(an['sccs'])}")
        for c in an["sccs"]:
            a("  - " + ", ".join(gg.label[m] for m in sorted(c, key=lambda m: _sort_key(gg.label[m]))))
        a(f"- Cicli semplici: {len(an['cycles'])}" +
          (f" (troncato a {CYCLE_CAP:,})" if an["cycles_truncated"] else ""))
        for cyc in an["cycles"]:
            i = min(range(len(cyc)), key=lambda k: _sort_key(gg.label[cyc[k]]))
            cyc = cyc[i:] + cyc[:i]
            a("  - " + " → ".join(gg.label[m] for m in cyc + [cyc[0]]))
    a("")

    a(f"## Generi senza alcuna relazione ({len(an['isolated'])})\n")
    for m in sorted(an["isolated"], key=lambda m: _sort_key(gg.label[m])):
        a(f"- {_fmt(gg, m)}")
    a("")

    a(f"## Nodi con più padri subgenre ({len(an['multi_parents'])})\n")
    for child in sorted(an["multi_parents"], key=lambda m: _sort_key(gg.label[m])):
        parents = sorted(an["multi_parents"][child], key=lambda m: _sort_key(gg.label[m]))
        a(f"- {_fmt(gg, child)} ← {len(parents)}: " + ", ".join(gg.label[p] for p in parents))
    a("")

    a(f"## Radici del sottografo subgenre ({len(an['roots'])})\n")
    a(f"Generi senza alcun arco subgenre (esclusi dalle radici): {an['no_subgenre_edges']}\n")
    for m in sorted(an["roots"], key=lambda m: _sort_key(gg.label[m])):
        a(f"- {_fmt(gg, m)}")
    a("")

    a(f"## Omonimi ({len(gg.homonyms)} nomi)\n")
    for name in sorted(gg.homonyms, key=_sort_key):
        a(f"- {name}: " + "; ".join(f"`{gg.label[m]}`" for m in gg.homonyms[name]))
    if gg.mbid_fallback_labels:
        a(f"\nEtichette con MBID (disambiguazione assente o in collisione): "
          f"{len(gg.mbid_fallback_labels)}")
    a("")

    a("## Separatore\n")
    a(f"- Separatore usato: `{SEP}`; non compare in nessuna etichetta.\n")

    a("## Avvisi\n")
    if ctx["warnings"]:
        for w in ctx["warnings"]:
            a(f"- {w}")
    else:
        a("- Nessuno.")
    a("")

    a("## Tempi\n")
    a(f"- Lettura dump: {ctx['t_read']:.0f} s; totale: {ctx['t_total']:.0f} s")
    a(f"- Lettura interrotta in anticipo: {'sì' if ctx['stopped_early'] else 'no'}\n")

    a("## Decisioni e assunzioni\n")
    for i, dsc in enumerate(DECISIONS, 1):
        a(f"{i}. {dsc}")
    a("")
    path.write_text("\n".join(L), encoding="utf-8")


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #

def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(
        description="Grafo dei generi MusicBrainz -> genres.graphml, genres.csv, genres_report.md",
        epilog="Istruzioni complete per il download manuale: vedi la docstring in testa al file.",
    )
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--dump", type=Path, help="percorso di mbdump.tar.bz2 gia' scaricato (modalita' principale)")
    src.add_argument("--download", action="store_true", help="scarica l'ultimo mbdump.tar.bz2 (opzione)")
    ap.add_argument("--download-dir", type=Path, default=Path("dumps"),
                    help="cartella per --download (default: ./dumps)")
    ap.add_argument("--base-url", default=DEFAULT_BASE_URL, help=f"default: {DEFAULT_BASE_URL}")
    ap.add_argument("--snapshot", help="nome della cartella dello snapshot, es. 20260923-002121")
    ap.add_argument("--out-dir", type=Path, default=Path("."), help="cartella di output (default: .)")
    args = ap.parse_args(argv)

    t0 = time.time()
    warnings: list[str] = []
    try:
        if args.download:
            scheme = urllib.parse.urlsplit(args.base_url).scheme.lower()
            dump_path, snapshot, url = download_latest(args.base_url, args.download_dir)
            if scheme != "https":
                warnings.append(f"Download eseguito via {scheme.upper()}, non HTTPS.")
            acquisition = f"download automatico via {scheme.upper()} da {url}, SHA256 verificato"
            if args.snapshot and args.snapshot != snapshot:
                warnings.append(f"--snapshot {args.snapshot} ignorato: scaricato {snapshot}.")
        else:
            dump_path = args.dump
            if not dump_path.is_file():
                raise FatalError(f"File non trovato: {dump_path}")
            snapshot = args.snapshot
            if snapshot is None and SNAPSHOT_RE.fullmatch(dump_path.resolve().parent.name):
                snapshot = dump_path.resolve().parent.name
            if snapshot is None:
                warnings.append("Nome dello snapshot non determinato: usare --snapshot.")
            elif not SNAPSHOT_RE.fullmatch(snapshot):
                warnings.append(f"Nome snapshot {snapshot!r} non nella forma AAAAMMGG-hhmmss.")
            acquisition = ("dump locale fornito con --dump (download e verifica SHA256 manuali, "
                           "fuori dallo script)")

        t_read0 = time.time()
        data = read_dump(dump_path)
        t_read = time.time() - t_read0

        ts = data.meta.get("TIMESTAMP", "")
        schema_seq = data.meta.get("SCHEMA_SEQUENCE", "")
        if schema_seq != str(EXPECTED_SCHEMA_SEQUENCE):
            warnings.append(f"SCHEMA_SEQUENCE = {schema_seq}, script scritto per "
                            f"{EXPECTED_SCHEMA_SEQUENCE} (colonne comunque compatibili).")
        delta = snapshot_timestamp_delta(snapshot, ts)
        if delta is not None and abs(delta) > 3600:
            warnings.append(f"TIMESTAMP e nome dello snapshot differiscono di {delta:+.0f} s: "
                            "verificare che il file appartenga allo snapshot indicato.")

        gg = build_graph(data)
        check_separator(gg)

        version_meta = {
            "dump_snapshot": snapshot or "non determinato",
            "dump_timestamp": ts,
            "dump_schema_sequence": schema_seq,
            "generated_by": f"build_genre_graph.py v{SCRIPT_VERSION}",
        }
        args.out_dir.mkdir(parents=True, exist_ok=True)
        graphml_path = args.out_dir / "genres.graphml"
        csv_path = args.out_dir / "genres.csv"
        report_path = args.out_dir / "genres_report.md"

        write_graphml(gg, graphml_path, version_meta)
        write_csv(gg, csv_path)
        log("Scritti genres.graphml e genres.csv; controlli in corso...")
        coh = coherence_check(graphml_path, csv_path)
        an = analyse(gg)

        ctx = {
            "generated": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC"),
            "snapshot": snapshot, "timestamp": ts, "schema_sequence": schema_seq,
            "dump_path": str(dump_path), "snap_ts_delta": delta, "acquisition": acquisition,
            "warnings": warnings, "t_read": t_read, "t_total": time.time() - t0,
            "stopped_early": data.stopped_early,
        }
        write_report(report_path, gg, an, coh, ctx)
    except FatalError as e:
        log(f"ERRORE: {e}")
        return 2

    coh_ok = coh["nodes_equal"] and coh["edge_multisets_equal"] and not coh["problems"]
    print(f"Snapshot {snapshot}  TIMESTAMP {ts}  SCHEMA_SEQUENCE {schema_seq}")
    print(f"Nodi: {len(gg.nodes)}   archi: {len(gg.edges)}  " +
          "  ".join(f"{t}={an['per_type'][t]}" for t in TYPE_ORDER))
    print(f"Coerenza GraphML/CSV: {'OK' if coh_ok else 'DIFFERENZE'}")
    print(f"Subgenre aciclico: {'sì' if an['acyclic'] else 'NO (' + str(len(an['cycles'])) + ' cicli)'}")
    print(f"Isolati: {len(an['isolated'])}   piu' padri: {len(an['multi_parents'])}   "
          f"radici: {len(an['roots'])}   omonimi: {len(gg.homonyms)}")
    for w in warnings:
        print(f"AVVISO: {w}")
    print(f"Report: {report_path}")
    return 0 if coh_ok else 1


if __name__ == "__main__":
    sys.exit(main())
