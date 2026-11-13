import { Component, computed, input } from '@angular/core';

/** Progress bar: determinate with a percent, animated stripes when the percent is unknown (null). */
@Component({
  selector: 'app-progress-bar',
  template: `
    <div class="track" role="progressbar" [attr.aria-valuenow]="percent()" aria-valuemin="0" aria-valuemax="100" [class.indeterminate]="percent() === null">
      <div class="fill" [style.width.%]="width()"></div>
    </div>
    @if (percent() !== null) {
      <span class="value">{{ percent() }}%</span>
    }
  `,
  styles: `
    :host { display: flex; align-items: center; gap: 8px; min-width: 160px; }
    .track { flex: 1; height: 8px; border-radius: 4px; background: var(--track); overflow: hidden; }
    .fill { height: 100%; background: var(--progress); transition: width 0.4s ease; }
    .indeterminate .fill {
      width: 100% !important;
      background: repeating-linear-gradient(-45deg, var(--progress-light) 0 10px, var(--progress) 10px 20px);
      background-size: 28px 28px;
      animation: move 0.8s linear infinite;
    }
    .value { font-size: 12px; color: var(--text-muted); font-variant-numeric: tabular-nums; min-width: 42px; text-align: right; }
    @keyframes move { to { background-position: 28px 0; } }
  `,
})
export class ProgressBar {
  /** 0-100, or null for indeterminate. */
  readonly percent = input<number | null>(null);
  protected readonly width = computed(() => Math.max(0, Math.min(100, this.percent() ?? 100)));
}
