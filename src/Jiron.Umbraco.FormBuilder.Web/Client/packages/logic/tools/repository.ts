// Where the repository's shared files are, for the vector runner and the tables tool: found by walking up from this
// file to the folder that holds the solution, so nothing is copied and no path is written down twice.
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const SOLUTION = 'Jiron.Umbraco.FormBuilder.slnx';

/** The repository root: the nearest folder above this file that holds the solution. */
export function repositoryRoot(): string {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, SOLUTION))) {
    const parent = dirname(folder);
    if (parent === folder) throw new Error(`No folder above ${fileURLToPath(import.meta.url)} holds ${SOLUTION}`);
    folder = parent;
  }
  return folder;
}

/** The folder of the shared vector files (ADR-0005). */
export const vectorsFolder = (): string => join(repositoryRoot(), 'tests', 'vectors');

/** The tables the TypeScript side writes for the C# tables check. */
export const tablesFile = (): string => join(vectorsFolder(), 'tables', 'typescript.json');
