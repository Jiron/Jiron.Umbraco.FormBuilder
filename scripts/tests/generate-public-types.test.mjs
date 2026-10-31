// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The types made from the server's schemas (ADR-0125, ADR-0128): the committed types are the ones the committed schemas
// make, a changed schema changes them, and a schema the generator cannot say fails instead of becoming a loose type.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { schemaPath, targets, typesOf, typesPath } from '../generate-public-types.mjs';

const committed = () => JSON.parse(readFileSync(schemaPath, 'utf8'));

test('the committed types are the ones the committed schema makes: run node scripts/generate-public-types.mjs', () => {
  assert.equal(readFileSync(typesPath, 'utf8').replaceAll('\r\n', '\n'), typesOf(committed()));
});

test("the element answer's committed types are the ones its committed schema makes", () => {
  const target = targets[1];
  const types = typesOf(JSON.parse(readFileSync(target.schemaPath, 'utf8')), target);
  assert.equal(readFileSync(target.typesPath, 'utf8').replaceAll('\r\n', '\n'), types);
  assert.match(types, /^\/\/ Made by scripts\/generate-public-types\.mjs from answer\.schema\.json; do not edit \(ADR-0128\)\.$/m);
  assert.match(types, /^export type JfbElementAnswer = ElementAnswer;$/m);
  assert.match(types, /^ {2}kind: "text";$/m);
});

test('a record is an object type, a property that may be left out optional, one of several kinds a union', () => {
  const types = typesOf(committed());
  assert.match(types, /^export type JfbPublicForm = PublicForm;$/m);
  assert.match(types, /^export type PublicPrefill = \{\n {2}query\?: string;\n {2}context\?: string;\n {2}fromMember: boolean;\n {2}memberMode: "show" \| "hide";\n\};$/m);
  assert.match(types, /^export type FieldCheck = PatternCheck \| MatchesFieldCheck \| UniquePerFormCheck;$/m);
  assert.match(types, /^ {2}kind: "pattern";$/m);
});

test('a changed schema changes the types, so the drift check fails until they are made again', () => {
  const schema = committed();
  delete schema.$defs.PublicPrefill.properties.context;
  schema.$defs.PublicField.properties.width = { type: 'integer' };
  const types = typesOf(schema);
  assert.notEqual(types, typesOf(committed()));
  assert.doesNotMatch(/^export type PublicPrefill = \{[^}]*\};$/m.exec(types)?.[0] ?? 'missing context', /context/);
  assert.match(types, /^ {2}width\?: number;$/m);
});

test('a schema the types cannot say fails', () => {
  const loose = committed();
  loose.$defs.PublicPrefill.additionalProperties = true;
  assert.throws(() => typesOf(loose), /allows other properties/);
  const odd = committed();
  odd.$defs.PublicPrefill.properties.query = { type: 'null' };
  assert.throws(() => typesOf(odd), /cannot say/);
  const far = committed();
  far.$ref = 'https://example.com/schema.json';
  assert.throws(() => typesOf(far), /cannot follow/);
});
