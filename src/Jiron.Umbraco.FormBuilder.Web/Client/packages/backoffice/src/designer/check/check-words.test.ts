// What the form check says of each problem (ROADMAP 9.5f, ADR-0181): the element by its name, and a sentence of the
// user's language with what it names read from the form at the path; the server's sentence for the codes whose words
// only it has.
import { describe, expect, it } from 'vitest';
import start from '../../lang/en';
import workspace from '../../lang/workspace.en';
import type { DesignDefinition } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { elementNames } from '../rules/rule-names';
import { issueWords, OWN_WORDS, SERVER_WORDS, type CheckWordsContext } from './check-words';
import type { CheckIssue, CheckSeverity } from './form-check';

type Dictionary = Record<string, Record<string, unknown>>;

/** A term as the backoffice gives it: the English text, a function given its values, the key where there is none. */
const term = (key: string, ...args: unknown[]): string => {
  const at = key.indexOf('_');
  const text = ({ ...start, ...workspace } as Dictionary)[key.slice(0, at)]?.[key.slice(at + 1)];
  return typeof text === 'function' ? (text as (...values: unknown[]) => string)(...args) : String(text ?? key);
};

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      title: { '': 'About you' },
      groups: [
        {
          key: 'g1',
          title: { '': 'Contact' },
          fields: [
            { key: 'email', type: 'email', alias: 'email', label: { '': 'Email address' } },
            { key: 'cv', type: 'file', alias: 'cv', label: { '': 'CV' }, settings: {} },
            { key: 'logo', type: 'image', settings: { alt: { '': '' } } },
            {
              key: 'topic',
              type: 'dropdown',
              alias: 'topic',
              label: { '': 'Topic' },
              help: { '': '#topicHelp' },
              options: { items: [{ value: 'a', label: { '': '{{ field.nope }}' } }] },
            },
          ],
        },
      ],
    },
    { key: 'p2', groups: [{ key: 'g2', fields: [] }] },
  ],
  settings: {
    appearance: { theme: 'bootstrap9' },
    availability: { memberGroups: ['Gold', '3f2504e0-4f89-41d3-9a0c-0305e82c3301'] },
    submission: { store: 'never', submitLabel: { '': '#send' }, backLabel: { '': '#back' } },
  },
  outcomes: [
    { kind: 'redirect', redirect: { content: 'c1', url: '/x?q={{ field.email }}' } },
    { kind: 'message', rules: [{ key: 'o', effect: 'use', conditions: [] }] },
  ],
  actions: [{ key: 'a1', type: 'email', settings: { subject: '{{ field.nope }}' } }],
  validationRules: [{ key: 'v', field: 'email', conditions: [], message: { '': '#rule' } }],
};

const context: CheckWordsContext = {
  term,
  say: (text) => (text?.startsWith('#') ? term(text.slice(1)) : (text ?? '')),
  types: builtInTypes,
  names: elementNames(DEFINITION, '', term),
};

const F = '$.pages[0].groups[0].fields';

const words = (code: string, path: string, severity: CheckSeverity = 'warning', message = 'The server says so.') =>
  issueWords({ severity, code, path, message } satisfies CheckIssue, DEFINITION, context);

describe("a problem's words", () => {
  it('name the element the problem is on, as the designer names it', () => {
    expect(words('label_missing', `${F}[0].label`).name).toBe('Email address');
    expect(words('page_unreachable', '$.pages[1].rules[0]').name).toBe('Page 2');
    expect(words('invalid_condition', "$.pages[0].groups[0].title['']").name).toBe('Contact');
    expect(words('theme_missing', '$.settings.appearance.theme').name).toBe('Form settings');
    expect(words('last_outcome_has_rule', '$.outcomes[1].rules[0]').name).toBe('After sending: outcome 2');
    expect(words('unknown_path', '$.actions[0].settings.subject').name).toBe('Action 1');
    expect(words('unknown_field', '$.validationRules[0].field').name).toBe('Email address');
    expect(words('cycle', '$').name).toBe('The form');
    expect(words('fields_missing', '$.pages[0].groups[0].fields', 'error').name).toBe('Add at least one field');
  });

  it('say what is wrong, naming the text a merge tag is in', () => {
    expect(words('unknown_path', `${F}[3].options.items[0].label['']`).said).toBe(
      'A merge tag in "Option 1" names nothing, so it shows nothing.',
    );
    expect(words('field_after_text', `${F}[0].label['']`, 'error').said).toBe(
      'A merge tag in "Label" recalls the answer of a field that comes after this text, so visitors haven\'t given it yet.',
    );
    expect(words('unknown_path', '$.actions[0].settings.subject').said).toBe(
      'A merge tag in "subject" names nothing, so it shows nothing.',
    );
    expect(words('tag_in_markup', "$.outcomes[1].message['']", 'error').said).toBe(
      'A merge tag in "Message" sits inside the text\'s markup, where it can\'t be shown.',
    );
    expect(words('unknown_path', '$').said).toBe('A merge tag names nothing, so it shows nothing.');
  });

  it("name a type's setting by its label, a theme, a member group, an alias and a dictionary key from the form", () => {
    expect(words('setting_missing', `${F}[2].settings.alt['']`, 'error').said).toBe(
      '"Alternative text" needs a value.',
    );
    expect(words('lock_replaces_value', `${F}[1].settings.maxBytes`).said).toBe(
      'The site locks "Largest file", so the form runs with the site\'s value instead of the one it holds.',
    );
    expect(words('theme_missing', '$.settings.appearance.theme').said).toBe(
      'The site has no theme "bootstrap9", so the form shows with the default theme.',
    );
    expect(words('member_group_missing', '$.settings.availability.memberGroups[0]').said).toContain(
      'no member group named "Gold"',
    );
    expect(words('member_group_missing', '$.settings.availability.memberGroups[1]').said).toBe(
      'The site has no such member group any more, so no member is in it.',
    );
    expect(words('dictionary_text_missing', `${F}[3].help['']`).said).toContain('"topicHelp" in "Help text"');
    expect(words('alias_duplicate', `${F}[0].alias`, 'error').said).toBe(
      'Another field has the alias "email" already.',
    );
    expect(words('alias_invalid', `${F}[2].alias`, 'error').said).toBe(
      'This field has no alias, which the entries keep its answer under.',
    );
    expect(words('dictionary_text_missing', "$.settings.submission.submitLabel['']").said).toContain(
      '"send" in "Button text"',
    );
    expect(words('dictionary_text_missing', "$.settings.submission.backLabel['']").said).toContain(
      '"back" in "Button text"',
    );
    expect(words('dictionary_text_missing', '$.settings.submission.store').said).not.toContain('Button text');
  });

  it("tell a type the site lacks by where it is, a page by its number, and kept limits by the form's storage", () => {
    expect(words('unknown_type', '$.actions[0].type', 'error').said).toBe('The site has no action type "email".');
    expect(words('unknown_type', `${F}[0].type`, 'error').said).toBe('The site has no field type "email".');
    expect(words('page_unreachable', '$.pages[1].rules[0]').said).toContain('before page 2 make it show');
    expect(words('limit_without_entries', `${F}[3].options`).said).toBe(
      'The form keeps no entries, so this limit forgets each entry once its actions have run.',
    );
    expect(words('content_missing', '$.outcomes[0].redirect.content').said).toContain("the outcome's message");
    expect(words('content_missing', `${F}[0].settings.page`).said).toBe('The site has no such content item any more.');
    expect(words('part_missing', `${F}[3].options.items`, 'error').said).toBe('This field has no options.');
    expect(words('part_missing', '$.pages[1].groups', 'error').said).toBe('This page has no group.');
  });

  it("say the content and Set state actions' problems, a loop naming the action that moves the entry back", () => {
    const pair: DesignDefinition = {
      ...DEFINITION,
      actions: [
        { key: 'a1', type: 'setState', name: 'Reject again', trigger: 'approved', settings: { state: 'rejected' } },
        // One that is off comes first, and moves nothing back.
        {
          key: 'a3',
          type: 'setState',
          name: 'Off',
          trigger: 'rejected',
          enabled: false,
          settings: { state: 'approved' },
        },
        { key: 'a2', type: 'setState', name: 'Approve again', trigger: 'rejected', settings: { state: 'approved' } },
      ],
    };
    const said = (code: string, path: string) =>
      issueWords({ severity: 'error', code, path, message: 'x' } satisfies CheckIssue, pair, context).said;
    expect(said('state_loop', '$.actions[0].settings.state')).toBe(
      '"Approve again" moves the entry back, so the two would move it back and forth for ever. Change one of them.',
    );
    expect(said('state_loop', '$.actions[1].settings.state')).toMatch(/^"Reject again" moves/);
    expect(said('state_loop', '$.actions[2].settings.state')).toMatch(/^"Reject again" moves/);
    expect(words('content_property_gone', '$.actions[0].settings.mapping[1].property', 'error').said).toBe(
      'The document type has no such property. Remove the row.',
    );
  });

  it("keep the server's sentence for the codes whose words only it has, and for a code the designer doesn't know", () => {
    for (const code of [...SERVER_WORDS, 'a_code_from_later'])
      expect(words(code, `${F}[1].settings.maxBytes`, 'error', 'Must be at most 10.').said).toBe('Must be at most 10.');
  });

  it("say each of the designer's own codes in words that name no code and leave no value out", () => {
    for (const code of OWN_WORDS) {
      const said = words(code, `${F}[0].label['']`).said;
      expect(said, code).not.toBe('The server says so.');
      expect(said, code).not.toContain('jfbCheck_');
      expect(said, code).not.toContain('undefined');
    }
  });
});
