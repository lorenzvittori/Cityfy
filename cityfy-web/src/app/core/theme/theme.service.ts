import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'system';
export type Theme = 'light' | 'dark';

export const THEME_KEY = 'cityfy.theme';
const ORDER: ThemePreference[] = ['system', 'light', 'dark'];

/**
 * Light/dark theme. The preference (light, dark or follow the system) is remembered in this browser;
 * the resolved theme is written on <html data-theme="..."> where the CSS variables of styles.scss
 * switch palette. index.html applies the stored theme before Angular starts (no white flash).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly media = this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.media?.matches ?? false);

  readonly preference = signal<ThemePreference>(readPreference());
  readonly theme = computed<Theme>(() => {
    const p = this.preference();
    return p === 'system' ? (this.systemDark() ? 'dark' : 'light') : p;
  });

  constructor() {
    const onChange = (e: MediaQueryListEvent) => this.systemDark.set(e.matches);
    this.media?.addEventListener('change', onChange);
    inject(DestroyRef).onDestroy(() => this.media?.removeEventListener('change', onChange));

    effect(() => this.document.documentElement.setAttribute('data-theme', this.theme()));
  }

  /** system -> light -> dark -> system */
  cycle(): void {
    this.set(ORDER[(ORDER.indexOf(this.preference()) + 1) % ORDER.length]);
  }

  set(preference: ThemePreference): void {
    this.preference.set(preference);
    try {
      localStorage.setItem(THEME_KEY, preference);
    } catch {
      // storage disabled: the choice lasts for this page only
    }
  }
}

function readPreference(): ThemePreference {
  try {
    const value = localStorage.getItem(THEME_KEY);
    return value === 'light' || value === 'dark' || value === 'system' ? value : 'system';
  } catch {
    return 'system';
  }
}
