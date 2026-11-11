import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription, filter, switchMap, take, timer } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { ImportStatus } from '../../core/api/api.models';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';

/** Steps shown while an export is processed (ERRORI_UI.md §3 "Stati di attesa" and §7.4). */
type Step =
  | { kind: 'idle' }
  | { kind: 'uploading' }
  | { kind: 'processing'; status: ImportStatus | null }
  | { kind: 'building' }
  | { kind: 'done'; newPlays: number }
  | { kind: 'error'; error: ApiError };

const MAX_BYTES = 200_000_000;
const POLL_MS = 2000;

@Component({
  selector: 'app-upload-page',
  imports: [ErrorMessage, RouterLink],
  templateUrl: './upload.page.html',
  styleUrl: './upload.page.scss',
})
export class UploadPage {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly step = signal<Step>({ kind: 'idle' });
  protected readonly dragging = signal(false);
  private polling?: Subscription;

  protected onFileInput(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.start(file);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) this.start(file);
  }

  protected reset(): void {
    this.polling?.unsubscribe();
    this.step.set({ kind: 'idle' });
  }

  private start(file: File): void {
    // Same checks as the server, answered immediately with the same codes
    if (!file.name.toLowerCase().endsWith('.zip')) return this.fail('UPLOAD_NOT_ZIP');
    if (file.size > MAX_BYTES) return this.fail('UPLOAD_TOO_LARGE');

    this.step.set({ kind: 'uploading' });
    this.api
      .upload(file)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ uploadId }) => this.followUpload(uploadId),
        error: (e: ApiError) => this.step.set({ kind: 'error', error: e }),
      });
  }

  /** Pending -> WORKING_IN_PROGRESS -> DONE | FAILED */
  private followUpload(uploadId: string): void {
    this.step.set({ kind: 'processing', status: null });
    this.polling = timer(0, POLL_MS)
      .pipe(
        switchMap(() => this.api.uploadStatus(uploadId)),
        filter((s) => {
          if (s.status === 'DONE' || s.status === 'FAILED' || s.status === 'CANCELLED') return true;
          this.step.set({ kind: 'processing', status: s });
          return false;
        }),
        take(1),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (s) => (s.status === 'DONE' ? this.followCity(s.newPlays) : this.fail(s.errorCode ?? 'UPLOAD_PROCESSING_FAILED')),
        error: (e: ApiError) => this.step.set({ kind: 'error', error: e }),
      });
  }

  /** The orchestrator rebuilds the city ~30s after the import. */
  private followCity(newPlays: number): void {
    if (newPlays === 0) return this.step.set({ kind: 'done', newPlays });

    this.step.set({ kind: 'building' });
    this.polling = timer(POLL_MS, POLL_MS * 2)
      .pipe(
        switchMap(() => this.api.runs(1)),
        filter(([run]) => !!run && (run.status === 'Done' || run.status === 'Failed' || run.status === 'Cancelled')),
        take(1),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ([run]) => (run.status === 'Done' ? this.step.set({ kind: 'done', newPlays }) : this.fail(run.errorCode ?? 'CITY_BUILD_FAILED')),
        error: (e: ApiError) => this.step.set({ kind: 'error', error: e }),
      });
  }

  private fail(code: string): void {
    this.step.set({ kind: 'error', error: new ApiError(code, 400, null) });
  }
}
