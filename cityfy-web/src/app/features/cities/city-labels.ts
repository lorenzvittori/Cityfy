// Labels of the saved cities (pure): the backend sends codes and data, the texts are built here.

import { CityLayoutType, GeneratedCity, GenerationState } from '../../core/api/api.models';

export const LAYOUTS: { value: CityLayoutType; label: string; description: string }[] = [
  { value: 'organic', label: 'Organica', description: 'Quartieri dalla forma irregolare, ognuno con la sua piazza e rotonda' },
  { value: 'grid', label: 'Griglia', description: 'Isolati rettangolari e strade dritte, come Manhattan' },
];

/** Form of the custom map: label and help of every key of the parameter string, labels of the choices. */
export const RECIPE_LABELS: Record<string, { label: string; help: string; values?: Record<string, string> }> = {
  base: { label: 'Parti da', help: 'Valori di partenza dei parametri non indicati', values: { organic: 'Organica', grid: 'Griglia' } },
  shape: {
    label: 'Forma della città',
    help: 'Contorno della città',
    values: { circle: 'Rotonda', rectangle: 'Rettangolare', square: 'Quadrata', elongated: 'Allungata' },
  },
  regularity: { label: 'Regolarità dei quartieri', help: 'Sotto 0,5 quartieri irregolari, da 0,5 quartieri rettangolari a griglia' },
  blocks: { label: 'Dimensione degli isolati', help: 'Più alto = isolati più grandi, meno strade interne' },
  streets: { label: 'Larghezza delle strade', help: 'Strade tra gli isolati' },
  avenues: { label: 'Larghezza dei viali', help: 'Strade principali tra i quartieri' },
  plazas: { label: 'Piazze', help: 'Piazza con rotonda al centro dei quartieri irregolari', values: { on: 'Sì', off: 'No' } },
  heights: {
    label: 'Altezze',
    help: 'Come crescono gli edifici con gli ascolti',
    values: { log: 'Logaritmiche (equilibrate)', linear: 'Lineari (i più ascoltati svettano)' },
  },
  lamps: { label: 'Lampioni', help: 'Densità dei lampioni (0 = nessuno)' },
  seed: { label: 'Variante', help: 'Stessi parametri, disposizione diversa (vuoto = quella standard)' },
};

/** Problems of the parameter string (CITY_INVALID_RECIPE "problems"). */
export const RECIPE_PROBLEMS: Record<string, string> = {
  MISSING: 'Scrivi almeno un parametro.',
  TOO_LONG: 'La stringa è troppo lunga.',
  SYNTAX: 'Formato non valido: usa chiave=valore.',
  UNKNOWN_KEY: 'Parametro sconosciuto.',
  DUPLICATE_KEY: 'Parametro ripetuto.',
  INVALID_VALUE: 'Valore non valido.',
  OUT_OF_RANGE: 'Valore fuori dai limiti.',
};

const LAYOUT_LABELS: Record<string, string> = { organic: 'Organica', grid: 'Griglia', custom: 'Personalizzata' };

const STATUS_LABELS: Record<GenerationState, string> = {
  Pending: 'In coda',
  InProgress: 'In costruzione',
  Done: 'Pronta',
  Failed: 'Non riuscita',
  Cancelled: 'Annullata',
};

export function layoutLabel(layout: string): string {
  return LAYOUT_LABELS[layout] ?? layout;
}

export function statusLabel(status: GenerationState): string {
  return STATUS_LABELS[status] ?? status;
}

export function isActive(status: GenerationState): boolean {
  return status === 'Pending' || status === 'InProgress';
}

const MONTH = new Intl.DateTimeFormat('it-IT', { month: 'short', year: 'numeric', timeZone: 'UTC' });

/** "Tutto il periodo", "dal gen 2024", "fino a dic 2024", "gen 2024 – dic 2024" (to is exclusive). */
export function periodLabel(from: string | null, to: string | null): string {
  const last = to ? new Date(Date.parse(to) - 1) : null; // last instant included
  if (!from && !last) return 'Tutto il periodo';
  if (from && !last) return `dal ${MONTH.format(new Date(from))}`;
  if (!from && last) return `fino a ${MONTH.format(last)}`;
  const a = MONTH.format(new Date(from!));
  const b = MONTH.format(last!);
  return a === b ? a : `${a} – ${b}`;
}

/**
 * "12 quartieri, altri generi uniti" / "8 quartieri, altri generi esclusi" / "Tutti i quartieri".
 * Null max (cities saved before it could be chosen): the default.
 */
export function districtsLabel(max: number | null, extra: string | null, defaultMax: number): string {
  if (max === 0) return 'Tutti i quartieri';
  const n = max ?? defaultMax;
  return `${n} quartier${n === 1 ? 'e' : 'i'}, altri generi ${extra === 'drop' ? 'esclusi' : 'uniti'}`;
}

/** The user's name, else "Rock e Jazz · 2024 · Griglia" from the parameters. */
export function cityTitle(city: Pick<GeneratedCity, 'name' | 'topGenres' | 'from' | 'to' | 'layout'>): string {
  if (city.name) return city.name;
  const genres = city.topGenres.slice(0, 2).map(capitalize);
  const parts = [genres.length ? genres.join(' e ') : 'La tua città', periodLabel(city.from, city.to), layoutLabel(city.layout)];
  return parts.join(' · ');
}

function capitalize(s: string): string {
  return s ? s[0].toLocaleUpperCase('it-IT') + s.slice(1) : s;
}
