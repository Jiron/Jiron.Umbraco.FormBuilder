// Answer recall from the form endpoint's answer alone (ADR-0068, ADR-0085): every visitor text the answer names, taken
// from the answer's form, rendered with the answer's recall form, words, formats and values for the given answers, is
// the text the server renders for the same answers. The C# PublicFormRecallTests writes the reviewed fixture
// tests/fixtures/public-api/recall.json from a real site.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  type CultureFormats,
  type DisplayWords,
  type MergeValue,
  type RecallAnswers,
  type RecallForm,
  type TemplateContext,
} from '../src/index.ts';
import { recallAnswers } from '../src/recall.ts';
import { parseTemplate, renderTemplate } from '../src/template.ts';
import { repositoryRoot } from '../tools/repository.ts';

/** A visitor text the answer names: its owner's key, the property it is, and the context it renders in. */
interface NamedText {
  readonly owner: string;
  readonly text: string;
  readonly context: TemplateContext;
  readonly refused?: readonly string[];
}

/** What the answer gives the browser for its visitor texts (FormDefinitionResponseModel.MergeTags). */
interface AnswerMergeTags {
  readonly texts: readonly NamedText[];
  readonly values: Readonly<Record<string, Readonly<Record<string, MergeValue>>>>;
  readonly recall: RecallForm;
}

/** An element of the answer's form, as far as its texts go. */
interface Element {
  readonly key: string;
  readonly [property: string]: unknown;
}

interface Fixture {
  readonly answer: {
    readonly form: {
      readonly pages: readonly (Element & {
        readonly groups: readonly (Element & {
          readonly fields: readonly (Element & { readonly fields: readonly Element[] })[];
        })[];
      })[];
    };
    readonly formats: CultureFormats;
    readonly words: DisplayWords;
    readonly mergeTags: AnswerMergeTags;
  };
  readonly answers: RecallAnswers;
  readonly hidden: readonly string[];
  readonly rendered: readonly { readonly owner: string; readonly text: string; readonly server: string }[];
}

const fixture = JSON.parse(
  readFileSync(join(repositoryRoot(), 'tests', 'fixtures', 'public-api', 'recall.json'), 'utf8'),
) as Fixture;

/** The answer form's pages, groups and fields, a repeatable group's children after it. */
function elements(form: Fixture['answer']['form']): Element[] {
  return form.pages.flatMap((page) => [
    page,
    ...page.groups.flatMap((group) => [group, ...group.fields.flatMap((field) => [field, ...field.fields])]),
  ]);
}

/** The text a visitor text is: the property its name says of the element its owner names. */
function textOf(owner: string, name: string): string {
  const element = elements(fixture.answer.form).find((each) => each.key === owner);
  if (element === undefined) throw new Error(`The answer's form has no element ${owner}`);
  const text = name.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], element);
  if (typeof text !== 'string') throw new Error(`${owner} has no text ${name}`);
  return text;
}

describe("the form endpoint's answer", () => {
  const { formats, words, mergeTags } = fixture.answer;

  it('names every text the server rendered, and only those', () => {
    expect(mergeTags.texts.map(({ owner, text }) => `${owner} ${text}`)).toEqual(
      fixture.rendered.map(({ owner, text }) => `${owner} ${text}`),
    );
  });

  it('names no field as sensitive', () => {
    expect(JSON.stringify(mergeTags.recall)).not.toContain('sensitive');
  });

  it.each(fixture.rendered.map((each) => [`${each.text} of ${each.owner}`, each] as const))(
    'renders %s as the server does',
    (_, { owner, text, server }) => {
      const named = mergeTags.texts.find((each) => each.owner === owner && each.text === text);
      if (named === undefined) throw new Error(`The answer does not name ${text} of ${owner}`);
      const recall = recallAnswers(mergeTags.recall, owner, fixture.answers, fixture.hidden, words, formats);
      const given = mergeTags.values[named.context] ?? {};
      const values = (path: string): MergeValue | undefined =>
        recall.values(path) ?? (Object.hasOwn(given, path) ? given[path] : undefined);
      // A tag of a source the text's place refuses is written as it stands, as the answer tells (ADR-0118).
      const refused = named.refused ?? [];
      const rendered = renderTemplate(
        parseTemplate(textOf(owner, text), named.context, (path) =>
          refused.includes(path) ? 'source_not_for_place' : recall.pathProblem(path),
        ),
        values,
        formats,
      );
      expect(rendered.text).toBe(server);
    },
  );
});
