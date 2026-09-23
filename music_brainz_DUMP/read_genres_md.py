import pandas as pd
from pathlib import Path
import networkx as nx

#import plotly.graph_objects as go

DUMP_DIR = Path(__file__).parent / "mbdump_22_09_2026"

genre = pd.read_csv(DUMP_DIR / "genre", sep="\t", header=None, na_values="\\N",
    names=["id","gid","name","comment","edits_pending","last_updated"])

link = pd.read_csv(DUMP_DIR / "link", sep="\t", header=None, na_values="\\N",
    names=["id","link_type","by","bm","bd","ey","em","ed","attr_count","created","ended"])

link_type = pd.read_csv(DUMP_DIR / "link_type", sep="\t", header=None, na_values="\\N",
    names=["id","parent","child_order","gid","entity_type0","entity_type1","name",
           "description","link_phrase","reverse_link_phrase","long_link_phrase",
           "last_updated","is_deprecated","has_dates","c0","c1"])

lgg = pd.read_csv(DUMP_DIR / "l_genre_genre", sep="\t", header=None, na_values="\\N",
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
# aggiunge anche i generi senza alcuna relazione (nodi isolati del grafo)
G.add_nodes_from(genre["name"])

print(f"Nodi: {G.number_of_nodes()}, Archi: {G.number_of_edges()}")
print(edges.head(10))
print(edges["relation_type"].value_counts())

def create_edge_csv(csv_name: str):
    full_name = csv_name + ".csv"
    csv_path = Path(__file__).parent / full_name
    edges.rename(columns={"source": "X_genere", "target": "Y_genere", "relation_type": "relazione"}).to_csv(csv_path, index=False)
    print(f"Grafo dei generi salvato in: {csv_path}")
    
    
def create_nodes_csv(csv_name: str):
    full_name = csv_name + ".csv"
    csv_path = Path(__file__).parent / full_name
    

def create_graphml(graph_name):
    full_name = graph_name + ".graphml"
    graphml_path = Path(__file__).parent / full_name
    nx.write_graphml(G, graphml_path)
    print(f"Grafo esportato in GraphML: {graphml_path}")
    

create_edge_csv("genre_graph_2")
