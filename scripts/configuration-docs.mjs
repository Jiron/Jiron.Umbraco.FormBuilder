#!/usr/bin/env node
// Writes docs/configuration.md, the reference of Jiron Form Builder's host configuration (ROADMAP 17.2b), from the
// appsettings JSON schema that the Core package ships (ADR-0008). A unit test generates that schema from the settings
// types and fails when it is stale, so the page comes from the types: every key with its type, its default and what it
// does, in the order the types declare them. scripts/tests/configuration-docs.test.mjs fails when the page is stale.
// Usage: node scripts/configuration-docs.mjs [--check]
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The schema the Core package ships. */
export const SCHEMA = join(root, 'src', 'Jiron.Umbraco.FormBuilder.Core', 'appsettings-schema.Jiron.Umbraco.FormBuilder.json');

/** The page this writes. */
export const PAGE = join(root, 'docs', 'configuration.md');

const SECTION = 'JironFormBuilder';

/** Text for a Markdown table cell: no line breaks, pipes escaped, and anything like a tag shown as text. */
export function cell(text) {
  return String(text)
    .replace(/\s+/g, ' ')
    .trim()
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('|', '\\|');
}

const types = (node) => (Array.isArray(node.type) ? node.type : [node.type]).filter((type) => type && type !== 'null');

/** A setting's type in words. */
function typeOf(node) {
  const [type] = types(node);
  if (type === 'array') {
    const [item] = types(node.items ?? {});
    return item === 'object' ? 'list of objects' : `list of ${item === 'string' ? 'texts' : `${item ?? 'value'}s`}`;
  }
  return { string: 'text', integer: 'whole number', number: 'number', boolean: 'true or false' }[type] ?? type ?? 'value';
}

/** A setting's default, as a Markdown cell. */
function defaultOf(node) {
  if (!Object.hasOwn(node, 'default')) return types(node)[0] === 'array' ? 'none' : '';
  const value = node.default;
  if (value === '' || value === null) return 'none';
  if (typeof value === 'number') return `\`${value}\``;
  return `\`${cell(typeof value === 'string' ? value : JSON.stringify(value))}\``;
}

/**
 * The rows of a section: each setting below it, by its path from the section (`Limits:Read:SitePerSecond`), with an
 * object's settings after it, a list of objects' as `Name[]:Setting`, and a map's as `Name:<name>`.
 */
export function rowsOf(node, path = []) {
  const rows = [];
  for (const [name, child] of Object.entries(node.properties ?? {})) {
    const key = [...path, name];
    const [type] = types(child);
    if (type === 'object' && child.properties) {
      rows.push(...rowsOf(child, key));
      continue;
    }
    if (type === 'object' && child.additionalProperties) {
      // A map: each of its names is a key of its own.
      const value = { ...child.additionalProperties, description: child.description };
      rows.push({ key: `${key.join(':')}:<name>`, type: typeOf(value), fallback: '', about: child.description ?? '' });
      continue;
    }
    rows.push({ key: key.join(':'), type: typeOf(child), fallback: defaultOf(child), about: child.description ?? '' });
    if (type === 'array' && types(child.items ?? {})[0] === 'object')
      rows.push(...rowsOf(child.items, [...path, `${name}[]`]));
  }
  return rows;
}

const HEAD = `# Configuration

Jiron Form Builder reads its host configuration from the \`${SECTION}\` section of the site's configuration:
\`appsettings.json\`, an environment's \`appsettings.<Environment>.json\`, environment variables or user secrets, as
any ASP.NET Core setting. Most of what editors and administrators change lives in the backoffice instead, under
**Form Builder \u203a Settings**; this section is for developers, for the keys that must never be in the database, and for
locks that the backoffice then shows read-only.

> This page is generated from the settings types by \`node scripts/configuration-docs.mjs\`, through the appsettings
> schema that the package ships. Don't edit it by hand: a test fails when it is out of date.

## How the section works

- **Checked at start:** the section is read once when the site starts. A value out of range, or a key Jiron Form
  Builder does not know, stops the site with a message that names the key, so a typing mistake never goes unnoticed.
- **A change needs a restart,** except \`Actions:Paused\`, which takes effect at once.
- **Your editor helps:** the Core package adds \`appsettings-schema.Jiron.Umbraco.FormBuilder.json\` to the site's
  \`appsettings-schema.json\` when the site builds, so editors that read JSON schemas suggest and explain every key.
- **Secrets stay out of files you commit:** keep keys such as a captcha's secret key in user secrets, environment
  variables or your host's secret store. Settings that need a secret name it by reference, as
  \`$${SECTION}:Secrets:<Name>\`, so the value itself never reaches the database or a screen.
- **Environment variables** use two underscores between the parts of a key, and a list's items are numbered from 0:
  \`${SECTION}__PublicApi__BasePath\`, \`${SECTION}__Webhooks__AllowedHosts__0\`.

\`\`\`json
{
  "${SECTION}": {
    "PublicApi": { "AllowedOrigins": [ "https://www.example.com" ] },
    "Webhooks": { "AllowedHosts": [ "hooks.example.com" ] },
    "LockedSections": [ "privacy" ]
  }
}
\`\`\`

Each table below lists a part of the section: the key below \`${SECTION}\`, what kind of value it takes, its default
(empty where the setting has none of its own), and what it does.
`;

/** The page, from the schema. */
export function render(schema) {
  const section = schema.properties[SECTION];
  const parts = [HEAD];
  for (const [name, node] of Object.entries(section.properties)) {
    // A part with settings of its own says what it is above them; a part that is one setting says it in its row.
    const grouped = types(node)[0] === 'object' && node.properties;
    parts.push(grouped ? `\n## ${name}\n\n${cell(node.description ?? '')}\n` : `\n## ${name}\n`);
    const rows = grouped ? rowsOf(node, [name]) : rowsOf({ properties: { [name]: node } });
    parts.push('\n| Key | Type | Default | What it does |\n| --- | --- | --- | --- |\n');
    for (const row of rows) parts.push(`| \`${row.key}\` | ${row.type} | ${row.fallback} | ${cell(row.about)} |\n`);
  }
  return parts.join('');
}

function main() {
  const page = render(JSON.parse(readFileSync(SCHEMA, 'utf8')));
  if (process.argv.includes('--check')) {
    if (readFileSync(PAGE, 'utf8') !== page) {
      console.error('configuration-docs: docs/configuration.md is out of date; run node scripts/configuration-docs.mjs');
      process.exit(1);
    }
    console.log('configuration-docs: docs/configuration.md is current');
    return;
  }
  writeFileSync(PAGE, page);
  console.log('configuration-docs: wrote docs/configuration.md');
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) main();
