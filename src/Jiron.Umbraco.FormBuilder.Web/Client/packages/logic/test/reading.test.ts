// The reading stage's table of the built-in types (ROADMAP 6.4a, ADR-0109): the same types as the served field types
// (docs/api/field-types.json, Core's declarations as the field types endpoint gives them), each declared alike. The
// vectors of tests/vectors/reading.json hold the readings themselves.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { BUILT_IN_TYPES, readField } from '../src/index.ts';
import { repositoryRoot } from '../tools/repository.ts';

interface ServedType {
  readonly alias: string;
  readonly holdsValue: boolean;
  readonly storage: string;
  readonly ruleKind: string | null;
  readonly multipleSetting: boolean;
  readonly messages: readonly string[];
}

const served = (
  JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'field-types.json'), 'utf8')) as {
    readonly types: readonly ServedType[];
  }
).types;

describe('the built-in types the reading stage knows', () => {
  it('are the served types', () => {
    expect(Object.keys(BUILT_IN_TYPES).sort()).toEqual(served.map((type) => type.alias).sort());
  });

  it('are declared as Core declares them', () => {
    for (const type of served) {
      expect(BUILT_IN_TYPES[type.alias], type.alias).toEqual({
        holdsValue: type.holdsValue,
        storage: type.storage,
        kind: type.ruleKind,
        multipleSetting: type.multipleSetting,
        maxLength: type.messages.includes('maxLength'),
      });
    }
  });

  it("leave a site's own type to the server, and never take a name of an object's own", () => {
    expect(readField({ type: 'colour' }, { values: ['red'] })).toBeNull();
    expect(readField({ type: 'constructor' }, { values: ['x'] })).toBeNull();
    expect(readField({ type: 'toString' }, { values: ['x'] })).toBeNull();
  });
});
