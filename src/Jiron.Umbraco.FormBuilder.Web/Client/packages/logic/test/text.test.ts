// What no vector can hold (ADR-0005): .NET's JSON reader refuses an escaped lone surrogate, so the shared files have
// none. This runtime's test covers them, as TextSemanticsTests does in C#.
import { describe, expect, it } from 'vitest';
import { clean } from '../src/text.ts';

const REPLACEMENT = String.fromCharCode(0xfffd);

describe('clean', () => {
  it('replaces each lone surrogate and keeps what is around it', () => {
    const pair = String.fromCharCode(0xd83d, 0xde00);
    const text = 'a' + pair + String.fromCharCode(0xd800) + 'b' + String.fromCharCode(0xdc00);

    expect(clean(text)).toBe('a' + pair + REPLACEMENT + 'b' + REPLACEMENT);
  });

  it('replaces a low surrogate before its high one', () => {
    expect(clean(String.fromCharCode(0xdc00, 0xd800))).toBe(REPLACEMENT + REPLACEMENT);
  });
});
