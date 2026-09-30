#!/usr/bin/env python3
"""Costruisce il grafo dei generi di MusicBrainz a partire da data_raw/.

Uso:
    python build_genre_graph.py [--data-raw data_raw] [--out-dir output]

Legge solo data_raw/{genre,l_genre_genre,link,link_type}.csv e snapshot.json.
Produce output/genres.graphml, output/genres.csv, output/genres_report.md.
Exit code: 0 tutto ok, 1 coerenza fallita (file scritti comunque), 2 errore bloccante.
Richiede networkx.
"""

import argparse
import csv
import itertools
import sys
import time
from collections import Counter, defaultdict
from pathlib import Path

import networkx as nx

from mbraw import read_rows, read_snapshot

TYPE_BY_NAME = {"subgenre": "subgenre", "influenced by": "influenced_by", "fusion of": "fusion_of"}
TYPES = ("subgenre", "influenced_by", "fusion_of")
SEMANTICS = {
    "subgenre": "dal genere padre al sottogenere (padre -> figlio)",
    "influenced_by": "dal genere derivato al genere che lo ha influenzato (derivato -> fonte)",
    "fusion_of": "dal genere derivato al genere di cui e' fusione (derivato -> fonte)",
}
SEP = "|"


class Errore(Exception):
    pass


def build_labels(genres):
    """genres: gid -> (name, comment). Restituisce gid -> etichetta univoca."""
    by_name = defaultdict(list)
    for gid, (name, _) in genres.items():
        by_name[name].append(gid)
    labels = {}
    for name, gids in by_name.items():
        for gid in gids:
            comment = genres[gid][1]
            if len(gids) == 1:
                labels[gid] = name
            elif comment:
                labels[gid] = f"{name} ({comment})"
            else:
                labels[gid] = f"{name} [{gid}]"
    for _ in range(2):
        count = Counter(labels.values())
        clash = [g for g, lab in labels.items() if count[lab] > 1]
        if not clash:
            break
        for g in clash:
            labels[g] = f"{genres[g][0]} [{g}]"
    if len(set(labels.values())) != len(labels):
        raise Errore("impossibile rendere univoche le etichette dei generi")
    return labels


def coherence(graphml_path, csv_path, labels, snap):
    """Confronta nodi e archi riletti da graphml e da csv su disco. Restituisce i problemi."""
    problems = []
    h = nx.read_graphml(graphml_path)
    h_nodes = {n: d.get("name") for n, d in h.nodes(data=True)}
    h_edges = {(u, v, d.get("type")) for u, v, d in h.edges(data=True)}
    if h.number_of_edges() != len(h_edges):
        problems.append("graphml contiene archi duplicati (stessa terna sorgente, destinazione, tipo)")
    if str(h.graph.get("dump_snapshot")) != str(snap.get("snapshot")):
        problems.append("dump_snapshot nei metadati del graphml non coincide con snapshot.json")
    label_to_gid = {lab: g for g, lab in labels.items()}
    c_nodes, c_edges = {}, set()
    with open(csv_path, "r", encoding="utf-8", newline="") as f:
        for row in csv.DictReader(f):
            gid = row["genre_mbid"]
            c_nodes[gid] = row["genre_name"]
            for col, typ in (("rel_sub", "subgenre"), ("rel_inf", "influenced_by"), ("rel_fus", "fusion_of")):
                if not row[col]:
                    continue
                for lab in row[col].split(SEP):
                    other = label_to_gid.get(lab)
                    if other is None:
                        problems.append(f"genres.csv: etichetta sconosciuta {lab!r} in {col} di {gid}")
                    else:
                        c_edges.add((gid, other, typ))
    if set(h_nodes) != set(c_nodes):
        problems.append(f"insiemi di nodi diversi: graphml {len(h_nodes)}, csv {len(c_nodes)}")
    else:
        for g, name in c_nodes.items():
            if h_nodes[g] != name:
                problems.append(f"nome diverso per {g}: graphml {h_nodes[g]!r}, csv {name!r}")
    for t in sorted(h_edges - c_edges, key=str)[:20]:
        problems.append(f"arco solo nel graphml: {t}")
    for t in sorted(c_edges - h_edges, key=str)[:20]:
        problems.append(f"arco solo nel csv: {t}")
    return problems, len(h_edges), len(c_edges)


def run(args):
    t0 = time.perf_counter()
    raw = Path(args.data_raw)
    out = Path(args.out_dir)
    snap = read_snapshot(raw)

    genres = {}          # gid -> (name, comment)
    gid_of_id = {}       # genre.id -> gid
    for r in read_rows(raw / "genre.csv", required=("id", "gid", "name", "comment")):
        if r["id"] is None or r["gid"] is None or r["name"] is None:
            raise Errore("genre.csv: id, gid o name mancanti")
        if r["gid"] in genres or r["id"] in gid_of_id:
            raise Errore(f"genre.csv: genere duplicato (id {r['id']}, gid {r['gid']})")
        genres[r["gid"]] = (r["name"], r["comment"] or "")
        gid_of_id[r["id"]] = r["gid"]
    if not genres:
        raise Errore("genre.csv e' vuoto")

    lt = {}
    for r in read_rows(raw / "link_type.csv", required=("id", "parent", "name", "entity_type0", "entity_type1")):
        if r["entity_type0"] == "genre" and r["entity_type1"] == "genre":
            lt[r["id"]] = r
    unexpected = sorted({r["name"] for r in lt.values()} - set(TYPE_BY_NAME))
    if unexpected:
        raise Errore(f"link_type genre-genre inattesi: {unexpected}")
    by_name = {r["name"]: r for r in lt.values()}
    if "fusion of" in by_name:
        inf = by_name.get("influenced by")
        if inf is None or by_name["fusion of"]["parent"] != inf["id"]:
            raise Errore("'fusion of' non risulta figlio di 'influenced by' in link_type")
    t_read_types = time.perf_counter()

    link_type = {}
    for r in read_rows(raw / "link.csv", required=("id", "link_type")):
        if r["link_type"] in lt:
            link_type[r["id"]] = r["link_type"]

    edges_set = set()
    duplicates = Counter()
    for r in read_rows(raw / "l_genre_genre.csv", required=("link", "entity0", "entity1")):
        lk = link_type.get(r["link"])
        if lk is None:
            raise Errore(f"l_genre_genre: link {r['link']} assente o non di tipo genre-genre")
        e0, e1 = gid_of_id.get(r["entity0"]), gid_of_id.get(r["entity1"])
        if e0 is None or e1 is None:
            raise Errore(f"l_genre_genre: generi inesistenti ({r['entity0']}, {r['entity1']})")
        typ = TYPE_BY_NAME[lt[lk]["name"]]
        key = (e0, e1, typ)
        if key in edges_set:
            duplicates[typ] += 1
        edges_set.add(key)
    edges = sorted(edges_set, key=lambda e: (TYPES.index(e[2]), e[0], e[1]))
    t_read = time.perf_counter()

    labels = build_labels(genres)
    for gid, lab in labels.items():
        if SEP in lab or SEP in genres[gid][0]:
            raise Errore(f"il separatore {SEP!r} compare in {lab!r}: usare un altro separatore (es. ';')")

    G = nx.MultiDiGraph()
    G.graph.update({
        "semantics_subgenre": SEMANTICS["subgenre"],
        "semantics_influenced_by": SEMANTICS["influenced_by"],
        "semantics_fusion_of": SEMANTICS["fusion_of"],
        "dump_snapshot": snap["snapshot"],
        "dump_timestamp": snap["timestamp"],
        "dump_schema_sequence": snap["schema_sequence"],
    })
    for gid in sorted(genres, key=lambda g: (genres[g][0], g)):
        G.add_node(gid, name=genres[gid][0], disambiguation=genres[gid][1])
    for i, (e0, e1, typ) in enumerate(edges):
        G.add_edge(e0, e1, key=f"e{i}", type=typ)

    out.mkdir(parents=True, exist_ok=True)
    graphml_path, csv_path, report_path = out / "genres.graphml", out / "genres.csv", out / "genres_report.md"
    nx.write_graphml(G, graphml_path, named_key_ids=True)

    rel = {t: defaultdict(list) for t in TYPES}
    for e0, e1, typ in edges:
        rel[typ][e0].append(labels[e1])
    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, quoting=csv.QUOTE_MINIMAL, lineterminator="\r\n")
        w.writerow(["genre_mbid", "genre_name", "rel_sub", "rel_inf", "rel_fus"])
        for gid in sorted(genres, key=lambda g: (genres[g][0], g)):
            w.writerow([gid, genres[gid][0]] + [SEP.join(sorted(rel[t][gid])) for t in TYPES])
    t_write = time.perf_counter()

    problems, n_h, n_c = coherence(graphml_path, csv_path, labels, snap)

    # ---- controlli per il report
    L = labels
    n_by_type = Counter(t for _, _, t in edges)
    selfloops = [(e0, t) for e0, e1, t in edges if e0 == e1]
    sub = nx.DiGraph()
    sub.add_nodes_from(genres)
    sub.add_edges_from((a, b) for a, b, t in edges if t == "subgenre")
    acyclic = nx.is_directed_acyclic_graph(sub)
    cycles = [] if acyclic else list(itertools.islice(nx.simple_cycles(sub), 50))
    isolated = sorted((L[n] for n in G.nodes if G.degree(n) == 0))
    multi = sorted((L[n], sorted(L[p] for p in sub.predecessors(n))) for n in sub if sub.in_degree(n) > 1)
    roots = sorted(L[n] for n in sub if sub.in_degree(n) == 0 and sub.out_degree(n) > 0)

    lines = [
        "# Grafo dei generi MusicBrainz", "",
        f"- snapshot: {snap['snapshot']} (timestamp {snap['timestamp']}, schema_sequence {snap['schema_sequence']})",
        f"- tempi: lettura tipi {t_read_types - t0:.1f} s, lettura link e archi {t_read - t_read_types:.1f} s, "
        f"scrittura {t_write - t_read:.1f} s, totale {time.perf_counter() - t0:.1f} s", "",
        "## Numeri", "",
        f"- nodi: {G.number_of_nodes()}",
        f"- archi (dopo il collasso dei duplicati): {len(edges)}",
    ]
    for t in TYPES:
        lines.append(f"  - {t}: {n_by_type[t]} (duplicati collassati: {duplicates[t]})")
    lines += [f"- auto-anelli: {len(selfloops)}"]
    lines += [f"  - {L[g]} ({t})" for g, t in selfloops]
    lines += ["", "## Controlli", "",
              f"- sottografo subgenre aciclico: {'si' if acyclic else 'NO'}"]
    for c in cycles:
        lines.append("  - ciclo: " + " -> ".join(L[x] for x in c + [c[0]]))
    lines += [f"- generi senza alcuna relazione: {len(isolated)}"]
    lines += [f"  - {x}" for x in isolated]
    lines += [f"- nodi con piu' padri subgenre: {len(multi)}"]
    lines += [f"  - {n}: {', '.join(ps)}" for n, ps in multi]
    lines += [f"- radici (archi subgenre uscenti, nessun padre): {len(roots)}"]
    lines += [f"  - {x}" for x in roots]
    lines += ["", "## Coerenza graphml / csv", "",
              f"- archi nel graphml riletto: {n_h}; archi ricostruiti dal csv: {n_c}",
              f"- esito: {'OK' if not problems else 'FALLITA'}"]
    lines += [f"  - {p}" for p in problems]
    lines += [
        "", "## Decisioni e assunzioni", "",
        "- Gli archi identici (entity0, entity1, tipo) sono collassati; gli auto-anelli sono mantenuti e segnalati.",
        "- Chiavi degli archi nel graphml: e0, e1, ... in ordine (tipo, sorgente, destinazione), per avere id XML univoci.",
        "- Etichette: nome; omonimi 'nome (disambiguazione)'; altrimenti 'nome [mbid]'. Le celle di genres.csv usano le etichette.",
        "- Ordinamenti per code point Unicode (ordinamento di default di Python).",
        "- Cicli subgenre, generi isolati e nodi con piu' padri sono solo segnalati: non cambiano l'exit code.",
        f"- Separatore delle celle multiple: {SEP!r}.",
    ]
    report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    if problems:
        print("ATTENZIONE: coerenza fallita, vedi " + str(report_path), file=sys.stderr)
        return 1
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(description="Costruisce il grafo dei generi MusicBrainz.")
    p.add_argument("--data-raw", default="data_raw")
    p.add_argument("--out-dir", default="output")
    args = p.parse_args(argv)
    try:
        return run(args)
    except (Errore, OSError, ValueError, KeyError) as e:
        print(f"ERRORE: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
