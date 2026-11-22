// For the tests that read the sources (Vitest, Node's file system): every file under a folder, as paths from it with
// the platform's separator, as `readdirSync(folder, { recursive: true })` lists them. A folder whose name starts with a
// dot is never entered: it is another test's scratch (the API check's `.api-check-<pid>`, a sibling of `api`), which
// comes and goes while the tests run side by side, and a recursive read that meets it as it goes throws. A folder or
// file that goes between being listed and being read is left out for the same reason.
import { readdirSync } from 'node:fs';
import { join } from 'node:path';

/** Every file under `folder`, as paths from it; folders named with a leading dot are left out. */
export function sourceFiles(folder: string): string[] {
  const found: string[] = [];
  const walk = (from: string): void => {
    let entries;
    try {
      entries = readdirSync(join(folder, from), { withFileTypes: true });
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return;
      throw error;
    }
    for (const entry of entries) {
      if (entry.name.startsWith('.')) continue;
      const path = from === '' ? entry.name : join(from, entry.name);
      if (entry.isDirectory()) walk(path);
      else found.push(path);
    }
  };
  walk('');
  return found;
}
