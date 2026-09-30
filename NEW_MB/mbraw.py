"""
mbraw.py -- convenzione CSV di data_raw/ e lettura di data_raw/snapshot.json.

Modulo condiviso da fetch_extract.py (scrittura) e dagli script di elaborazione
(lettura). Contiene SOLO:
  - la convenzione CSV (scrittura e lettura con csv.QUOTE_NOTNULL);
  - la lettura di snapshot.json.
Il parser del formato COPY di PostgreSQL sta in fetch_extract.py.

CONVENZIONE CSV (uguale a PostgreSQL  COPY ... (FORMAT csv, HEADER, FORCE_QUOTE *))
  - UTF-8 senza BOM, separatore virgola, fine riga CRLF, quoting RFC 4180
    (virgolette raddoppiate);
  - prima riga: nomi delle colonne;
  - NULL          -> campo vuoto SENZA virgolette     ...,,...
  - stringa vuota -> campo vuoto TRA virgolette        ...,"",...
  - ogni altro valore e' sempre tra virgolette.
  In Python e' esattamente csv.QUOTE_NOTNULL (Python >= 3.12): in scrittura None
  diventa un campo vuoto non quotato, in lettura un campo vuoto non quotato
  diventa None.
"""

from __future__ import annotations

import csv
import json
import sys
from pathlib import Path
from typing import Iterator

if sys.version_info < (3, 12):
    raise SystemExit("Serve Python >= 3.12 (csv.QUOTE_NOTNULL in lettura).")

SNAPSHOT_FILE = "snapshot.json"
SNAPSHOT_FORMAT_VERSION = 1

CSV_ENCODING = "utf-8"
CSV_FORMAT = {"quoting": csv.QUOTE_NOTNULL, "lineterminator": "\r\n"}

# Alcune tabelle (es. annotation) hanno testi oltre il limite di default (128 KiB).
csv.field_size_limit(2**31 - 1)


class RawDataError(RuntimeError):
    """data_raw/ mancante, incompleta o incoerente con snapshot.json."""


# --------------------------------------------------------------------------- #
# Scrittura
# --------------------------------------------------------------------------- #

def open_csv_writer(path: Path):
    """Restituisce (file, writer) per un CSV di data_raw/. Il chiamante chiude il file."""
    fh = open(path, "w", encoding=CSV_ENCODING, newline="")
    return fh, csv.writer(fh, **CSV_FORMAT)


# --------------------------------------------------------------------------- #
# Lettura
# --------------------------------------------------------------------------- #

def load_snapshot(data_raw: Path) -> dict:
    """Legge data_raw/snapshot.json e ne controlla la versione di formato."""
    path = Path(data_raw) / SNAPSHOT_FILE
    if not path.is_file():
        raise RawDataError(f"{path} non trovato: eseguire prima fetch_extract.py.")
    with open(path, encoding="utf-8") as fh:
        snap = json.load(fh)
    if snap.get("format_version") != SNAPSHOT_FORMAT_VERSION:
        raise RawDataError(f"{path}: format_version {snap.get('format_version')!r}, "
                           f"atteso {SNAPSHOT_FORMAT_VERSION}.")
    return snap


def read_rows(data_raw: Path, snap: dict, table: str,
              columns: list[str]) -> Iterator[tuple[str | None, ...]]:
    """Itera le righe di data_raw/<table>.csv restituendo solo `columns`, nell'ordine dato.

    NULL -> None. Controlla che la tabella sia in snapshot.json, che l'intestazione
    coincida con le colonne registrate e, a fine lettura, il numero di righe.
    """
    info = snap.get("tables", {}).get(table)
    if info is None:
        raise RawDataError(f"Tabella {table} non estratta: attivarla in TABLES di "
                           "fetch_extract.py e rieseguire l'estrazione.")
    missing = [c for c in columns if c not in info["columns"]]
    if missing:
        raise RawDataError(f"{table}: colonne {missing} assenti dallo schema "
                           f"(colonne: {info['columns']}).")
    path = Path(data_raw) / info["file"]
    with open(path, encoding=CSV_ENCODING, newline="") as fh:
        reader = csv.reader(fh, quoting=csv.QUOTE_NOTNULL)
        header = next(reader, None)
        if header != info["columns"]:
            raise RawDataError(f"{path}: intestazione {header} diversa da snapshot.json.")
        idx = [header.index(c) for c in columns]
        n = 0
        for row in reader:
            n += 1
            yield tuple(row[i] for i in idx)
    if n != info["rows"]:
        raise RawDataError(f"{path}: {n} righe lette, snapshot.json ne indica {info['rows']}.")
