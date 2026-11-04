// The inspector (ROADMAP 9.4a, IA 5.1, ADR-0170): what it is headed with, its sections that open and close as the user
// left them, the one line a closed one says, the requests its boxes send (typing in one box one step to undo), why a
// value can't be kept, locks, the language it edits, the alias, and its ways of showing.
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import type { DesignDefinition, DesignField } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { INSPECTOR_REQUEST, InspectorElement, readOpen, type InspectorRequest } from './inspector.element';

// The option editor reads what the site serves; here, the endpoint's snapshot.
vi.mock('./option-data', async () => ({
  readOptionSources: async () => (await import('../preview/fixtures')).optionSources,
  readOptionLists: async () => [],
  resolveOptions: async () => undefined,
}));

const LANGUAGES = [
  { culture: 'en-US', name: 'English (United States)', isDefault: true },
  { culture: 'de-DE', name: 'German (Germany)', isDefault: false },
];

const NAME: DesignField = { key: 'name', type: 'text', alias: 'name', label: { '': 'Your name' }, required: true };
const GUESTS: DesignField = { key: 'guests', type: 'number', alias: 'guests', label: { '': 'Guests' } };
const ARRIVAL: DesignField = { key: 'arrival', type: 'date', alias: 'arrival', label: { '': 'Arrival' } };

const definitionOf = (...fields: DesignField[]): DesignDefinition => ({
  pages: [{ key: 'p', title: { '': 'About you' }, groups: [{ key: 'g', columns: 2, fields }] }],
});

async function inspectorOf(selection: string | null, extra: Partial<InspectorElement> = {}, ...fields: DesignField[]) {
  const element = new InspectorElement();
  Object.assign(element, {
    definition: definitionOf(...(fields.length > 0 ? fields : [NAME, GUESTS, ARRIVAL])),
    selection,
    types: builtInTypes,
    culture: 'en-US',
    languages: LANGUAGES,
    ...extra,
  });
  const requests: InspectorRequest[] = [];
  element.addEventListener(INSPECTOR_REQUEST, (event) =>
    requests.push((event as CustomEvent<InspectorRequest>).detail),
  );
  document.body.append(element);
  await settle(element);
  return { element, requests, root: shadow(element) };
}

/** A change request's update applied to an element. */
function applied(request: InspectorRequest | undefined, element: DesignField | Record<string, unknown>) {
  if (request?.kind !== 'change') throw new Error(`no change but ${request?.kind}`);
  return request.update(element as DesignField) as Record<string, unknown>;
}

const type = (box: HTMLInputElement | HTMLTextAreaElement, value: string, done = false) => {
  box.value = value;
  box.dispatchEvent(new Event('input', { bubbles: true }));
  if (done) box.dispatchEvent(new Event('change', { bubbles: true }));
};

const choose = (box: HTMLSelectElement, value: string) => {
  box.value = value;
  box.dispatchEvent(new Event('change', { bubbles: true }));
};

const sectionButton = (root: ShadowRoot, id: string) =>
  must(root.querySelector<HTMLButtonElement>(`[data-section="${id}"] .section-head button`), `the ${id} button`);

beforeAll(registerEnglish);

beforeEach(() => localStorage.clear());

afterEach(() => {
  document.body.replaceChildren();
});

describe('the inspector', () => {
  it('is headed with what the element is, and shows nothing while nothing is selected', async () => {
    expect(text((await inspectorOf('name')).root.querySelector('h2'))).toBe('Short text field');
    expect(text((await inspectorOf('g')).root.querySelector('h2'))).toBe('Group');
    expect(text((await inspectorOf('p')).root.querySelector('h2'))).toBe('Page');
    expect((await inspectorOf(null)).root.querySelector('h2')).toBeNull();
    const gone = await inspectorOf('x', {}, { key: 'x', type: 'gone', alias: 'x' });
    expect(text(gone.root.querySelector('.body'))).toBe(
      'This site has no field type "gone" any more, so its settings can\'t be shown.',
    );
  });

  it('opens Essentials at first, says what a closed section holds, and keeps what the user opens', async () => {
    const { root } = await inspectorOf('name');
    const buttons = all(must(root.host, 'the host'), '.section-head button');
    expect(buttons.map((button) => [text(button), button.getAttribute('aria-expanded')])).toEqual([
      ['Essentials', 'true'],
      ['Validation Nothing changed', 'false'],
      ['Rules Always shown', 'false'],
      ['Appearance Nothing changed', 'false'],
      ['Advanced Alias: name', 'false'],
    ]);
    expect(buttons.every((button) => root.getElementById(button.getAttribute('aria-controls') ?? ''))).toBe(true);
    sectionButton(root, 'advanced').click();
    sectionButton(root, 'essentials').click();
    await settle();
    expect(sectionButton(root, 'advanced').getAttribute('aria-expanded')).toBe('true');
    expect(text(sectionButton(root, 'essentials'))).toBe('Essentials Your name \u00b7 Required');
    expect([...readOpen()]).toEqual(['advanced']);
    const again = await inspectorOf('guests');
    expect(sectionButton(again.root, 'advanced').getAttribute('aria-expanded')).toBe('true');
  });

  it("opens Rules on what a rule does, and counts an element's rules", async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['rules']));
    const ruled = await inspectorOf('name', {}, { ...NAME, rules: [{ key: 'r', effect: 'show', conditions: [] }] });
    expect(all(ruled.element, '[data-section="rules"] .help').map((help) => text(help))).toEqual([
      '1 rule',
      'A rule sits on the element it shows, hides or requires.',
    ]);
    localStorage.setItem('jfb.designer.inspector.open', 'not json');
    expect([...readOpen()]).toEqual(['essentials']);
  });

  it('sends what is typed in a box as a change, one step to undo until the focus leaves the box', async () => {
    const { root, requests } = await inspectorOf('name');
    const label = must(root.querySelector<HTMLInputElement>('#prop-label'), 'the label box');
    expect(label.value).toBe('Your name');
    label.dispatchEvent(new FocusEvent('focusin', { bubbles: true, composed: true }));
    type(label, 'Your full name');
    type(label, 'Your full name, please');
    label.dispatchEvent(new FocusEvent('focusin', { bubbles: true, composed: true }));
    type(label, 'Name');
    const merges = requests.map((request) => (request.kind === 'change' ? request.merge : null));
    expect(merges[0]).toBe(merges[1]);
    expect(merges[2]).not.toBe(merges[1]);
    expect(merges[0]).toMatch(/^name:label:\d+$/);
    expect(requests[0]?.kind === 'change' && requests[0].said).toBe('Changed Label');
    expect(applied(requests[1], NAME)['label']).toEqual({ '': 'Your full name, please' });
    type(label, '');
    expect(applied(requests[3], NAME)).not.toHaveProperty('label');
  });

  it('sends a click as a step of its own, and leaves a property that is as it comes out of the field', async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['essentials', 'advanced', 'appearance']));
    const { root, requests } = await inspectorOf('name');
    const required = must(root.querySelector<HTMLInputElement>('#prop-required'), 'Required');
    expect([required.getAttribute('role'), required.checked]).toEqual(['switch', true]);
    required.click();
    expect(requests[0]?.kind === 'change' && requests[0].merge).toBeUndefined();
    expect(applied(requests[0], NAME)).not.toHaveProperty('required');
    const emails = must(root.querySelector<HTMLInputElement>('#prop-includeInEmails'), 'Include in emails');
    expect(emails.checked).toBe(true);
    emails.click();
    expect(applied(requests[1], NAME)['includeInEmails']).toBe(false);
    choose(must(root.querySelector<HTMLSelectElement>('#prop-span'), 'the width'), '2');
    expect(applied(requests[2], NAME)['span']).toBe(2);
    choose(must(root.querySelector<HTMLSelectElement>('#prop-keep'), 'keep'), 'untilActionsRun');
    expect(applied(requests[3], NAME)['keep']).toBe('untilActionsRun');
    choose(must(root.querySelector<HTMLSelectElement>('#prop-keep'), 'keep'), 'always');
    expect(applied(requests[4], { ...NAME, keep: 'untilActionsRun' })).toEqual(NAME);
    expect([...all<HTMLOptionElement>(root.host, '#prop-span option')].map((option) => text(option))).toEqual([
      '1 of 2 columns',
      '2 of 2 columns',
    ]);
  });

  it("writes a setting as the type's schema says, and says why a value can't be kept once the box is left", async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['validation']));
    const { element, root, requests } = await inspectorOf('name');
    const max = must(root.querySelector<HTMLInputElement>('#setting-maxLength'), 'Maximum length');
    expect([max.value, max.getAttribute('inputmode')]).toEqual(['255', 'numeric']);
    type(max, '80');
    expect(applied(requests[0], NAME)['settings']).toEqual({ maxLength: 80 });
    expect(requests[0]?.kind === 'change' && requests[0].merge).toMatch(/^name:maxLength:\d+$/);
    type(max, '9000');
    await settle(element);
    expect(requests).toHaveLength(1);
    expect(root.querySelector('.why')).toBeNull();
    max.dispatchEvent(new Event('change', { bubbles: true }));
    await settle(element);
    const why = must(root.querySelector('#setting-maxLength-why'), 'why');
    expect(text(why)).toBe('Enter 4000 or less.');
    expect([max.getAttribute('aria-invalid'), max.getAttribute('aria-describedby')]).toEqual(['true', why.id]);
    type(max, '');
    await settle(element);
    expect(root.querySelector('.why')).toBeNull();
    expect(applied(requests[1], { ...NAME, settings: { maxLength: 80 } })).toEqual(NAME);
  });

  it('shows seven items of a section, then More options for the rest', async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['validation']));
    const { element, root } = await inspectorOf('guests');
    const body = must(root.querySelector('#section-validation'), 'Validation');
    const more = must(body.querySelector<HTMLButtonElement>('.more'), 'More options');
    expect([text(more), more.getAttribute('aria-expanded')]).toEqual(['More options (5)', 'false']);
    const items = () => [...body.children].filter((child) => child.classList.contains('item'));
    expect(items()).toHaveLength(7);
    more.click();
    await settle(element);
    expect(items()).toHaveLength(12);
    expect([text(more), more.getAttribute('aria-expanded')]).toEqual(['Fewer options', 'true']);
    const decimals = must(root.querySelector<HTMLInputElement>('#setting-step'), 'Step');
    expect(decimals.getAttribute('inputmode')).toBe('decimal');
  });

  it("shows a locked setting's value and key, read-only, and a hidden one not at all (ADR-0060)", async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['validation', 'appearance']));
    const text_ = must(builtInTypes['text'], 'the text type');
    const locked = {
      ...builtInTypes,
      text: {
        ...text_,
        settings: text_.settings.map((setting) =>
          setting.name === 'maxLength'
            ? {
                ...setting,
                lock: { hidden: false, configurationKey: 'JironFormBuilder:Designer:SettingLocks:0', value: 120 },
              }
            : setting.name === 'prefix'
              ? { ...setting, lock: { hidden: true, configurationKey: 'JironFormBuilder:Designer:SettingLocks:1' } }
              : setting,
        ),
      },
    };
    const { root } = await inspectorOf('name', { types: locked });
    expect(root.querySelector('#setting-maxLength')?.localName).toBe('p');
    const item = must(root.querySelector('[data-locked="maxLength"]'), 'the locked item');
    expect(text(item)).toBe(
      "Maximum length 120 Set by the site's configuration: JironFormBuilder:Designer:SettingLocks:0",
    );
    expect(root.querySelector('input#setting-maxLength')).toBeNull();
    expect(root.querySelector('#setting-prefix')).toBeNull();
    expect(text(sectionButton(root, 'validation'))).toBe('Validation');

    // A hidden lock inside nested settings: the email's blocked or allowed domains keep their list, not their mode.
    const email_ = must(builtInTypes['email'], 'the email type');
    const nested = {
      ...builtInTypes,
      email: {
        ...email_,
        settings: email_.settings.map((setting) =>
          setting.name === 'domains'
            ? {
                ...setting,
                settings: setting.settings.map((inner) =>
                  inner.name === 'mode'
                    ? { ...inner, lock: { hidden: true, configurationKey: 'JironFormBuilder:Designer:SettingLocks:2' } }
                    : inner,
                ),
              }
            : setting,
        ),
      },
    };
    const mail: DesignField = { key: 'mail', type: 'email', alias: 'mail', label: { '': 'Email' } };
    const hidden = await inspectorOf('mail', { types: nested }, mail);
    expect(hidden.root.querySelector('#setting-domains-list')).not.toBeNull();
    expect(hidden.root.querySelector('#setting-domains-mode')).toBeNull();
  });

  it('edits the texts of the language the canvas shows, and says where visitors see the default instead', async () => {
    const field = { ...NAME, label: { '': 'Your name' }, placeholder: { '': 'Ada', 'de-DE': 'Ada' } };
    const { root, requests } = await inspectorOf('name', { culture: 'de-DE' }, field);
    expect(text(root.querySelector('.note'))).toBe(
      'Editing the German texts. Where one is empty, visitors see the English text.',
    );
    const label = must(root.querySelector<HTMLInputElement>('#prop-label'), 'the label');
    expect([label.value, label.placeholder, label.lang]).toEqual(['', 'Your name', 'de-DE']);
    expect(must(root.querySelector<HTMLInputElement>('#prop-placeholder'), 'the placeholder').value).toBe('Ada');
    type(label, 'Ihr Name');
    expect(applied(requests[0], field)['label']).toEqual({ '': 'Your name', 'de-DE': 'Ihr Name' });
    // In the default language a box holds its own text, with no placeholder to say it twice.
    const english = await inspectorOf('name', { culture: 'en-US' }, field);
    expect(english.root.querySelector('.note')).toBeNull();
    const own = must(english.root.querySelector<HTMLInputElement>('#prop-label'), 'the label');
    expect([own.value, own.hasAttribute('placeholder')]).toEqual(['Your name', false]);
  });

  it("shows a text setting's default words as its placeholder in another language, and none in the default", async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['appearance']));
    const field = { ...NAME, settings: { prefix: { '': 'Dr.' } } };
    const german = await inspectorOf('name', { culture: 'de-DE' }, field);
    const prefix = must(german.root.querySelector<HTMLInputElement>('#setting-prefix'), 'the prefix');
    expect([prefix.value, prefix.placeholder]).toEqual(['', 'Dr.']);
    const english = await inspectorOf('name', { culture: 'en-US' }, field);
    const own = must(english.root.querySelector<HTMLInputElement>('#setting-prefix'), 'the prefix');
    expect([own.value, own.hasAttribute('placeholder')]).toEqual(['Dr.', false]);
  });

  it('checks an alias as the server does, and sends one the form takes with a way back', async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['advanced']));
    const taken: DesignField = { ...GUESTS, formerAliases: ['people'] };
    const { element, root, requests } = await inspectorOf('name', {}, NAME, taken);
    const alias = must(root.querySelector<HTMLInputElement>('#prop-alias'), 'the alias');
    const tryAlias = async (value: string) => {
      type(alias, value, true);
      await settle(element);
      return text(root.querySelector('#prop-alias-why'));
    };
    expect(await tryAlias('Name')).toBe('Start with a small letter, then use letters and digits only, 64 at most.');
    expect(alias.getAttribute('aria-invalid')).toBe('true');
    expect(await tryAlias('my-name')).toBe('Start with a small letter, then use letters and digits only, 64 at most.');
    expect(await tryAlias('')).toBe('A field that holds an answer needs an alias.');
    expect(await tryAlias('Page')).toBe('Start with a small letter, then use letters and digits only, 64 at most.');
    expect(await tryAlias('page')).toBe("That name is kept for the form's own use.");
    expect(await tryAlias('guests')).toBe('Another field has or had that alias.');
    expect(await tryAlias('People')).toBe('Start with a small letter, then use letters and digits only, 64 at most.');
    expect(await tryAlias('people')).toBe('Another field has or had that alias.');
    expect(requests).toEqual([]);
    expect(await tryAlias('name')).toBe('');
    expect(requests).toEqual([]);
    expect(await tryAlias('fullName')).toBe('');
    const sent = must(requests[0], 'the alias request');
    expect(sent.kind === 'alias' && [sent.key, sent.alias]).toEqual(['name', 'fullName']);
    if (sent.kind === 'alias') sent.revert();
    expect(alias.value).toBe('name');
  });

  it('limits a date by none, a date, or a number of units from today, and lists dates one a line', async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['validation']));
    const { root, requests } = await inspectorOf('arrival');
    choose(must(root.querySelector<HTMLSelectElement>('#setting-min'), 'the earliest kind'), 'today');
    expect(applied(requests[0], ARRIVAL)['settings']).toEqual({ min: { today: '+0d' } });
    const later = await inspectorOf('arrival', {}, { ...ARRIVAL, settings: { min: { today: '+2w' } } });
    const count = must(later.root.querySelector<HTMLInputElement>('#setting-min-count'), 'the count');
    expect(count.value).toBe('2');
    count.value = '-3';
    count.dispatchEvent(new Event('change', { bubbles: true }));
    expect(applied(later.requests[0], ARRIVAL)['settings']).toEqual({ min: { today: '-3w' } });
    choose(must(later.root.querySelector<HTMLSelectElement>('#setting-min-unit'), 'the unit'), 'm');
    expect(applied(later.requests[1], ARRIVAL)['settings']).toEqual({ min: { today: '+2m' } });
    expect(text(later.root.querySelector('#section-validation'))).toContain('A number below 0 counts back from today.');
    const dated = await inspectorOf('arrival', {}, { ...ARRIVAL, settings: { max: '2026-12-31' } });
    expect(must(dated.root.querySelector<HTMLInputElement>('#setting-max-date'), 'the date').value).toBe('2026-12-31');
    const excluded = must(dated.root.querySelector<HTMLTextAreaElement>('#setting-excluded'), 'excluded dates');
    type(excluded, '2026-12-24\n2026-12-25');
    expect(applied(dated.requests[0], ARRIVAL)['settings']).toEqual({ excluded: ['2026-12-24', '2026-12-25'] });
    expect(excluded.getAttribute('aria-describedby')).toBe('setting-excluded-lines');
  });

  it('writes nested settings in their parent, and a choice list as the boxes ticked', async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['validation']));
    const email: DesignField = { key: 'mail', type: 'email', alias: 'mail', label: { '': 'Email' } };
    const { root, requests } = await inspectorOf('mail', {}, email);
    const list = must(root.querySelector<HTMLTextAreaElement>('#setting-domains-list'), 'the domains');
    type(list, 'example.com');
    expect(applied(requests[0], email)['settings']).toEqual({ domains: { list: ['example.com'] } });
    expect(
      applied(requests[0], { ...email, settings: { domains: { list: ['a.com'], mode: 'block' } } })['settings'],
    ).toEqual({
      domains: { list: ['example.com'], mode: 'block' },
    });
    type(list, '');
    expect(applied(requests[1], { ...email, settings: { domains: { list: ['a.com'] } } })).toEqual(email);

    const url: DesignField = { key: 'site', type: 'url', alias: 'site', label: { '': 'Website' } };
    const ticked = await inspectorOf('site', {}, url);
    const boxes = all<HTMLInputElement>(ticked.element, '#setting-schemes input[type="checkbox"]');
    expect(boxes.length).toBeGreaterThan(1);
    expect(boxes.map((box) => box.checked)).toEqual(
      must(builtInTypes['url'], 'the url type')
        .settings.find((setting) => setting.name === 'schemes')
        ?.choices.map((choice) => choice.value === 'https'),
    );
    const http = must(
      boxes.find((box) => !box.checked),
      'an unticked scheme',
    );
    http.click();
    expect(applied(ticked.requests[0], url)['settings']).toEqual({ schemes: ['https', 'http'] });
    // The standard's one scheme can't be unticked into the standard.
    must(
      boxes.find((box) => box.checked),
      'https',
    ).click();
    await settle(ticked.element);
    expect(ticked.requests).toHaveLength(1);
    expect(text(ticked.root.querySelector('#setting-schemes-why'))).toBe('Choose 1 at least.');
  });

  it('never rewrites a box while it is typed in, nor one holding a value it could not keep', async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['appearance']));
    const { element, root } = await inspectorOf('name');
    const before = must(root.querySelector<HTMLInputElement>('#setting-prefix'), 'Text before');
    before.focus();
    before.value = 'Dr ';
    before.dispatchEvent(new Event('input', { bubbles: true }));
    // The form keeps "Dr", and draws again with it: the space just typed stays in the box.
    element.definition = definitionOf({ ...NAME, settings: { prefix: { '': 'Dr' } } }, GUESTS, ARRIVAL);
    await settle(element);
    expect(before.value).toBe('Dr ');
    before.blur();
    element.definition = definitionOf({ ...NAME, settings: { prefix: { '': 'Dr' } } }, GUESTS, ARRIVAL);
    element.requestUpdate();
    await settle(element);
    expect(before.value).toBe('Dr');
  });

  it("puts an alias back only into its own field's box, and forgets problems when Undo draws the form again", async () => {
    localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['advanced', 'validation']));
    const { element, root, requests } = await inspectorOf('name');
    const alias = must(root.querySelector<HTMLInputElement>('#prop-alias'), 'the alias');
    type(alias, 'fullName', true);
    const sent = must(requests[0], 'the alias request');
    element.selection = 'guests';
    await settle(element);
    expect(alias.value).toBe('guests');
    if (sent.kind === 'alias') sent.revert();
    expect(alias.value).toBe('guests');

    const max = must(root.querySelector<HTMLInputElement>('#setting-max'), 'Maximum');
    type(max, 'lots', true);
    await settle(element);
    expect(root.querySelector('#setting-max-why')).not.toBeNull();
    element.forget();
    await settle(element);
    expect(root.querySelector('#setting-max-why')).toBeNull();
    expect(max.value).toBe('');
  });

  it('says nothing of a missing type while the types are read', async () => {
    const { root } = await inspectorOf('name', { types: {} });
    expect(root.querySelector('.body uui-loader')).not.toBeNull();
    expect(root.querySelector('.body .note')).toBeNull();
  });

  it("edits a group's title, columns and style, and a page's title", async () => {
    const group = await inspectorOf('g');
    choose(must(group.root.querySelector<HTMLSelectElement>('#prop-columns'), 'columns'), '3');
    choose(must(group.root.querySelector<HTMLSelectElement>('#prop-style'), 'style'), 'card');
    type(must(group.root.querySelector<HTMLInputElement>('#prop-title'), 'title'), 'Contact');
    const base = { key: 'g', fields: [] };
    expect(applied(group.requests[0], base)['columns']).toBe(3);
    expect(applied(group.requests[1], base)['style']).toBe('card');
    expect(applied(group.requests[2], base)['title']).toEqual({ '': 'Contact' });
    choose(must(group.root.querySelector<HTMLSelectElement>('#prop-columns'), 'columns'), '1');
    expect(applied(group.requests[3], { ...base, columns: 3 })).toEqual(base);
    // Fewer columns take a wider field down to their width, as the server needs.
    const wide = { key: 'g', columns: 3, fields: [{ ...NAME, span: 3 }, { ...GUESTS, span: 2 }, ARRIVAL] };
    expect(applied(group.requests[0], wide)['fields']).toEqual([{ ...NAME, span: 3 }, { ...GUESTS, span: 2 }, ARRIVAL]);
    choose(must(group.root.querySelector<HTMLSelectElement>('#prop-columns'), 'columns'), '2');
    expect(applied(group.requests[4], wide)['fields']).toEqual([{ ...NAME, span: 2 }, { ...GUESTS, span: 2 }, ARRIVAL]);
    expect(applied(group.requests[3], wide)['fields']).toEqual([NAME, GUESTS, ARRIVAL]);
    const page = await inspectorOf('p');
    expect(must(page.root.querySelector<HTMLInputElement>('#prop-title'), 'title').value).toBe('About you');
    expect(all(page.element, '.section-head button').map((button) => text(button))).toEqual([
      'Essentials',
      'Rules Always shown',
    ]);
  });

  it("folds to a rail beside the canvas, and carries the element's bar over a narrow one", async () => {
    const docked = await inspectorOf('name');
    must(docked.root.querySelector<HTMLElement>('uui-button[label="Fold the inspector"]'), 'Fold').dispatchEvent(
      new Event('click'),
    );
    must(docked.root.querySelector<HTMLElement>('uui-button[label="Close"]'), 'Close').dispatchEvent(
      new Event('click'),
    );
    expect(docked.requests).toEqual([{ kind: 'fold' }, { kind: 'close' }]);
    expect(docked.root.querySelector('.tools')).toBeNull();

    const rail = await inspectorOf('name', { mode: 'rail' });
    expect(rail.root.querySelector('h2')).toBeNull();
    must(rail.root.querySelector<HTMLElement>('uui-button[label="Open the inspector"]'), 'Open').dispatchEvent(
      new Event('click'),
    );
    expect(rail.requests).toEqual([{ kind: 'unfold' }]);

    const over = await inspectorOf('name', { mode: 'overlay' });
    expect(over.element.getAttribute('mode')).toBe('overlay');
    const bar = must(over.root.querySelector('.tools'), 'the bar');
    expect(bar.getAttribute('role')).toBe('group');
    for (const button of bar.querySelectorAll<HTMLButtonElement>('button')) button.click();
    expect(over.requests).toEqual([
      { kind: 'element', action: 'moveTo', key: 'name' },
      { kind: 'element', action: 'duplicate', key: 'name' },
      { kind: 'element', action: 'delete', key: 'name' },
      { kind: 'element', action: 'newPageAfter', key: 'name' },
      { kind: 'element', action: 'copyAlias', key: 'name' },
    ]);
    const page = await inspectorOf('p', { mode: 'overlay' });
    expect(all(page.element, '.tools button').map((button) => text(button))).toEqual(['Move to\u2026', 'Delete']);
  });

  it('offers Change type by the type of a field that may change, in every way of showing, and once', async () => {
    for (const mode of ['docked', 'overlay'] as const) {
      const { root, requests } = await inspectorOf('name', { mode });
      const changeType = must(root.querySelector<HTMLElement>('.head uui-button.change-type'), `Change type, ${mode}`);
      expect(changeType.getAttribute('label')).toBe('Change type');
      changeType.dispatchEvent(new Event('click'));
      expect(requests).toEqual([{ kind: 'element', action: 'changeType', key: 'name' }]);
      expect(all(root.querySelector('.head') ?? root.host, 'button').map((button) => text(button))).not.toContain(
        'Change type\u2026',
      );
      document.body.replaceChildren();
    }

    const heading: DesignField = { key: 'h', type: 'heading', settings: { text: { '': 'Hello' } } };
    expect((await inspectorOf('h', {}, heading)).root.querySelector('.change-type')).toBeNull();
    expect((await inspectorOf('g')).root.querySelector('.change-type')).toBeNull();
  });
});

describe("a choice field's options in the inspector", () => {
  it('may depend on a field now after it, which "Depends on" still names, and show as cards by the form\'s layout', async () => {
    const size: DesignField = { key: 'size', type: 'dropdown', alias: 'size', options: { items: [{ value: 's' }] } };
    const colour: DesignField = {
      key: 'colour',
      type: 'radio',
      alias: 'colour',
      options: { parent: 'size', items: [{ value: 'red' }] },
    };
    const definition: DesignDefinition = {
      ...definitionOf(colour, size),
      settings: { appearance: { choiceLayout: 'cards' } },
    };
    const { root } = await inspectorOf('colour', { definition }, colour, size);
    const editor = must(root.querySelector('jfb-option-editor'), 'the option editor');
    await settle(editor);
    expect(editor.parents.map((each) => each.key)).toEqual(['size']);
    expect(editor.cards).toBe(true);
    expect(must(editor.querySelector<HTMLSelectElement>('#options-parent'), 'Depends on').value).toBe('size');
  });
});

describe('merge tags in the inspector (ADR-0173)', () => {
  it("put a tag into a heading's text where the caret was, as a value it keeps", async () => {
    const heading: DesignField = { key: 'h', type: 'heading', alias: 'title', settings: { text: { '': 'Hello !' } } };
    const { root, requests } = await inspectorOf('h', { form: 'form1' }, heading);
    const pickers = [...root.querySelectorAll('jfb-merge-tag-picker')];
    expect(pickers.map((picker) => [picker.place, picker.form, picker.culture, picker.owner])).toEqual([
      ['visitorText', 'form1', 'en-US', 'h'],
    ]);
    const box = must(root.querySelector<HTMLInputElement>('#setting-text'), 'the text');
    box.setSelectionRange(6, 6);
    must(pickers[0], 'the picker').insert('{{ form.name }}');
    expect(applied(requests.at(-1), heading)['settings']).toEqual({ text: { '': 'Hello {{ form.name }}!' } });
    // A step of its own, which Undo takes back alone.
    const last = requests.at(-1);
    expect(last?.kind === 'change' ? last.merge : 'none').toBeUndefined();
    expect(root.activeElement).toBe(box);
    expect(box.selectionStart).toBe(21);
  });

  it("put a tag into a field's default value, of its own place, and offer none without a form", async () => {
    const { root, requests } = await inspectorOf('name', { form: 'form1' });
    sectionButton(root, 'advanced').click();
    await settle(root.host as InspectorElement);
    const picker = must(
      [...root.querySelectorAll('jfb-merge-tag-picker')].find((each) => each.place === 'defaultValue'),
      'the default value picker',
    );
    expect(picker.owner).toBe('name');
    picker.insert('{{ query.ref }}');
    expect(applied(requests.at(-1), NAME)['defaultValue']).toBe('{{ query.ref }}');

    const none = await inspectorOf('h', {}, { key: 'h', type: 'heading', alias: 'title' });
    expect(none.root.querySelector('jfb-merge-tag-picker')).toBeNull();
  });
});

describe("Umbraco's pickers in the inspector (ADR-0173)", () => {
  it("take as many items as a list's rule allows, or one, and write a list's keys or the one key", async () => {
    const image = must(builtInTypes['image'], 'the image type');
    const media = must(
      image.settings.find((setting) => setting.name === 'media'),
      'the picture',
    );
    const gallery = {
      ...image,
      alias: 'gallery',
      settings: [
        { ...media, name: 'pictures', kind: 'list', itemKind: 'key', rules: { ...media.rules, maximumCount: 3 } },
        media,
      ],
    };
    const field: DesignField = { key: 'g1', type: 'gallery', alias: 'pictures' };
    const { root, requests } = await inspectorOf('g1', { types: { ...builtInTypes, gallery } }, field);
    const pickers = [...root.querySelectorAll('umb-input-media')] as (HTMLElement & { max: number })[];
    expect(pickers.map((picker) => [picker.id, picker.max])).toEqual([
      ['setting-pictures', 3],
      ['setting-media', 1],
    ]);
    const pick = (picker: HTMLElement | undefined, selection: string[]) => {
      Object.assign(must(picker, 'a picker'), { selection });
      picker?.dispatchEvent(new Event('change'));
    };
    pick(pickers[0], ['a', 'b']);
    expect(applied(requests.at(-1), field)['settings']).toEqual({ pictures: ['a', 'b'] });
    pick(pickers[1], ['c']);
    expect(applied(requests.at(-1), field)['settings']).toEqual({ media: 'c' });
    expect(must(root.querySelector('#setting-pictures')?.parentElement, 'its group').getAttribute('role')).toBe(
      'group',
    );
  });
});

describe('rich text in the inspector (ADR-0173)', () => {
  const CONSENT: DesignField = {
    key: 'c',
    type: 'consent',
    alias: 'agree',
    settings: { text: { '': 'I agree to the <a href="/privacy"><strong>terms</strong></a>, &amp; the rules.' } },
  };

  it("is one line of words in a consent's label, and paragraphs and lists in a paragraph", async () => {
    const consent = await inspectorOf('c', {}, CONSENT);
    const wording = must(consent.root.querySelector('jfb-rich-text'), 'the wording');
    expect([wording.id, wording.inline, wording.value]).toEqual([
      'setting-text',
      true,
      'I agree to the <a href="/privacy"><strong>terms</strong></a>, &amp; the rules.',
    ]);
    const paragraph = await inspectorOf('p1', {}, { key: 'p1', type: 'paragraph', alias: 'intro' });
    expect(must(paragraph.root.querySelector('jfb-rich-text'), 'the paragraph').inline).toBe(false);
    // Each takes tags through the picker under it, of its own place, for its own element.
    const tagged = await inspectorOf('c', { form: 'form1' }, CONSENT);
    const picker = must(
      tagged.root.querySelector<HTMLElement & { place: string; owner: string }>('jfb-rich-text + jfb-merge-tag-picker'),
      'the tag picker',
    );
    expect([picker.place, picker.owner]).toEqual(['consentText', 'c']);
  });

  it("shows the default language's words, without their markup, as another language's hint", async () => {
    const { root } = await inspectorOf('c', { culture: 'de-DE' }, CONSENT);
    const wording = must(root.querySelector('jfb-rich-text'), 'the wording');
    expect([wording.value, wording.placeholder]).toEqual(['', 'I agree to the terms, & the rules.']);
    const paragraph = await inspectorOf(
      'p1',
      { culture: 'de-DE' },
      { key: 'p1', type: 'paragraph', alias: 'intro', settings: { html: { '': '<p>One</p><ul><li>Two</li></ul>' } } },
    );
    expect(must(paragraph.root.querySelector('jfb-rich-text'), 'the paragraph').placeholder).toBe('One Two');
  });
});

describe("a field's pattern in the inspector (ADR-0173)", () => {
  const CODE = '[A-Z]{2}[0-9]{4}';
  const open = () => localStorage.setItem('jfb.designer.inspector.open', JSON.stringify(['validation']));

  it('is written as the first pattern rule, in its place among the checks, and gone when emptied', async () => {
    open();
    const { root, requests } = await inspectorOf('name');
    const box = must(root.querySelector<HTMLInputElement>('#prop-pattern'), 'the pattern');
    expect(text(root.querySelector('label[for="prop-pattern"]'))).toBe('Must match a pattern');
    expect(box.getAttribute('aria-describedby')).toBe('prop-pattern-help');
    expect(root.querySelector('#prop-pattern-sample')).toBeNull();
    type(box, CODE);
    expect(applied(requests[0], NAME)['validation']).toEqual([{ kind: 'pattern', pattern: CODE }]);
    expect(requests[0]?.kind === 'change' && requests[0].merge).toMatch(/^name:pattern\.pattern:\d+$/);
    expect(requests[0]?.kind === 'change' && requests[0].said).toBe('Changed Must match a pattern');

    const checks = [
      { kind: 'uniquePerForm' },
      { kind: 'pattern', pattern: 'x', message: { '': 'No' } },
      { kind: 'pattern', pattern: 'y' },
    ];
    const several = await inspectorOf('name', {}, { ...NAME, validation: checks });
    const first = must(several.root.querySelector<HTMLInputElement>('#prop-pattern'), 'the pattern');
    expect(first.value).toBe('x');
    type(first, CODE);
    expect(applied(several.requests[0], { ...NAME, validation: checks })['validation']).toEqual([
      { kind: 'uniquePerForm' },
      { kind: 'pattern', pattern: CODE, message: { '': 'No' } },
      { kind: 'pattern', pattern: 'y' },
    ]);
    // Emptied, the rule stays with its message while the box is typed in, so the next keys edit it, not the next rule.
    type(first, '');
    expect(applied(several.requests[1], { ...NAME, validation: checks })['validation']).toEqual([
      { kind: 'uniquePerForm' },
      { kind: 'pattern', pattern: '', message: { '': 'No' } },
      { kind: 'pattern', pattern: 'y' },
    ]);
    type(first, '', true);
    expect(applied(several.requests.at(-1), { ...NAME, validation: checks })['validation']).toEqual([
      { kind: 'uniquePerForm' },
      { kind: 'pattern', pattern: 'y' },
    ]);
    const only = { ...NAME, validation: [{ kind: 'pattern', pattern: CODE }] };
    expect(applied(several.requests.at(-1), only)).not.toHaveProperty('validation');
    // A field without a rule, whose box is emptied, sends nothing.
    const before = requests.length;
    type(box, '', true);
    expect(requests).toHaveLength(before);
  });

  it("says when the site's matcher doesn't take the pattern, and then tries no answer against it", async () => {
    open();
    const unsafe = { ...NAME, validation: [{ kind: 'pattern', pattern: '(?=a)a' }] };
    const { root } = await inspectorOf('name', {}, unsafe);
    const box = must(root.querySelector<HTMLInputElement>('#prop-pattern'), 'the pattern');
    expect(box.getAttribute('aria-invalid')).toBe('true');
    expect(box.getAttribute('aria-describedby')).toBe('prop-pattern-help prop-pattern-why');
    expect(text(root.querySelector('#prop-pattern-why'))).toContain("doesn't take this pattern");
    expect(must(root.querySelector<HTMLInputElement>('#prop-pattern-sample'), 'the sample').disabled).toBe(true);
    const result = must(root.querySelector<HTMLElement>('#prop-pattern-result'), 'the result');
    expect([text(result), result.dataset['result']]).toEqual([
      'Answers can be tried once the pattern is one the site takes.',
      'waits',
    ]);
  });

  it('tries an answer typed in Try it as the site would, saves none of it, and forgets it on another element', async () => {
    open();
    const coded = { ...NAME, validation: [{ kind: 'pattern', pattern: CODE }] };
    const { element, root, requests } = await inspectorOf('name', {}, coded, GUESTS);
    const sample = must(root.querySelector<HTMLInputElement>('#prop-pattern-sample'), 'the sample');
    const result = () => must(root.querySelector<HTMLElement>('#prop-pattern-result'), 'the result');
    expect([sample.getAttribute('aria-describedby'), result().getAttribute('role')]).toEqual([
      'prop-pattern-result',
      'status',
    ]);
    expect(text(result())).toBe('Type an answer to see whether the pattern takes it. It is not saved.');
    type(sample, 'AB1234');
    await settle(element);
    expect([text(result()), result().dataset['result']]).toEqual(['The pattern takes this answer.', 'match']);
    type(sample, 'AB12345');
    await settle(element);
    expect([text(result()), result().dataset['result']]).toEqual(["The pattern doesn't take this answer.", 'miss']);
    expect(requests).toEqual([]);
    element.selection = 'guests';
    await settle(element);
    element.selection = 'name';
    await settle(element);
    expect(must(root.querySelector<HTMLInputElement>('#prop-pattern-sample'), 'the sample').value).toBe('');
  });

  it('keeps a message of its own in the language edited, and is summed up by the pattern itself', async () => {
    const coded = { ...NAME, validation: [{ kind: 'pattern', pattern: CODE }] };
    const { root, requests } = await inspectorOf('name', {}, coded);
    expect(text(sectionButton(root, 'validation'))).toBe(`Validation Must match a pattern: ${CODE}`);
    sectionButton(root, 'validation').click();
    await settle(root.host as InspectorElement);
    const message = must(root.querySelector<HTMLInputElement>('#prop-pattern-message'), 'the message');
    expect(message.placeholder).toBe('The standard message');
    type(message, 'Use two letters, then four digits.');
    expect(applied(requests[0], coded)['validation']).toEqual([
      { kind: 'pattern', pattern: CODE, message: { '': 'Use two letters, then four digits.' } },
    ]);
    expect(requests[0]?.kind === 'change' && requests[0].merge).toMatch(/^name:pattern\.message:\d+$/);
  });
});
