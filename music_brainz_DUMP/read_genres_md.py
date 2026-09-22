import pandas as pd
from pathlib import Path
import networkx as nx

import plotly.graph_objects as go

genre = pd.read_csv(Path(r"C:\Users\lvitt\OneDrive\Documenti\GiuHub Local Repository\Cityfy\music_brainz_DUMP\mbdump_use\genre"), sep="\t", header=None, na_values="\\N",
    names=["id","gid","name","comment","edits_pending","last_updated"])

link = pd.read_csv(Path(r"C:\Users\lvitt\OneDrive\Documenti\GiuHub Local Repository\Cityfy\music_brainz_DUMP\mbdump_use\link"), sep="\t", header=None, na_values="\\N",
    names=["id","link_type","by","bm","bd","ey","em","ed","attr_count","created","ended"])

link_type = pd.read_csv(Path(r"C:\Users\lvitt\OneDrive\Documenti\GiuHub Local Repository\Cityfy\music_brainz_DUMP\mbdump_use\link_type"), sep="\t", header=None, na_values="\\N",
    names=["id","parent","child_order","gid","entity_type0","entity_type1","name",
           "description","link_phrase","reverse_link_phrase","long_link_phrase",
           "last_updated","is_deprecated","has_dates","c0","c1"])

lgg = pd.read_csv(Path(r"C:\Users\lvitt\OneDrive\Documenti\GiuHub Local Repository\Cityfy\music_brainz_DUMP\mbdump_use\l_genre_genre"), sep="\t", header=None, na_values="\\N",
    names=["id","link","entity0","entity1","edits_pending","last_updated",
           "link_order","entity0_credit","entity1_credit"])


edges = (
    lgg.merge(link, left_on="link", right_on="id", suffixes=("", "_l"))
       .merge(link_type, left_on="link_type", right_on="id", suffixes=("", "_lt"))
       .merge(genre[["id", "name"]].rename(columns={"name": "source"}),
              left_on="entity0", right_on="id")
       .merge(genre[["id", "name"]].rename(columns={"name": "target"}),
              left_on="entity1", right_on="id")
)

edges = edges[["source", "target", "name"]].rename(columns={"name": "relation_type"})

G = nx.from_pandas_edgelist(
    edges, "source", "target", edge_attr="relation_type",
    create_using=nx.DiGraph
)

print(f"Nodi: {G.number_of_nodes()}, Archi: {G.number_of_edges()}")
print(edges.head(10))
print(edges["relation_type"].value_counts())

csv_path = Path(__file__).parent / "genre_graph.csv"
edges.rename(columns={"source": "genere", "target": "genere_collegato", "relation_type": "relazione"}).to_csv(csv_path, index=False)
print(f"Grafo dei generi salvato in: {csv_path}")

graphml_path = Path(__file__).parent / "genre_graph.graphml"
nx.write_graphml(G, graphml_path)
print(f"Grafo esportato in GraphML: {graphml_path}")




import math
from collections import deque

roots = [n for n in G.nodes() if G.in_degree(n) == 0]

depth = {r: 0 for r in roots}
children_map = {n: [] for n in G.nodes()}
visited = set(roots)
queue = deque(roots)
while queue:
    u = queue.popleft()
    for v in G.successors(u):
        if v not in visited:
            visited.add(v)
            depth[v] = depth[u] + 1
            children_map[u].append(v)
            queue.append(v)

# nodi non raggiunti da nessuna radice (es. cicli isolati): trattati come radici a sé
for n in G.nodes():
    if n not in depth:
        depth[n] = 0
        roots.append(n)

def count_leaves(n):
    kids = children_map.get(n, [])
    if not kids:
        return 1
    return sum(count_leaves(c) for c in kids)

leaves = {n: count_leaves(n) for n in G.nodes()}

angle = {}
stack = []
n_roots = len(roots)
slice_ = 2 * math.pi / n_roots
for i, r in enumerate(roots):
    stack.append((r, i * slice_, (i + 1) * slice_))

while stack:
    node, a0, a1 = stack.pop()
    angle[node] = (a0 + a1) / 2
    kids = children_map.get(node, [])
    if not kids:
        continue
    total = sum(leaves[c] for c in kids)
    start = a0
    for c in kids:
        span = (a1 - a0) * leaves[c] / total
        stack.append((c, start, start + span))
        start += span

pos = {}
for n in G.nodes():
    radius = depth[n] + 1
    pos[n] = (radius * math.cos(angle[n]), radius * math.sin(angle[n]), depth[n])

edge_x, edge_y, edge_z = [], [], []
for u, v in G.edges():
    x0, y0, z0 = pos[u]
    x1, y1, z1 = pos[v]
    edge_x += [x0, x1, None]
    edge_y += [y0, y1, None]
    edge_z += [z0, z1, None]

edge_trace = go.Scatter3d(
    x=edge_x, y=edge_y, z=edge_z,
    mode="lines",
    line=dict(color="lightgray", width=1),
    hoverinfo="none",
)

node_x = [pos[n][0] for n in G.nodes()]
node_y = [pos[n][1] for n in G.nodes()]
node_z = [pos[n][2] for n in G.nodes()]

node_trace = go.Scatter3d(
    x=node_x, y=node_y, z=node_z,
    mode="markers",
    text=list(G.nodes()),
    hoverinfo="text",
    marker=dict(
        size=4,
        color="#1f77b4",
    ),
)

# trace vuoto e separato, usato solo per disegnare sopra i nodi evidenziati:
# il trace principale (node_trace) non viene mai piu' modificato dopo la creazione
highlight_trace = go.Scatter3d(
    x=[], y=[], z=[],
    mode="markers",
    text=[],
    hoverinfo="text",
    marker=dict(size=[], color=[]),
)

fig = go.Figure(data=[edge_trace, node_trace, highlight_trace])
fig.update_layout(
    title="Grafo generi musicali (MusicBrainz) in 3D",
    showlegend=False,
    scene=dict(
        xaxis=dict(visible=False),
        yaxis=dict(visible=False),
        zaxis=dict(visible=False),
        uirevision="keep",
    ),
    uirevision="keep",
    margin=dict(l=0, r=0, b=0, t=40),
)

output_path = Path(__file__).parent / "genre_graph_3d.html"
fig.write_html(output_path)

import json

node_list = list(G.nodes())
node_index = {n: i for i, n in enumerate(node_list)}
undirected = G.to_undirected()
adjacency = {
    node_index[n]: [node_index[nb] for nb in undirected.neighbors(n)]
    for n in node_list
}

controls_html = """
<div id="graph-controls" style="position:fixed;top:10px;left:10px;z-index:1000;background:white;padding:8px;border:1px solid #ccc;border-radius:6px;font-family:sans-serif;">
  <input id="genre-search" type="text" placeholder="Cerca genere..." style="padding:4px;width:200px;">
  <button id="genre-search-btn" style="padding:4px 8px;">Cerca</button>
  <button id="genre-reset-btn" style="padding:4px 8px;">Reset</button>
  <div id="genre-search-msg" style="font-size:12px;color:#900;margin-top:4px;"></div>
</div>
"""

script = f"""
<script>
window.addEventListener('load', function() {{
  var gd = document.getElementsByClassName('plotly-graph-div')[0];
  var nodeNames = {json.dumps(node_list)};
  var nodeX = {json.dumps(node_x)};
  var nodeY = {json.dumps(node_y)};
  var nodeZ = {json.dumps(node_z)};
  var adjacency = {json.dumps(adjacency)};
  var baseTraceIndex = 1;
  var highlightTraceIndex = 2;

  function resetHighlight() {{
    Plotly.restyle(gd, {{
      x: [[]], y: [[]], z: [[]], text: [[]],
      'marker.color': [[]], 'marker.size': [[]],
    }}, [highlightTraceIndex]);
    document.getElementById('genre-search-msg').textContent = '';
  }}

  function highlightNode(idx) {{
    if (idx === -1) {{
      document.getElementById('genre-search-msg').textContent = 'Genere non trovato';
      return;
    }}
    var neigh = adjacency[idx] || [];
    var ids = [idx].concat(neigh);
    var x = ids.map(function(i) {{ return nodeX[i]; }});
    var y = ids.map(function(i) {{ return nodeY[i]; }});
    var z = ids.map(function(i) {{ return nodeZ[i]; }});
    var text = ids.map(function(i) {{ return nodeNames[i]; }});
    var colors = ids.map(function(i) {{ return i === idx ? 'red' : 'orange'; }});
    var sizes = ids.map(function(i) {{ return i === idx ? 12 : 8; }});

    Plotly.restyle(gd, {{
      x: [x], y: [y], z: [z], text: [text],
      'marker.color': [colors], 'marker.size': [sizes],
    }}, [highlightTraceIndex]);

    document.getElementById('genre-search-msg').textContent =
      'Trovato: ' + nodeNames[idx] + ' (' + neigh.length + ' collegamenti)';
  }}

  gd.on('plotly_click', function(data) {{
    var pt = data.points.find(function(p) {{ return p.curveNumber === baseTraceIndex; }});
    if (pt) {{ highlightNode(pt.pointIndex); }}
  }});

  document.getElementById('genre-search-btn').addEventListener('click', function() {{
    var q = document.getElementById('genre-search').value.trim().toLowerCase();
    var idx = nodeNames.findIndex(function(n) {{ return n.toLowerCase() === q; }});
    if (idx === -1) {{
      idx = nodeNames.findIndex(function(n) {{ return n.toLowerCase().includes(q); }});
    }}
    highlightNode(idx);
  }});

  document.getElementById('genre-search').addEventListener('keydown', function(e) {{
    if (e.key === 'Enter') {{ document.getElementById('genre-search-btn').click(); }}
  }});

  document.getElementById('genre-reset-btn').addEventListener('click', resetHighlight);
}});
</script>
"""

html = output_path.read_text(encoding="utf-8")
html = html.replace("<body>", "<body>" + controls_html, 1)
html = html.replace("</body>", script + "</body>", 1)
output_path.write_text(html, encoding="utf-8")

print(f"Grafo 3D interattivo salvato in: {output_path}")