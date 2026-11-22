// @vitest-environment node
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, describe, expect, it } from 'vitest';
import { sourceFiles } from './source-files';

describe('the files a test reads the sources from', () => {
  let folder: string | undefined;
  afterEach(() => {
    if (folder) rmSync(folder, { recursive: true, force: true });
    folder = undefined;
  });

  it('are every file under the folder, as paths from it, without what lies in a folder named with a dot', () => {
    folder = mkdtempSync(join(tmpdir(), 'jfb-source-files-'));
    mkdirSync(join(folder, 'access'));
    mkdirSync(join(folder, 'designer', 'actions'), { recursive: true });
    mkdirSync(join(folder, '.api-check-1', 'core'), { recursive: true });
    for (const file of [
      'bundle.ts',
      'access/access.screen.ts',
      'designer/actions/card.ts',
      '.api-check-1/core/x.gen.ts',
    ])
      writeFileSync(join(folder, file), '');

    expect(sourceFiles(folder).sort()).toEqual(
      ['access/access.screen.ts', 'bundle.ts', 'designer/actions/card.ts']
        .map((file) => file.split('/').join(sep))
        .sort(),
    );
  });

  it('are none for a folder that is not there, as one that went while it was read', () => {
    expect(sourceFiles(join(tmpdir(), 'jfb-source-files-none', 'gone'))).toEqual([]);
  });

  it("hold this package's own sources", () => {
    const files = sourceFiles(dirname(fileURLToPath(import.meta.url)));
    expect(files).toContain('source-files.ts');
    expect(files).toContain(join('styles', 'tokens.ts'));
    expect(files.filter((file) => file.split(sep).some((part) => part.startsWith('.')))).toEqual([]);
  });
});
