import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
import { createUserLock } from './user-lock.ts';

const root = mkdtempSync(join(tmpdir(), 'jfb-user-lock-'));
after(() => rmSync(root, { recursive: true, force: true }));

const quick = { staleAfterMs: 60_000, waitLimitMs: 2_000 };

test('callers run one at a time', async () => {
  const withLock = createUserLock(join(root, 'serial'), quick);
  let running = 0;
  let most = 0;
  const work = () =>
    withLock(async () => {
      running++;
      most = Math.max(most, running);
      await delay(30);
      running--;
    });

  await Promise.all([work(), work(), work()]);

  assert.equal(most, 1);
});

test('a lock whose holder process is gone is taken over at once', async () => {
  const dir = join(root, 'crashed');
  const gone = spawnSync(process.execPath, ['-e', '']).pid;
  mkdirSync(dir);
  writeFileSync(join(dir, 'owner'), String(gone));
  const started = Date.now();

  const result = await createUserLock(dir, quick)(async () => 'ran');

  assert.equal(result, 'ran');
  assert.ok(Date.now() - started < 1_000, 'no wait for the stale time');
});

test('a lock whose holder still runs is waited for', async () => {
  const dir = join(root, 'held');
  mkdirSync(dir);
  writeFileSync(join(dir, 'owner'), String(process.pid));

  await assert.rejects(
    createUserLock(dir, { staleAfterMs: 60_000, waitLimitMs: 300 })(async () => 'ran'),
    /Waited/,
  );
});
