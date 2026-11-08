import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

// The browser specs share one host per project and run side by side, so none of them moves the host's clock: a moved
// clock shifts every other spec's times while it runs. The Phase 11 gate found it, when one spec's thirty seconds ahead
// gave another's entry an undo window its browser counted as forty seconds long.

const specs = join(import.meta.dirname, '..', 'specs');

/** Every spec file under `specs`, with its path from there. */
function specFiles(dir: string, prefix = ''): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
    entry.isDirectory()
      ? specFiles(join(dir, entry.name), `${prefix}${entry.name}/`)
      : entry.name.endsWith('.spec.ts')
        ? [`${prefix}${entry.name}`]
        : [],
  );
}

test('no browser spec moves the shared host clock', () => {
  const files = specFiles(specs);
  assert.ok(files.length > 50, 'the specs are found');
  const moving = files.filter((file) => readFileSync(join(specs, file), 'utf8').includes('/jfb-testing/clock'));
  assert.deepEqual(moving, []);
});
