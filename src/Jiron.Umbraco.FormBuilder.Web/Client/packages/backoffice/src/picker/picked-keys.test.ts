// The stored value of the form picker (ADR-0129) as the elements read and write it.
import { describe, expect, it } from 'vitest';
import formats from '../../../../../../../tests/fixtures/picker/key-formats.json';
import { normalKey, readKeys, writeKeys } from './picked-keys';

const a = '7b0c2f4e-1a2b-4c3d-8e9f-0a1b2c3d4e5f';
const b = '1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9';

describe('the picked keys', () => {
  it('are read in order, each once, in lower case, from keys separated by commas', () => {
    expect(readKeys(`${b}, ${a.toUpperCase()},${b}`)).toEqual([b, a]);
    expect(readKeys(a)).toEqual([a]);
  });

  it('are read in the hyphenated format from every format the server reads, and count once', () => {
    const plain = a.replaceAll('-', '');
    expect(readKeys(plain)).toEqual([a]);
    expect(readKeys(`{${a}}`)).toEqual([a]);
    expect(readKeys(`(${a.toUpperCase()})`)).toEqual([a]);
    expect(readKeys(`${plain}, {${a}}, ${b}, ${a}`)).toEqual([a, b]);
  });

  it('are read from hexadecimal groups in a setting, which holds one key to an entry', () => {
    expect(normalKey('{0x7b0c2f4e,0x1a2b,0x4c3d,{0x8e,0x9f,0x0a,0x1b,0x2c,0x3d,0x4e,0x5f}}')).toBe(a);
    expect(normalKey(' {0x7B0C2F4E, 0x1A2B, 0x4C3D, {0x8E, 0x9F, 0xA, 0x1B, 0x2C, 0x3D, 0x4E, 0x5F}} ')).toBe(a);
    expect(normalKey('{0x1,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}')).toBe('00000001-0002-0003-0405-060708090a0b');
  });

  it.each(formats)('read $text as the server reads it', ({ text, key }) => {
    expect(normalKey(text)).toBe(key ?? text.trim().toLowerCase());
  });

  it('keep text that is no key as it is, trimmed and in lower case', () => {
    expect(normalKey(' Not A Key ')).toBe('not a key');
    expect(normalKey(`{${a.replaceAll('-', '')}}`)).toBe(`{${a.replaceAll('-', '')}}`);
    expect(normalKey(a.slice(1))).toBe(a.slice(1));
    expect(readKeys('gone,a-contact')).toEqual(['gone', 'a-contact']);
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
