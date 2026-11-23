// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/sqlserver-deadlocks.mjs. Nothing here starts Docker or reaches a database.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { QUERIES, SHOWN, deadlockReports, describe as describeReport, summary } from '../sqlserver-deadlocks.mjs';

const script = join(dirname(fileURLToPath(import.meta.url)), '..', 'sqlserver-deadlocks.mjs');

// A report shaped as SQL Server writes it, with two processes and one key each wants.
const report = (time, first = 'process1', second = 'process2') => `<event name="xml_deadlock_report" package="sqlserver" timestamp="${time}"><data name="xml_report"><type name="xml" package="package0" /><value><deadlock><victim-list><victimProcess id="${first}" /></victim-list><process-list><process id="${first}" waitresource="KEY: 7:72057594043170816 (8194443284a0)" lockMode="U" isolationlevel="read committed (2)"><executionStack><frame procname="adhoc" line="1">UPDATE [jfbActionRun] SET [state] = &apos;running&apos; WHERE [id] = @0 AND
NOT EXISTS (SELECT 1 FROM [jfbActionRun] p WITH (UPDLOCK, ROWLOCK))</frame></executionStack><inputbuf>(@0 int)UPDATE [jfbActionRun] SET [state] = &apos;running&apos;</inputbuf></process><process id="${second}" waitresource="KEY: 7:72057594043170816 (61a06abd401c)" lockMode="X" isolationlevel="read committed (2)"><executionStack><frame procname="adhoc" line="1">unknown</frame></executionStack><inputbuf>(@0 int)UPDATE [jfbActionRun] SET [state] = &apos;succeeded&apos; WHERE [id] = @0</inputbuf></process></process-list><resource-list><keylock hobtid="72057594043170816" dbid="7" objectname="db.dbo.jfbActionRun" indexname="IX_jfbActionRun_state_nextAttemptUtc" mode="X"><owner-list><owner id="${second}" mode="X" /></owner-list><waiter-list><waiter id="${first}" mode="U" requestType="wait" /></waiter-list></keylock></resource-list></deadlock></value></data></event>`;

const escaped = (xml) => xml.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');

describe('reading the reports', () => {
  test('each report is found once, in the ring buffer and in the file, oldest first', () => {
    const ring = `<RingBufferTarget truncated="0"><event name="wait_info" timestamp="2026-11-23T08:00:00.000Z"><data /></event>${report('2026-11-23T09:00:02.000Z')}${report('2026-11-23T09:00:01.000Z')}</RingBufferTarget>`;
    const file = `${report('2026-11-23T09:00:01.000Z')}\n${report('2026-11-23T09:00:03.000Z')}`;
    const reports = deadlockReports(`${ring}\n${file}`);
    assert.deepEqual(
      reports.map((each) => each.time),
      ['2026-11-23T09:00:01.000Z', '2026-11-23T09:00:02.000Z', '2026-11-23T09:00:03.000Z'],
    );
  });

  test('a report written as text inside the output is read too', () => {
    assert.equal(deadlockReports(escaped(report('2026-11-23T09:00:01.000Z'))).length, 1);
    assert.deepEqual(deadlockReports('nothing here'), []);
  });
});

describe('describing a report', () => {
  test('names the victim, each statement, what it waited for and the index it was on', () => {
    const lines = describeReport(deadlockReports(report('2026-11-23T09:00:01.000Z'))[0]);
    assert.equal(lines[0], 'deadlock at 2026-11-23T09:00:01.000Z');
    assert.match(lines[1], /^ {2}process process1 \(the victim\): waits for U on KEY: 7:72057594043170816 \(8194443284a0\), isolation read committed \(2\)$/);
    assert.equal(lines[2], "    statement: UPDATE [jfbActionRun] SET [state] = 'running' WHERE [id] = @0 AND NOT EXISTS (SELECT 1 FROM [jfbActionRun] p WITH (UPDLOCK, ROWLOCK))");
    assert.match(lines[3], /^ {2}process process2: waits for X on /);
    assert.equal(lines[4], "    statement: (@0 int)UPDATE [jfbActionRun] SET [state] = 'succeeded' WHERE [id] = @0", 'the input buffer where the stack names no statement');
    assert.equal(lines[5], '  keylock on db.dbo.jfbActionRun IX_jfbActionRun_state_nextAttemptUtc: process2 holds X, process1 wants U');
  });

  test('the summary counts them all and shows the last ones', () => {
    const many = Array.from({ length: SHOWN + 2 }, (_, i) => report(`2026-11-23T09:00:${String(i).padStart(2, '0')}.000Z`)).join('\n');
    const lines = summary(many);
    assert.equal(lines[0], `sqlserver-deadlocks: ${SHOWN + 2} deadlock(s) recorded by the server`);
    assert.equal(lines.filter((line) => line.includes('deadlock at')).length, SHOWN);
    assert.ok(!lines.some((line) => line.includes('09:00:00.000Z')), 'the oldest are left out');
    assert.equal(lines.at(-1), `sqlserver-deadlocks: the last ${SHOWN} are shown`);
    assert.deepEqual(summary(''), ['sqlserver-deadlocks: 0 deadlock(s) recorded by the server']);
  });
});

describe('the script', () => {
  test('refuses to run outside the workflow', () => {
    const result = spawnSync(process.execPath, [script], { encoding: 'utf8', env: { ...process.env, GITHUB_ACTIONS: '', MSSQL_CONTAINER: 'x', SQLCMDPASSWORD: 'JFB_FAKE' } });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /only in the workflow/);
  });

  test('reads the system_health session only, and changes nothing', () => {
    for (const query of QUERIES) {
      assert.match(query, /system_health/);
      assert.doesNotMatch(query, /\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|EXEC)\b/i);
    }
  });
});
