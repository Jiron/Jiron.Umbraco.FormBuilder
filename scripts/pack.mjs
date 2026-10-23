#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Packs Jiron Form Builder's three packages (ROADMAP 3.2; the file allowlist follows in 18.1, ADR-0026). Each pack
 * builds the client for the version it packs (JFB_VERSION), then runs `dotnet pack` of the packages' solution filter
 * in Release, which runs NuGet's package validation and the JFB001 check of the client assets. CI, the local gate
 * and the release pack through this script, so they pack the same way.
 *
 *   node scripts/pack.mjs --output <folder> [--version <version>] [--no-build]
 *     --version   the packages' version; without it a local test version <VersionPrefix>-dev.<yyyyMMddHHmm> in UTC
 *                 (D16), so a test pack never meets an older pack of the same version in a package cache
 *     --no-build  packs what an earlier step built for that version: no client build and `dotnet pack --no-build`
 *                 (the static web assets are fixed when .NET builds, so the client must be built before it). Without
 *                 --version it packs the version a plain build stamps, VersionPrefix-VersionSuffix, as the gate and
 *                 CI do after their Release build
 *
 * Package files already in the folder are removed first; other files stay. Exit codes: 0 packed, 1 a step or a
 * check failed, 2 usage.
 */
import { spawnSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
export const SOLUTION_FILTER = 'src/Jiron.Umbraco.FormBuilder.Packages.slnf';
export const CLIENT = 'src/Jiron.Umbraco.FormBuilder.Web/Client';
export const PACKAGE_IDS = ['Jiron.Umbraco.FormBuilder', 'Jiron.Umbraco.FormBuilder.Core', 'Jiron.Umbraco.FormBuilder.Web'];

/** The local test version for a moment: VersionPrefix plus -dev.<yyyyMMddHHmm> in UTC. */
export function devVersion(propsText, now = new Date()) {
  const prefix = propsText.match(/<VersionPrefix>([^<]+)<[/]VersionPrefix>/)?.[1]?.trim();
  if (!prefix) throw new Error('Directory.Build.props has no VersionPrefix');
  const pad = (n) => String(n).padStart(2, '0');
  const stamp = `${now.getUTCFullYear()}${pad(now.getUTCMonth() + 1)}${pad(now.getUTCDate())}${pad(now.getUTCHours())}${pad(now.getUTCMinutes())}`;
  return `${prefix}-dev.${stamp}`;
}

/** VersionPrefix-VersionSuffix: what a build without a version of its own stamps (the client's version.mjs too). */
export function builtVersion(propsText) {
  const prefix = propsText.match(/<VersionPrefix>([^<]+)<[/]VersionPrefix>/)?.[1]?.trim();
  const suffix = propsText.match(/<VersionSuffix>([^<]*)<[/]VersionSuffix>/)?.[1]?.trim();
  if (!prefix) throw new Error('Directory.Build.props has no VersionPrefix');
  return suffix ? `${prefix}-${suffix}` : prefix;
}

/**
 * The version a pack uses: the one asked for; otherwise a local test version when the pack builds, and the version
 * the earlier build stamped when it does not (a test version would never match that build's assets).
 */
export function packVersion({ version, build, propsText, now = new Date() }) {
  if (version) return version;
  return build ? devVersion(propsText, now) : builtVersion(propsText);
}

/** The arguments of `dotnet pack` for an output folder and a version. */
export function packArguments({ output, version, build = true }) {
  const args = ['pack', SOLUTION_FILTER, '-c', 'Release', '-nologo', '-o', output, `-p:Version=${version}`];
  if (!build) args.push('--no-build');
  return args;
}

/** The package files in a folder (none when it does not exist): what a pack replaces. */
export function packageFiles(folder) {
  return existsSync(folder) ? readdirSync(folder).filter((file) => /[.]s?nupkg$/i.test(file)) : [];
}

/**
 * What is wrong with the packed files: each of the three packages must be there once with the version, every file
 * must carry the version, and nothing else may be packed. Symbol packages (.snupkg) belong to Core and Web.
 */
export function packProblems(files, version) {
  const problems = [];
  for (const id of PACKAGE_IDS) {
    if (!files.includes(`${id}.${version}.nupkg`)) problems.push(`${id}.${version}.nupkg is missing`);
  }
  const expected = new Set([
    ...PACKAGE_IDS.map((id) => `${id}.${version}.nupkg`),
    ...PACKAGE_IDS.filter((id) => id !== 'Jiron.Umbraco.FormBuilder').map((id) => `${id}.${version}.snupkg`),
  ]);
  for (const file of files) {
    if (!expected.has(file)) problems.push(`${file} is not one of the packages of ${version}`);
  }
  return problems;
}

function option(args, name) {
  const at = args.indexOf(name);
  return at >= 0 ? args[at + 1] : undefined;
}

/**
 * Runs a command and stops on failure. npm is a .cmd script on Windows, which only a shell starts; the shell gets
 * one command line, built here from this script's own fixed arguments.
 */
function run(command, args, options) {
  const viaShell = process.platform === 'win32' && command === 'npm';
  const quote = (arg) => (/[ "]/.test(arg) ? `"${arg.replaceAll('"', '""')}"` : arg);
  const result = viaShell
    ? spawnSync([command, ...args].map(quote).join(' '), { stdio: 'inherit', shell: true, ...options })
    : spawnSync(command, args, { stdio: 'inherit', ...options });
  if (result.status !== 0) {
    console.error(`pack: ${command} ${args.join(' ')} failed (exit ${result.status ?? 'none'})`);
    process.exit(1);
  }
}

function main() {
  const args = process.argv.slice(2);
  const output = option(args, '--output');
  if (!output) {
    console.error('usage: node scripts/pack.mjs --output <folder> [--version <version>] [--no-build]');
    process.exit(2);
  }
  const build = !args.includes('--no-build');
  const version = packVersion({ version: option(args, '--version'), build, propsText: readFileSync(join(root, 'Directory.Build.props'), 'utf8') });
  const folder = resolve(output);
  for (const file of packageFiles(folder)) rmSync(join(folder, file));

  if (build) {
    const client = join(root, CLIENT);
    if (!existsSync(join(client, 'node_modules'))) run('npm', ['ci'], { cwd: client });
    run('npm', ['run', 'build'], { cwd: client, env: { ...process.env, JFB_VERSION: version } });
  }
  run('dotnet', packArguments({ output: folder, version, build }), { cwd: root });

  const problems = packProblems(packageFiles(folder), version);
  for (const problem of problems) console.error(`pack: ${problem}`);
  if (problems.length > 0) process.exit(1);
  console.log(`pack: ${PACKAGE_IDS.length} packages of ${version} in ${output}`);
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) main();
