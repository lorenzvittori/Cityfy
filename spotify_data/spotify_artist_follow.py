import base64
import csv
import hashlib
import secrets
import urllib.parse
import webbrowser

import requests
from flask import Flask, request

app = Flask(__name__)

CLIENT_ID = "be8b7ff06bea4076b3f7e2b311219517"
REDIRECT_URI = "http://127.0.0.1:8888/callback"

SCOPE = "user-follow-read"

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

    access_token = response.json()["access_token"]

    print("Access token ottenuto.")

    # -----------------------------------------------------
    # Ottieni tutti gli artisti seguiti
    # -----------------------------------------------------

    artists = get_followed_artists(access_token)

    print("\n======================================")
    print(f"ARTISTI SEGUITI: {len(artists)}")
    print("======================================\n")

    for i, artist in enumerate(artists, 1):

        genres = artist.get("genres", [])

        print(
            f"{i:3}. {artist['name']}"
            f" | {', '.join(genres)}"
        )

    print("\n======================================\n")

    # -----------------------------------------------------
    # Salva su file
    # -----------------------------------------------------

    csv_filename = "spotify_followed_artists.csv"

    save_artists_csv(artists, csv_filename)

    print(f"File salvato: {csv_filename}")

    return f"""
    <h1>Fatto!</h1>
    <p>Hai {len(artists)} artisti seguiti.</p>
    <p>Salvati in <b>{csv_filename}</b>.</p>
    """


# ---------------------------------------------------------
# GET /me/following
# ---------------------------------------------------------

def get_followed_artists(access_token):

    headers = {
        "Authorization": f"Bearer {access_token}"
    }

    artists = []

    url = "https://api.spotify.com/v1/me/following"

    params = {
        "type": "artist",
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

        page = data["artists"]

        artists.extend(page["items"])

        print(
            f"Scaricati {len(artists)} / {page['total']}"
        )

        # Spotify ci dice se esiste una pagina successiva
        if page["next"] is None:
            break

        # URL della pagina successiva
        url = page["next"]

        # I parametri sono già contenuti nell'URL next
        params = {}

    return artists


# ---------------------------------------------------------
# Salva CSV (riassunto leggibile)
# ---------------------------------------------------------

def save_artists_csv(artists, filename):

    with open(
        filename,
        "w",
        newline="",
        encoding="utf-8-sig"
    ) as file:

        writer = csv.writer(file)

        writer.writerow([
            "artist_id",
            "name",
            "genres",
            "popularity",
            "followers",
            "spotify_uri",
            "spotify_url",
        ])

        for artist in artists:

            genres = artist.get("genres", [])

            writer.writerow([
                artist.get("id"),
                artist.get("name"),
                ", ".join(genres),
                artist.get("popularity"),
                artist.get("followers", {}).get("total"),
                artist.get("uri"),
                artist.get("external_urls", {}).get("spotify"),
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