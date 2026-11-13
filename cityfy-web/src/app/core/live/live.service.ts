import { Injectable, signal } from '@angular/core';
import { NowPlaying } from '../api/api.models';

/**
 * Live events of api/v1/live (Server-Sent Events, accounts only): what the user is listening to.
 * EventSource reconnects by itself; the API keeps the Last.fm reading fast while a page is open.
 */
@Injectable({ providedIn: 'root' })
export class LiveService {
  readonly nowPlaying = signal<NowPlaying | null>(null);

  private source: EventSource | null = null;

  connect(): void {
    if (this.source || typeof EventSource === 'undefined') return;
    this.source = new EventSource('/api/v1/live');
    this.source.addEventListener('now-playing', (e) => this.nowPlaying.set(JSON.parse((e as MessageEvent<string>).data) as NowPlaying));
  }

  disconnect(): void {
    this.source?.close();
    this.source = null;
    this.nowPlaying.set(null);
  }
}
