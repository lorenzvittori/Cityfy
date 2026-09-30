"""Lettura e scrittura dei CSV di data_raw/ e di snapshot.json.

Convenzione CSV: UTF-8 senza BOM, RFC 4180, fine riga CRLF, intestazione con i
nomi delle colonne, csv.QUOTE_NOTNULL in scrittura e lettura:
  - NULL            -> campo vuoto senza virgolette
  - stringa vuota   -> ""
  - ogni altro dato -> tra virgolette
Limite noto: una riga con un solo campo NULL non e' scrivibile (errore esplicito).

Uso (da altri script):
    from mbraw import write_table, read_rows, read_snapshot
    write_table("x.csv", ["a", "b"], [["1", None], ["2", ""]])
    for riga in read_rows("x.csv"):   # dict nome colonna -> valore
        ...
    snap = read_snapshot("data_raw")
"""

import csv
import json
import sys
from pathlib import Path

csv.field_size_limit(min(sys.maxsize, 2**31 - 1))


def write_table(path, columns, rows):
    """Scrive `rows` (iterabile di sequenze) in `path` con intestazione `columns`."""
    columns = list(columns)
    n = len(columns)
    if n == 0:
        raise ValueError(f"{path}: nessuna colonna")
    with open(path, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f, quoting=csv.QUOTE_NOTNULL, lineterminator="\r\n")
        w.writerow(columns)
        for i, row in enumerate(rows, 1):
            if len(row) != n:
                raise ValueError(f"{path}: riga {i}: attesi {n} campi, trovati {len(row)}")
            if n == 1 and row[0] is None:
                raise ValueError(
                    f"{path}: riga {i}: una riga con un solo campo NULL non e' scrivibile in CSV"
                )
            w.writerow(row)


def read_rows(path, required=()):
    """Generatore di dict (nome colonna -> str | None); controlla l'intestazione.

    `required`: nomi di colonna che devono essere presenti.
    """
    with open(path, "r", encoding="utf-8", newline="") as f:
        reader = csv.reader(f, quoting=csv.QUOTE_NOTNULL)
        try:
            header = next(reader)
        except StopIteration:
            raise ValueError(f"{path}: file vuoto (manca l'intestazione)") from None
        if any(h is None or h == "" for h in header):
            raise ValueError(f"{path}: intestazione non valida")
        if header[0].startswith("﻿"):
            raise ValueError(f"{path}: presente un BOM, atteso UTF-8 senza BOM")
        if len(set(header)) != len(header):
            raise ValueError(f"{path}: nomi di colonna duplicati nell'intestazione")
        missing = [c for c in required if c not in header]
        if missing:
            raise ValueError(f"{path}: colonne mancanti: {', '.join(missing)}")
        n = len(header)
        for row in reader:
            if len(row) != n:
                raise ValueError(
                    f"{path}: riga {reader.line_num}: attesi {n} campi, trovati {len(row)}"
                )
            yield dict(zip(header, row))


def read_snapshot(directory):
    """Legge <directory>/snapshot.json."""
    with open(Path(directory) / "snapshot.json", "r", encoding="utf-8") as f:
        return json.load(f)
