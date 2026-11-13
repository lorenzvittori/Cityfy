import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';
import { LiveService } from './core/live/live.service';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient()],
    }).compileComponents();
  });

  it('hides the top bar without a session', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.topbar')).toBeNull();
  });

  it('shows when an anonymous session expires', async () => {
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    TestBed.inject(AuthService).me.set({
      kind: 'anonymous',
      userId: 'anon_x',
      displayName: null,
      linkedAt: null,
      lastSyncAt: null,
      expiresAt: tomorrow.toISOString(),
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('.user')?.textContent).toContain('Senza account · fino a domani');
  });

  it('shows the Last.fm name, the logout and the attribution for an account', async () => {
    TestBed.inject(AuthService).me.set({
      kind: 'lastfm',
      userId: 'u_1',
      displayName: 'alice',
      linkedAt: null,
      lastSyncAt: null,
      expiresAt: null,
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.user')?.textContent).toContain('alice');
    expect(el.querySelector('.topbar button:last-of-type')?.textContent).toContain('Esci');
    expect(el.querySelector('.powered')?.textContent).toContain('AudioScrobbler');
  });

  it('shows the track playing now, linked to Last.fm', async () => {
    TestBed.inject(AuthService).me.set({
      kind: 'lastfm',
      userId: 'u_1',
      displayName: 'alice',
      linkedAt: null,
      lastSyncAt: null,
      expiresAt: null,
    });
    TestBed.inject(LiveService).nowPlaying.set({
      playing: true,
      artist: 'Radiohead',
      track: 'Reckoner',
      album: null,
      artistMbid: null,
      startedAt: null,
      trackUrl: 'https://www.last.fm/music/Radiohead/_/Reckoner',
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const link = (fixture.nativeElement as HTMLElement).querySelector<HTMLAnchorElement>('.now-playing');
    expect(link?.textContent).toContain('Radiohead – Reckoner');
    expect(link?.href).toBe('https://www.last.fm/music/Radiohead/_/Reckoner');
  });
});
