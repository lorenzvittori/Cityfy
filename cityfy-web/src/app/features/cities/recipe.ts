// Parameter string of a custom map ("key=value; key=value"), pure. The rules come from the
// backend schema (GET /api/v1/cities/recipe/schema): the same checks as the backend parser, for an
// immediate feedback; the backend checks again (CITY_INVALID_RECIPE with "problems").

import { RecipeKey, RecipeSchema } from '../../core/api/recipe.models';
import { FieldProblem } from '../../core/errors/api-error';

export type { RecipeKey, RecipeSchema };

/** key -> value (normalized), base excluded. A missing optional key (seed) = default. */
export type RecipeValues = Partial<Record<string, string>>;

export const BASE_KEY = 'base';
export const DEFAULT_BASE = 'organic';

export interface ParsedRecipe {
  base: string;
  values: RecipeValues;
  problems: FieldProblem[];
}

/** Values of a preset. */
export function baseValues(schema: RecipeSchema, base: string): RecipeValues {
  return parseRecipe(schema.bases[base] ?? schema.bases[DEFAULT_BASE], schema).values;
}

export function parseRecipe(text: string, schema: RecipeSchema): ParsedRecipe {
  const problems: FieldProblem[] = [];
  if (!text.trim()) return { base: DEFAULT_BASE, values: {}, problems: [{ key: '', code: 'MISSING' }] };
  if (text.length > schema.maxLength) return { base: DEFAULT_BASE, values: {}, problems: [{ key: '', code: 'TOO_LONG' }] };

  const keys = new Map(schema.keys.map((k) => [k.key, k]));
  const raw = new Map<string, string>();
  for (const segment of text.split(';')) {
    const s = segment.trim();
    if (!s) continue;
    const eq = s.indexOf('=');
    if (eq <= 0) {
      problems.push({ key: s.toLowerCase(), code: 'SYNTAX' });
      continue;
    }
    const key = s.slice(0, eq).trim().toLowerCase();
    const value = s.slice(eq + 1).trim().toLowerCase();
    if (!keys.has(key)) problems.push({ key, code: 'UNKNOWN_KEY' });
    else if (raw.has(key)) problems.push({ key, code: 'DUPLICATE_KEY' });
    else raw.set(key, value);
  }

  let base = DEFAULT_BASE;
  const rawBase = raw.get(BASE_KEY);
  if (rawBase !== undefined) {
    if (rawBase in schema.bases) base = rawBase;
    else problems.push({ key: BASE_KEY, code: 'INVALID_VALUE' });
  }
  const values: RecipeValues = { ...presetValues(schema, base) }; // missing keys come from the base

  for (const [key, value] of raw) {
    if (key === BASE_KEY) continue;
    const spec = keys.get(key)!;
    const checked = checkValue(spec, value);
    if (typeof checked === 'string') values[key] = checked;
    else problems.push({ key, code: checked.code });
  }
  return { base, values, problems };
}

/** Normalized value, or the problem. */
export function checkValue(spec: RecipeKey, value: string): string | { code: string } {
  if (spec.type === 'choice') return spec.values?.includes(value) ? value : { code: 'INVALID_VALUE' };
  if (!/^-?\d+(\.\d+)?$/.test(value)) return { code: 'INVALID_VALUE' };
  const n = Number(value);
  if (spec.type === 'integer' && !Number.isInteger(n)) return { code: 'INVALID_VALUE' };
  if ((spec.min !== null && n < spec.min) || (spec.max !== null && n > spec.max)) return { code: 'OUT_OF_RANGE' };
  return String(spec.type === 'integer' ? n : Math.round(n * 100) / 100);
}

/** "base=grid; blocks=0.6": the base and only the keys that differ from it (short and readable). */
export function formatRecipe(base: string, values: RecipeValues, schema: RecipeSchema): string {
  const preset = presetValues(schema, base);
  const parts = [`${BASE_KEY}=${base}`];
  for (const k of schema.keys) {
    if (k.key === BASE_KEY) continue;
    const v = values[k.key];
    if (v !== undefined && v !== '' && v !== preset[k.key]) parts.push(`${k.key}=${v}`);
  }
  return parts.join('; ');
}

// Values of a preset string (canonical: always valid), parsed without recursion
function presetValues(schema: RecipeSchema, base: string): RecipeValues {
  const values: RecipeValues = {};
  for (const segment of (schema.bases[base] ?? '').split(';')) {
    const [key, value] = segment.split('=').map((s) => s.trim());
    if (key && value !== undefined) values[key] = value;
  }
  return values;
}
