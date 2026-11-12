#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Reads an install-test site's SQLite database (ROADMAP 16.5, ADR-0240) with Node's own SQLite, opened read-only, so
 * the test needs no other tool.
 *
 *   node jfb-db.mjs state <db>                 Jiron Form Builder's migration state
 *   node jfb-db.mjs umbraco <db>               Umbraco's own migration state
 *   node jfb-db.mjs dump <db> <fixture.json>   the baseline's schema and the rows its seed wrote: the upgrade fixture
 *   node jfb-db.mjs check <db> [expected.json] the plan's last state is reached, and the rows the seed wrote are all
 *                                              there with their values (the seed's file of expectations)
 *
 * Exit codes: 0 fine, 1 a check failed (with the reason), 2 usage.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath } from 'node:url';
import { finalState, planName } from './migration-plan.mjs';

/** Umbraco keeps each plan's state under this prefix in umbracoKeyValue. */
const STATE_PREFIX = 'Umbraco.Core.Upgrader.State+';

/** The tables the baseline's seed writes, in the order a fixture is applied (parents first, ADR-0240). */
export const SEEDED = [
  'jfbSetting',
  'jfbFolder',
  'jfbForm',
  'jfbFormVersion',
  'jfbFormField',
  'jfbEntry',
  'jfbEntryFile',
  'jfbEntryValue',
  'jfbEntryEvent',
  'jfbAuditEntry',
];

/**
 * A filtered index's condition as the integration tests' SchemaInspector writes it: `where column is not null` for a
 * filter on one column's value, the filter in lower case for any other, null for an index without one.
 */
export function normaliseFilter(sql) {
  const where = /\sWHERE\s+(.+)$/is.exec(sql ?? '');
  if (!where) return null;
  let condition = where[1].replaceAll('[', '').replaceAll(']', '').replaceAll('"', '').replace(/\s+/g, ' ').trim();
  while (condition.startsWith('(') && condition.endsWith(')')) condition = condition.slice(1, -1).trim();
  const notNull = /^(\w+) IS NOT NULL$/i.exec(condition);
  return notNull ? `where ${notNull[1]} is not null` : `where ${condition.toLowerCase()}`;
}

/** Jiron Form Builder's schema in the form of the tests' SchemaInspector: columns per table, and the indexes. */
export function schemaOf(db) {
  const all = (sql) => db.prepare(sql).all();
  const tables = all("SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name LIKE 'jfb%' ORDER BY name").map((t) => t.name);
  const columns = Object.fromEntries(
    tables.map((table) => [
      table,
      all(`SELECT name, "notnull", cid FROM pragma_table_info('${table}')`)
        .sort((a, b) => Number(a.cid) - Number(b.cid))
        .map((c) => (Number(c.notnull) === 0 ? `${c.name}?` : c.name))
        .join(', '),
    ]),
  );
  const filters = new Map(
    all("SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name LIKE 'jfb%' AND sql IS NOT NULL").map((i) => [i.name, normaliseFilter(i.sql)]),
  );
  const indexes = tables
    .flatMap((table) =>
      all(`SELECT name, "unique", origin FROM pragma_index_list('${table}')`)
        .filter((i) => i.origin === 'c')
        .map((i) => {
          const cols = all(`SELECT name, seqno FROM pragma_index_info('${i.name}')`)
            .sort((a, b) => Number(a.seqno) - Number(b.seqno))
            .map((c) => c.name);
          const filter = filters.get(i.name);
          return `${table}.${i.name}(${cols.join(',')})${Number(i.unique) === 1 ? ' unique' : ''}${filter ? ` ${filter}` : ''}`;
        }),
    )
    .sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
  return { columns, indexes };
}

function main() {
  const [command, file, extra] = process.argv.slice(2);
  if (!command || !file) {
    console.error('usage: node jfb-db.mjs state|umbraco|dump|check <db> [file]');
    process.exit(2);
  }
  const db = new DatabaseSync(file, { readOnly: true });
  const one = (sql, ...args) => db.prepare(sql).get(...args);
  const all = (sql, ...args) => db.prepare(sql).all(...args);
  const fail = (reason) => {
    console.error(`jfb-db: ${reason}`);
    process.exit(1);
  };
  const stateOf = (plan) => one('SELECT value FROM umbracoKeyValue WHERE key = ?', STATE_PREFIX + plan)?.value ?? null;
  const json = (value) => JSON.stringify(value, (_key, v) => (typeof v === 'bigint' ? Number(v) : v), 2);

  if (command === 'state') {
    console.log(stateOf(planName()) ?? '(none)');
  } else if (command === 'umbraco') {
    console.log(stateOf('Umbraco.Core') ?? '(none)');
  } else if (command === 'dump') {
    if (!extra) fail('dump needs the fixture path');
    // Every column of every row the seed wrote, as SQLite keeps it (times and keys as text). The settings rows are
    // those that differ from the defaults the baseline's second step wrote: the general section.
    const tables = Object.fromEntries(SEEDED.map((table) => [table, all(`SELECT * FROM ${table} ORDER BY 1`)]));
    tables.jfbSetting = tables.jfbSetting.filter((row) => row.updatedByUserKey !== null);
    const fixture = {
      $comment:
        "Rows a site on the baseline packages (tag baseline/schema-v1, 17.0.0-alpha.0.baseline) stored, and its schema, written by scripts/install-test.ps1 -Mode upgrade -WriteFixture from scripts/install-test/BaselineSeed (ROADMAP 16.5, ADR-0240). UpgradeFixtureTests applies them after the baseline's steps; StoredDefinitionFixtureTests reads their definitions.",
      state: stateOf(planName()),
      schema: schemaOf(db),
      tables,
    };
    writeFileSync(extra, json(fixture) + '\n');
    console.log(`jfb-db: ${SEEDED.map((t) => `${tables[t].length} ${t}`).join(', ')} -> ${extra}`);
  } else if (command === 'check') {
    const final = finalState();
    const state = stateOf(planName());
    if (state !== final) fail(`the migration state is ${state}, not the plan's last, ${final}`);
    const checks = [`state ${state}`];
    if (extra) {
      const expected = JSON.parse(readFileSync(extra, 'utf8'));
      for (const folder of expected.folders) {
        const row = one('SELECT name, parentId FROM jfbFolder WHERE key = ? COLLATE NOCASE', folder.key);
        if (!row || row.name !== folder.name) fail(`the folder ${folder.name} is gone`);
      }
      for (const form of expected.forms) {
        const row = one('SELECT id, alias, name, publishedVersionId, draftVersionId FROM jfbForm WHERE key = ? COLLATE NOCASE', form.key);
        if (!row || row.alias !== form.alias || row.name !== form.name) fail(`the form ${form.alias} is gone or renamed`);
        const published = one("SELECT COUNT(*) AS n FROM jfbFormVersion WHERE formId = ? AND kind = 'published'", row.id).n;
        if (Number(published) !== form.publishedVersions) fail(`the form ${form.alias} has ${published} published versions, not ${form.publishedVersions}`);
      }
      for (const entry of expected.entries) {
        const row = one('SELECT id, number, state FROM jfbEntry WHERE key = ? COLLATE NOCASE', entry.key);
        if (!row || Number(row.number) !== entry.number || row.state !== entry.state) fail(`the entry ${entry.form} #${entry.number} is gone or changed`);
        const stored = Object.fromEntries(
          all('SELECT f.alias, v.text FROM jfbEntryValue v JOIN jfbFormField f ON f.id = v.fieldId WHERE v.entryId = ?', row.id).map((v) => [v.alias, v.text]),
        );
        for (const [alias, text] of Object.entries(entry.values)) {
          if (stored[alias] !== text) fail(`the entry ${entry.form} #${entry.number} lost its answer to ${alias}`);
        }
      }
      const file = one('SELECT fileHash, sizeBytes, entryId FROM jfbEntryFile WHERE key = ? COLLATE NOCASE', expected.file.key);
      if (!file || file.fileHash !== expected.file.sha256 || file.entryId === null) fail('the entry file is gone or changed');
      checks.push(`${expected.folders.length} folders, ${expected.forms.length} forms, ${expected.entries.length} entries with their answers and the file`);
    }
    console.log(`jfb-db: ${checks.join('; ')}`);
  } else {
    fail(`unknown command ${command}`);
  }
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) main();
