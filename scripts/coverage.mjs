#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Line coverage per assembly from the Cobertura reports of Microsoft's code coverage collector ("Code Coverage",
// instrumenting in memory, ADR-0079), merged across the test projects: a line counts as covered when any report hit
// it. The phase-review gate needs Core at 85% or more from Phase 4 on (ROADMAP "How to use" and section 4.12). Ported
// from Tandem's script (ROADMAP 3.3).
//
//   node scripts/coverage.mjs --run     empties .local/coverage, runs the .NET tests into it, then merges and checks
//   node scripts/coverage.mjs [--dir .local/coverage] [--min 85] [--package <assembly>]...
//
// Prints one line per assembly, sorted by name. Without --package it checks Jiron.Umbraco.FormBuilder.Core. Exits 1
// when a checked assembly is below --min or missing, or when the tests fail.

import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/** Every Cobertura report under a folder: each file whose name ends in .cobertura.xml, as each collector names it. */
export function findReports(dir) {
  const found = [];
  const walk = (folder) => {
    for (const name of readdirSync(folder)) {
      const path = join(folder, name);
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cobertura.xml')) found.push(path);
    }
  };
  walk(dir);
  return found;
}

/**
 * Merges Cobertura documents into Map<package, Map<file, Map<line, hits>>>. Compiler-generated classes (async state
 * machines, lambdas) report the same file's lines again; the merge counts each line once.
 */
export function merge(documents) {
  const packages = new Map();
  for (const xml of documents) {
    let pkg = null;
    let file = null;
    for (const match of xml.matchAll(/<(package|class|line)\b([^>]*)>/g)) {
      const attributes = Object.fromEntries([...match[2].matchAll(/(\w+)="([^"]*)"/g)].map((m) => [m[1], m[2]]));
      if (match[1] === 'package') {
        pkg = attributes.name;
        if (!packages.has(pkg)) packages.set(pkg, new Map());
      } else if (match[1] === 'class') {
        file = attributes.filename;
        if (pkg && !packages.get(pkg).has(file)) packages.get(pkg).set(file, new Map());
      } else if (pkg && file && attributes.number) {
        const lines = packages.get(pkg).get(file);
        const number = Number(attributes.number);
        lines.set(number, Math.max(lines.get(number) ?? 0, Number(attributes.hits ?? 0)));
      }
    }
  }
  return packages;
}

/** Covered and total lines per package, sorted by name. */
export function summarise(packages) {
  return [...packages.entries()]
    .map(([name, files]) => {
      let covered = 0;
      let total = 0;
      for (const lines of files.values()) {
        for (const hits of lines.values()) {
          total += 1;
          if (hits > 0) covered += 1;
        }
      }
      return { name, covered, total, percent: total === 0 ? 0 : (covered * 100) / total };
    })
    .sort((a, b) => a.name.localeCompare(b.name));
}

/** The problems of a summary against the floor: each checked package that is missing or below it. */
export function check(summary, packages, min) {
  const problems = [];
  for (const name of packages) {
    const row = summary.find((r) => r.name === name);
    if (!row) problems.push(`${name} is not in the reports`);
    else if (row.percent < min) problems.push(`${name} is at ${row.percent.toFixed(1)}%, below ${min}%`);
  }
  return problems;
}

/** The command line: the folder, the floor, the packages to check (Core when none is named) and whether to run. */
export function parseArguments(argv) {
  const options = { dir: '.local/coverage', min: 85, packages: [], run: false };
  for (let i = 0; i < argv.length; i++) {
    const value = () => {
      if (i + 1 >= argv.length) throw new Error(`${argv[i]} needs a value`);
      return argv[++i];
    };
    if (argv[i] === '--dir') options.dir = value();
    else if (argv[i] === '--min') options.min = Number(value());
    else if (argv[i] === '--package') options.packages.push(value());
    else if (argv[i] === '--run') options.run = true;
    else throw new Error(`Unknown argument ${argv[i]}`);
  }
  if (!Number.isFinite(options.min) || options.min < 0 || options.min > 100) throw new Error('--min is a percentage');
  if (options.packages.length === 0) options.packages.push('Jiron.Umbraco.FormBuilder.Core');
  return options;
}

/** The arguments of the test run: every test project, Release, the collector through the run settings. */
export function testArguments(dir) {
  return [
    'test',
    join(repo, 'Jiron.Umbraco.FormBuilder.slnx'),
    '-c',
    'Release',
    '-nologo',
    '--settings',
    join(repo, 'tests', 'coverage.runsettings'),
    '--results-directory',
    dir,
    '--blame-hang',
    '--blame-hang-timeout',
    '10m',
    '--blame-hang-dump-type',
    'none',
  ];
}

function main(argv) {
  const options = parseArguments(argv);
  const dir = resolve(repo, options.dir);

  if (options.run) {
    // An old report would count lines this run no longer covers.
    rmSync(dir, { recursive: true, force: true });
    const tests = spawnSync('dotnet', testArguments(dir), { cwd: repo, stdio: 'inherit' });
    if (tests.status !== 0) {
      console.error(`coverage: the tests failed (exit ${tests.status ?? tests.signal})`);
      return 1;
    }
  }

  const reports = existsSync(dir) ? findReports(dir) : [];
  if (reports.length === 0) {
    console.error(`coverage: no Cobertura report under ${options.dir}; run node scripts/coverage.mjs --run`);
    return 1;
  }

  const summary = summarise(merge(reports.map((path) => readFileSync(path, 'utf8'))));
  console.log(`coverage: ${reports.length} report(s) merged`);
  for (const row of summary) {
    console.log(`  ${row.name.padEnd(48)} ${row.percent.toFixed(1).padStart(5)}%  (${row.covered}/${row.total} lines)`);
  }

  const problems = check(summary, options.packages, options.min);
  for (const problem of problems) console.error(`coverage: ${problem}`);
  if (problems.length === 0) console.log(`coverage: ${options.packages.join(', ')} at ${options.min}% or more`);
  return problems.length === 0 ? 0 : 1;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(2));
}
