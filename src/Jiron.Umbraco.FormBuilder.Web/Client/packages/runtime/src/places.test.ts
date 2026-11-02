// The places left on a form's options (ROADMAP 7.1e, ADR-0156): how many in each option's label, or that it is full,
// which disables it, unless the visitor has chosen it or the view disabled it; a later answer shows the new number.
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FormModel, ModelField } from './model';
import { showPlaces } from './places';
import { challengesOf, type Told } from './send';
import { ENGLISH } from './testing';

const FIELD = 'k-slot';

const shape = (extra = ''): void => {
  document.body.innerHTML =
    `<form><div data-jfb-field="${FIELD}">` +
    '<input type="radio" id="a" name="slot" value="a"><label for="a">Morning</label>' +
    '<input type="radio" id="b" name="slot" value="b"><label for="b">Noon</label>' +
    '<input type="radio" id="c" name="slot" value="c"><label for="c">Evening</label>' +
    `${extra}</div>` +
    '<div data-jfb-field="k-list"><select id="s" name="list"><option value="">Choose</option>' +
    '<option value="x">Ex</option><option value="y">Why</option></select></div>' +
    '<div data-jfb-field="k-other"><input type="checkbox" id="o" name="other" value="a"><label for="o">Other</label></div>' +
    '</form>';
};

const control = (id: string): HTMLInputElement => document.getElementById(id) as HTMLInputElement;
const label = (id: string): string | undefined => document.querySelector(`label[for="${id}"]`)?.textContent;
const option = (value: string): HTMLOptionElement =>
  document.querySelector(`option[value="${value}"]`) as HTMLOptionElement;

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('an option in a label', () => {
  it('says how many places it has left, in the words of the number', async () => {
    shape();
    await showPlaces(document, { [FIELD]: { a: 3, b: 1 } }, 'en-US');

    expect(label('a')).toBe('Morning 3 places left');
    expect(label('b')).toBe('Noon 1 place left');
    expect(document.querySelector('label[for="a"] .jfb-places')?.textContent).toBe('3 places left');
    expect(control('a').disabled).toBe(false);
  });

  it("says so in the page's language, with its number format", async () => {
    shape();
    await showPlaces(document, { [FIELD]: { a: 1200, b: 1, c: 0 } }, 'de-DE');

    expect(label('a')).toBe('Morning 1.200 Pl\u00e4tze frei');
    expect(label('b')).toBe('Noon 1 Platz frei');
    expect(label('c')).toBe('Evening Ausgebucht');
  });

  it('is disabled when it is full, which a rule leaves as it is', async () => {
    shape();
    await showPlaces(document, { [FIELD]: { c: 0 } }, 'en-US');

    expect(label('c')).toBe('Evening Full');
    expect(control('c').disabled).toBe(true);
    expect(control('c').hasAttribute('data-jfb-disabled')).toBe(true);
    expect(control('a').disabled, 'one the answer names no places for').toBe(false);
    expect(label('a')).toBe('Morning');
  });

  it("stays the visitor's to change when they have chosen it, for the server to say why", async () => {
    shape();
    control('c').checked = true;
    await showPlaces(document, { [FIELD]: { c: 0 } }, 'en-US');

    expect(label('c')).toBe('Evening Full');
    expect(control('c').disabled).toBe(false);
    expect(control('c').checked).toBe(true);
  });

  it('lets a full option go that only a default chose, as the visitor never did', async () => {
    shape();
    control('c').defaultChecked = true;
    control('c').checked = true;
    await showPlaces(document, { [FIELD]: { c: 0 } }, 'en-US');

    expect(control('c').checked).toBe(false);
    expect(control('c').disabled).toBe(true);
  });

  it('keeps the one the keyboard is on until it moves on', async () => {
    shape();
    control('c').focus();
    await showPlaces(document, { [FIELD]: { c: 0 } }, 'en-US');
    expect(control('c').disabled).toBe(false);
    expect(document.activeElement).toBe(control('c'));
    expect(label('c')).toBe('Evening Full');

    control('c').blur();
    await showPlaces(document, { [FIELD]: { c: 0 } }, 'en-US');
    expect(control('c').disabled).toBe(true);
  });

  it('leaves an option with places as a rule left it', async () => {
    shape();
    control('a').disabled = true;
    await showPlaces(document, { [FIELD]: { a: 2 } }, 'en-US');

    expect(control('a').disabled).toBe(true);
  });

  it('shows what a later answer says, once, and is free again when places came back', async () => {
    shape();
    await showPlaces(document, { [FIELD]: { c: 0, a: 2 } }, 'en-US');
    await showPlaces(document, { [FIELD]: { c: 4, a: 1 } }, 'en-US');

    expect(label('a')).toBe('Morning 1 place left');
    expect(label('c')).toBe('Evening 4 places left');
    expect(document.querySelectorAll('.jfb-places')).toHaveLength(2);
    expect(control('c').disabled).toBe(false);
    expect(control('c').hasAttribute('data-jfb-disabled')).toBe(false);
    expect(control('c').hasAttribute('data-jfb-full')).toBe(false);
  });

  it('stays disabled when its view disabled it, whatever places it has', async () => {
    shape();
    control('a').disabled = true;
    control('a').setAttribute('data-jfb-disabled', '');
    await showPlaces(document, { [FIELD]: { a: 2 } }, 'en-US');

    expect(label('a')).toBe('Morning 2 places left');
    expect(control('a').disabled).toBe(true);
    expect(control('a').hasAttribute('data-jfb-disabled')).toBe(true);

    await showPlaces(document, { [FIELD]: { a: 0 } }, 'en-US');
    await showPlaces(document, { [FIELD]: { a: 2 } }, 'en-US');
    expect(control('a').disabled, 'also once it was full and has places again').toBe(true);
    expect(control('a').hasAttribute('data-jfb-disabled')).toBe(true);
  });

  it('says so on a checkbox as on a radio button', async () => {
    shape();
    await showPlaces(document, { 'k-other': { a: 0 } }, 'en-US');

    expect(label('o')).toBe('Other Full');
    expect(control('o').disabled).toBe(true);
  });

  it('counts as an option of its own field only', async () => {
    shape();
    await showPlaces(document, { [FIELD]: { a: 0 } }, 'en-US');

    expect(control('o').disabled).toBe(false);
    expect(label('o')).toBe('Other');
  });

  it('takes no value the answer inherits as a name', async () => {
    shape('<input type="radio" id="p" name="slot" value="constructor"><label for="p">Odd</label>');
    await showPlaces(document, { [FIELD]: { a: 1 } }, 'en-US');

    expect(label('p')).toBe('Odd');
    expect(control('p').disabled).toBe(false);
  });

  it('shows in a row just added, as far as the root goes', async () => {
    shape();
    const row = document.createElement('div');
    row.innerHTML = `<div data-jfb-field="${FIELD}"><input type="radio" id="r" name="row" value="a"><label for="r">Row</label></div>`;
    document.body.append(row);
    await showPlaces(row, { [FIELD]: { a: 5 } }, 'en-US');

    expect(label('r')).toBe('Row 5 places left');
    expect(label('a'), 'nothing outside it').toBe('Morning');
  });
});

describe('an option of a list', () => {
  it('says it in its text, from its own label each time', async () => {
    shape();
    await showPlaces(document, { 'k-list': { x: 3 } }, 'en-US');
    await showPlaces(document, { 'k-list': { x: 2, y: 0 } }, 'en-US');

    expect(option('x').textContent).toBe('Ex (2 places left)');
    expect(option('y').textContent).toBe('Why (Full)');
    expect(option('y').disabled).toBe(true);
    expect(option('x').disabled).toBe(false);
    expect(option('').textContent, 'the prompt').toBe('Choose');
  });

  it('lets a full option go that only a default chose', async () => {
    shape();
    option('y').defaultSelected = true;
    await showPlaces(document, { 'k-list': { y: 0 } }, 'en-US');

    expect(option('y').selected).toBe(false);
    expect(option('y').disabled).toBe(true);
    expect((document.getElementById('s') as HTMLSelectElement).value).toBe('');
  });

  it('stays chosen when it is full', async () => {
    shape();
    (document.getElementById('s') as HTMLSelectElement).value = 'y';
    await showPlaces(document, { 'k-list': { y: 0 } }, 'en-US');

    expect(option('y').disabled).toBe(false);
    expect(option('y').selected).toBe(true);
    expect(option('y').textContent).toBe('Why (Full)');
  });
});

describe('what the challenge tells a form', () => {
  const SLOT: ModelField = { key: FIELD, type: 'radio', alias: 'slot', settings: {}, label: 'Slot' };
  const NOTE: ModelField = {
    key: 'k-note',
    type: 'radio',
    alias: 'note',
    settings: {},
    label: 'Note',
    rules: [{ effect: 'show', match: 'all', conditions: [{ context: 'memberSignedIn', op: 'isChecked' }] }],
  };
  const GUESTS: ModelField = { key: 'k-guests', type: 'repeater', alias: 'guests', settings: {}, fields: [SLOT] };

  const modelOf = (fields: ModelField[]): FormModel => ({
    alias: 'evening',
    revision: 1,
    culture: 'en-US',
    timeZone: 'UTC',
    open: true,
    form: { key: 'f', pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }] },
    fields: {
      [FIELD]: { kind: 'choice', canBeRequired: true, builtIn: true },
      'k-note': { kind: 'choice', canBeRequired: true, builtIn: true },
      'k-guests': { kind: null, canBeRequired: false, builtIn: true },
    },
    consents: {},
    ...ENGLISH,
  });

  const open = (asks: boolean, body: string): HTMLFormElement => {
    document.body.innerHTML =
      `<form id="jfb-1" data-jfb-form="f" data-jfb-api="/jfb/api/v1"${asks ? ' data-jfb-asks' : ''}>` +
      '<template data-jfb-templates data-jfb-sending="Sending"><p data-jfb-error></p></template>' +
      `<section data-jfb-page="p1"><div data-jfb-group="g1">${body}</div></section><div data-jfb-live></div></form>`;
    return document.querySelector('form') as HTMLFormElement;
  };
  const SLOTS =
    `<div data-jfb-field="${FIELD}" data-jfb-alias="slot" data-jfb-type="radio">` +
    '<input type="radio" id="a" name="slot" value="a"><label for="a">Morning</label></div>';
  const NOTES =
    '<div data-jfb-field="k-note" data-jfb-alias="note" data-jfb-type="radio" hidden data-jfb-hidden>' +
    '<input type="radio" id="n" name="note" value="y" disabled><label for="n">Yes</label>' +
    '<input type="radio" id="m" name="note" value="z" disabled><label for="m">Maybe</label></div>';

  const answering = (told: Told | null): ReturnType<typeof vi.fn> => {
    const asked = vi.fn((url: string) =>
      Promise.resolve(
        url.endsWith('/challenge') && told !== null
          ? new Response(JSON.stringify({ challenge: 'c1', ...told }))
          : new Response('{}', { status: 500 }),
      ),
    );
    vi.stubGlobal('fetch', asked);
    return asked;
  };

  it('shows the places left and decides the rules for the member', async () => {
    answering({ places: { [FIELD]: { a: 4 } }, member: { signedIn: true, groups: [] } });
    const form = open(true, SLOTS + NOTES);
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT, NOTE]));

    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    await vi.waitFor(() => expect(form.querySelector('[data-jfb-field="k-note"]')?.hasAttribute('hidden')).toBe(false));
    expect(control('n').disabled).toBe(false);
  });

  it('keeps a full option disabled in a field the member brings out', async () => {
    answering({ places: { 'k-note': { y: 0 } }, member: { signedIn: true, groups: [] } });
    const form = open(true, NOTES);
    const { attach } = await import('./form');
    await attach(form, modelOf([NOTE]));

    await vi.waitFor(() => expect(form.querySelector('[data-jfb-field="k-note"]')?.hasAttribute('hidden')).toBe(false));
    expect(control('n').disabled).toBe(true);
    expect(control('m').disabled).toBe(false);
  });

  it('leaves a visitor out of what the rules ask of a member', async () => {
    answering({ places: { [FIELD]: { a: 4 } } });
    const form = open(true, SLOTS + NOTES);
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT, NOTE]));

    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    expect(form.querySelector('[data-jfb-field="k-note"]')?.hasAttribute('hidden')).toBe(true);
    expect(control('n').disabled).toBe(true);
  });

  it('shows what the answer holds at the first touch too, on a page cached before the form asked for it', async () => {
    answering({ places: { [FIELD]: { a: 4 } }, member: { signedIn: true, groups: [] } });
    const form = open(false, SLOTS + NOTES);
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT, NOTE]));

    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    await vi.waitFor(() => expect(form.querySelector('[data-jfb-field="k-note"]')?.hasAttribute('hidden')).toBe(false));
  });

  describe('of a captcha', () => {
    const CHECK = {
      provider: 'hcaptcha',
      siteKey: 'k',
      scriptAddress: 'https://check.example/own.js',
      consent: 'explicit',
    };
    const AGREE: ModelField = {
      key: 'k-agree',
      type: 'consent',
      alias: 'agree',
      settings: { loadsCaptcha: true },
      label: 'Agree',
    };
    const AGREEMENT =
      '<div data-jfb-field="k-agree" data-jfb-alias="agree" data-jfb-type="consent"><input type="checkbox" id="agree" name="agree" value="true"><label for="agree">Agree</label></div>';
    const sends = (): HTMLFormElement => {
      const form = open(
        true,
        SLOTS +
          AGREEMENT +
          '</div></section><div class="jfb-navigation"><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button></div><div>',
      );
      form.setAttribute('action', '/page/#jfb-1');
      return form;
    };
    const pressing = (form: HTMLFormElement): void => {
      form.dispatchEvent(
        new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: document.getElementById('send') }),
      );
    };

    afterEach(() => {
      Reflect.deleteProperty(window, 'JironFormBuilder');
      document.head.innerHTML = '';
    });

    it('shows its notice, and a send without its token asks for it and sends nothing', async () => {
      const asked = answering({ captcha: CHECK });
      const form = sends();
      Object.assign(window, {
        JironFormBuilder: { consent: { has: () => false, grant: () => undefined, withdraw: () => undefined } },
      });
      const model = modelOf([SLOT]);
      const { attach } = await import('./form');
      await attach(form, model);
      await vi.waitFor(() => expect(form.querySelector('.jfb-captcha p')).not.toBeNull());
      control('a').checked = true;
      pressing(form);

      await vi.waitFor(() => expect(form.querySelector('.jfb-captcha .jfb-error')).not.toBeNull());
      expect(asked.mock.calls.filter(([url]) => String(url).endsWith('/entries'))).toEqual([]);
    });

    it('takes the consent field that says it agrees', async () => {
      answering({ captcha: CHECK });
      const form = sends();
      Object.assign(window, {
        JironFormBuilder: { consent: { has: () => false, grant: () => undefined, withdraw: () => undefined } },
      });
      const model = modelOf([SLOT, AGREE]);
      const { attach } = await import('./form');
      await attach(form, {
        ...model,
        fields: { ...model.fields, 'k-agree': { kind: 'choice', canBeRequired: true, builtIn: true } },
      });
      await vi.waitFor(() => expect(form.querySelector('.jfb-captcha')).not.toBeNull());
      expect(form.querySelector('.jfb-captcha p'), 'the field says it itself').toBeNull();

      control('agree').checked = true;
      control('agree').dispatchEvent(new Event('change', { bubbles: true }));
      await vi.waitFor(() => expect(document.head.querySelector('script')).not.toBeNull());
    });
  });

  it('asks once, for the sends to use', async () => {
    const asked = answering({ places: { [FIELD]: { a: 4 } } });
    const form = open(true, SLOTS);
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT]));
    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));

    expect(asked.mock.calls.filter(([url]) => String(url).endsWith('/challenge'))).toHaveLength(1);
  });

  it('asks without taking the files of a form that posts them', async () => {
    answering({ places: { [FIELD]: { a: 4 } } });
    const file: ModelField = { key: 'k-file', type: 'file', alias: 'plan', settings: {}, label: 'Plan' };
    const form = open(
      true,
      SLOTS +
        '</div></section><section data-jfb-page="p2" hidden><div>' +
        '<div data-jfb-field="k-file" data-jfb-alias="plan" data-jfb-type="file"><div class="jfb-upload">' +
        '<input type="file" id="f" name="plan"></div><template data-jfb-file><ul class="jfb-files"></ul><li class="jfb-file"><strong></strong><p></p><progress></progress></li></template></div>',
    );
    const model = modelOf([SLOT, file]);
    const { attach } = await import('./form');
    await attach(form, {
      ...model,
      fields: { ...model.fields, 'k-file': { kind: 'files', canBeRequired: true, builtIn: true } },
    });
    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(form.querySelector('.jfb-upload')?.hasAttribute('data-jfb-enhanced')).toBe(false);
  });

  it('tells nothing to a form whose answer holds nothing for it', async () => {
    answering({});
    const inputs: EventTarget[] = [];
    const ruled = open(true, SLOTS + NOTES);
    ruled.addEventListener('input', (event) => inputs.push(event.target as EventTarget));
    const { attach } = await import('./form');
    await attach(ruled, modelOf([SLOT, NOTE]));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(inputs).toEqual([]);
  });

  it('tells what follows the rules, as an answer does, on a form with rules only', async () => {
    answering({ places: { [FIELD]: { a: 4 } } });
    const inputs: EventTarget[] = [];
    const ruled = open(true, SLOTS + NOTES);
    ruled.addEventListener('input', (event) => inputs.push(event.target as EventTarget));
    const { attach } = await import('./form');
    await attach(ruled, modelOf([SLOT, NOTE]));
    await vi.waitFor(() => expect(inputs).toEqual([ruled]));

    inputs.length = 0;
    const plain = open(true, SLOTS);
    plain.addEventListener('input', (event) => inputs.push(event.target as EventTarget));
    await attach(plain, modelOf([SLOT]));
    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(inputs).toEqual([]);
  });

  it('holds an option the visitor moved off to what the answer said, and options written anew', async () => {
    answering({ places: { [FIELD]: { a: 0, b: 2 } } });
    const form = open(
      true,
      `<div data-jfb-field="${FIELD}" data-jfb-alias="slot" data-jfb-type="radio">` +
        '<input type="radio" id="a" name="slot" value="a" checked><label for="a">Morning</label>' +
        '<input type="radio" id="b" name="slot" value="b"><label for="b">Noon</label></div>',
    );
    control('a').defaultChecked = false;
    control('a').checked = true;
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT]));
    await vi.waitFor(() => expect(label('a')).toBe('Morning Full'));
    expect(control('a').disabled).toBe(false);

    control('b').checked = true;
    control('b').dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(control('a').disabled).toBe(true));

    const written = form.querySelector(`[data-jfb-field="${FIELD}"]`);
    written?.insertAdjacentHTML(
      'beforeend',
      '<input type="radio" id="c" name="slot" value="a"><label for="c">Later</label>',
    );
    written?.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(label('c')).toBe('Later Full'));
    expect(control('c').disabled).toBe(true);
  });

  const sending = (): HTMLFormElement => {
    const form = open(
      true,
      SLOTS +
        '</div></section><div><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button><div>',
    );
    form.setAttribute('action', '/page/#jfb-1');
    return form;
  };
  const pressSend = (form: HTMLFormElement): void => {
    form.dispatchEvent(
      new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: document.getElementById('send') }),
    );
  };

  it('hears the numbers anew after a send a full option refused, and keeps its challenge', async () => {
    const left = [4, 0];
    const sent: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        if (url.endsWith('/challenge'))
          return Promise.resolve(
            new Response(
              JSON.stringify({ challenge: `c${String(left.length)}`, places: { [FIELD]: { a: left.shift() } } }),
            ),
          );
        sent.push((JSON.parse(init?.body as string) as { challenge: unknown }).challenge);
        return Promise.resolve(
          new Response(JSON.stringify({ code: 'validation_failed', errors: [{ code: 'full', path: 'slot' }] }), {
            status: 422,
          }),
        );
      }),
    );
    const form = sending();
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT]));
    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    control('a').checked = true;
    pressSend(form);

    await vi.waitFor(() => expect(label('a')).toBe('Morning Full'));
    expect(control('a').disabled, 'still the answer on the page').toBe(false);
    expect(sent).toEqual(['c2']);
    pressSend(form);
    await vi.waitFor(() => expect(sent).toEqual(['c2', 'c2']));
  });

  it('asks nothing anew after a send refused for anything else', async () => {
    const asked = vi.fn((url: string) =>
      Promise.resolve(
        url.endsWith('/challenge')
          ? new Response(JSON.stringify({ challenge: 'c1', places: { [FIELD]: { a: 4 } } }))
          : new Response(JSON.stringify({ code: 'validation_failed', errors: [{ code: 'required', path: 'slot' }] }), {
              status: 422,
            }),
      ),
    );
    vi.stubGlobal('fetch', asked);
    const form = sending();
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT]));
    await vi.waitFor(() => expect(label('a')).toBe('Morning 4 places left'));
    control('a').checked = true;
    pressSend(form);
    await vi.waitFor(() => expect(asked.mock.calls.filter(([url]) => url.endsWith('/entries'))).toHaveLength(1));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(asked.mock.calls.filter(([url]) => url.endsWith('/challenge'))).toHaveLength(1);
  });

  it('forgets a member the next answer does not name', async () => {
    let member: Told['member'] = { signedIn: true, groups: [] };
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) =>
        Promise.resolve(
          url.endsWith('/challenge')
            ? new Response(JSON.stringify({ challenge: 'c1', member }))
            : new Response(JSON.stringify({ code: 'validation_failed', errors: [{ code: 'full', path: 'slot' }] }), {
                status: 422,
              }),
        ),
      ),
    );
    const form = open(
      true,
      SLOTS +
        NOTES +
        '</div></section><div><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button><div>',
    );
    form.setAttribute('action', '/page/#jfb-1');
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT, NOTE]));
    const note = form.querySelector('[data-jfb-field="k-note"]');
    await vi.waitFor(() => expect(note?.hasAttribute('hidden')).toBe(false));
    member = undefined;
    control('a').checked = true;
    pressSend(form);

    await vi.waitFor(() => expect(note?.hasAttribute('hidden')).toBe(true));
  });

  it('keeps the member for the form that comes in its place, for the rules to hold at its wake', async () => {
    const markup = (revision: number): string =>
      `<form id="jfb-1" data-jfb-form="f" data-jfb-revision="${String(revision)}" data-jfb-contract="1" data-jfb-api="/jfb/api/v1" action="/page/#jfb-1" data-jfb-asks>` +
      `<script type="application/json" data-jfb-model>${JSON.stringify({ ...modelOf([SLOT, NOTE]), revision })}</script>` +
      '<template data-jfb-templates data-jfb-sending="Sending"><p data-jfb-error></p><div data-jfb-notice="form_changed" role="alert" tabindex="-1"></div></template>' +
      `<section data-jfb-page="p1"><div data-jfb-group="g1">${SLOTS + NOTES}</div></section>` +
      '<div><button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit" id="send">Send</button></div><div data-jfb-live></div></form>';
    let asks = 0;
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) => {
        if (url.endsWith('/challenge')) {
          asks += 1;
          // What the new form hears comes late: it holds the member without it.
          return asks === 1
            ? Promise.resolve(new Response(JSON.stringify({ challenge: 'c1', member: { signedIn: true, groups: [] } })))
            : new Promise<Response>(() => undefined);
        }
        if (url.includes('/render')) return Promise.resolve(new Response(markup(2)));
        return Promise.resolve(new Response(JSON.stringify({ code: 'form_changed' }), { status: 409 }));
      }),
    );
    document.body.innerHTML = markup(1);
    const form = document.querySelector('form') as HTMLFormElement;
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT, NOTE]));
    await vi.waitFor(() => expect(form.querySelector('[data-jfb-field="k-note"]')?.hasAttribute('hidden')).toBe(false));
    control('a').checked = true;
    pressSend(form);

    await vi.waitFor(() => expect(form.isConnected).toBe(false));
    await vi.waitFor(() => expect(asks).toBe(2));
    expect(document.querySelector('[data-jfb-field="k-note"]')?.hasAttribute('hidden')).toBe(false);
  });

  it('leaves the form as it is when no answer comes', async () => {
    answering(null);
    const form = open(true, SLOTS);
    const { attach } = await import('./form');

    expect(await attach(form, modelOf([SLOT]))).not.toBeNull();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(label('a')).toBe('Morning');
  });

  it('shows them in a row added after the answer', async () => {
    answering({ places: { [FIELD]: { a: 2 } } });
    const row =
      '<fieldset class="jfb-row" id="jfb-1-guests-0" data-jfb-row="0"><legend>Guest 1</legend>' +
      `<div data-jfb-field="${FIELD}" data-jfb-alias="guests[0].slot" data-jfb-type="radio">` +
      '<input type="radio" id="r" name="guests[0].slot" value="a"><label for="r">In a row</label></div></fieldset>';
    const form = open(
      true,
      SLOTS +
        '<div data-jfb-field="k-guests" data-jfb-alias="guests"><div class="jfb-repeater" id="jfb-1-guests" data-jfb-least="0" data-jfb-most="3" data-jfb-legend="Guest {0}" data-jfb-removed="{0} removed.">' +
        `<input type="hidden" name="jfb-rows-guests" value="0"><template>${row}</template>` +
        '<button type="submit" name="jfb-nav" value="add:guests">Add a row</button></div></div>',
    );
    const { attach } = await import('./form');
    await attach(form, modelOf([SLOT, GUESTS]));
    await vi.waitFor(() => expect(label('a')).toBe('Morning 2 places left'));

    form.querySelector<HTMLElement>('[value="add:guests"]')?.click();

    await vi.waitFor(() =>
      expect(form.querySelector('.jfb-repeater > fieldset label')?.textContent).toBe('In a row 2 places left'),
    );
  });

  describe('through its challenge', () => {
    const FORM = (): HTMLFormElement => open(true, SLOTS);

    it('hears each answer, a renewal too', async () => {
      const asked = answering({ places: { [FIELD]: { a: 4 } } });
      const heard: Told[] = [];
      const challenges = challengesOf(FORM(), '/jfb/api/v1', undefined, (told) => heard.push(told));
      await challenges.current();
      await challenges.renew();

      expect(asked).toHaveBeenCalledTimes(2);
      expect(heard).toHaveLength(2);
      expect(heard[0]?.places).toEqual({ [FIELD]: { a: 4 } });
    });

    it('asks once to hear it for a form that came with its challenge, and keeps that one', async () => {
      const asked = answering({ places: { [FIELD]: { a: 4 } } });
      const heard: Told[] = [];
      const challenges = challengesOf(FORM(), '/jfb/api/v1', 'kept', (told) => heard.push(told));

      await vi.waitFor(() => expect(heard).toHaveLength(1));
      expect(await challenges.current()).toBe('kept');
      expect(asked).toHaveBeenCalledOnce();
    });

    it('listens without changing the challenge it holds, and only for a form that hears', async () => {
      const asked = answering({ places: { [FIELD]: { a: 4 } } });
      const heard: Told[] = [];
      const listening = challengesOf(FORM(), '/jfb/api/v1', 'kept', (told) => heard.push(told));
      await vi.waitFor(() => expect(heard).toHaveLength(1));
      listening.listen();
      await vi.waitFor(() => expect(heard).toHaveLength(2));
      expect(await listening.current()).toBe('kept');

      asked.mockClear();
      challengesOf(FORM(), '/jfb/api/v1', 'kept').listen();
      expect(asked).not.toHaveBeenCalled();
    });

    it('asks for nothing when a form came with its challenge and hears nothing', async () => {
      const asked = answering({});
      const challenges = challengesOf(FORM(), '/jfb/api/v1', 'kept');

      expect(await challenges.current()).toBe('kept');
      expect(asked).not.toHaveBeenCalled();
    });

    it('keeps the puzzle of the one it holds when it only listens', async () => {
      let nonce = 'first';
      vi.stubGlobal(
        'fetch',
        vi.fn(() =>
          Promise.resolve(new Response(JSON.stringify({ challenge: 'c1', proofOfWork: { nonce, bits: 8 } }))),
        ),
      );
      const heard: Told[] = [];
      const challenges = challengesOf(FORM(), '/jfb/api/v1', undefined, (told) => heard.push(told));
      await challenges.current();
      nonce = 'second';
      challenges.listen();
      await vi.waitFor(() => expect(heard).toHaveLength(2));

      expect(challenges.puzzle()?.nonce).toBe('first');
    });
  });
});
