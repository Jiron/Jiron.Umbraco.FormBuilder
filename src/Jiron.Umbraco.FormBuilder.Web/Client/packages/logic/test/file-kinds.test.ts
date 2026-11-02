// The kinds of file (ROADMAP 6.4m, ADR-0152), beside the shared vectors of tests/vectors/file-kinds.json, which the server's
// FileKinds passes too: a mark that lies past what a vector shows at a glance.
import { describe, expect, it } from 'vitest';
import { matchesKind } from '../src/file-kinds.ts';

describe('the kinds of file', () => {
  it('reads a tar by the mark at its place', () => {
    const head = new Uint8Array(300);
    head.set(
      [...'ustar'].map((letter) => letter.charCodeAt(0)),
      257,
    );

    expect(matchesKind('tar', head)).toBe(true);
  });
});
