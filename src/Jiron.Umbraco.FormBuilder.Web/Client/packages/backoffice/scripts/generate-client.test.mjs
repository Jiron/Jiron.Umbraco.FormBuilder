// @vitest-environment node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
import { readdirSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { clientFolder, generateClient, packageFolder } from './generate-client.mjs';

// A sibling of src/api, so the generated import of src/hey-api.ts reads the same.
const check = join(packageFolder, 'src', `.api-check-${process.pid}`);

/** @param {string} folder */
function files(folder) {
  return readdirSync(folder, { recursive: true })
    .map(String)
    .filter((file) => file.endsWith('.ts'))
    .map((file) => file.replaceAll('\\', '/'))
    .sort();
}

describe('the generated client', () => {
  afterEach(() => rmSync(check, { recursive: true, force: true }));

  it('is the one the committed snapshot docs/api/jfb.json makes: run npm run generate-client after a change', async () => {
    await generateClient(check, { silent: true });

    expect(files(clientFolder)).toEqual(files(check));
    for (const file of files(check)) {
      expect(readFileSync(join(clientFolder, file), 'utf8'), file).toBe(readFileSync(join(check, file), 'utf8'));
    }
  }, 60_000);

  it('calls the management API through the backoffice client and one function per operation', () => {
    const sdk = readFileSync(join(clientFolder, 'sdk.gen.ts'), 'utf8');
    const client = readFileSync(join(clientFolder, 'client.gen.ts'), 'utf8');

    expect(client).toContain("import { createClientConfig } from '../hey-api.ts';");
    expect(sdk).toMatch(/export const folderCreate = /);
    expect(sdk).toContain("url: '/umbraco/jfb/management/api/v1/tree/root'");
  });
});
