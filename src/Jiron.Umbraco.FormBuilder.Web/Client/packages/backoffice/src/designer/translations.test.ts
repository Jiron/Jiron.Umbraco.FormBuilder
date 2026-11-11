import { describe, expect, it } from 'vitest';
import type { FieldSettingResponseModel } from '../api';
import { actionLacks, groupLacks, lacks, outcomeLacks, pageLacks, settingsLacking } from './translations';

const setting = (name: string, kind: string, settings: FieldSettingResponseModel[] = []) =>
  ({ name, kind, settings }) as unknown as FieldSettingResponseModel;

describe('what a translator has left', () => {
  it('finds a text written in some language but not in this one or a more general one', () => {
    expect(lacks({ '': 'Email' }, 'de-DE')).toBe(true);
    expect(lacks({ '': 'Email', 'de-DE': 'E-Mail' }, 'de-DE')).toBe(false);
    expect(lacks({ '': 'Email', de: 'E-Mail' }, 'de-CH')).toBe(false);
    expect(lacks({ '': 'Email', 'DE-de': 'E-Mail' }, 'de-DE')).toBe(false);
    // Written only in German: visitors in French read the empty default.
    expect(lacks({ '': '', 'de-DE': 'Nur Deutsch' }, 'fr-FR')).toBe(true);
    // A default naming a dictionary item is the dictionary's to translate.
    expect(lacks({ '': '#Demo.Login.Email' }, 'de-DE')).toBe(false);
    expect(lacks({ '': '#Demo.Login.Email and more' }, 'de-DE')).toBe(true);
    // Nothing written lacks nothing.
    expect(lacks({ '': '  ' }, 'de-DE')).toBe(false);
    expect(lacks(null, 'de-DE')).toBe(false);
    expect(lacks(undefined, 'de-DE')).toBe(false);
  });

  it("reads a page's and a group's title and description, and an outcome's message", () => {
    expect(pageLacks({ key: 'p', groups: [], title: { '': 'Hi', de: 'Hallo' } }, 'de-DE')).toBe(false);
    expect(pageLacks({ key: 'p', groups: [], description: { '': '<p>Hi</p>' } }, 'de-DE')).toBe(true);
    expect(groupLacks({ key: 'g', fields: [], title: { '': 'Contact' } }, 'de-DE')).toBe(true);
    expect(groupLacks({ key: 'g', fields: [], title: null }, 'de-DE')).toBe(false);
    expect(outcomeLacks({ key: 'o', message: { '': '<p>Thanks</p>' } }, 'de-DE')).toBe(true);
    expect(outcomeLacks({ key: 'o', kind: 'redirect', message: null }, 'de-DE')).toBe(false);
  });

  it("reads each text an action's type declares, nested ones too, and nothing else", () => {
    const schema = [
      setting('to', 'list'),
      setting('subject', 'localizedText'),
      setting('body', 'localizedHtml'),
      setting('advanced', 'nested', [setting('footer', 'localizedText')]),
    ];
    expect(
      actionLacks(
        { to: ['a@b.test'], subject: { '': 'Hi', de: 'Hallo' }, body: { '': '<p>x</p>', de: '<p>y</p>' } },
        schema,
        'de-DE',
      ),
    ).toBe(false);
    expect(actionLacks({ subject: { '': 'Hi', de: 'Hallo' }, body: { '': '<p>x</p>' } }, schema, 'de-DE')).toBe(true);
    expect(actionLacks({ advanced: { footer: { '': 'Bye' } } }, schema, 'de-DE')).toBe(true);
    // A text the type doesn't declare is no text of the action's.
    expect(actionLacks({ to: { '': 'not a text' }, other: { '': 'x' } }, schema, 'de-DE')).toBe(false);
    expect(actionLacks(null, schema, 'de-DE')).toBe(false);
  });

  it("names the Settings view's texts that lack the language: the buttons' labels and the closed message", () => {
    const settings = {
      submission: { submitLabel: { '': 'Send', de: 'Senden' }, nextLabel: { '': 'On' }, entryTitle: 'not a text' },
      availability: { closedMessage: { '': '<p>Closed</p>' } },
    };
    expect([...settingsLacking(settings, 'de-DE')]).toEqual(['nextLabel', 'closedMessage']);
    expect([...settingsLacking(undefined, 'de-DE')]).toEqual([]);
  });
});
