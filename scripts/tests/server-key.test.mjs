// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// A server key for a headless server (ADR-0126): 256 random bits, the hash the site's configuration holds, a name the
// site accepts.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { test } from 'node:test';
import { entryOf, hashOf, newKey } from '../server-key.mjs';

test('a key is 43 base64url characters of 256 random bits, a new one each time', () => {
  const key = newKey();
  assert.match(key, /^[A-Za-z0-9_-]{43}$/);
  assert.notEqual(newKey(), key);
});

test("the hash is the SHA-256 of the key's UTF-8 bytes as 64 hexadecimal digits", () => {
  const key = 'JFB_FAKE_server_key_for_the_test_0123456789';
  assert.equal(hashOf(key), createHash('sha256').update(Buffer.from(key, 'utf8')).digest('hex'));
  assert.match(hashOf(key), /^[0-9a-f]{64}$/);
});

test('the entry holds the name and the hash, never the key, and refuses a name the site would refuse', () => {
  const key = newKey();
  const entry = entryOf('shop-1', hashOf(key));
  assert.deepEqual(Object.keys(entry), ['Name', 'Sha256']);
  assert.ok(!JSON.stringify(entry).includes(key));
  assert.throws(() => entryOf('a shop', hashOf(key)), /1 to 64 letters/);
  assert.throws(() => entryOf('', hashOf(key)), /1 to 64 letters/);
});
