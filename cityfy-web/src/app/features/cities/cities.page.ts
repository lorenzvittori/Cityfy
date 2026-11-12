import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { GeneratedCity, GeneratedCityPage } from '../../core/api/api.models';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';
import { cityTitle, isActive, layoutLabel, periodLabel, statusLabel } from './city-labels';

const PAGE_SIZE = 10;
const MAX_CITIES = 10;
const POLL_MS = 4000;

/** The user's saved cities: paged table, open / cancel / delete, new city. */
@Component({
  selector: 'app-cities-page',
  imports: [RouterLink, DatePipe, ErrorMessage],
  templateUrl: './cities.page.html',
  styleUrl: './cities.page.scss',
})
export class CitiesPage {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly page = signal(1);
  protected readonly data = signal<GeneratedCityPage | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly busy = signal<string | null>(null);
  protected readonly rebuilding = signal(false);
  protected readonly rebuilt = signal(false);

  protected readonly title = cityTitle;
  protected readonly status = statusLabel;
  protected readonly layout = layoutLabel;
  protected readonly period = periodLabel;
  protected readonly active = isActive;
  protected readonly maxCities = MAX_CITIES;

  protected readonly pages = computed(() => {
    const d = this.data();
    return d ? Math.max(1, Math.ceil(d.total / d.pageSize)) : 1;
  });
  protected readonly limitReached = computed(() => (this.data()?.total ?? 0) >= MAX_CITIES);

  private polling?: Subscription;

  constructor() {
    this.load();
    this.destroyRef.onDestroy(() => this.polling?.unsubscribe());
  }

  protected load(): void {
    this.api
      .cities(this.page(), PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.error.set(null);
          this.data.set(data);
          this.pollWhileActive(data.items);
        },
        error: (e: ApiError) => this.error.set(e),
      });
  }

  protected goTo(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pages()));
    this.load();
  }

  protected cancel(city: GeneratedCity): void {
    this.busy.set(city.cityId);
    this.api.cancelCity(city.cityId).subscribe({
      next: () => this.done(),
      error: (e: ApiError) => this.done(e),
    });
  }

  protected remove(city: GeneratedCity): void {
    if (!confirm(`Cancellare la città "${cityTitle(city)}"?`)) return;
    this.busy.set(city.cityId);
    this.api.deleteCity(city.cityId).subscribe({
      next: () => {
        // last item of the last page: go back one page
        if (this.data()?.items.length === 1 && this.page() > 1) this.page.update((p) => p - 1);
        this.done();
      },
      error: (e: ApiError) => this.done(e),
    });
  }

  /** Recomputes the artist catalog from all the imported plays (e.g. after a new genre catalog). */
  protected rebuild(): void {
    this.rebuilding.set(true);
    this.api.rebuild().subscribe({
      next: () => {
        this.rebuilding.set(false);
        this.rebuilt.set(true);
      },
      error: (e: ApiError) => {
        this.rebuilding.set(false);
        this.error.set(e);
      },
    });
  }

  private done(error?: ApiError): void {
    this.busy.set(null);
    if (error) this.error.set(error);
    this.load();
  }

  private pollWhileActive(items: GeneratedCity[]): void {
    this.polling?.unsubscribe();
    if (items.some((c) => isActive(c.status))) this.polling = timer(POLL_MS).subscribe(() => this.load());
  }
}
