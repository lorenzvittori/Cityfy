import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { CityLayoutType, GenreOption } from '../../core/api/api.models';
import { ApiError } from '../../core/errors/api-error';
import { ErrorMessage } from '../../shared/error-message/error-message';
import {
  GenreSelection,
  NO_SELECTION,
  RangeShortcut,
  SHORTCUTS,
  apiRange,
  districtState,
  formTo,
  isEmpty,
  isoDay,
  rangeError,
  selectionOf,
  shortcutRange,
  subgenreChecked,
  toggleDistrict,
  toggleSubgenre,
} from './city-form';
import { LAYOUTS, RECIPE_LABELS, RECIPE_PROBLEMS } from './city-labels';
import { BASE_KEY, DEFAULT_BASE, RecipeSchema, RecipeValues, baseValues, formatRecipe, parseRecipe } from './recipe';
import { FieldProblem } from '../../core/errors/api-error';

const MAX_NAME = 80;

/**
 * New city: name (optional), date range with shortcuts, map type and genres. Query parameters
 * (from, to, layout, recipe, genres, subgenres, name) prefill the form ("duplicate and edit").
 */
@Component({
  selector: 'app-new-city-page',
  imports: [RouterLink, ErrorMessage],
  templateUrl: './new-city.page.html',
  styleUrls: ['./new-city.page.scss', './recipe-panel.scss'],
})
export class NewCityPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly shortcuts = SHORTCUTS;
  protected readonly layouts = LAYOUTS;
  protected readonly maxName = MAX_NAME;
  protected readonly today = isoDay(new Date());

  protected readonly name = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly layout = signal<CityLayoutType>('organic');
  protected readonly selection = signal<GenreSelection>(NO_SELECTION);
  protected readonly search = signal('');
  protected readonly expanded = signal<ReadonlySet<number>>(new Set());

  // Custom map: the parameter string and a form of its keys, kept in sync
  protected readonly recipeLabels = RECIPE_LABELS;
  protected readonly recipeSchema = signal<RecipeSchema | null>(null);
  protected readonly recipeMode = signal<'form' | 'text'>('form');
  protected readonly recipeBase = signal(DEFAULT_BASE);
  protected readonly recipeValues = signal<RecipeValues>({});
  protected readonly recipeText = signal('');
  protected readonly recipeProblems = signal<FieldProblem[]>([]);
  protected readonly recipeKeys = computed(() => this.recipeSchema()?.keys.filter((k) => k.key !== BASE_KEY) ?? []);
  protected readonly bases = computed(() => Object.keys(this.recipeSchema()?.bases ?? {}));

  protected readonly options = signal<GenreOption[] | null>(null);
  protected readonly optionsError = signal<ApiError | null>(null);
  protected readonly submitError = signal<ApiError | null>(null);
  protected readonly submitting = signal(false);

  protected readonly rangeError = computed(() => rangeError(this.from(), this.to(), new Date()));
  protected readonly catalogMissing = computed(() => this.optionsError()?.code === 'CITY_CATALOG_MISSING');
  protected readonly allGenres = computed(() => isEmpty(this.selection()));
  protected readonly visibleOptions = computed(() => {
    const q = this.search().trim().toLowerCase();
    const all = this.options() ?? [];
    return q ? all.filter((o) => o.name.toLowerCase().includes(q) || o.subgenres.some((s) => s.name.toLowerCase().includes(q))) : all;
  });
  protected readonly canSubmit = computed(
    () =>
      !this.submitting() &&
      !this.rangeError() &&
      !this.catalogMissing() &&
      this.name().length <= MAX_NAME &&
      (this.layout() !== 'custom' || (!!this.recipeSchema() && this.recipeProblems().length === 0)),
  );

  protected readonly districtState = districtState;
  protected readonly subgenreChecked = subgenreChecked;

  private optionsRequest?: Subscription;

  constructor() {
    const q = inject(ActivatedRoute).snapshot.queryParamMap;
    this.name.set(q.get('name') ?? '');
    this.from.set(q.get('from') ? isoDay(new Date(q.get('from')!)) : '');
    this.to.set(formTo(q.get('to')));
    const layout = q.get('layout');
    if (layout === 'grid' || layout === 'custom') this.layout.set(layout);
    const recipe = q.get('recipe');
    const ids = (key: string) => (q.get(key) ?? '').split(',').filter(Boolean).map(Number);
    this.selection.set(selectionOf(ids('genres'), ids('subgenres')));
    this.loadOptions();
    this.loadSchema(recipe);
  }

  protected setLayout(layout: CityLayoutType): void {
    this.layout.set(layout);
    this.submitError.set(null);
  }

  // --- Custom map ---------------------------------------------------------------

  /** Form value changed: the string follows (only the keys that differ from the base). */
  protected setRecipeValue(key: string, value: string): void {
    const values = { ...this.recipeValues() };
    if (value === '') delete values[key];
    else values[key] = value;
    this.recipeValues.set(values);
    this.writeText();
  }

  /** String typed or pasted: the form follows when it is valid. */
  protected setRecipeText(text: string): void {
    const schema = this.recipeSchema();
    this.recipeText.set(text);
    if (!schema) return;
    const parsed = parseRecipe(text, schema);
    this.recipeProblems.set(parsed.problems);
    if (parsed.problems.length === 0) {
      this.recipeBase.set(parsed.base);
      this.recipeValues.set(parsed.values);
    }
  }

  /** "Start from organic / grid": the preset values. */
  protected startFrom(base: string): void {
    const schema = this.recipeSchema();
    if (!schema) return;
    this.recipeBase.set(base);
    this.recipeValues.set(baseValues(schema, base));
    this.writeText();
  }

  protected baseLabel(base: string): string {
    return RECIPE_LABELS['base'].values?.[base] ?? base;
  }

  protected randomSeed(): void {
    this.setRecipeValue('seed', String(Math.floor(Math.random() * 1_000_000)));
  }

  protected copyRecipe(): void {
    navigator.clipboard?.writeText(this.recipeText());
  }

  /** Problem of a key (local check or backend "problems"), as a message. */
  protected recipeProblem(key: string): string | null {
    const p = this.recipeProblems().find((x) => x.key === key);
    return p ? (RECIPE_PROBLEMS[p.code] ?? p.code) : null;
  }

  protected problemText(p: FieldProblem): string {
    const label = p.key ? (RECIPE_LABELS[p.key]?.label ?? p.key) : 'Parametri';
    return `${label}: ${RECIPE_PROBLEMS[p.code] ?? p.code}`;
  }

  private writeText(): void {
    const schema = this.recipeSchema();
    if (!schema) return;
    const text = formatRecipe(this.recipeBase(), this.recipeValues(), schema);
    this.recipeText.set(text);
    this.recipeProblems.set(parseRecipe(text, schema).problems);
  }

  private loadSchema(initial: string | null): void {
    this.api
      .recipeSchema()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (schema) => {
          this.recipeSchema.set(schema);
          if (initial) this.setRecipeText(initial);
          else this.startFrom(DEFAULT_BASE);
        },
        // the custom map stays unavailable; organic and grid still work
        error: () => this.recipeSchema.set(null),
      });
  }

  protected setShortcut(value: RangeShortcut): void {
    const range = shortcutRange(value, new Date());
    this.from.set(range.from);
    this.to.set(range.to);
    this.loadOptions();
  }

  protected setDate(which: 'from' | 'to', value: string): void {
    (which === 'from' ? this.from : this.to).set(value);
    if (!this.rangeError()) this.loadOptions();
  }

  protected toggleDistrict(option: GenreOption): void {
    this.selection.set(toggleDistrict(option, this.selection()));
  }

  protected toggleSubgenre(option: GenreOption, subgenreId: number): void {
    this.selection.set(toggleSubgenre(option, subgenreId, this.selection()));
  }

  protected toggleExpanded(genreId: number): void {
    const next = new Set(this.expanded());
    if (next.has(genreId)) next.delete(genreId);
    else next.add(genreId);
    this.expanded.set(next);
  }

  protected selectAll(): void {
    this.selection.set(NO_SELECTION);
  }

  /** The n most listened districts. */
  protected selectTop(n: number): void {
    this.selection.set(selectionOf((this.options() ?? []).slice(0, n).map((o) => o.genreId), []));
  }

  protected submit(): void {
    if (!this.canSubmit()) return;
    const range = apiRange(this.from(), this.to());
    const s = this.selection();
    this.submitting.set(true);
    this.submitError.set(null);
    this.api
      .createCity({
        name: this.name().trim() || null,
        from: range.from,
        to: range.to,
        layout: this.layout(),
        recipe: this.layout() === 'custom' ? this.recipeText() : null,
        genres: [...s.genres],
        subgenres: [...s.subgenres],
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (city) => this.router.navigate(['/cities', city.cityId]),
        error: (e: ApiError) => {
          this.submitting.set(false);
          this.submitError.set(e);
          if (e.code === 'CITY_INVALID_RECIPE') this.recipeProblems.set(e.problems);
        },
      });
  }

  protected loadOptions(): void {
    this.optionsRequest?.unsubscribe();
    const range = apiRange(this.from(), this.to());
    this.optionsError.set(null);
    this.optionsRequest = this.api
      .genreOptions(range.from, range.to)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (options) => this.options.set(options),
        error: (e: ApiError) => {
          this.options.set(null);
          this.optionsError.set(e);
        },
      });
  }
}
