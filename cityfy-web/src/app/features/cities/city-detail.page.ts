import { Component, DestroyRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { Block, Building, District, GeneratedCity, GeneratedCityDetail } from '../../core/api/api.models';
import { ApiError } from '../../core/errors/api-error';
import { errorMessage } from '../../core/errors/error-messages';
import { LiveService } from '../../core/live/live.service';
import { playingArtistId } from '../../core/live/now-playing';
import { ErrorMessage } from '../../shared/error-message/error-message';
import { CityScene } from './city-scene/city-scene';
import { cityTitle, districtsLabel, isActive, layoutLabel, periodLabel, statusLabel } from './city-labels';
import { DEFAULT_DISTRICTS } from './city-form';

const POLL_MS = 3000;
/** Artists listed under a genre before "Mostra tutti". */
const ARTISTS_SHOWN = 25;

/** A saved city: generation status while it is built, then the 3D map with legend and artist details. */
@Component({
  selector: 'app-city-detail-page',
  imports: [CityScene, ErrorMessage, RouterLink],
  templateUrl: './city-detail.page.html',
  styleUrls: ['./city-detail.page.scss', './now-playing.scss'],
})
export class CityDetailPage {
  /** Route parameter. */
  readonly cityId = input.required<string>();

  private readonly api = inject(ApiService);
  protected readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly detail = signal<GeneratedCityDetail | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly selectedArtistId = signal<string | null>(null);
  protected readonly busy = signal(false);

  // Legend: district -> its genres -> the artists of the open genre; a click flies the map there
  private readonly scene = viewChild(CityScene);
  protected readonly openDistrictId = signal<string | null>(null);
  protected readonly openBlockId = signal<string | null>(null);
  protected readonly allArtistsOf = signal<string | null>(null);
  protected readonly artistsShown = ARTISTS_SHOWN;

  protected readonly message = errorMessage;
  protected readonly status = statusLabel;
  protected readonly active = isActive;
  protected readonly title = computed(() => {
    const d = this.detail();
    return d ? cityTitle(d.info) : '';
  });
  protected readonly subtitle = computed(() => {
    const i = this.detail()?.info;
    return i
      ? `${periodLabel(i.from, i.to)} · mappa ${layoutLabel(i.layout).toLowerCase()} · ${districtsLabel(i.maxDistricts, i.extraGenres, DEFAULT_DISTRICTS).toLowerCase()}`
      : '';
  });

  /** Track playing now (accounts, from Last.fm) and its building in this city, if any. */
  protected readonly nowPlaying = inject(LiveService).nowPlaying;
  protected readonly playingArtistId = computed(() => playingArtistId(this.detail()?.city, this.nowPlaying()));

  protected readonly selected = computed(() => {
    const city = this.detail()?.city;
    const id = this.selectedArtistId();
    if (!city || !id) return null;
    for (const d of city.districts)
      for (const b of d.blocks) {
        const building = b.buildings.find((x) => x.artistId === id);
        if (building) return { building, district: d.name, genre: b.name, color: d.color };
      }
    return null;
  });

  private polling?: Subscription;

  constructor() {
    this.destroyRef.onDestroy(() => this.polling?.unsubscribe());
    // (re)load when the route parameter changes
    effect(() => {
      this.cityId();
      untracked(() => {
        this.selectedArtistId.set(null);
        this.openDistrictId.set(null);
        this.openBlockId.set(null);
        this.load();
      });
    });
  }

  protected load(): void {
    this.polling?.unsubscribe();
    this.api
      .generatedCity(this.cityId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.error.set(null);
          this.detail.set(d);
          if (isActive(d.info.status)) this.polling = timer(POLL_MS).subscribe(() => this.load());
        },
        error: (e: ApiError) => this.error.set(e),
      });
  }

  /** Building clicked on the map: selected, and its district and genre open in the legend. */
  protected select(artistId: string): void {
    const next = this.selectedArtistId() === artistId ? null : artistId;
    this.selectedArtistId.set(next);
    const city = this.detail()?.city;
    if (!next || !city) return;
    for (const d of city.districts)
      for (const b of d.blocks)
        if (b.buildings.some((x) => x.artistId === next)) {
          this.openDistrictId.set(d.districtId);
          this.openBlockId.set(b.blockId);
          if (b.buildings.findIndex((x) => x.artistId === next) >= ARTISTS_SHOWN) this.allArtistsOf.set(b.blockId);
          return;
        }
  }

  protected openDistrict(d: District): void {
    const closing = this.openDistrictId() === d.districtId;
    this.openDistrictId.set(closing ? null : d.districtId);
    this.openBlockId.set(null);
    if (!closing) this.scene()?.focusDistrict(d.districtId);
  }

  protected openBlock(b: Block): void {
    const closing = this.openBlockId() === b.blockId;
    this.openBlockId.set(closing ? null : b.blockId);
    if (!closing) this.scene()?.focusBlock(b.blockId);
  }

  /** "Mostra" on the track playing: select its building, open its genre and fly there. */
  protected showPlaying(): void {
    const id = this.playingArtistId();
    if (!id) return;
    if (this.selectedArtistId() !== id) this.select(id);
    this.scene()?.focusArtist(id);
  }

  /** Artist clicked in the legend: selected (outline on the map) and the camera flies to it. */
  protected openArtist(b: Building): void {
    this.selectedArtistId.set(b.artistId);
    this.scene()?.focusArtist(b.artistId);
  }

  protected visibleArtists(b: Block): Building[] {
    return this.allArtistsOf() === b.blockId ? b.buildings : b.buildings.slice(0, ARTISTS_SHOWN);
  }

  protected cancel(): void {
    this.busy.set(true);
    this.api.cancelCity(this.cityId()).subscribe({
      next: () => this.afterAction(),
      error: (e: ApiError) => this.afterAction(e),
    });
  }

  protected remove(): void {
    if (!confirm(`Cancellare la città "${this.title()}"?`)) return;
    this.busy.set(true);
    this.api.deleteCity(this.cityId()).subscribe({
      next: () => this.router.navigate(['/cities']),
      error: (e: ApiError) => this.afterAction(e),
    });
  }

  /** "Duplicate and edit": the new-city form prefilled with these parameters. */
  protected duplicateParams(info: GeneratedCity): Record<string, string> {
    const params: Record<string, string> = { layout: info.layout };
    if (info.recipe) params['recipe'] = info.recipe;
    if (info.name) params['name'] = info.name;
    if (info.from) params['from'] = info.from;
    if (info.to) params['to'] = info.to;
    if (info.genres.length) params['genres'] = info.genres.join(',');
    if (info.subgenres.length) params['subgenres'] = info.subgenres.join(',');
    if (info.maxDistricts !== null) params['districts'] = String(info.maxDistricts);
    if (info.extraGenres) params['extra'] = info.extraGenres;
    return params;
  }

  /** spotify:artist:{id} -> web page (works without the Spotify app). */
  protected spotifyUrl(uri: string): string {
    return `https://open.spotify.com/artist/${encodeURIComponent(uri.replace('spotify:artist:', ''))}`;
  }

  protected spotifySearchUrl(name: string): string {
    return `https://open.spotify.com/search/${encodeURIComponent(name)}/artists`;
  }

  protected minutes(b: Building): string {
    return b.listeningMinutes >= 60 ? `${Math.round(b.listeningMinutes / 60)} ore` : `${Math.round(b.listeningMinutes)} min`;
  }

  private afterAction(error?: ApiError): void {
    this.busy.set(false);
    if (error) this.error.set(error);
    this.load();
  }
}
