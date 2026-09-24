"""
Estrae selettivamente dai dump ufficiali MusicBrainz (mbdump.tar.bz2) solo i
file (tabelle) necessari per costruire il grafo dei generi e l'associazione
artista -> generi, senza decomprimere l'intero archivio.

Uso:
    python extract_mbdump.py --input /percorso/mbdump.tar.bz2
    python extract_mbdump.py --input /percorso/mbdump.tar.bz2 --output mbdump_use
    python extract_mbdump.py --input /percorso/mbdump.tar.bz2 --files genre artist
"""

import argparse
import tarfile
from pathlib import Path

# Tabelle necessarie per: (a) il grafo genere<->genere, (b) l'associazione
# artista -> genere per nome (con gestione alias).
DEFAULT_FILES = [
    "artist",
    "artist_alias",
    "genre",
    "l_artist_genre",
    "l_genre_genre",
    "link",
    "link_type",
]

manual_input = Path(r"C:\Users\sstam\Desktop\mbdump.tar.bz2")
manual_output = Path(r"C:\Users\sstam\Documents\GitHubRepository\Cityfy\music_brainz_DUMP\mbdump_LAST")


def extract_selected(input_path: Path, output_dir: Path, wanted: list[str]) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    remaining = set(wanted)
    found = []

    with tarfile.open(input_path, "r:bz2") as tar:
        # Iterazione in streaming: non carica l'intero archivio in memoria,
        # ma legge un membro alla volta e lo estrae solo se serve.
        for member in tar:
            if not remaining:
                break
            name = Path(member.name).name
            if name in remaining and member.isfile():
                extracted = tar.extractfile(member)
                if extracted is None:
                    continue
                target = output_dir / name
                with open(target, "wb") as f:
                    f.write(extracted.read())
                found.append(name)
                remaining.discard(name)

    missing = sorted(remaining)
    print(f"File estratti in '{output_dir}': {sorted(found)}")
    if missing:
        raise SystemExit(
            f"ERRORE: i seguenti file richiesti non sono stati trovati nell'archivio: {missing}"
        )


def main() -> None:
    if manual_input and manual_output:
        extract_selected(manual_input, manual_output, DEFAULT_FILES)
        return
    
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path, help="percorso a mbdump.tar.bz2")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).parent / "mbdump_use",
        help="cartella di destinazione (default: mbdump_use accanto allo script)",
    )
    parser.add_argument(
        "--files",
        nargs="+",
        default=DEFAULT_FILES,
        help="lista di file da estrarre (default: i 7 file necessari al task)",
    )
    args = parser.parse_args()

    extract_selected(args.input, args.output, args.files)


if __name__ == "__main__":
    main()
