import assert from 'node:assert/strict';
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import {
  describeStall,
  HANG_MARKER,
  MARKER_NOT_LAST,
  recordStall,
  stalledDatabase,
  STARTUP_LINES,
  startupWorkDone,
  waitForStartupWork,
} from './host-health.ts';

// The host's log after a run (ADR-0011): a database command that ran into its timeout, or Umbraco's own warning about
// a hung connection, marks the run and leaves the hang marker; quick failures do not. And the start-up wait (ADR-0031):
// it ends on the Testing project's marker whether the Content Delivery API is on or off.

const root = mkdtempSync(join(tmpdir(), 'jfb-host-health-'));
after(() => rmSync(root, { recursive: true, force: true }));
let logs = 0;

/** A host log in a folder of its own, as .local/e2e/host-5520/host.log is. */
function logFile(text: string): string {
  const dir = join(root, `host-${++logs}`);
  mkdirSync(dir, { recursive: true });
  const file = join(dir, 'host.log');
  writeFileSync(file, text);
  return file;
}

const HUNG = [
  '[05:59:54 INF] Registering distributed background jobs',
  "[06:00:24 ERR] Failed executing DbCommand (30,050ms) [Parameters=[@identifier='?' (Size = 19)], CommandType='Text', CommandTimeout='30']",
  'SELECT "u"."Id" FROM "umbracoOpenIddictApplications" AS "u"',
  '[06:00:54 ERR] Cache instruction sync did not complete within 00:01:00 and may be stalled on a hung database connection.',
  "[06:00:55 ERR] Failed executing DbCommand (30,145ms) [Parameters=[@identifier='?' (Size = 19)], CommandType='Text', CommandTimeout='30']",
].join('\r\n');

// The start-up work as two real runs of the test host logged it (3.4c): with the Delivery API off, Umbraco adds its
// "not enabled" lines; with it on, it logs nothing for the rebuild, and only the marker shows that it is done.
const DELIVERY_API_OFF = [
  '[18:26:43 INF] Registered distributed background job OpenIddictCleanupJob, running every 01:00:00',
  '[18:26:43 INF] Completed registering distributed background jobs',
  '[18:26:45 INF] The Delivery API is not enabled, no indexing will performed for the Delivery API content index.',
  '[18:26:45 INF] The Delivery API is not enabled, no indexing will performed for the Delivery API content index.',
  '[18:26:45 INF] Jiron Form Builder test host: indexes rebuilt (InternalIndex, ExternalIndex, MembersIndex, DeliveryApiContentIndex)',
].join('\n');

const DELIVERY_API_ON = [
  '[18:31:21 INF] Registered distributed background job OpenIddictCleanupJob, running every 01:00:00',
  '[18:31:21 INF] Completed registering distributed background jobs',
  '[18:31:22 INF] Jiron Form Builder test host: indexes rebuilt (DeliveryApiContentIndex, MembersIndex, ExternalIndex, InternalIndex)',
].join('\n');

const quick = { settleMs: 0, pollMs: 10, timeoutMs: 300 };

test('finds the commands that timed out and when the first one was logged', () => {
  assert.deepEqual(stalledDatabase(HUNG), { timedOut: 2, firstAt: '06:00:24' });
});

test("counts Umbraco's own warning about a hung connection", () => {
  const log =
    '[06:01:09 ERR] Touching the server registration did not complete within 00:01:00 and may be stalled on a hung database connection.';
  assert.deepEqual(stalledDatabase(log), { timedOut: 0, firstAt: '06:01:09' });
});

test('reads the elapsed time with either thousands separator', () => {
  const log =
    "[07:10:02 ERR] Failed executing DbCommand (30.004ms) [Parameters=[], CommandType='Text', CommandTimeout='30']";
  assert.deepEqual(stalledDatabase(log), { timedOut: 1, firstAt: '07:10:02' });
});

test('passes a log whose failed commands failed quickly', () => {
  const log = [
    "[05:59:32 ERR] Failed executing DbCommand (12ms) [Parameters=[], CommandType='Text', CommandTimeout='30']",
    "[05:59:33 ERR] Failed executing DbCommand (2,104ms) [Parameters=[], CommandType='Text', CommandTimeout='30']",
    "[05:59:34 ERR] Failed executing DbCommand (29,999ms) [Parameters=[], CommandType='Text', CommandTimeout='30']",
  ].join('\n');
  assert.equal(stalledDatabase(log), null);
});

test('names the time, the count, the reason and the log', () => {
  const line = describeStall({ timedOut: 2, firstAt: '06:00:24' }, 'host.log');
  assert.match(line, /from 06:00:24 \(2 command/);
  assert.match(line, /ADR-0011/);
  assert.match(line, /host\.log$/);
});

test('a hung database writes the hang marker next to the log', () => {
  const file = logFile([DELIVERY_API_OFF, HUNG].join('\n'));

  const stall = recordStall(file);

  assert.deepEqual(stall, { timedOut: 2, firstAt: '06:00:24' });
  assert.equal(readFileSync(join(file, '..', HANG_MARKER), 'utf8'), 'from 06:00:24, 2 command(s) timed out\n');
});

test('a healthy log, or none, writes no hang marker', () => {
  const file = logFile(DELIVERY_API_ON);

  assert.equal(recordStall(file), null);
  assert.equal(recordStall(join(root, 'no-such-host', 'host.log')), null);
  assert.equal(existsSync(join(file, '..', HANG_MARKER)), false);
});

test('the start-up wait ends with the Delivery API off', async () => {
  assert.deepEqual(await waitForStartupWork(logFile(DELIVERY_API_OFF), quick), { state: 'done' });
});

test('the start-up wait ends with the Delivery API on', async () => {
  assert.deepEqual(await waitForStartupWork(logFile(DELIVERY_API_ON), quick), { state: 'done' });
});

test("Umbraco's Delivery API line alone does not end the wait", async () => {
  const log = DELIVERY_API_OFF.split('\n')
    .filter((line) => !line.includes('indexes rebuilt'))
    .join('\n');

  assert.equal(startupWorkDone(log), false);
  assert.deepEqual(await waitForStartupWork(logFile(log), quick), { state: 'timeout', missing: [STARTUP_LINES[1]] });
});

test('the start-up wait reads lines the host writes while it waits', async () => {
  const [jobsLine, ...rest] = DELIVERY_API_ON.split('\n').slice(1);
  const file = logFile(`${jobsLine}\n`);
  setTimeout(() => appendFileSync(file, `${rest.join('\n')}\n`), 50);

  assert.deepEqual(await waitForStartupWork(file, { ...quick, timeoutMs: 5_000 }), { state: 'done' });
});

test('the start-up wait ends at once on a hung database or a misplaced marker', async () => {
  const started = Date.now();

  assert.deepEqual(await waitForStartupWork(logFile(HUNG), { ...quick, timeoutMs: 60_000 }), {
    state: 'stalled',
    stall: { timedOut: 2, firstAt: '06:00:24' },
  });
  const misplaced = `[18:31:22 ERR] ${MARKER_NOT_LAST}; the browser tests' start-up wait cannot rely on it`;
  assert.deepEqual(await waitForStartupWork(logFile(misplaced), { ...quick, timeoutMs: 60_000 }), {
    state: 'misordered',
  });
  assert.ok(Date.now() - started < 5_000, 'no wait for the time-out');
});
