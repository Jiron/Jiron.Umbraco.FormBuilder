#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Makes the TypeScript types of the visitor form (schemas/jfb-public-form.v1.d.ts) from its JSON schema
 * (schemas/jfb-public-form.v1.schema.json), which a unit test writes from the projection's records (ADR-0125): a type
 * per definition of the schema, an object type for a record, a union for one of several kinds. It reads only what that
 * schema uses, and refuses anything else, so a schema it cannot say fails here instead of becoming a loose type.
 *
 *   node scripts/generate-public-types.mjs
 *
 * scripts/tests/generate-public-types.test.mjs fails when the committed types are not the ones the committed schema
 * makes.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The committed schema and the types made from it. */
export const schemaPath = join(root, 'schemas', 'jfb-public-form.v1.schema.json');
export const typesPath = join(root, 'schemas', 'jfb-public-form.v1.d.ts');

const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]*$/;
const DEFINITION = /^#\/\$defs\/([A-Za-z][A-Za-z0-9]*)$/;

/**
 * The TypeScript for a schema.
 * @param {Record<string, unknown>} schema
 * @returns {string}
 */
export function typesOf(schema) {
  const definitions = /** @type {Record<string, Record<string, unknown>>} */ (schema['$defs'] ?? {});
  const rootName = referenced(schema['$ref']);
  const lines = [
    '// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1',
    '// Made by scripts/generate-public-types.mjs from jfb-public-form.v1.schema.json; do not edit (ADR-0125).',
    '',
    ...comment(String(schema['description'] ?? '')),
    `export type JfbPublicForm = ${rootName};`,
  ];
  for (const name of Object.keys(definitions).sort()) {
    const definition = definitions[name];
    lines.push('');
    if (isRecord(definition)) lines.push(`export type ${name} = ${record(definition, '')};`);
    else lines.push(`export type ${name} = ${type(definition, '')};`);
  }
  return lines.join('\n') + '\n';
}

/** @param {unknown} value */
function referenced(value) {
  const match = typeof value === 'string' ? DEFINITION.exec(value) : null;
  if (match === null) throw new Error(`generate-public-types: a reference the types cannot follow: ${JSON.stringify(value)}`);
  return match[1];
}

/** @param {Record<string, unknown>} node */
function isRecord(node) {
  return node['type'] === 'object' && typeof node['properties'] === 'object';
}

/**
 * An object with its properties, those not required optional.
 * @param {Record<string, unknown>} node
 * @param {string} indent
 */
function record(node, indent) {
  const properties = /** @type {Record<string, Record<string, unknown>>} */ (node['properties']);
  const required = new Set(/** @type {string[]} */ (node['required'] ?? []));
  if (node['additionalProperties'] !== false) {
    throw new Error('generate-public-types: a record that allows other properties');
  }
  const inner = indent + '  ';
  const members = Object.entries(properties).map(([name, value]) => {
    const key = IDENTIFIER.test(name) ? name : JSON.stringify(name);
    return `${inner}${key}${required.has(name) ? '' : '?'}: ${type(value, inner)};`;
  });
  return ['{', ...members, `${indent}}`].join('\n');
}

/**
 * The type of a schema node.
 * @param {Record<string, unknown>} node
 * @param {string} indent
 * @returns {string}
 */
function type(node, indent) {
  if ('$ref' in node) return referenced(node['$ref']);
  if ('const' in node) return JSON.stringify(node['const']);
  if (Array.isArray(node['enum'])) return node['enum'].map((value) => JSON.stringify(value)).join(' | ');
  if (Array.isArray(node['oneOf'])) {
    return node['oneOf'].map((item) => type(/** @type {Record<string, unknown>} */ (item), indent)).join(' | ');
  }
  switch (node['type']) {
    case 'string':
      return 'string';
    case 'integer':
    case 'number':
      return 'number';
    case 'boolean':
      return 'boolean';
    case 'array': {
      const items = type(/** @type {Record<string, unknown>} */ (node['items']), indent);
      return /[|&]/.test(items) ? `(${items})[]` : `${items}[]`;
    }
    case 'object':
      if (isRecord(node)) return record(node, indent);
      if (typeof node['additionalProperties'] === 'object') {
        return `Record<string, ${type(/** @type {Record<string, unknown>} */ (node['additionalProperties']), indent)}>`;
      }
      return 'Record<string, unknown>';
    default:
      throw new Error(`generate-public-types: a schema node the types cannot say: ${JSON.stringify(node)}`);
  }
}

/** A JSDoc block of a text, wrapped at 116 columns. @param {string} text */
function comment(text) {
  if (text.length === 0) return [];
  const lines = ['/**'];
  let line = ' *';
  for (const word of text.split(/\s+/)) {
    if (line.length + word.length + 1 > 116) {
      lines.push(line);
      line = ' *';
    }
    line += ' ' + word;
  }
  lines.push(line, ' */');
  return lines;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const schema = JSON.parse(readFileSync(schemaPath, 'utf8'));
  writeFileSync(typesPath, typesOf(schema));
  console.log(`generate-public-types: wrote ${typesPath}`);
}
