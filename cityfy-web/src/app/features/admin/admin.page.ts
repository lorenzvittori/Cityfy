import { DatePipe, DecimalPipe, JsonPipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { EMPTY, Subscription, catchError, forkJoin, switchMap, timer } from 'rxjs';
import {
  AdminAccount,
  AdminRun,
  AdminService,
  AdminSpotifyJob,
  AdminUpload,
  MbImportStatus,
  ServicesOverview,
  TableInfo,
  TablePage,
} from '../../core/api/admin.service';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';
import { ProgressBar } from '../../shared/progress-bar/progress-bar';

type Tab = 'activity' | 'services' | 'musicbrainz' | 'pipeline' | 'spotify' | 'database';
type Source = 'mongo' | 'sql';

/**
 * Admin panel (mock admin user): service health and log links, MusicBrainz import,
 * pipeline runs, Spotify accounts and uploads, read-only database browser.
 */
@Component({
  selector: 'app-admin-page',
  imports: [FormsModule, DatePipe, DecimalPipe, JsonPipe, ErrorMessage, ProgressBar],
  templateUrl: './admin.page.html',
  styleUrl: './admin.page.scss',
})
export class AdminPage {
  private readonly admin = inject(AdminService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly tabs: { id: Tab; label: string }[] = [
    { id: 'activity', label: 'Attività' },
    { id: 'services', label: 'Servizi e log' },
    { id: 'musicbrainz', label: 'MusicBrainz' },
    { id: 'pipeline', label: 'Pipeline' },
    { id: 'spotify', label: 'Spotify e upload' },
    { id: 'database', label: 'Database' },
  ];
  protected readonly tab = signal<Tab>('activity');
  protected readonly error = signal<ApiError | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly services = signal<ServicesOverview | null>(null);
  protected readonly mb = signal<MbImportStatus | null>(null);
  protected readonly runs = signal<AdminRun[]>([]);
  protected readonly accounts = signal<AdminAccount[]>([]);
  protected readonly uploads = signal<AdminUpload[]>([]);
  protected readonly collections = signal<TableInfo[]>([]);
  protected readonly tables = signal<TableInfo[]>([]);
  protected readonly page = signal<TablePage | null>(null);
  protected readonly selected = signal<{ source: Source; name: string } | null>(null);
  protected rebuildUserId = '';

  // --- Activity (live) --------------------------------------------------------------
  protected readonly spotifyJobs = signal<AdminSpotifyJob[]>([]);
  protected readonly lastRefresh = signal<Date | null>(null);
  protected readonly activeUploads = computed(() => this.uploads().filter((u) => isActive(u.status)));
  protected readonly recentUploads = computed(() => this.uploads().filter((u) => !isActive(u.status)).slice(0, 5));
  protected readonly activeJobs = computed(() => this.spotifyJobs().filter((j) => isActive(j.status)));
  protected readonly recentJobs = computed(() => this.spotifyJobs().filter((j) => !isActive(j.status)).slice(0, 5));
  protected readonly activeRuns = computed(() => this.runs().filter((r) => isActive(r.status)));
  protected readonly recentRuns = computed(() => this.runs().filter((r) => !isActive(r.status)).slice(0, 5));
  protected readonly anythingRunning = computed(
    () => this.mb()?.state === 'Running' || this.activeUploads().length + this.activeJobs().length + this.activeRuns().length > 0,
  );

  private activityPolling?: Subscription;
  private mbPolling?: Subscription;

  constructor() {
    this.show('activity');
  }

  protected show(tab: Tab): void {
    this.tab.set(tab);
    this.error.set(null);
    this.notice.set(null);
    this.activityPolling?.unsubscribe();
    switch (tab) {
      case 'activity':
        this.pollActivity();
        break;
      case 'services':
        this.load(this.admin.services(), (s) => this.services.set(s));
        break;
      case 'musicbrainz':
        this.refreshMb();
        break;
      case 'pipeline':
        this.load(this.admin.runs(), (r) => this.runs.set(r));
        break;
      case 'spotify':
        this.load(this.admin.accounts(), (a) => this.accounts.set(a));
        this.load(this.admin.uploads(), (u) => this.uploads.set(u));
        break;
      case 'database':
        this.load(this.admin.collections(), (c) => this.collections.set(c));
        this.load(this.admin.tables(), (t) => this.tables.set(t));
        break;
    }
  }

  /** Every 3 s while the Activity tab is open: MusicBrainz import, uploads, Spotify jobs, pipeline runs. */
  private pollActivity(): void {
    this.activityPolling = timer(0, 3000)
      .pipe(
        switchMap(() =>
          forkJoin({
            mb: this.admin.mbStatus(),
            uploads: this.admin.uploads(),
            jobs: this.admin.spotifyJobs(),
            runs: this.admin.runs(30),
          }).pipe(
            catchError((e: ApiError) => {
              this.error.set(e);
              return EMPTY;
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ mb, uploads, jobs, runs }) => {
        this.error.set(null);
        this.mb.set(mb);
        this.uploads.set(uploads);
        this.spotifyJobs.set(jobs);
        this.runs.set(runs);
        this.lastRefresh.set(new Date());
      });
  }

  /** Stops a running/queued process after confirmation; the Activity view refreshes on its own. */
  protected cancel(kind: 'import' | 'upload' | 'spotify' | 'run', id = ''): void {
    const short = id.slice(0, 8);
    const questions = {
      import: "Annullare l'import MusicBrainz in corso?\n\nIl catalogo resta quello precedente (la scrittura è in una transazione).",
      upload: `Annullare l'upload ${short}?\n\nGli ascolti già importati restano; ricaricando lo stesso file non si creano duplicati.`,
      spotify: `Annullare il retrieve Spotify ${short}?`,
      run: `Annullare la costruzione della città ${short}?\n\nSe è già in corso, Elaboration la completerà ma il risultato verrà ignorato.`,
    };
    if (!confirm(questions[kind])) return;

    const request = {
      import: () => this.admin.cancelImport(),
      upload: () => this.admin.cancelUpload(id),
      spotify: () => this.admin.cancelSpotifyJob(id),
      run: () => this.admin.cancelRun(id),
    }[kind]();

    this.load(request, () => {
      this.notice.set('Annullamento richiesto.');
      if (kind === 'import') this.refreshMb();
    });
  }

  /** Minutes since the last progress update of a running item (a long silence = probably stuck). */
  protected silentMinutes(updatedAt: string | null): number {
    return updatedAt ? Math.floor((Date.now() - new Date(updatedAt).getTime()) / 60000) : 0;
  }

  // --- MusicBrainz -------------------------------------------------------------

  protected refreshMb(): void {
    this.load(this.admin.mbStatus(), (s) => {
      this.mb.set(s);
      if (s.state === 'Running') this.pollMb();
    });
  }

  protected startMb(): void {
    const ok = confirm(
      "Avviare l'import del catalogo MusicBrainz?\n\nScarica l'ultimo export (circa 7-8 GB), sostituisce il dump locale precedente e reimporta generi e artisti in SQL Server. Può richiedere da decine di minuti a qualche ora.",
    );
    if (!ok) return;
    this.load(this.admin.mbStart(), (s) => {
      this.mb.set(s);
      this.notice.set('Import avviato.');
      this.pollMb();
    });
  }

  private pollMb(): void {
    this.mbPolling?.unsubscribe();
    this.mbPolling = timer(5000, 5000)
      .pipe(
        switchMap(() => this.admin.mbStatus()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((s) => {
        this.mb.set(s);
        if (s.state !== 'Running') this.mbPolling?.unsubscribe();
      });
  }

  // --- Pipeline / Spotify ----------------------------------------------------------

  protected rebuild(userId: string): void {
    if (!userId.trim()) return;
    this.load(this.admin.rebuild(userId.trim()), (run) => {
      this.notice.set(`Ricostruzione richiesta per ${run.userId} (run ${run.runId.slice(0, 8)}).`);
      this.load(this.admin.runs(), (r) => this.runs.set(r));
    });
  }

  protected sources(run: AdminRun): string {
    return [...new Set(run.triggers.map((t) => t.source))].join(', ');
  }

  protected newPlays(run: AdminRun): number {
    return run.triggers.reduce((sum, t) => sum + t.newPlays, 0);
  }

  protected sync(userId: string): void {
    this.load(this.admin.sync(userId), (job) => this.notice.set(`Sincronizzazione avviata per ${userId} (job ${job.jobId.slice(0, 8)}).`));
  }

  // --- Database ----------------------------------------------------------------------

  protected open(source: Source, name: string, pageNumber = 1): void {
    this.selected.set({ source, name });
    const request = source === 'mongo' ? this.admin.collection(name, pageNumber) : this.admin.table(name, pageNumber);
    this.load(request, (p) => this.page.set(p));
  }

  protected pages(p: TablePage): number {
    return Math.max(1, Math.ceil(p.total / p.pageSize));
  }

  protected cell(value: unknown): string {
    if (value === null || value === undefined) return '';
    const text = typeof value === 'object' ? JSON.stringify(value) : String(value);
    return text.length > 120 ? text.slice(0, 117) + '…' : text;
  }

  protected logout(): void {
    this.admin.logout().subscribe({ complete: () => this.router.navigate(['/admin/login']) });
  }

  private load<T>(request: import('rxjs').Observable<T>, next: (value: T) => void): void {
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next,
      error: (e: ApiError) => this.error.set(e),
    });
  }
}

/** Upload/Spotify statuses (Pending, WORKING_IN_PROGRESS) and pipeline statuses (Pending, Building). */
function isActive(status: string): boolean {
  return status === 'Pending' || status === 'WORKING_IN_PROGRESS' || status === 'Building';
}
