// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The install test's helpers (ROADMAP 16.5, ADR-0240): the migration plan read from its source, so the expected state is
// never written down twice, and the schema read from SQLite in the form of the integration tests' SchemaInspector, which
// UpgradeFixtureTests compares with what today's first steps build. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { test } from 'node:test';
import { normaliseFilter, schemaOf, SEEDED } from '../install-test/jfb-db.mjs';
import { CONSTANTS, finalState, PLAN, planName, steps } from '../install-test/migration-plan.mjs';
import { startChunkOf } from '../install-test/start-chunk.mjs';

const PLAN_TEXT = `
    public const string StateCreateSchema = "jfb-001-create-schema";
    public const string StateSecond = "jfb-002-second";
    internal static readonly IReadOnlyList<(string State, Type Step)> Steps =
    [
        (StateCreateSchema, typeof(CreateSchemaV1)),
        (StateSecond, typeof(SecondV1)),
    ];
`;

test('the plan reader takes each step with its state from the Steps list, the last one as the final state', () => {
  assert.deepEqual(steps(PLAN_TEXT), [
    { constant: 'StateCreateSchema', step: 'CreateSchemaV1', state: 'jfb-001-create-schema' },
    { constant: 'StateSecond', step: 'SecondV1', state: 'jfb-002-second' },
  ]);
  assert.equal(finalState(PLAN_TEXT), 'jfb-002-second');
});

test('the plan reader refuses a plan without steps and a step whose state has no value', () => {
  assert.throws(() => steps('public const string StateA = "a";'), /no Steps list/);
  assert.throws(() => steps('Steps = [ (StateMissing, typeof(Step)), ];'), /StateMissing has no value/);
});

test("the repository's own plan: the last constant of its Steps list, and the plan's stored name", () => {
  const source = readFileSync(PLAN, 'utf8');
  const constants = [...source.matchAll(/\((State\w+), typeof\(/g)].map((match) => match[1]);
  const last = source.match(new RegExp(`const string ${constants.at(-1)} = "([^"]+)"`))?.[1];
  assert.equal(finalState(), last);
  assert.equal(planName(), readFileSync(CONSTANTS, 'utf8').match(/MigrationPlanName = "([^"]+)"/)?.[1]);
});

test("a filtered index's condition reads as SchemaInspector writes it", () => {
  assert.equal(normaliseFilter('CREATE UNIQUE INDEX "IX_a" ON "jfbA" ("b") WHERE "b" IS NOT NULL'), 'where b is not null');
  assert.equal(normaliseFilter('CREATE INDEX "IX_a" ON "jfbA" ("b") WHERE ("b" IS NOT NULL)'), 'where b is not null');
  assert.equal(normaliseFilter('CREATE INDEX "IX_a" ON "jfbA" ("b") WHERE "b" > 0 AND "c" = 1'), 'where b > 0 and c = 1');
  assert.equal(normaliseFilter('CREATE INDEX "IX_a" ON "jfbA" ("b")'), null);
  assert.equal(normaliseFilter(null), null);
});

test("the schema reader lists Jiron Form Builder's tables' columns in order, nullable ones marked, and their indexes", () => {
  const db = new DatabaseSync(':memory:');
  db.exec(`
    CREATE TABLE jfbThing (id INTEGER NOT NULL PRIMARY KEY, name TEXT NOT NULL, note TEXT NULL, keyText TEXT NULL);
    CREATE UNIQUE INDEX IX_jfbThing_name ON jfbThing (name);
    CREATE INDEX IX_jfbThing_keyText_name ON jfbThing (keyText, name) WHERE keyText IS NOT NULL;
    CREATE TABLE umbracoOther (id INTEGER NOT NULL);
  `);
  assert.deepEqual(schemaOf(db), {
    columns: { jfbThing: 'id, name, note?, keyText?' },
    indexes: ['jfbThing.IX_jfbThing_keyText_name(keyText,name) where keyText is not null', 'jfbThing.IX_jfbThing_name(name) unique'],
  });
});

test("the seeded tables come parents first: a form before its versions and fields, an entry before its files and values", () => {
  const before = (a, b) => assert.ok(SEEDED.indexOf(a) < SEEDED.indexOf(b), `${a} before ${b}`);
  before('jfbFolder', 'jfbForm');
  before('jfbForm', 'jfbFormVersion');
  before('jfbForm', 'jfbFormField');
  before('jfbFormVersion', 'jfbEntry');
  before('jfbEntry', 'jfbEntryFile');
  before('jfbEntryFile', 'jfbEntryValue');
});

// The functions of install-test.ps1 that know the run's own site by its record, cut from the script as they stand.
function recordFunctions() {
  const script = readFileSync(new URL('../install-test.ps1', import.meta.url), 'utf8').replace(/\r\n/g, '\n');
  const start = script.indexOf('function Get-StartTime');
  const end = script.indexOf('function Stop-RecordedSite');
  assert.ok(start >= 0 && end > start, 'install-test.ps1 has Get-StartTime and Get-RecordedSite before Stop-RecordedSite');
  return script.slice(start, end);
}

test("the install test knows its own site by a record of digits, which no edition of PowerShell reads as a date (16.5's review)", (t) => {
  if (process.platform !== 'win32') return t.skip('Windows PowerShell runs only on Windows');
  const folder = mkdtempSync(join(tmpdir(), 'jfb-install-record-'));
  try {
    const run = [
      recordFunctions(),
      `$PidFile = '${join(folder, 'site.pid.json').replace(/'/g, "''")}'`,
      // The record as the run writes it, for this very process.
      "$record = [pscustomobject]@{ pid = $PID; startedAt = (Get-StartTime $PID) }",
      "[System.IO.File]::WriteAllText($PidFile, ($record | ConvertTo-Json))",
      "$own = $null -ne (Get-RecordedSite)",
      "$digits = (Get-StartTime $PID) -match '^[0-9]+$'",
      // Another process's start under the same pid, and a record an older run wrote as a date.
      "[System.IO.File]::WriteAllText($PidFile, (@{ pid = $PID; startedAt = '1' } | ConvertTo-Json))",
      "$other = $null -eq (Get-RecordedSite)",
      "[System.IO.File]::WriteAllText($PidFile, (@{ pid = $PID; startedAt = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o') } | ConvertTo-Json))",
      "$dated = $null -eq (Get-RecordedSite)",
      "Write-Output ('own=' + $own + ' digits=' + $digits + ' other=' + $other + ' dated=' + $dated)",
    ].join('\n');
    const answer = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', '-'], {
      input: run,
      encoding: 'utf8',
      timeout: 60_000,
    });
    assert.equal(answer.stderr.trim(), '');
    assert.equal(answer.stdout.trim(), 'own=True digits=True other=True dated=True');
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});

test("the upgrade check on the floor knows a build by its Web package's start chunk, which must be exactly one (18.3)", () => {
  const chunks = 'staticwebassets/App_Plugins/JironFormBuilder/chunks/';
  assert.equal(startChunkOf([`${chunks}section-4a_OWk5C.js`, `${chunks}start-C9Ke8sis.js`, 'README.md']), 'start-C9Ke8sis.js');
  assert.throws(() => startChunkOf([`${chunks}section-4a_OWk5C.js`]), /holds 0 start chunks/);
  assert.throws(() => startChunkOf([`${chunks}start-a.js`, `${chunks}start-b.js`]), /holds 2 start chunks/);
  assert.throws(() => startChunkOf([`${chunks}nested/start-a.js`]), /holds 0 start chunks/);
});
