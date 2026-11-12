// The configuration reference (ROADMAP 17.2b): docs/configuration.md is what the appsettings schema, which a unit test
// generates from the settings types, says, every key with its type, default and description; and it is current.
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { cell, PAGE, render, rowsOf, SCHEMA } from '../configuration-docs.mjs';

const schema = JSON.parse(readFileSync(SCHEMA, 'utf8'));

test('a cell keeps to one line, escapes its pipes and shows anything like a tag as text', () => {
  assert.equal(cell('Load it with\n  <jfb-assets> | here & now'), 'Load it with &lt;jfb-assets&gt; \\| here &amp; now');
});

test("a part's rows follow its objects, its lists of objects and its maps, by their paths", () => {
  const rows = rowsOf(
    {
      properties: {
        Read: { type: ['object', 'null'], properties: { PerMinute: { type: 'integer', default: 120, description: 'Per minute.' } } },
        Keys: {
          type: ['array', 'null'],
          items: { type: 'object', properties: { Name: { type: 'string', default: '', description: 'Its name.' } } },
          description: 'The keys.',
        },
        Secrets: { type: ['object', 'null'], additionalProperties: { type: 'string' }, description: 'Named secrets.' },
        Hosts: { type: ['array', 'null'], items: { type: 'string' }, description: 'Hosts.' },
      },
    },
    ['Part'],
  );
  assert.deepEqual(
    rows.map((row) => [row.key, row.type, row.fallback]),
    [
      ['Part:Read:PerMinute', 'whole number', '`120`'],
      ['Part:Keys', 'list of objects', 'none'],
      ['Part:Keys[]:Name', 'text', 'none'],
      ['Part:Secrets:<name>', 'text', ''],
      ['Part:Hosts', 'list of texts', 'none'],
    ],
  );
});

test('every setting the schema holds has its row, with its default', () => {
  const page = render(schema);
  const section = schema.properties.JironFormBuilder.properties;
  for (const name of Object.keys(section)) assert.match(page, new RegExp(`^## ${name}$`, 'm'), name);
  assert.match(page, /^\| `PublicApi:BasePath` \| text \| `\/jfb\/api` \|/m);
  assert.match(page, /^\| `Limits:Read:PerAddressPerMinute` \| whole number \| `120` \|/m);
  assert.match(page, /^\| `Actions:Paused` \| true or false \| `false` \|/m);
});

test('docs/configuration.md is current; run node scripts/configuration-docs.mjs after changing a setting', () => {
  assert.equal(readFileSync(PAGE, 'utf8'), render(schema));
});
