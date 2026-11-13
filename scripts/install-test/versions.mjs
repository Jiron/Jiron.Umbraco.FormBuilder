#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The install test's versions (ROADMAP 18.3 and 18.4, ADR-0240, ADR-0248), here rather than in PowerShell so they are
 * tested:
 *
 *   node scripts/install-test/versions.mjs next <version>        the higher version the floor's update packs
 *   node scripts/install-test/versions.mjs baseline <checkout>   the version a baseline's checkout is packed as
 *   node scripts/install-test/versions.mjs newest                the newest released schema's tag in this repository
 *
 * Exit codes: 0 printed, 1 refused, 2 usage.
 */
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$/;

/**
 * A version that sorts above the given one, for a second build of the same sources: a prerelease gains one more
 * identifier (17.0.0-alpha.1 -> 17.0.0-alpha.1.2), a release its next patch (17.0.0 -> 17.0.1).
 */
export function nextVersion(version) {
  const match = SEMVER.exec(String(version ?? '').trim());
  if (!match) throw new Error(`"${version}" is not a semantic version`);
  return match[4] ? `${match[0]}.2` : `${match[1]}.${match[2]}.${Number(match[3]) + 1}`;
}

/**
 * The version a baseline's checkout is packed as: its own version with `baseline` added, so a feed can hold it beside
 * the build it is upgraded to (17.0.0-alpha.1 -> 17.0.0-alpha.1.baseline, 17.0.0 -> 17.0.0-baseline).
 */
export function baselineVersion(propsText) {
  const prefix = propsText.match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1]?.trim();
  const suffix = propsText.match(/<VersionSuffix>([^<]*)<\/VersionSuffix>/)?.[1]?.trim();
  if (!prefix) throw new Error('Directory.Build.props has no VersionPrefix');
  return suffix ? `${prefix}-${suffix}.baseline` : `${prefix}-baseline`;
}

/** The newest released schema's tag among a repository's tags: baseline/schema-v<N> with the highest N. */
export function newestBaseline(tags) {
  const found = tags
    .map((tag) => tag.trim())
    .map((tag) => ({ tag, n: /^baseline\/schema-v(\d+)$/.exec(tag)?.[1] }))
    .filter((entry) => entry.n !== undefined)
    .sort((a, b) => Number(a.n) - Number(b.n));
  if (found.length === 0) throw new Error('the repository has no tag baseline/schema-v<N>');
  return found.at(-1).tag;
}

function main() {
  const [command, value] = process.argv.slice(2);
  try {
    if (command === 'next' && value) console.log(nextVersion(value));
    else if (command === 'baseline' && value) console.log(baselineVersion(readFileSync(join(resolve(value), 'Directory.Build.props'), 'utf8')));
    else if (command === 'newest') {
      const tags = execFileSync('git', ['-C', root, 'tag', '--list', 'baseline/schema-v*'], { encoding: 'utf8' }).split(/\r?\n/);
      console.log(newestBaseline(tags));
    } else {
      console.error('usage: node scripts/install-test/versions.mjs next <version> | baseline <checkout> | newest');
      process.exit(2);
    }
  } catch (error) {
    console.error(`versions: ${error.message}`);
    process.exit(1);
  }
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) main();
