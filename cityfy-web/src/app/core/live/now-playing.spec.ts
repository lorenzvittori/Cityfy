import { City, NowPlaying } from '../api/api.models';
import { playingArtistId } from './now-playing';

const building = (artistId: string, name: string) => ({
  artistId,
  name,
  spotifyArtistUri: null,
  plays: 1,
  qualifiedPlays: 1,
  listeningMinutes: 3,
  height: 1,
  width: 1,
  color: '#000',
  genres: [],
});

const city = {
  districts: [{ districtId: 'd', blocks: [{ blockId: 'b', buildings: [building('mb-bjork', 'Björk'), building('mb-genesis-us', 'Genesis')] }] }],
} as unknown as City;

const playing = (artist: string, artistMbid: string | null = null): NowPlaying => ({
  playing: true,
  artist,
  track: 'x',
  album: null,
  artistMbid,
  startedAt: null,
  trackUrl: null,
});

describe('playingArtistId', () => {
  it('finds the building by MusicBrainz id first', () => {
    expect(playingArtistId(city, playing('Genesis', 'mb-genesis-us'))).toBe('mb-genesis-us');
  });

  it('falls back to the name, ignoring case and accents', () => {
    expect(playingArtistId(city, playing('bjork'))).toBe('mb-bjork');
  });

  it('is null when nothing plays or the artist is not in the city', () => {
    expect(playingArtistId(city, { ...playing('Björk'), playing: false })).toBeNull();
    expect(playingArtistId(city, playing('Muse'))).toBeNull();
    expect(playingArtistId(null, playing('Björk'))).toBeNull();
  });
});
