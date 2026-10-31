// The form engine's model the browser makes of the form endpoint's answer (ROADMAP 6.4c, ADR-0111), held to the one
// Core's DefinitionLogic makes of the definition by tests/fixtures/definitions/logic.json, which Core's
// LogicModelFixtureTests writes for the full definition fixture and each of the demo's forms.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { logicFormOf, type FieldLogic, type ProjectedForm } from '../src/index.ts';
import { repositoryRoot } from '../tools/repository.ts';

interface FixtureForm {
  readonly form: ProjectedForm;
  readonly fields: Readonly<Record<string, FieldLogic>>;
  readonly logic: unknown;
}

const fixture = JSON.parse(
  readFileSync(join(repositoryRoot(), 'tests', 'fixtures', 'definitions', 'logic.json'), 'utf8'),
) as Readonly<Record<string, FixtureForm>>;

describe("the form engine's model of the endpoint's answer", () => {
  it('holds the full definition and every demo form', () => {
    expect(Object.keys(fixture)).toEqual([
      'full',
      'application',
      'contact',
      'feedback',
      'newsletter',
      'roomHire',
      'specimen',
      'volunteer',
      'workshop',
    ]);
  });

  for (const [name, { form, fields, logic }] of Object.entries(fixture)) {
    it(`is Core's for ${name}`, () => {
      expect(logicFormOf(form, fields)).toEqual(logic);
    });
  }

  it('reads a calculation whose settings are not what Core reads as an empty formula with the default places', () => {
    const form: ProjectedForm = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'a', type: 'calculation', settings: { formula: 7, format: { decimals: 1.5 } } },
                { key: 'b', type: 'calculation', settings: null },
              ],
            },
          ],
        },
      ],
    };
    const logic = { kind: 'number', canBeRequired: false, builtIn: true };
    const fields = logicFormOf(form, { a: logic, b: logic }).pages[0]?.groups?.[0]?.fields;
    expect(fields?.map((field) => field.calculation)).toEqual([
      { formula: '', decimals: null },
      { formula: '', decimals: null },
    ]);
  });

  it("gives a site's own type no calculation, and a field the answer does not know no kind", () => {
    const form: ProjectedForm = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'a', type: 'calculation', settings: { formula: '1' } },
                { key: 'b', type: 'x' },
              ],
            },
          ],
        },
      ],
    };
    const fields = logicFormOf(form, { a: { kind: 'number', canBeRequired: false, builtIn: false } }).pages[0]
      ?.groups?.[0]?.fields;
    expect(fields?.map((field) => [field.calculation, field.kind, field.canBeRequired])).toEqual([
      [null, 'number', false],
      [null, null, false],
    ]);
  });
});
