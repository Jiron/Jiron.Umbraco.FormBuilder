#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Guards NuGetAudit.props (ROADMAP 3.5, ADR-0024). Its suppressions are only for packages that reach Jiron Form
 * Builder transitively through Umbraco, so this fails when:
 *
 *   - a package whose advisories it suppresses is referenced directly: a PackageReference or a PackageVersion in
 *     any project, props or targets file;
 *   - a package that only whole Umbraco sites bring (ImageSharp) is referenced directly at all, suppressed or not
 *     (CLAUDE.md rule 4);
 *   - a suppression for such a package leaves the host-only group, the ItemGroup with the condition
 *     HOST_CONDITION, where it would cover Jiron Form Builder's own packages too;
 *   - a suppression sits outside any ItemGroup, where the groups cannot be told apart.
 *
 * XML comments are ignored, so a comment may name a package.
 *
 *   node scripts/check-audit-suppressions.mjs
 *
 * Exit codes: 0 fine, 1 a rule is broken.
 */
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(fileURLToPath(new URL('..', import.meta.url)));

/** The condition of the ItemGroup whose suppressions apply to Umbraco hosts only (ADR-0024). */
export const HOST_CONDITION = "'$(JfbUmbracoHost)' == 'true'";

/** Packages that only a whole Umbraco site brings: never referenced directly, suppressed only for hosts. */
export const HOST_ONLY = Object.freeze([/^SixLabors\.ImageSharp(?:\..+)?$/i]);

/** Folders that hold no build file of the repository's own. */
const SKIP = new Set(['node_modules', 'bin', 'obj', '.git', '.local', '.private', 'artifacts', 'TestResults']);

/** The text without its XML comments. */
export function withoutComments(text) {
  return text.replace(/<!--[\s\S]*?-->/g, '');
}

function attributes(tag) {
  return Object.fromEntries([...tag.matchAll(/([A-Za-z_][\w.-]*)\s*=\s*"([^"]*)"/g)].map((match) => [match[1], match[2]]));
}

/** True for a package that only whole Umbraco sites bring. */
export function isHostOnly(packageId) {
  return HOST_ONLY.some((pattern) => pattern.test(packageId));
}

/**
 * The suppressed advisories of NuGetAudit.props as [{ url, packageId, condition }]: `condition` is the enclosing
 * ItemGroup's condition ('' without one, null outside any ItemGroup).
 */
export function readSuppressions(propsText) {
  const text = withoutComments(propsText);
  const groups = [...text.matchAll(/<ItemGroup\b([^>]*)>([\s\S]*?)<\/ItemGroup>/g)].map((match) => ({
    start: match.index,
    end: match.index + match[0].length,
    condition: (attributes(match[1]).Condition ?? '').trim(),
  }));
  return [...text.matchAll(/<NuGetAuditSuppress\b([^>]*?)\/?>/g)].map((match) => {
    const { Include: url = '', Package: packageId = '' } = attributes(match[1]);
    const group = groups.find((candidate) => match.index > candidate.start && match.index < candidate.end);
    return { url, packageId, condition: group ? group.condition : null };
  });
}

/** The package ids a build file references (PackageReference) or pins (PackageVersion), as [{ kind, packageId }]. */
export function readReferences(projectText) {
  return [...withoutComments(projectText).matchAll(/<(PackageReference|PackageVersion)\b([^>]*?)\/?>/g)].flatMap((match) => {
    const { Include, Update } = attributes(match[2]);
    const packageId = Include ?? Update;
    return packageId ? [{ kind: match[1], packageId }] : [];
  });
}

/** Every broken rule, as report lines. */
export function findViolations(suppressions, filesWithText) {
  const problems = [];
  const suppressed = new Set(suppressions.map((s) => s.packageId.toLowerCase()));
  for (const { file, text } of filesWithText) {
    for (const ref of readReferences(text)) {
      if (suppressed.has(ref.packageId.toLowerCase())) {
        problems.push(`${file} has a ${ref.kind} for ${ref.packageId}, whose advisories NuGetAudit.props suppresses (ADR-0024)`);
      } else if (isHostOnly(ref.packageId)) {
        problems.push(`${file} has a ${ref.kind} for ${ref.packageId}, which only Umbraco sites may bring (CLAUDE.md rule 4)`);
      }
    }
  }
  for (const s of suppressions) {
    if (s.condition === null) {
      problems.push(`NuGetAudit.props: the suppression of ${s.url} (${s.packageId}) is outside an ItemGroup`);
    } else if (isHostOnly(s.packageId) && s.condition !== HOST_CONDITION) {
      problems.push(`NuGetAudit.props: ${s.packageId}'s ${s.url} left the host-only group (Condition="${HOST_CONDITION}", ADR-0024)`);
    }
  }
  return problems;
}

/** The repository's project, props and targets files below a folder, NuGetAudit.props left out. */
export function* buildFiles(dir) {
  for (const name of readdirSync(dir)) {
    if (SKIP.has(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      yield* buildFiles(path);
    } else if (/\.(csproj|props|targets)$/i.test(name) && name !== 'NuGetAudit.props') {
      yield path;
    }
  }
}

/** The build files below a root as [{ file, text }], with repository-relative paths in forward slashes. */
export function buildFileTexts(root) {
  return [...buildFiles(root)].map((file) => ({ file: relative(root, file).replaceAll('\\', '/'), text: readFileSync(file, 'utf8') }));
}

/** Checks the repository under a root. */
export function run(root = repo) {
  const suppressions = readSuppressions(readFileSync(join(root, 'NuGetAudit.props'), 'utf8'));
  const files = buildFileTexts(root);
  return { suppressions, problems: findViolations(suppressions, files), checked: files.length };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { suppressions, problems, checked } = run();
  if (problems.length > 0) {
    for (const problem of problems) console.error(`check-audit-suppressions: ${problem}`);
    process.exit(1);
  }
  const packages = new Set(suppressions.map((s) => s.packageId));
  const hostOnly = suppressions.filter((s) => s.condition === HOST_CONDITION).length;
  console.log(
    `check-audit-suppressions: ok (${suppressions.length} advisories on ${packages.size} transitive packages, ` +
      `${hostOnly} of them for Umbraco hosts only; ${checked} build files checked)`,
  );
}
