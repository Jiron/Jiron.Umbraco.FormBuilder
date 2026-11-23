#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Says which deadlocks the workflow's SQL Server met during a job (ROADMAP 26.1): the server records each in its
 * `system_health` session with the statements and the locks of both sides, and a passing run shows none of that, as
 * the product runs a deadlock victim's unit again. Run after the integration tests of a SQL Server job; it reads the
 * session's ring buffer and its event file through `sqlcmd` inside the job's service container and prints, per
 * deadlock, who waited for what on which index while running which statement.
 *
 *   MSSQL_CONTAINER=<id> SQLCMDPASSWORD=<sa password> node scripts/sqlserver-deadlocks.mjs
 *
 * Only in the workflow: it refuses to run outside GitHub Actions, as the development machine never connects to a SQL
 * Server (CLAUDE.md rule 6). It never fails a job: what it cannot read it says, with exit code 0.
 */
import { spawnSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

/** The most deadlocks printed in full; the rest are counted. */
export const SHOWN = 6;

const unescape = (text) =>
  text
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&#x0A;', ' ')
    .replaceAll('&#x0D;', ' ')
    .replaceAll('&amp;', '&');

const attribute = (tag, name) => new RegExp(`\\b${name}="([^"]*)"`).exec(tag)?.[1] ?? '';

/** The deadlock reports in what the two queries wrote, each once (by its time), oldest first. */
export function deadlockReports(output) {
  const reports = new Map();
  for (const text of [output, unescape(output)]) {
    for (const match of text.matchAll(/<event\b[^>]*\bname="xml_deadlock_report"[^>]*>[\s\S]*?<\/event>/g)) {
      const time = attribute(match[0].slice(0, match[0].indexOf('>')), 'timestamp');
      if (!reports.has(time)) reports.set(time, match[0]);
    }
  }
  return [...reports].sort(([a], [b]) => a.localeCompare(b)).map(([time, xml]) => ({ time, xml }));
}

/**
 * One report as lines: each process with its statement and what it waited for, and each resource with its table,
 * index, the mode held and the mode waited for.
 */
export function describe(report) {
  const lines = [`deadlock at ${report.time}`];
  const victim = /<victimProcess\b[^>]*\bid="([^"]*)"/.exec(report.xml)?.[1] ?? '';
  for (const match of report.xml.matchAll(/<process\s([^>]*)>([\s\S]*?)<\/process>/g)) {
    const id = attribute(match[1], 'id');
    const statement = unescape(/<inputbuf>([\s\S]*?)<\/inputbuf>/.exec(match[2])?.[1] ?? '')
      .replace(/\s+/g, ' ')
      .trim();
    const frame = unescape(/<frame\b[^>]*>([\s\S]*?)<\/frame>/.exec(match[2])?.[1] ?? '')
      .replace(/\s+/g, ' ')
      .trim();
    lines.push(
      `  process ${id}${id === victim ? ' (the victim)' : ''}: waits for ${attribute(match[1], 'lockMode')} on ${attribute(match[1], 'waitresource')}, isolation ${attribute(match[1], 'isolationlevel')}`,
      `    statement: ${(frame.length > 20 ? frame : statement).slice(0, 700)}`,
    );
  }
  for (const match of report.xml.matchAll(/<(keylock|pagelock|ridlock|objectlock|hobtlock)\b([^>]*)>([\s\S]*?)<\/\1>/g)) {
    const holders = [...match[3].matchAll(/<owner\s([^>]*)\/>/g)].map((owner) => `${attribute(owner[1], 'id')} holds ${attribute(owner[1], 'mode')}`);
    const waiters = [...match[3].matchAll(/<waiter\s([^>]*)\/>/g)].map((waiter) => `${attribute(waiter[1], 'id')} wants ${attribute(waiter[1], 'mode')}`);
    lines.push(`  ${match[1]} on ${attribute(match[2], 'objectname')} ${attribute(match[2], 'indexname')}: ${[...holders, ...waiters].join(', ')}`);
  }
  return lines;
}

/** What the script prints for the queries' output. */
export function summary(output) {
  const reports = deadlockReports(output);
  const lines = [`sqlserver-deadlocks: ${reports.length} deadlock(s) recorded by the server`];
  for (const report of reports.slice(-SHOWN)) lines.push(...describe(report).map((line) => `sqlserver-deadlocks: ${line}`));
  if (reports.length > SHOWN) lines.push(`sqlserver-deadlocks: the last ${SHOWN} are shown`);
  return lines;
}

/** The two readings of the system_health session: its ring buffer, which is current, and its file, which holds more. */
export const QUERIES = Object.freeze([
  "SET NOCOUNT ON; SELECT CAST(st.target_data AS nvarchar(max)) FROM sys.dm_xe_session_targets st JOIN sys.dm_xe_sessions s ON s.address = st.event_session_address WHERE s.name = N'system_health' AND st.target_name = N'ring_buffer';",
  "SET NOCOUNT ON; SELECT CAST(event_data AS nvarchar(max)) FROM sys.fn_xe_file_target_read_file(N'system_health*.xel', NULL, NULL, NULL) WHERE object_name = N'xml_deadlock_report';",
]);

function main() {
  if (process.env.GITHUB_ACTIONS !== 'true') {
    console.error('sqlserver-deadlocks: refused: this runs only in the workflow, against its own SQL Server container');
    return 2;
  }
  const container = process.env.MSSQL_CONTAINER ?? '';
  if (container === '' || !process.env.SQLCMDPASSWORD) {
    console.log('sqlserver-deadlocks: no container or no password given; nothing read');
    return 0;
  }
  let output = '';
  for (const query of QUERIES) {
    // The password reaches sqlcmd through the environment (SQLCMDPASSWORD), never as an argument.
    const result = spawnSync('docker', ['exec', '-e', 'SQLCMDPASSWORD', container, '/opt/mssql-tools18/bin/sqlcmd', '-C', '-S', 'localhost', '-U', 'sa', '-y', '0', '-Q', query], {
      encoding: 'utf8',
      maxBuffer: 256 * 1024 * 1024,
    });
    if (result.status !== 0) {
      console.log(`sqlserver-deadlocks: a query could not be read (exit ${result.status}): ${(result.stderr || result.stdout || '').trim().split(/\r?\n/)[0] ?? ''}`);
      continue;
    }
    output += `${result.stdout}\n`;
  }
  for (const line of summary(output)) console.log(line);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main();
}
