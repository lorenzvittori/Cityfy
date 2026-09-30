#!/usr/bin/env python3
"""
test_roundtrip.py -- test di andata e ritorno COPY -> CSV -> COPY sui dati reali.

Per ogni tabella di data_raw/snapshot.json:
  1. rilegge <tabella>.csv con la convenzione di mbraw (csv.QUOTE_NOTNULL);
  2. riconverte ogni riga nel formato COPY testuale canonico con
     fetch_extract.encode_copy_line;
  3. confronta SHA256, numero di byte e numero di righe con i valori registrati da
     fetch_extract.py mentre leggeva i byte COPY originali del dump.
Se coincidono, la conversione e' reversibile byte per byte, compresa la
distinzione tra NULL e stringa vuota. Il test conta anche NULL e stringhe vuote
per tabella, per mostrare che entrambi i casi sono presenti nei dati.

Prima dei dati reali esegue un controllo sintetico sui casi limite del parser.

USO
  python tests/test_roundtrip.py [--data-raw PERCORSO]      (default: ../data_raw)
Esce con codice 0 se tutto coincide, 1 altrimenti.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import mbraw  # noqa: E402
from fetch_extract import decode_copy_line, encode_copy_line  # noqa: E402

SYNTHETIC = [
    [None, "", "\\N", "a\tb", "riga1\nriga2", "cr\r", "back\\slash", "\x08\x0c\x0b", "città ♪"],
    ["", None],
    [None, None],
    ['virgolette "doppie"', "a,b", " spazi "],
]


def synthetic_check() -> list[str]:
    errors = []
    for fields in SYNTHETIC:
        line = encode_copy_line(fields)
        back = decode_copy_line(line[:-1])
        if back != fields:
            errors.append(f"COPY: {fields!r} -> {line!r} -> {back!r}")
    # sequenze che COPY TO non emette ma che il parser deve comunque accettare
    for raw, expected in ((b"\\101\\x42\\q", "ABq"), (b"\\303\\251", "é")):
        got = decode_copy_line(raw)
        if got != [expected]:
            errors.append(f"COPY: {raw!r} -> {got!r}, atteso {[expected]!r}")
    return errors


def check_table(data_raw: Path, table: str, info: dict) -> tuple[bool, str]:
    h = hashlib.sha256()
    nbytes = rows = nulls = empties = 0
    with open(data_raw / info["file"], encoding=mbraw.CSV_ENCODING, newline="") as fh:
        reader = csv.reader(fh, quoting=csv.QUOTE_NOTNULL)
        header = next(reader)
        for row in reader:
            b = encode_copy_line(row)
            h.update(b)
            nbytes += len(b)
            rows += 1
            for v in row:
                if v is None:
                    nulls += 1
                elif v == "":
                    empties += 1
    ok = (header == info["columns"] and rows == info["rows"]
          and nbytes == info["copy_bytes"] and h.hexdigest() == info["copy_sha256"])
    detail = (f"righe {rows:,}/{info['rows']:,}, byte COPY {nbytes:,}/{info['copy_bytes']:,}, "
              f"sha256 {'uguale' if h.hexdigest() == info['copy_sha256'] else 'DIVERSO'}; "
              f"NULL {nulls:,}, stringhe vuote {empties:,}")
    return ok, detail


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description="Andata e ritorno COPY -> CSV -> COPY su data_raw/.")
    ap.add_argument("--data-raw", type=Path, default=HERE.parent / "data_raw")
    args = ap.parse_args(argv)

    errors = synthetic_check()
    print(f"Casi sintetici: {'OK' if not errors else 'ERRORI'}")
    for e in errors:
        print(f"  {e}")

    snap = mbraw.load_snapshot(args.data_raw)
    print(f"data_raw: {args.data_raw}  snapshot {snap['snapshot']}  TIMESTAMP {snap['TIMESTAMP']}")
    all_ok = not errors
    for table, info in snap["tables"].items():
        t0 = time.time()
        ok, detail = check_table(args.data_raw, table, info)
        all_ok &= ok
        print(f"  {'OK ' if ok else 'KO '} {table:<22} {detail}  ({time.time() - t0:.0f} s)", flush=True)
    print(f"Esito: {'OK, conversione reversibile byte per byte' if all_ok else 'FALLITO'}")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
