#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Which part of CI a run runs (ROADMAP 26.1): the first job of .github/workflows/ci.yml reads its two inputs through
 * the environment (ADR-0247) and says which jobs run and which integration shares, so a targeted run
 * (.github/workflows/targeted.yml) checks one failure for a fraction of a full run's runner time. A push, a tag and the
 * release call ci.yml without inputs, which is everything, exactly as before.
 *
 *   ONLY=sqlserver SHARES='["3","8"]' node scripts/ci-selection.mjs   writes the selection to $GITHUB_OUTPUT
 *
 * `ONLY` is all (or empty), build, sqlite, sqlserver, smoke or loadbalancing. `SHARES` is empty, or a JSON list of the
 * share names of the one database `ONLY` names: 1 to 6 and timed for SQLite, 1 to 10 and timed for SQL Server. A wrong
 * value fails the run at once, naming it. The outputs: `build`, `sqlite`, `sqlserver`, `smoke`, `loadbalancing` and
 * `everything` as true or false, and `sqlite-shares` and `sqlserver-shares` as JSON lists (the full list for a
 * database that does not run, so a skipped matrix is never empty).
 *
 * Exit codes: 0 fine, 2 a wrong input.
 */
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

/** The parts a run may be limited to. */
export const PARTS = Object.freeze(['all', 'build', 'sqlite', 'sqlserver', 'smoke', 'loadbalancing']);

/** Every share of each database, as ci.yml's integration jobs and JFB_SHARES name them. */
export const SHARES = Object.freeze({
  sqlite: Object.freeze(['1', '2', '3', '4', '5', '6', 'timed']),
  sqlserver: Object.freeze(['1', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'timed']),
});

/** The selection of two inputs as given (strings, maybe empty), or `{ error }`. */
export function select(only = '', shares = '') {
  const part = only.trim() === '' ? 'all' : only.trim();
  if (!PARTS.includes(part)) return { error: `only must be one of ${PARTS.join(', ')}, not "${only}"` };
  let named = null;
  if (shares.trim() !== '') {
    if (part !== 'sqlite' && part !== 'sqlserver') return { error: `shares go with only sqlite or only sqlserver, not with "${part}"` };
    try {
      named = JSON.parse(shares);
    } catch {
      return { error: `shares must be a JSON list such as ["3","timed"], not ${shares}` };
    }
    if (!Array.isArray(named) || named.length === 0 || !named.every((share) => typeof share === 'string')) {
      return { error: `shares must be a JSON list of share names such as ["3","timed"], not ${shares}` };
    }
    const unknown = named.filter((share) => !SHARES[part].includes(share));
    if (unknown.length > 0) return { error: `${part} has no share ${unknown.join(', ')}; its shares are ${SHARES[part].join(', ')}` };
    named = SHARES[part].filter((share) => named.includes(share));
  }

  const runs = (name) => part === 'all' || part === name;
  return {
    build: runs('build'),
    sqlite: runs('sqlite'),
    sqlserver: runs('sqlserver'),
    smoke: runs('smoke'),
    loadbalancing: runs('loadbalancing'),
    everything: part === 'all',
    sqliteShares: part === 'sqlite' && named ? named : [...SHARES.sqlite],
    sqlserverShares: part === 'sqlserver' && named ? named : [...SHARES.sqlserver],
  };
}

/** The lines `$GITHUB_OUTPUT` takes for a selection. */
export function outputLines(selection) {
  return [
    `build=${selection.build}`,
    `sqlite=${selection.sqlite}`,
    `sqlserver=${selection.sqlserver}`,
    `smoke=${selection.smoke}`,
    `loadbalancing=${selection.loadbalancing}`,
    `everything=${selection.everything}`,
    `sqlite-shares=${JSON.stringify(selection.sqliteShares)}`,
    `sqlserver-shares=${JSON.stringify(selection.sqlserverShares)}`,
  ];
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const selection = select(process.env.ONLY ?? '', process.env.SHARES ?? '');
  if (selection.error) {
    console.error(`ci-selection: ${selection.error}`);
    process.exit(2);
  }
  const lines = outputLines(selection);
  for (const line of lines) console.log(`ci-selection: ${line}`);
  if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `${lines.join('\n')}\n`);
}
