// The tables check's TypeScript half (ADR-0005): the C# suite compares Core against tests/vectors/tables/typescript.json,
// so this test makes sure the file is what this runtime does. When Node's Unicode data changes, it fails until
// `npm run tables` writes the file again, and the C# suite then shows whether .NET agrees.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { tablesFile } from '../tools/repository.ts';
import { readTables, tablesText, unicodeTables } from '../tools/unicode-tables.ts';

describe('the Unicode tables', () => {
  const written = readTables(readFileSync(tablesFile(), 'utf8'));
  const current = unicodeTables();

  it('fold as this runtime folds', () => {
    expect(written.fold).toEqual(current.fold);
  });

  it('clean as this runtime cleans', () => {
    expect(written.clean).toEqual(current.clean);
  });

  it('recompose as this runtime recomposes', () => {
    expect(written.recompose).toEqual(current.recompose);
  });

  it('read back what they write', () => {
    expect(readTables(tablesText(current))).toEqual(current);
  });
});
