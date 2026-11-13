// Schema of the parameter string of a custom map (ServiceDefault/City/RecipeContracts.cs).

export interface RecipeKey {
  key: string;
  type: 'choice' | 'number' | 'integer';
  values: string[] | null;
  min: number | null;
  max: number | null;
  step: number | null;
  /** May be missing from the canonical string (base, seed). */
  optional: boolean;
}

export interface RecipeSchema {
  maxLength: number;
  /** Canonical order; the first one is "base". */
  keys: RecipeKey[];
  /** Canonical string of every preset. */
  bases: Record<string, string>;
}
