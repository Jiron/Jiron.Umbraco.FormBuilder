#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Secret scanner for this repository (ROADMAP 0.2, CLAUDE.md rule 3).
 *
 *   node scripts/secret-scan.mjs            scan the staged files (the pre-commit hook uses this)
 *   node scripts/secret-scan.mjs --tracked  scan every tracked file, as it is in the index
 *   node scripts/secret-scan.mjs --history  scan every line ever added on any ref (`git log -p --all`)
 *
 * Patterns are shape-based, so prose that merely mentions a kind of secret does not match. Test fakes that
 * look like secrets carry the marker JFB_FAKE. No matched value is ever printed: a hit names the file, the
 * line, the rule and a fingerprint that `.secret-scan-allowlist.json` can accept for that one path.
 *
 * Exit codes: 0 clean, 1 hits, 2 usage or git error.
 */
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { forEachAddedLine, historyPaths, indexFiles, isBinary, repoRoot } from './lib/git.mjs';

/** Test fakes that look like secrets must contain this exact marker. */
export const FAKE_MARKER = 'JFB_FAKE';
export const ALLOWLIST_FILE = '.secret-scan-allowlist.json';

/** Key names that hold secrets in JSON or appsettings files: the name ends with one of these. */
const SECRET_KEY_NAME =
  /(?:api[-_]?key|(?:access|secret|private|signing|encryption|client|server)[-_]?key|password|passwd|pwd|secret|token)$/i;

/** A value that only points at a secret: a configuration reference or a placeholder. */
const PLACEHOLDER = /^[$({<%]/;

/**
 * The shape-based rules. `check` (optional) decides whether a regex match is a real hit; the matched value
 * (or the `value` capture group) is never a hit when it contains the fake marker.
 */
export const RULES = [
  { id: 'anthropic-key', pattern: /sk-ant-[A-Za-z0-9_-]{24,}/g },
  // Anthropic keys also fit this shape; the anthropic-key rule reports those.
  { id: 'openai-key', pattern: /sk-(?:proj-)?[A-Za-z0-9_-]{32,}/g, check: (m) => !m[0].startsWith('sk-ant-') },
  { id: 'aws-access-key', pattern: /AKIA[0-9A-Z]{16}/g },
  { id: 'github-token', pattern: /gh[pousr]_[A-Za-z0-9]{36}/g },
  { id: 'github-pat', pattern: /github_pat_[A-Za-z0-9_]{60,}/g },
  { id: 'slack-token', pattern: /xox[abprs]-[A-Za-z0-9-]{10,}/g },
  // Incoming-webhook addresses are secrets: anyone who has one can post (ROADMAP 8.4).
  { id: 'slack-webhook', pattern: /https:\/\/hooks\.slack\.com\/(?:services|workflows|triggers)\/[A-Za-z0-9_/-]{20,}/g },
  { id: 'teams-webhook', pattern: /https:\/\/[A-Za-z0-9-]+\.webhook\.office\.com\/webhookb2\/[A-Za-z0-9@/-]{20,}/g },
  // Signed addresses (Azure SAS, Power Automate workflows) carry their secret in a sig parameter.
  { id: 'signed-url', pattern: /[?&]sig=[A-Za-z0-9%_-]{20,}/g },
  { id: 'private-key', pattern: /-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----/g },
  { id: 'google-api-key', pattern: /AIza[0-9A-Za-z_-]{35}/g },
  // reCAPTCHA keys (site and secret share the shape) and hCaptcha secrets (ROADMAP 7.2, D22).
  { id: 'recaptcha-key', pattern: /\b6L[0-9A-Za-z_-]{38}\b/g },
  { id: 'hcaptcha-secret', pattern: /\b0x[0-9a-fA-F]{40}\b/g },
  { id: 'stripe-live-key', pattern: /\b(?:sk|rk)_live_[0-9A-Za-z]{16,}/g },
  { id: 'sendgrid-key', pattern: /\bSG\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}\b/g },
  { id: 'nuget-key', pattern: /\boy2[a-z0-9]{43}\b/g },
  { id: 'npm-token', pattern: /\bnpm_[A-Za-z0-9]{36}\b/g },
  { id: 'jwt', pattern: /\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{16,}/g },
  { id: 'azure-account-key', pattern: /AccountKey=[A-Za-z0-9+/]{40,}={0,2}/g },
  {
    // A user and password in an address, such as smtp://user:<password>@host with a real password. Placeholders are not hits.
    id: 'url-credentials',
    pattern: /\b[a-z][a-z0-9+.-]*:\/\/([^/\s:@'"]+):([^/\s:@'"]{3,})@[A-Za-z0-9.-]+/gi,
    value: 2,
    check: (m) => !PLACEHOLDER.test(m[2]) && !PLACEHOLDER.test(m[1]),
  },
  {
    // A password in a connection string: the same line names the server, the user or the database.
    // Placeholders such as $VAR, {password}, <password> or %PASSWORD% are not hits.
    id: 'connection-string-password',
    pattern: /(?:^|[;"'\s])(?:Password|Pwd)\s*=\s*([^;"'\s]{4,})/gi,
    value: 1,
    check: (m) => !PLACEHOLDER.test(m[1]) && /(?:Server|Data Source|Host|User ID|Uid|Username|Database|Initial Catalog)\s*=/i.test(m.input),
  },
  {
    id: 'json-secret',
    pattern: /"([^"\\\r\n]{1,200})"\s*:\s*"((?:[^"\\\r\n]|\\.)*)"/g,
    value: 2,
    check: (m) => SECRET_KEY_NAME.test(m[1]) && m[2].length >= 12 && !m[2].startsWith('$'),
  },
];

/** SHA-256 (hex) of one line, the fingerprint that the allowlist accepts. */
export function fingerprint(line) {
  return createHash('sha256').update(line, 'utf8').digest('hex');
}

/** Returns why a path may never be committed, or null when it may. */
export function forbiddenPathReason(path) {
  const segments = path.replace(/\\/g, '/').split('/');
  const name = segments[segments.length - 1];
  const folders = segments.slice(0, -1);
  if (folders.includes('.private')) return "the owner's git-ignored .private/ folder";
  if (folders.includes('.local')) return 'the git-ignored .local/ folder';
  if (/-key\.txt$/i.test(name)) return 'a key file (*-key.txt)';
  if (/\.local\.json$/i.test(name)) return 'local settings (*.Local.json)';
  if (/^\.env/i.test(name)) return 'an environment file (.env*)';
  if (/^appsettings\.(?:Production|Staging)\.json$/i.test(name)) {
    return "a live site's settings (appsettings.Production.json, appsettings.Staging.json)";
  }
  if (/\.(?:pem|pfx|p12|key)$/i.test(name)) return 'a key or certificate file (*.pem, *.pfx, *.p12, *.key)';
  return null;
}

/** Loads and validates the allowlist: `{ entries: [{ path, sha256, reason }] }`. */
export function loadAllowlist(root) {
  const file = join(root, ALLOWLIST_FILE);
  if (!existsSync(file)) return [];
  const parsed = JSON.parse(readFileSync(file, 'utf8'));
  const entries = Array.isArray(parsed.entries) ? parsed.entries : [];
  for (const entry of entries) {
    const valid =
      typeof entry.path === 'string' &&
      /^[0-9a-f]{64}$/.test(entry.sha256 ?? '') &&
      typeof entry.reason === 'string' &&
      entry.reason.trim().length > 0;
    if (!valid) throw new Error(`${ALLOWLIST_FILE}: every entry needs a path, a sha256 and a reason`);
  }
  return entries;
}

/** Scans one line. Returns hits `{ path, line, rule, fingerprint }`; never the matched value. */
export function scanLine(path, text, lineNumber, { allowlist = [] } = {}) {
  const rules = new Set();
  for (const rule of RULES) {
    for (const match of text.matchAll(rule.pattern)) {
      const value = rule.value ? match[rule.value] : match[0];
      if (value.includes(FAKE_MARKER)) continue;
      if (rule.check && !rule.check(match)) continue;
      rules.add(rule.id);
      break;
    }
  }
  if (rules.size === 0) return [];
  const print = fingerprint(text);
  if (allowlist.some((entry) => entry.path === path && entry.sha256 === print)) return [];
  return [...rules].map((rule) => ({ path, line: lineNumber, rule, fingerprint: print }));
}

/** Scans a text, line by line (line numbers start at 1). */
export function scanText(path, text, context = {}) {
  const hits = [];
  text.split(/\r?\n/).forEach((line, index) => hits.push(...scanLine(path, line, index + 1, context)));
  return hits;
}

/** Scans file content. Binary content has no lines to scan. */
export function scanBuffer(path, buffer, context = {}) {
  return isBinary(buffer) ? [] : scanText(path, buffer.toString('utf8'), context);
}

function pathHits(paths) {
  const hits = [];
  for (const path of paths) {
    const reason = forbiddenPathReason(path);
    if (reason) hits.push({ path, line: 0, rule: 'forbidden-path', fingerprint: '', reason });
  }
  return hits;
}

/**
 * Runs a scan. `mode` is 'staged', 'tracked' or 'history'. Returns `{ exitCode, output }`, where `output` is
 * the list of lines to print.
 */
export async function run({ mode = 'staged', cwd = process.cwd() } = {}) {
  let root;
  try {
    root = repoRoot(cwd);
  } catch {
    return { exitCode: 2, output: ['secret-scan: not inside a git repository'] };
  }

  let context;
  try {
    context = { allowlist: loadAllowlist(root) };
  } catch (error) {
    return { exitCode: 2, output: [`secret-scan: ${error.message}`] };
  }

  const hits = [];
  let summary;
  try {
    if (mode === 'history') {
      hits.push(...pathHits(historyPaths(root)));
      const commits = await forEachAddedLine(root, ({ commit, path, line, text }) => {
        for (const hit of scanLine(path, text, line, context)) hits.push({ ...hit, commit });
      });
      summary = `${commits} commits`;
    } else {
      const files = indexFiles(root, mode);
      hits.push(...pathHits(files.map((file) => file.path)));
      for (const file of files) hits.push(...scanBuffer(file.path, file.content, context));
      summary = `${files.length} files`;
    }
  } catch (error) {
    return { exitCode: 2, output: [`secret-scan: ${error.message}`] };
  }

  if (hits.length === 0) return { exitCode: 0, output: [`secret-scan: clean (${mode}, ${summary})`] };

  const output = [`secret-scan: ${hits.length} hit(s) (${mode}, ${summary})`];
  for (const hit of hits) {
    const where = `${hit.commit ? `commit ${hit.commit} ` : ''}${hit.path}${hit.line ? `:${hit.line}` : ''}`;
    if (hit.rule === 'forbidden-path') output.push(`  ${where}  forbidden-path  (${hit.reason})`);
    else output.push(`  ${where}  ${hit.rule}  fingerprint ${hit.fingerprint}`);
  }
  output.push(`False positive? Add {"path", "sha256": "<fingerprint>", "reason"} to ${ALLOWLIST_FILE}.`);
  return { exitCode: 1, output };
}

function parseMode(args) {
  const known = { '--tracked': 'tracked', '--history': 'history', '--staged': 'staged' };
  const modes = args.map((arg) => known[arg]);
  if (modes.some((mode) => !mode) || modes.length > 1) return null;
  return modes[0] ?? 'staged';
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const mode = parseMode(process.argv.slice(2));
  if (!mode) {
    console.error('usage: node scripts/secret-scan.mjs [--staged | --tracked | --history]');
    process.exit(2);
  }
  const { exitCode, output } = await run({ mode });
  (exitCode === 0 ? console.log : console.error)(output.join('\n'));
  process.exit(exitCode);
}
