// The inspector's merge tags (ROADMAP 9.4c, ADR-0173): a text's place, a tag put in at the caret, and the paths grouped
// by their source and found by what the editor types, from the merge tag paths endpoint's snapshot.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import type { MergeTagPathResponseModel } from '../../api';
import { groupPaths, placeOf, tagOf, withTag, type PathWords } from './merge-tags';

const root = join(dirname(fileURLToPath(import.meta.url)), ...Array<string>(8).fill('..'));
const served = JSON.parse(readFileSync(join(root, 'docs', 'api', 'merge-tag-paths.json'), 'utf8')) as Record<
  string,
  MergeTagPathResponseModel[]
>;
const visitor = served['visitorText'] ?? [];

const words: PathWords = {
  source: (source) => ({ field: 'Answers', form: 'The form', site: 'The site' })[source],
  path: (path) => ({ 'form.name': 'Name of the form' })[path],
  own: "The site's own",
};

describe('a merge tag', () => {
  it("is written for its text's place: a consent's wording its own, any other setting a visitor's text", () => {
    expect(placeOf('consent', 'text')).toBe('consentText');
    expect(placeOf('heading', 'text')).toBe('visitorText');
    expect(placeOf('paragraph', 'html')).toBe('visitorText');
    expect(tagOf('form.name')).toBe('{{ form.name }}');
  });

  it('goes in where the caret is, replacing what is selected, with the caret after it', () => {
    expect(withTag('Hello !', 6, 6, '{{ x }}')).toEqual({ text: 'Hello {{ x }}!', caret: 13 });
    expect(withTag('Hello you', 6, 9, '{{ x }}')).toEqual({ text: 'Hello {{ x }}', caret: 13 });
    expect(withTag('Hi', 9, 3, '{{ x }}')).toEqual({ text: 'Hi{{ x }}', caret: 9 });
  });
});

describe("the picker's paths", () => {
  it("come grouped by source in the server's order, the site's own under one heading, each with its words", () => {
    const groups = groupPaths(visitor, words);
    expect(groups.map((group) => group.heading)).toEqual([
      'Answers',
      'The form',
      'page',
      'The site',
      'date',
      "The site's own",
    ]);
    expect(groups[0]?.paths[0]).toEqual({ path: 'field.name', label: 'Your name' });
    expect(groups[1]?.paths).toEqual([
      { path: 'form.name', label: 'Name of the form' },
      { path: 'form.alias', label: 'form.alias' },
    ]);
    expect(groups.at(-1)?.paths).toEqual([{ path: 'greeting.text', label: 'Greeting' }]);
  });

  it('are found by their words or their path, in any letter case, a source with none left gone', () => {
    expect(groupPaths(visitor, words, 'GUEST').flatMap((group) => group.paths.map((each) => each.path))).toEqual([
      'field.guests',
      'field.guests.guestName',
    ]);
    expect(groupPaths(visitor, words, 'site.u').map((group) => group.heading)).toEqual(['The site']);
    expect(groupPaths(visitor, words, 'nothing like it')).toEqual([]);
  });
});
