// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The version stamped into the built assets (ADR-0010, ADR-0026): JFB_VERSION when set (scripts/pack.mjs and the
// release pass the version they pack), otherwise VersionPrefix-VersionSuffix from Directory.Build.props.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The repository root: Client/scripts is four folders below it. */
export const repositoryRoot = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..', '..', '..');

const SEMVER = /^[0-9]+[.][0-9]+[.][0-9]+(-[0-9A-Za-z.-]+)?$/;

/**
 * Reads VersionPrefix and VersionSuffix from the text of Directory.Build.props.
 * @param {string} propsText
 */
export function versionFromProps(propsText) {
  const prefix = propsText.match(/<VersionPrefix>([^<]+)<[/]VersionPrefix>/)?.[1]?.trim();
  const suffix = propsText.match(/<VersionSuffix>([^<]*)<[/]VersionSuffix>/)?.[1]?.trim();
  if (!prefix) throw new Error('Directory.Build.props has no VersionPrefix');
  return suffix ? `${prefix}-${suffix}` : prefix;
}

/**
 * The version of this build.
 * @param {Record<string, string | undefined>} [env]
 * @param {string} [root]
 */
export function buildVersion(env = process.env, root = repositoryRoot) {
  const fromEnv = env.JFB_VERSION?.trim();
  if (fromEnv) {
    if (!SEMVER.test(fromEnv)) throw new Error(`JFB_VERSION "${fromEnv}" is not a semantic version`);
    return fromEnv;
  }
  return versionFromProps(readFileSync(join(root, 'Directory.Build.props'), 'utf8'));
}
