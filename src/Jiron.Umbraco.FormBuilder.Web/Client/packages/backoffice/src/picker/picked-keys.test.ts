// The stored value of the form picker (ADR-0129) as the elements read and write it.
import { describe, expect, it } from 'vitest';
import { readKeys, writeKeys } from './picked-keys';

const a = '7b0c2f4e-1a2b-4c3d-8e9f-0a1b2c3d4e5f';
const b = '1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9';

describe('the picked keys', () => {
  it('are read in order, each once, in lower case, from keys separated by commas', () => {
    expect(readKeys(`${b}, ${a.toUpperCase()},${b}`)).toEqual([b, a]);
    expect(readKeys(a)).toEqual([a]);
  });

  it('are none for no value, an empty one or one that is not text', () => {
    expect(readKeys(undefined)).toEqual([]);
    expect(readKeys('')).toEqual([]);
    expect(readKeys(' , ')).toEqual([]);
    expect(readKeys(42)).toEqual([]);
  });

  it('are written as the server reads them, and none as no value at all', () => {
    expect(writeKeys([a, b])).toBe(`${a},${b}`);
    expect(writeKeys([])).toBeUndefined();
  });
});
