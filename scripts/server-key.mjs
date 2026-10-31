#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Makes a server key for a headless server (ADR-0126): 256 random bits as base64url, and the SHA-256 of the key for the
 * site's configuration, `JironFormBuilder:PublicApi:ServerKeys`. The key goes only to the headless server's own secret
 * store, which sends it as `Authorization: Jfb-Key <key>`; the site keeps only the name and the hash.
 *
 *   node scripts/server-key.mjs <name>
 *
 * It writes the key to the console once, for the person setting the server up, and never to a file.
 */
import { createHash, randomBytes } from 'node:crypto';
import { pathToFileURL } from 'node:url';

const NAME = /^[A-Za-z0-9_-]{1,64}$/;

/** A new key: 256 random bits, base64url. */
export function newKey() {
  return randomBytes(32).toString('base64url');
}

/** The hash the configuration holds: the SHA-256 of the key's UTF-8 bytes, 64 lower-case hexadecimal digits. */
export function hashOf(key) {
  return createHash('sha256').update(key, 'utf8').digest('hex');
}

/** The configuration entry for a key's name and hash. */
export function entryOf(name, hash) {
  if (!NAME.test(name)) throw new Error('server-key: a name has 1 to 64 letters, digits, hyphens and underscores');
  return { Name: name, Sha256: hash };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const name = process.argv[2] ?? '';
  try {
    const key = newKey();
    const entry = entryOf(name, hashOf(key));
    console.log('Add this entry to JironFormBuilder:PublicApi:ServerKeys in the site\'s configuration:');
    console.log(JSON.stringify(entry, null, 2));
    console.log('');
    console.log('Give this key to the headless server only, which sends it as "Authorization: Jfb-Key <key>":');
    console.log(key);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(2);
  }
}
