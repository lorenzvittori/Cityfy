import base64
import hashlib
import secrets
import urllib.parse
import webbrowser
import csv

import requests
from flask import Flask, request

app = Flask(__name__)

CLIENT_ID = "be8b7ff06bea4076b3f7e2b311219517"
REDIRECT_URI = "http://127.0.0.1:8888/callback"

SCOPE = "user-library-read"


# ---------------------------------------------------------
# PKCE
# ---------------------------------------------------------

code_verifier = secrets.token_urlsafe(64)

code_challenge = (
    base64.urlsafe_b64encode(
        hashlib.sha256(code_verifier.encode()).digest()
    )
    .rstrip(b"=")
    .decode("utf-8")
)


# ---------------------------------------------------------
# Login Spotify
# ---------------------------------------------------------

@app.route("/")
def index():

    params = {
        "client_id": CLIENT_ID,
        "response_type": "code",
        "redirect_uri": REDIRECT_URI,
        "scope": SCOPE,
        "code_challenge_method": "S256",
        "code_challenge": code_challenge,
    }

    url = (
        "https://accounts.spotify.com/authorize?"
        + urllib.parse.urlencode(params)
    )

    print("\nApro Spotify...")
    webbrowser.open(url)

    return "Fai il login a Spotify nel browser."


# ---------------------------------------------------------
# Callback
# ---------------------------------------------------------

@app.route("/callback")
def callback():

    print("\n--- CALLBACK ---")

    error = request.args.get("error")

    if error:
        return f"Errore Spotify: {error}", 400

    code = request.args.get("code")

    if not code:
        return "Authorization code mancante.", 400


    # -----------------------------------------------------
    # Authorization code -> access token
    # -----------------------------------------------------

    response = requests.post(
        "https://accounts.spotify.com/api/token",
        data={
            "client_id": CLIENT_ID,
            "grant_type": "authorization_code",
            "code": code,
            "redirect_uri": REDIRECT_URI,
            "code_verifier": code_verifier,
        },
    )

    if not response.ok:
        print(response.text)
        return f"Errore token: {response.text}", 400

    token_data = response.json()

    access_token = token_data["access_token"]

    print("Access token ottenuto.")
    print("Scope:", token_data.get("scope"))


    # -----------------------------------------------------
    # LIBRERIA
    # -----------------------------------------------------

    tracks = get_saved_tracks(access_token)

    print("\n======================================")
    print(f"BRANI NELLA LIBRERIA: {len(tracks)}")
    print("======================================\n")


    # -----------------------------------------------------
    # Salva CSV
    # -----------------------------------------------------

    filename = "spotify_library.csv"

    save_library_csv(tracks, filename)

    print(f"File salvato: {filename}")


    return f"""
    <h1>Fatto!</h1>

    <p>Brani nella libreria: <b>{len(tracks)}</b></p>

    <p>
        Ho salvato tutto in:
        <b>{filename}</b>
    </p>

    <p>Controlla la cartella dove hai eseguito lo script.</p>
    """


# ---------------------------------------------------------
# GET /me/tracks
# ---------------------------------------------------------

def get_saved_tracks(access_token):

    headers = {
        "Authorization": f"Bearer {access_token}"
    }

    tracks = []

    url = "https://api.spotify.com/v1/me/tracks"

    params = {
        "limit": 50,
    }

    while True:

        response = requests.get(
            url,
            headers=headers,
            params=params,
        )

        if not response.ok:
            raise Exception(
                f"Spotify API error "
                f"{response.status_code}: "
                f"{response.text}"
            )

        data = response.json()

        page_items = data["items"]

        tracks.extend(page_items)

        print(
            f"Scaricati {len(tracks)} / {data['total']}"
        )

        # Nessuna pagina successiva
        if data["next"] is None:
            break

        # Spotify ci fornisce direttamente
        # l'URL della pagina successiva
        url = data["next"]

        # I parametri sono già dentro "next"
        params = {}

    return tracks


# ---------------------------------------------------------
# Salva CSV
# ---------------------------------------------------------

def save_library_csv(tracks, filename):

    with open(
        filename,
        "w",
        newline="",
        encoding="utf-8-sig"
    ) as file:

        writer = csv.writer(file)

        writer.writerow([
            "added_at",
            "track_id",
            "track_name",
            "artist_id",
            "artist_name",
            "album_id",
            "album_name",
            "duration_ms",
            "spotify_uri",
        ])

        for item in tracks:

            track = item["track"]

            artists = track.get("artists", [])

            # In caso di brano con più artisti
            artist_names = ", ".join(
                artist["name"]
                for artist in artists
            )

            artist_ids = ", ".join(
                artist["id"]
                for artist in artists
                if artist.get("id")
            )

            writer.writerow([
                item.get("added_at"),
                track.get("id"),
                track.get("name"),
                artist_ids,
                artist_names,
                track.get("album", {}).get("id"),
                track.get("album", {}).get("name"),
                track.get("duration_ms"),
                track.get("uri"),
            ])


# ---------------------------------------------------------
# Start server
# ---------------------------------------------------------

if __name__ == "__main__":

    print("Server Spotify avviato.")
    print("Apri http://127.0.0.1:8888")

    app.run(
        host="127.0.0.1",
        port=8888,
        debug=False,
    )
    