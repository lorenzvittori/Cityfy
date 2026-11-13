import { City, NowPlaying } from '../api/api.models';

/**
 * The building of the artist playing now, if the city has it: by MusicBrainz id (exact, from
 * Last.fm), otherwise by name (case and accents ignored).
 */
export function playingArtistId(city: City | null | undefined, playing: NowPlaying | null): string | null {
  if (!city || !playing?.playing || !playing.artist) return null;
  const name = normalize(playing.artist);
  let byName: string | null = null;
  for (const d of city.districts)
    for (const b of d.blocks)
      for (const building of b.buildings) {
        if (playing.artistMbid && building.artistId === playing.artistMbid) return building.artistId;
        if (!byName && normalize(building.name) === name) byName = building.artistId;
      }
  return byName;
}

function normalize(name: string): string {
  return name.normalize('NFD').replace(/\p{M}/gu, '').trim().toLowerCase();
}
