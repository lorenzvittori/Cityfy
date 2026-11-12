import { RecipeSchema, baseValues, formatRecipe, parseRecipe } from './recipe';

// Same keys, ranges and presets as LayoutRecipeParseSubService.Schema()
const schema: RecipeSchema = {
  maxLength: 300,
  keys: [
    { key: 'base', type: 'choice', values: ['organic', 'grid'], min: null, max: null, step: null, optional: true },
    { key: 'shape', type: 'choice', values: ['circle', 'rectangle', 'square', 'elongated'], min: null, max: null, step: null, optional: false },
    { key: 'regularity', type: 'number', values: null, min: 0, max: 1, step: 0.05, optional: false },
    { key: 'blocks', type: 'number', values: null, min: 0.5, max: 2, step: 0.1, optional: false },
    { key: 'streets', type: 'number', values: null, min: 2, max: 6, step: 0.5, optional: false },
    { key: 'avenues', type: 'number', values: null, min: 4, max: 10, step: 0.5, optional: false },
    { key: 'plazas', type: 'choice', values: ['on', 'off'], min: null, max: null, step: null, optional: false },
    { key: 'heights', type: 'choice', values: ['log', 'linear'], min: null, max: null, step: null, optional: false },
    { key: 'lamps', type: 'number', values: null, min: 0, max: 2, step: 0.1, optional: false },
    { key: 'seed', type: 'integer', values: null, min: 0, max: 999999, step: 1, optional: true },
  ],
  bases: {
    organic: 'shape=circle; regularity=0; blocks=1; streets=3; avenues=6; plazas=on; heights=log; lamps=1',
    grid: 'shape=rectangle; regularity=1; blocks=1; streets=3; avenues=6; plazas=off; heights=log; lamps=1',
  },
};

describe('map parameter string', () => {
  it('fills the missing keys from the base', () => {
    const parsed = parseRecipe(' BASE=grid ; blocks=0.60;plazas=on ', schema);

    expect(parsed.problems).toEqual([]);
    expect(parsed.base).toBe('grid');
    expect(parsed.values).toEqual({ ...baseValues(schema, 'grid'), blocks: '0.6', plazas: 'on' });
  });

  it('writes only the keys that differ from the base, and reads them back', () => {
    const values = { ...baseValues(schema, 'grid'), blocks: '0.6', seed: '7' };
    const text = formatRecipe('grid', values, schema);

    expect(text).toBe('base=grid; blocks=0.6; seed=7');
    expect(parseRecipe(text, schema).values).toEqual(values);
    expect(formatRecipe('organic', baseValues(schema, 'organic'), schema)).toBe('base=organic');
  });

  it('reports every problem with its key', () => {
    const { problems } = parseRecipe('regularity=3; colour=red; plazas=on; plazas=off; shape=triangle; nonsense; seed=1.5', schema);

    expect(problems).toEqual(
      expect.arrayContaining([
        { key: 'colour', code: 'UNKNOWN_KEY' },
        { key: 'plazas', code: 'DUPLICATE_KEY' },
        { key: 'nonsense', code: 'SYNTAX' },
        { key: 'regularity', code: 'OUT_OF_RANGE' },
        { key: 'shape', code: 'INVALID_VALUE' },
        { key: 'seed', code: 'INVALID_VALUE' },
      ]),
    );
    expect(parseRecipe('  ', schema).problems).toEqual([{ key: '', code: 'MISSING' }]);
  });
});
