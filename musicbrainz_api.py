"""
Libreria di funzioni per interrogare l'API di MusicBrainz e recuperare
i generi musicali associati a un artista.

Rispetta le linee guida di MusicBrainz:
- richiede uno User-Agent identificativo (nome app / versione / contatto)
- limita le richieste a ~1 al secondo

Esempio d'uso:
    from musicbrainz_api import get_artist_genres_by_name

    genres = get_artist_genres_by_name("Radiohead")
    print(genres)  # ['alternative rock', 'art rock', 'electronic', ...]
"""

from __future__ import annotations

import time
import threading
from dataclasses import dataclass, field
from typing import Optional

import requests

BASE_URL = "https://musicbrainz.org/ws/2"

# MusicBrainz richiede uno User-Agent con nome, versione e contatto.
# Personalizza questi valori con i dati del tuo progetto.
USER_AGENT = "CityfyMusicBrainzClient/1.0 (lorenzvittori@gmail.com)"

MIN_REQUEST_INTERVAL = 1.05  # secondi, con un piccolo margine rispetto al limite di 1 req/s

_last_request_time = 0.0
_rate_limit_lock = threading.Lock()


class MusicBrainzError(Exception):
    """Errore generico nelle chiamate all'API MusicBrainz."""


@dataclass
class ArtistMatch:
    """Un artista trovato tramite ricerca testuale."""
    mbid: str
    name: str
    score: int
    disambiguation: Optional[str] = None
    country: Optional[str] = None


@dataclass
class ArtistGenres:
    """Genere musicali associati a un artista, con il relativo conteggio di voti."""
    mbid: str
    name: str
    genres: list[str] = field(default_factory=list)
    genre_counts: dict[str, int] = field(default_factory=dict)


def _rate_limited_get(path: str, params: dict) -> dict:
    """Esegue una GET verso l'API MusicBrainz rispettando il rate limit."""
    global _last_request_time

    with _rate_limit_lock:
        elapsed = time.monotonic() - _last_request_time
        if elapsed < MIN_REQUEST_INTERVAL:
            time.sleep(MIN_REQUEST_INTERVAL - elapsed)

        params = {**params, "fmt": "json"}
        headers = {"User-Agent": USER_AGENT}

        response = requests.get(f"{BASE_URL}{path}", params=params, headers=headers, timeout=10)
        _last_request_time = time.monotonic()

    if response.status_code == 503:
        raise MusicBrainzError("Limite di richieste superato (503). Riprova più tardi.")
    if not response.ok:
        raise MusicBrainzError(
            f"Errore API MusicBrainz [{response.status_code}]: {response.text[:300]}"
        )

    return response.json()


def search_artist(name: str, limit: int = 10) -> list[ArtistMatch]:
    """
    Cerca artisti per nome e restituisce i risultati ordinati per punteggio
    di rilevanza (score), dal più al meno pertinente.
    """
    data = _rate_limited_get("/artist", {"query": f'artist:"{name}"', "limit": limit})

    matches = []
    for entry in data.get("artists", []):
        matches.append(
            ArtistMatch(
                mbid=entry["id"],
                name=entry.get("name", ""),
                score=int(entry.get("score", 0)),
                disambiguation=entry.get("disambiguation"),
                country=entry.get("country"),
            )
        )

    return sorted(matches, key=lambda a: a.score, reverse=True)


def get_mbid_from_spotify_id(spotify_artist_id: str) -> Optional[str]:
    """
    Recupera l'MBID di un artista a partire dal suo Spotify artist ID,
    sfruttando le relazioni "external links" che MusicBrainz mantiene
    verso le pagine Spotify degli artisti.

    Restituisce None se nessun artista MusicBrainz ha un link a quello
    Spotify ID (capita per artisti minori o non ancora collegati).
    """
    spotify_url = f"https://open.spotify.com/artist/{spotify_artist_id}"
    data = _rate_limited_get("/url", {"resource": spotify_url, "inc": "artist-rels"})

    for relation in data.get("relations", []):
        if relation.get("target-type") == "artist" and "artist" in relation:
            return relation["artist"]["id"]

    return None


def get_artist_genres_from_spotify_id(spotify_artist_id: str) -> ArtistGenres:
    """
    Funzione di comodo: dato uno Spotify artist ID, trova l'MBID collegato
    e ne recupera i generi.

    Solleva MusicBrainzError se nessun artista MusicBrainz risulta collegato
    a quello Spotify ID (in tal caso conviene fare fallback su
    get_artist_genres_by_name usando il nome dell'artista da Spotify).
    """
    mbid = get_mbid_from_spotify_id(spotify_artist_id)
    if mbid is None:
        raise MusicBrainzError(
            f"Nessun artista MusicBrainz collegato allo Spotify ID '{spotify_artist_id}'"
        )
    return get_artist_genres(mbid)


def get_artist_genres(mbid: str) -> ArtistGenres:
    """
    Recupera tutti i generi associati a un artista tramite il suo MusicBrainz ID (MBID).

    MusicBrainz espone i generi tramite l'endpoint artist con inc=genres:
    ogni genere ha un "count" che rappresenta quante volte è stato votato/taggato.
    """
    data = _rate_limited_get(f"/artist/{mbid}", {"inc": "genres"})

    genre_counts: dict[str, int] = {}
    for genre in data.get("genres", []):
        genre_counts[genre["name"]] = int(genre.get("count", 0))

    # Ordina i generi per numero di voti decrescente, poi alfabeticamente
    sorted_genres = sorted(genre_counts.keys(), key=lambda g: (-genre_counts[g], g))

    return ArtistGenres(
        mbid=mbid,
        name=data.get("name", ""),
        genres=sorted_genres,
        genre_counts=genre_counts,
    )


def get_artist_genres_by_name(name: str, min_score: int = 0) -> ArtistGenres:
    """
    Funzione di comodo: cerca l'artista per nome, prende il match migliore
    e ne recupera i generi.

    Solleva MusicBrainzError se non viene trovato nessun artista
    (o nessuno con score >= min_score).
    """
    candidates = search_artist(name)
    candidates = [c for c in candidates if c.score >= min_score]

    if not candidates:
        raise MusicBrainzError(f"Nessun artista trovato per '{name}'")

    best_match = candidates[0]
    return get_artist_genres(best_match.mbid)


def get_genres_for_multiple_artists(names: list[str]) -> dict[str, list[str]]:
    """
    Recupera i generi per una lista di nomi di artisti.
    Rispetta automaticamente il rate limit tra una richiesta e l'altra.

    Restituisce un dizionario {nome_artista: [lista_generi]}.
    Se un artista non viene trovato, la lista associata sarà vuota e
    l'errore viene ignorato silenziosamente (loggabile se necessario).
    """
    results: dict[str, list[str]] = {}
    for name in names:
        try:
            artist_genres = get_artist_genres_by_name(name)
            results[name] = artist_genres.genres
        except MusicBrainzError:
            results[name] = []

    return results


if __name__ == "__main__":
    # Esempio rapido da riga di comando
    import sys

    artist_name = sys.argv[1] if len(sys.argv) > 1 else "Radiohead"
    result = get_artist_genres_by_name(artist_name)
    print(f"Artista: {result.name} ({result.mbid})")
    print(f"Generi: {result.genres}")
