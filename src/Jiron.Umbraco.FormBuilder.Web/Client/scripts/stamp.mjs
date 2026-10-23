// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The build stamp of the client assets (ROADMAP 3.2, ADR-0001, ADR-0026). The built files are not committed, so
// `dotnet pack` must know that they are there and match the sources:
//   node scripts/stamp.mjs write            after the package builds: checks the output, records the inputs' hash
//                                           and the version in .build/stamp.json
//   node scripts/stamp.mjs check [version] [assembly]
//                                           before packing (the JFB001 target): fails when the output is missing,
//                                           stale, built for another version than the package, lacks the licence
//                                           banner or a compressed copy, or holds a source map, and when the
//                                           assembly to be packed does not embed the current website assets
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';
import { bannerProblem } from './banner.mjs';
import { COPIES, walk } from './compress.mjs';
import { buildVersion } from './version.mjs';

export const clientRoot = join(fileURLToPath(new URL('.', import.meta.url)), '..');
/** The Web project, whose folders the client builds into. */
export const defaultProjectRoot = join(clientRoot, '..');
/** @param {string} root */
const stampFile = (root) => join(root, '.build', 'stamp.json');

/**
 * The folders the client builds into, relative to the Web project: everything in them ships, the backoffice as
 * static web assets and the website assets embedded in the assembly (ADR-0026).
 */
export const OUTPUT_FOLDERS = ['wwwroot/App_Plugins/JironFormBuilder', 'WebsiteAssets'];

/** The built files that must exist, relative to the Web project. */
export const OUTPUTS = [
  'wwwroot/App_Plugins/JironFormBuilder/umbraco-package.json',
  'wwwroot/App_Plugins/JironFormBuilder/jfb-backoffice.js',
  'WebsiteAssets/runtime/jfb-runtime.js',
  'WebsiteAssets/element/jfb-element.js',
];

/** The website assets, which 6.2 serves precompressed. */
const COMPRESSED_FOLDER = 'WebsiteAssets';

const ROOT_INPUTS = ['package.json', 'package-lock.json', 'tsconfig.base.json', 'scripts'];
const PACKAGE_INPUTS = ['package.json', 'tsconfig.json', 'vite.config.ts', 'src', 'public'];
/** @param {string} path */
const isTest = (path) => /[.]test[.][cm]?[jt]s$/.test(path);

/**
 * The files whose content determines the built assets (tests excluded), in a stable order.
 * @param {string} [root]
 * @returns {string[]}
 */
export function inputFiles(root = clientRoot) {
  const files = [];
  for (const entry of ROOT_INPUTS) files.push(...expand(join(root, entry)));
  const packages = join(root, 'packages');
  for (const name of existsSync(packages) ? readdirSync(packages).sort() : []) {
    for (const entry of PACKAGE_INPUTS) files.push(...expand(join(packages, name, entry)));
  }
  return files.filter((file) => !isTest(file));
}

/** @param {string} path */
function expand(path) {
  if (!existsSync(path)) return [];
  return statSync(path).isDirectory() ? [...walk(path)] : [path];
}

/**
 * A SHA-256 over the relative path and the content of every input.
 * @param {string} [root]
 */
export function inputsHash(root = clientRoot) {
  const hash = createHash('sha256');
  for (const file of inputFiles(root)) {
    hash.update(relative(root, file).split(sep).join('/'));
    hash.update('\0');
    hash.update(readFileSync(file));
    hash.update('\0');
  }
  return hash.digest('hex');
}

/**
 * Whether compressed bytes decompress to the original; bytes that are not compressed at all are no copy either.
 * @param {Buffer} compressed
 * @param {Buffer} original
 * @param {(bytes: Buffer) => Buffer} decompress
 */
function isCopyOf(compressed, original, decompress) {
  try {
    return decompress(compressed).equals(original);
  } catch {
    return false;
  }
}

/**
 * Every file under the output folders, relative to the Web project, with forward slashes.
 * @param {string} projectRoot
 */
export function outputFiles(projectRoot) {
  return OUTPUT_FOLDERS.flatMap((folder) => [...walk(join(projectRoot, folder))]).map((file) =>
    relative(projectRoot, file).split(sep).join('/'),
  );
}

/**
 * What is wrong with the built files themselves, for the given version: missing outputs, the manifest's version,
 * a licence banner on every JS and CSS file, current compressed copies of every website asset, and (when packing)
 * no source maps.
 * @param {{ projectRoot?: string, version: string, licenseText?: string | undefined, packing?: boolean }} options
 * @returns {string[]}
 */
export function outputProblems({ projectRoot = defaultProjectRoot, version, licenseText, packing = false }) {
  const problems = [];
  const missing = OUTPUTS.filter((file) => !existsSync(join(projectRoot, file)));
  if (missing.length > 0) return [`build outputs are missing: ${missing.join(', ')}`];

  const manifest = JSON.parse(readFileSync(join(projectRoot, OUTPUTS[0] ?? ''), 'utf8'));
  if (manifest.version !== version)
    problems.push(`umbraco-package.json has version ${manifest.version}, not ${version}`);

  const files = outputFiles(projectRoot);
  for (const file of files.filter((f) => /[.](js|css)$/.test(f))) {
    const problem = bannerProblem(readFileSync(join(projectRoot, file), 'utf8'), version, licenseText);
    if (problem) problems.push(`${file} ${problem}`);
  }
  for (const file of files.filter((f) => f.startsWith(`${COMPRESSED_FOLDER}/`) && !COPIES.some((c) => f.endsWith(c)))) {
    const bytes = readFileSync(join(projectRoot, file));
    for (const [copy, decompress] of /** @type {const} */ ([
      ['.br', brotliDecompressSync],
      ['.gz', gunzipSync],
    ])) {
      const path = join(projectRoot, `${file}${copy}`);
      if (!existsSync(path)) problems.push(`${file} has no ${copy} copy`);
      else if (!isCopyOf(readFileSync(path), bytes, decompress))
        problems.push(`${file}${copy} is not a copy of ${file}`);
    }
  }
  if (packing) {
    const maps = files.filter((file) => file.endsWith('.map'));
    if (maps.length > 0)
      problems.push(`source maps would be packed (${maps.join(', ')}): build again without JFB_SOURCEMAP`);
  }
  return problems;
}

/**
 * What the assembly to be packed lacks: it must embed every website asset and copy as they are now (ADR-0026).
 * Embedded resources are stored as they are, so each file's bytes must appear in the assembly; an assembly built
 * before the last client build (`dotnet pack --no-build`) holds older bytes.
 * @param {string} projectRoot
 * @param {string} assembly
 * @returns {string[]}
 */
export function assemblyProblems(projectRoot, assembly) {
  if (!existsSync(assembly)) return [`${assembly} does not exist: build the Web project before packing`];
  const bytes = readFileSync(assembly);
  const stale = outputFiles(projectRoot)
    .filter((file) => file.startsWith(`${COMPRESSED_FOLDER}/`))
    .filter((file) => !bytes.includes(readFileSync(join(projectRoot, file))));
  return stale.length > 0
    ? [`the assembly does not embed the current ${stale.join(', ')}: build the Web project again before packing`]
    : [];
}

/**
 * Checks the built files and records the stamp.
 * @param {{ root?: string, projectRoot?: string, version?: string, licenseText?: string | undefined }} [options]
 */
export function write({
  root = clientRoot,
  projectRoot = defaultProjectRoot,
  version = buildVersion(),
  licenseText,
} = {}) {
  const problems = outputProblems({ projectRoot, version, licenseText });
  if (problems.length > 0) throw new Error(problems.join('\n'));
  const stamp = { version, inputsHash: inputsHash(root), outputs: outputFiles(projectRoot) };
  mkdirSync(join(root, '.build'), { recursive: true });
  writeFileSync(stampFile(root), `${JSON.stringify(stamp, null, 2)}\n`);
  return stamp;
}

/**
 * Why the built assets cannot be packed as the given package version, or null when they can. With `assembly`, the
 * Web assembly that will be packed must embed the current website assets too.
 * @param {{ root?: string, projectRoot?: string, packageVersion?: string, licenseText?: string | undefined, assembly?: string }} [options]
 * @returns {string | null}
 */
export function check({
  root = clientRoot,
  projectRoot = defaultProjectRoot,
  packageVersion,
  licenseText,
  assembly,
} = {}) {
  const fix =
    'build them with `node scripts/pack.mjs`, or `npm run build` in src/Jiron.Umbraco.FormBuilder.Web/Client with JFB_VERSION set to the package version';
  if (!existsSync(stampFile(root))) return `the client assets were never built: ${fix}`;
  const stamp = JSON.parse(readFileSync(stampFile(root), 'utf8'));
  if (stamp.inputsHash !== inputsHash(root)) return `the client sources changed since the last build: ${fix}`;
  if (packageVersion && packageVersion !== stamp.version)
    return `the client assets were built for ${stamp.version}, but the package is ${packageVersion}: ${fix}`;
  const extra = outputFiles(projectRoot).filter((file) => !stamp.outputs.includes(file));
  if (extra.length > 0) return `files appeared in the output after the build (${extra.join(', ')}): ${fix}`;
  const problems = outputProblems({
    projectRoot,
    version: stamp.version,
    licenseText,
    packing: Boolean(packageVersion),
  });
  if (problems.length > 0) return `${problems.join('; ')}: ${fix}`;
  // The assets themselves are fine; each assembly problem says how to fix it.
  const embedded = assembly ? assemblyProblems(projectRoot, assembly) : [];
  return embedded.length > 0 ? embedded.join('; ') : null;
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const [command, packageVersion, assembly] = process.argv.slice(2);
  if (command === 'write') {
    try {
      const stamp = write();
      console.log(
        `stamp: ${stamp.outputs.length} file(s) for ${stamp.version}, inputs ${stamp.inputsHash.slice(0, 12)}`,
      );
    } catch (error) {
      console.error(`stamp: ${error instanceof Error ? error.message : String(error)}`);
      process.exit(1);
    }
  } else if (command === 'check') {
    const problem = check({ ...(packageVersion ? { packageVersion } : {}), ...(assembly ? { assembly } : {}) });
    if (problem) {
      console.error(`error JFB001: ${problem}`);
      process.exit(1);
    }
    console.log('client assets: up to date');
  } else {
    console.error('usage: node scripts/stamp.mjs write | check [packageVersion] [assembly]');
    process.exit(2);
  }
}
