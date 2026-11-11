// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The NuGet packages of the solution's restore graphs and their metadata, shared by the NuGet licence check and the
 * third-party notices (ROADMAP 3.5). Each package gets a tier:
 *
 *   - platform: an Umbraco CMS package brings it in every graph it appears in. Sites already run it (rule 4 names
 *     Umbraco HQ's packages first), so Jiron Form Builder neither lists nor ships it;
 *   - shipped: a project of the packages' solution filter brings it, and Umbraco does not;
 *   - dev: only the tests and the demo bring it.
 *
 * A package in several graphs gets the strictest tier it has in any of them. Needs `dotnet restore` first.
 */
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join, resolve } from 'node:path';

export const SOLUTION = 'Jiron.Umbraco.FormBuilder.slnx';
export const PACKAGES_FILTER = 'src/Jiron.Umbraco.FormBuilder.Packages.slnf';

/** The tiers, from the least strict to the strictest. */
export const TIERS = Object.freeze(['platform', 'dev', 'shipped']);

/**
 * NuGet's global packages folder: NUGET_PACKAGES when it is set, as CI sets it, and the default cache otherwise.
 * Restore and these scripts must agree, or a fresh runner has no nuspec where the script looks.
 */
export function packagesFolder(env = process.env, home = homedir()) {
  const fromEnv = env.NUGET_PACKAGES?.trim();
  return fromEnv ? resolve(fromEnv) : join(home, '.nuget', 'packages');
}

/** The project paths of a .slnx, relative to its folder, with forward slashes. */
export function solutionProjects(slnxText) {
  return [...slnxText.matchAll(/<Project\s+Path="([^"]+)"/g)].map((match) => match[1].replaceAll('\\', '/'));
}

/** The project paths of a solution filter, relative to the solution's folder, with forward slashes. */
export function filterProjects(slnfText) {
  return (JSON.parse(slnfText).solution?.projects ?? []).map((path) => path.replaceAll('\\', '/'));
}

/** True for the Umbraco CMS packages, whose graphs make up the platform. */
export function isUmbracoCms(id) {
  const lower = id.toLowerCase();
  return lower === 'umbraco.cms' || lower.startsWith('umbraco.cms.');
}

/**
 * The packages of one project.assets.json, as [{ id, version, platform }]: platform when, in every target framework
 * the package appears in, an Umbraco CMS package brings it.
 */
export function graphPackages(assets) {
  const found = new Map();
  for (const target of Object.values(assets.targets ?? {})) {
    const children = new Map();
    const names = new Map();
    for (const [key, value] of Object.entries(target)) {
      if (value.type !== 'package') continue;
      const slash = key.indexOf('/');
      const id = key.slice(0, slash);
      const lower = id.toLowerCase();
      children.set(
        lower,
        Object.keys(value.dependencies ?? {}).map((child) => child.toLowerCase()),
      );
      names.set(lower, { id, version: key.slice(slash + 1) });
    }
    const platform = new Set();
    const stack = [...children.keys()].filter(isUmbracoCms);
    while (stack.length > 0) {
      const next = stack.pop();
      if (platform.has(next)) continue;
      platform.add(next);
      stack.push(...(children.get(next) ?? []));
    }
    for (const [lower, { id, version }] of names) {
      const key = `${lower}/${version.toLowerCase()}`;
      const known = found.get(key);
      found.set(key, { id, version, platform: (known?.platform ?? true) && platform.has(lower) });
    }
  }
  return [...found.values()];
}

/** The stricter of two tiers. */
export function stricter(a, b) {
  return TIERS.indexOf(a) >= TIERS.indexOf(b) ? a : b;
}

/**
 * Every restored package of the solution, as [{ id, version, tier, projects }], sorted by id and version.
 * `readText(path)` reads a file relative to the root (a test passes its own).
 */
export function solutionPackages(root, readText = (path) => readFileSync(join(root, path), 'utf8')) {
  const shipped = new Set(filterProjects(readText(PACKAGES_FILTER)));
  const packages = new Map();
  for (const project of solutionProjects(readText(SOLUTION))) {
    const assetsPath = `${dirname(project)}/obj/project.assets.json`;
    let text;
    try {
      text = readText(assetsPath);
    } catch {
      throw new Error(`no ${assetsPath}: run \`dotnet restore\` first`);
    }
    for (const { id, version, platform } of graphPackages(JSON.parse(text))) {
      const tier = platform ? 'platform' : shipped.has(project) ? 'shipped' : 'dev';
      const key = `${id.toLowerCase()}/${version.toLowerCase()}`;
      const known = packages.get(key);
      const name = project.split('/').at(-1).replace(/\.csproj$/, '');
      packages.set(key, {
        id: known?.id ?? id,
        version,
        tier: known ? stricter(known.tier, tier) : tier,
        projects: [...(known?.projects ?? []), name],
      });
    }
  }
  return [...packages.values()].sort((a, b) => a.id.localeCompare(b.id, 'en') || a.version.localeCompare(b.version, 'en'));
}

/**
 * The licence and copyright of a nuspec's text: { kind, value, copyright }, where kind is `expression`, `file`,
 * `url` or `none`.
 */
export function readNuspec(text) {
  const expression = text.match(/<license\s+type="expression"[^>]*>([^<]+)<\/license>/)?.[1]?.trim();
  const file = text.match(/<license\s+type="file"[^>]*>([^<]+)<\/license>/)?.[1]?.trim();
  const url = text.match(/<licenseUrl>([^<]+)<\/licenseUrl>/)?.[1]?.trim();
  const copyright = text.match(/<copyright>([^<]*)<\/copyright>/)?.[1]?.trim() ?? '';
  if (expression) return { kind: 'expression', value: expression, copyright };
  if (file) return { kind: 'file', value: file, copyright };
  if (url) return { kind: 'url', value: url, copyright };
  return { kind: 'none', value: '', copyright };
}

/** A package's folder in the packages folder (NuGet writes ids and versions in lower case). */
export function packageFolder(cache, { id, version }) {
  return join(cache, id.toLowerCase(), version.toLowerCase());
}

/**
 * The nuspec metadata of a restored package, with its licence file's text when it has one. Throws when the package
 * is not in the packages folder, which happens when restore used another one.
 */
export function packageMetadata(cache, pkg) {
  const folder = packageFolder(cache, pkg);
  const nuspec = existsSync(folder) ? readdirSync(folder).find((file) => file.toLowerCase().endsWith('.nuspec')) : undefined;
  if (!nuspec) {
    throw new Error(`${pkg.id} ${pkg.version} is not in ${cache}: restore and this script must use the same NUGET_PACKAGES`);
  }
  const meta = readNuspec(readFileSync(join(folder, nuspec), 'utf8'));
  if (meta.kind === 'none') {
    // A nuspec without a licence may still carry one beside it (Umbraco.AI's LICENSE.md): its text, for a review.
    const bundled = readdirSync(folder).find((file) => /^licen[cs]e(\.(md|txt))?$/i.test(file));
    return bundled ? { ...meta, bundled, text: readFileSync(join(folder, bundled), 'utf8') } : meta;
  }
  if (meta.kind !== 'file') return meta;
  const licenceFile = join(folder, meta.value);
  if (!existsSync(licenceFile)) throw new Error(`${pkg.id} ${pkg.version} names the licence file ${meta.value}, which is missing`);
  return { ...meta, text: readFileSync(licenceFile, 'utf8') };
}

/** The SHA-256 of a licence text with its line ends and outer white space normalised, as hex. */
export function licenceTextHash(text) {
  return createHash('sha256')
    .update(text.replace(/\r\n?/g, '\n').trim(), 'utf8')
    .digest('hex');
}
