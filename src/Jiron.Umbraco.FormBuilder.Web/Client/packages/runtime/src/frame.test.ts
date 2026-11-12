// The frame (ROADMAP 6.4b, ADR-0110): the forms of the markup contract found and enhanced once, later ones through
// init(root), others left to the post without JavaScript, `jfb:ready` once per form, and the chunk with the logic
// loaded on a form's first interaction only, or at once for a form with a default the page fills in (6.4h, ADR-0118),
// the print button of the copy of the answers below an outcome revealed and bound once (6.5b, ADR-0123), every form
// root fitted to its page once, a form to work with through `get` (6.4l, section 4.9), and what visitors do with a form
// counted where its markup names where to (ROADMAP 11.5, ADR-0211).
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createFrame, type Chunk, type ReadyDetail } from './frame';
import type { FormModel } from './model';

const model = (alias: string): string =>
  JSON.stringify({
    alias,
    revision: 1,
    culture: 'en-US',
    timeZone: 'UTC',
    open: true,
    form: {
      key: 'f',
      pages: [{ key: 'p', groups: [{ key: 'g', fields: [{ key: 'k', type: 'text', alias: 'name' }] }] }],
    },
    fields: { k: { kind: 'text', canBeRequired: true, builtIn: true } },
    formats: {},
    words: { yes: 'Yes', no: 'No' },
    mergeTags: null,
    consents: {},
  });

/** A form as the views render it: the contract's attributes, its model data, one input. */
function form(
  id: string,
  options: { contract?: string | null; data?: string | null; alias?: string } = {},
): HTMLFormElement {
  const element = document.createElement('form');
  element.id = id;
  element.dataset['jfbForm'] = `key-${id}`;
  if (options.contract !== null) element.dataset['jfbContract'] = options.contract ?? '1';
  if (options.data !== null) {
    const script = document.createElement('script');
    script.type = 'application/json';
    script.dataset['jfbModel'] = '';
    script.textContent = options.data ?? model(options.alias ?? id);
    element.append(script);
  }
  const input = document.createElement('input');
  input.name = 'name';
  element.append(input);
  return element;
}

/** An outcome in a form's place with the copy of the answers below it, as Outcome.cshtml writes it. */
function outcome(id: string, contract = '1'): HTMLDivElement {
  const wrapper = document.createElement('div');
  wrapper.id = id;
  wrapper.dataset['jfbForm'] = `key-${id}`;
  wrapper.dataset['jfbContract'] = contract;
  const message = document.createElement('div');
  message.dataset['jfbOutcome'] = '';
  const copy = document.createElement('div');
  copy.dataset['jfbCopy'] = '';
  const print = document.createElement('button');
  print.type = 'button';
  print.dataset['jfbPrint'] = '';
  print.hidden = true;
  print.textContent = 'Print your answers';
  const again = document.createElement('a');
  again.href = `#${id}`;
  copy.append(print, again);
  wrapper.append(message, copy);
  return wrapper;
}

/** The print button below an outcome. */
function printOf(root: ParentNode): HTMLButtonElement {
  const button = root.querySelector<HTMLButtonElement>('button[data-jfb-print]');
  if (button === null) throw new Error('the outcome has no print button');
  return button;
}

/** The `jfb:ready` events the document hears. */
function readies(): ReadyDetail[] {
  const heard: ReadyDetail[] = [];
  document.addEventListener('jfb:ready', (event) => heard.push((event as CustomEvent<ReadyDetail>).detail));
  return heard;
}

function chunk(): { load: () => Promise<Chunk>; attached: [HTMLFormElement, FormModel][]; loads: () => number } {
  const attached: [HTMLFormElement, FormModel][] = [];
  let loads = 0;
  return {
    attached,
    loads: () => loads,
    load: () => {
      loads++;
      return Promise.resolve({ attach: (element: HTMLFormElement, data: FormModel) => attached.push([element, data]) });
    },
  };
}

/** The form's input, which every form here has. */
function inputOf(root: ParentNode): HTMLInputElement {
  const input = root.querySelector('input');
  if (input === null) throw new Error('the form has no input');
  return input;
}

const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

afterEach(() => {
  document.body.replaceChildren();
});

describe('the frame', () => {
  it('enhances every form of the contract on the page once, and says so once per form', () => {
    const heard = readies();
    document.body.append(form('jfb-1', { alias: 'contact' }), form('jfb-2', { alias: 'newsletter' }));
    const frame = createFrame(chunk().load);
    frame.init();
    frame.init(document);
    expect(heard).toEqual([
      { key: 'key-jfb-1', alias: 'contact' },
      { key: 'key-jfb-2', alias: 'newsletter' },
    ]);
  });

  it('enhances forms added later through init(root), and the root itself when it is a form', () => {
    const heard = readies();
    const frame = createFrame(chunk().load);
    frame.init();
    expect(heard).toEqual([]);
    const later = document.createElement('div');
    later.append(form('jfb-3', { alias: 'later' }));
    document.body.append(later);
    frame.init(later);
    const alone = form('jfb-4', { alias: 'alone' });
    document.body.append(alone);
    frame.init(alone);
    frame.init();
    expect(heard.map((detail) => detail.alias)).toEqual(['later', 'alone']);
  });

  it('leaves a form of another contract version, or without one, to the post without JavaScript', async () => {
    const heard = readies();
    const loaded = chunk();
    const two = form('jfb-1', { contract: '2' });
    const none = form('jfb-2', { contract: null });
    document.body.append(two, none);
    createFrame(loaded.load).init();
    inputOf(two).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    inputOf(none).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await settled();
    expect(heard).toEqual([]);
    expect(loaded.loads()).toBe(0);
  });

  it('leaves a form whose model data is missing or not what it reads', () => {
    const heard = readies();
    const shapes = [
      null,
      '',
      'not json',
      '[]',
      '{"alias":"x"}',
      model('x').replace('"pages":[', '"pages":[1,'),
      model('x').replace('"type":"text"', '"type":7'),
      model('x').replace('"fields":{', '"fields":[{'),
      model('x').replace('"mergeTags":null', '"mergeTags":[]'),
      model('x').replace('"mergeTags":null', '"mergeTags":{"texts":[],"values":{}}'),
      model('x').replace('"mergeTags":null', '"mergeTags":{"texts":[{"owner":1}],"values":{},"recall":{"pages":[]}}'),
      model('x').replace(
        '"mergeTags":null',
        '"mergeTags":{"texts":[{"owner":"k","text":"label","context":"text","refused":[1]}],"values":{},"recall":{"pages":[]}}',
      ),
      model('x').replace(
        '"mergeTags":null',
        '"mergeTags":{"texts":[{"owner":"k","text":"label","context":"text","refused":"member.name"}],"values":{},"recall":{"pages":[]}}',
      ),
    ];
    shapes.forEach((data, at) => document.body.append(form(`jfb-${at + 1}`, { data })));
    createFrame(chunk().load).init();
    expect(heard).toEqual([]);
  });

  it('reads a form whose merge tags are the shape it renders its texts with', () => {
    const heard = readies();
    const tags =
      '{"texts":[{"owner":"k","text":"label","context":"text"},' +
      '{"owner":"k","text":"defaultValue","context":"text","refused":["member.name"]}],"values":{},"recall":{"pages":[]}}';
    document.body.append(form('jfb-1', { data: model('tagged').replace('"mergeTags":null', '"mergeTags":' + tags) }));
    createFrame(chunk().load).init();
    expect(heard).toEqual([{ key: 'key-jfb-1', alias: 'tagged' }]);
  });

  it('enhances only form elements, not an outcome or a notice in place of a form', () => {
    const heard = readies();
    const outcome = document.createElement('div');
    outcome.dataset['jfbForm'] = 'key';
    outcome.dataset['jfbContract'] = '1';
    document.body.append(outcome);
    createFrame(chunk().load).init();
    expect(heard).toEqual([]);
  });

  it("loads the logic on a form's first interaction only, once for the page, and gives it each form once", async () => {
    const loaded = chunk();
    const first = form('jfb-1');
    const second = form('jfb-2');
    document.body.append(first, second);
    createFrame(loaded.load).init();
    await settled();
    expect(loaded.loads()).toBe(0);

    inputOf(first).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    inputOf(first).dispatchEvent(new Event('input', { bubbles: true }));
    await settled();
    expect(loaded.loads()).toBe(1);
    expect(loaded.attached.map(([element, data]) => [element.id, data.alias])).toEqual([['jfb-1', 'jfb-1']]);

    second.dispatchEvent(new Event('pointerdown', { bubbles: true }));
    second.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await settled();
    expect(loaded.loads()).toBe(1);
    expect(loaded.attached.map(([element]) => element.id)).toEqual(['jfb-1', 'jfb-2']);
  });

  it('loads the logic at once for a form with a default the page fills in, and attaches it once', async () => {
    const loaded = chunk();
    const marked = form('jfb-1');
    const field = document.createElement('div');
    field.dataset['jfbField'] = 'k';
    field.dataset['jfbDefault'] = '';
    marked.append(field);
    document.body.append(marked, form('jfb-2'));
    createFrame(loaded.load).init();
    await settled();
    expect(loaded.attached.map(([element]) => element.id)).toEqual(['jfb-1']);

    inputOf(marked).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await settled();
    expect(loaded.loads()).toBe(1);
    expect(loaded.attached.map(([element]) => element.id)).toEqual(['jfb-1']);
  });

  it('loads the logic at once for a form that asks at once, what a cached page cannot carry', async () => {
    const loaded = chunk();
    const asking = form('jfb-1');
    asking.setAttribute('data-jfb-asks', '');
    document.body.append(asking, form('jfb-2'));
    createFrame(loaded.load).init();
    await settled();

    expect(loaded.attached.map(([element]) => element.id)).toEqual(['jfb-1']);
  });

  it.each(['focusin', 'pointerdown', 'input', 'change'])('loads the logic on %s', async (type) => {
    const loaded = chunk();
    document.body.append(form('jfb-1'));
    createFrame(loaded.load).init();
    inputOf(document).dispatchEvent(new Event(type, { bubbles: true }));
    await settled();
    expect(loaded.attached).toHaveLength(1);
  });

  it("reveals the copy's print button below an outcome and binds it to the browser's print once", () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    try {
      document.body.append(outcome('jfb-1'));
      const frame = createFrame(chunk().load);
      frame.init();
      frame.init(document);
      const button = printOf(document);
      expect(button.hidden).toBe(false);
      button.click();
      expect(print).toHaveBeenCalledTimes(1);
    } finally {
      print.mockRestore();
    }
  });

  it('reveals the print button of an outcome added later through init(root), the root itself included', () => {
    const frame = createFrame(chunk().load);
    frame.init();
    const later = outcome('jfb-2');
    document.body.append(later);
    expect(printOf(later).hidden).toBe(true);
    frame.init(later);
    expect(printOf(later).hidden).toBe(false);
  });

  it('leaves the print button of another contract version hidden, as a page without JavaScript has it', () => {
    document.body.append(outcome('jfb-1', '2'));
    createFrame(chunk().load).init();
    expect(printOf(document).hidden).toBe(true);
  });

  // Vitest fails the run on an unhandled rejection, so a failed load that leaked one would fail here.
  it('goes on to the other forms when one throws as it is enhanced, and reports what it threw', () => {
    // One broken form on a page (16.4, ADR-0237), whatever it throws.
    const broken = new Error('broken');
    const reported: unknown[] = [];
    vi.stubGlobal('reportError', (error: unknown) => reported.push(error));
    const first = form('jfb-1');
    Object.defineProperty(first, 'querySelector', {
      value: () => {
        throw broken;
      },
    });
    document.body.append(first, form('jfb-2'), form('jfb-3'));
    const heard = readies();
    const frame = createFrame(chunk().load);

    expect(() => frame.init()).not.toThrow();
    expect(heard.map((detail) => detail.alias)).toEqual(['jfb-2', 'jfb-3']);
    expect(reported).toEqual([broken]);
    expect(frame.get(first), 'the broken one is left to the post without JavaScript').toBeNull();
    expect(reported, 'asked for again, it throws again, and says so').toEqual([broken, broken]);
    vi.unstubAllGlobals();
  });

  it('leaves the forms to the post without JavaScript when the logic cannot load, and tries once', async () => {
    const failing = vi.fn(() => Promise.reject(new Error('offline')));
    document.body.append(form('jfb-1'), form('jfb-2'));
    createFrame(failing).init();
    for (const input of document.querySelectorAll('input'))
      input.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await settled();
    await settled();
    expect(failing).toHaveBeenCalledTimes(1);
  });
});

describe('a form to work with', () => {
  const session = {
    validate: vi.fn(() => Promise.resolve(false)),
    submit: vi.fn(() => true),
    values: vi.fn(() => ({ name: ['Ana'] })),
  };

  function placed(): HTMLFormElement {
    const element = form('f1');
    element.className = 'jfb-form';
    document.body.append(element);
    return element;
  }

  it('loads the logic when first asked, once, and asks it', async () => {
    const element = placed();
    const attach = vi.fn(() => Promise.resolve(session));
    const load = vi.fn(() => Promise.resolve({ attach } as Chunk));
    const handle = createFrame(load).get(element);
    expect(load, 'not before it is asked').not.toHaveBeenCalled();
    expect(Object.isFrozen(handle), "a site's code cannot change it").toBe(true);

    expect(await handle?.validate()).toBe(false);
    expect(session.validate).toHaveBeenCalledWith('send');
    expect(await handle?.submit()).toBe(true);
    expect(await handle?.values()).toEqual({ name: ['Ana'] });
    element.dispatchEvent(new Event('focusin', { bubbles: true }));

    expect(load).toHaveBeenCalledTimes(1);
    expect(attach).toHaveBeenCalledTimes(1);
  });

  it('shares the logic a touch woke', async () => {
    const element = placed();
    const attach = vi.fn(() => Promise.resolve(session));
    const frame = createFrame(() => Promise.resolve({ attach } as Chunk));
    frame.init();
    element.dispatchEvent(new Event('focusin', { bubbles: true }));

    await frame.get(element)?.values();

    expect(attach).toHaveBeenCalledTimes(1);
  });

  it('is none for an element that is no form of the contract', () => {
    const frame = createFrame(() => Promise.reject(new Error('never')));
    const other = form('o', { contract: '9' });
    const unreadable = form('u', { data: 'nope' });
    const unnamed = form('n');
    unnamed.removeAttribute('data-jfb-form');
    document.body.append(other, unreadable, unnamed);

    expect(frame.get(document.body)).toBeNull();
    expect(frame.get(document.createElement('form'))).toBeNull();
    expect(frame.get(other)).toBeNull();
    expect(frame.get(unreadable)).toBeNull();
    expect(frame.get(unnamed)).toBeNull();
  });

  it('says nothing stops a form whose logic cannot load, as the server checks every send', async () => {
    const element = placed();
    const handle = createFrame(() => Promise.reject(new Error('offline'))).get(element);

    expect(await handle?.validate()).toBe(true);
    expect(await handle?.submit()).toBe(false);
    expect(await handle?.values()).toEqual({});
  });

  it("reaches a form that came in another one's place through the logic that woke it", async () => {
    const element = placed();
    element.setAttribute('data-jfb-woken', '');
    const attach = vi.fn();
    const sessionOf = vi.fn(() => Promise.resolve(session));
    const other = placed();
    const frame = createFrame(() => Promise.resolve({ attach, sessionOf } as Chunk));
    frame.init();
    other.dispatchEvent(new Event('focusin', { bubbles: true }));

    expect(await frame.get(element)?.values()).toEqual({ name: ['Ana'] });
    expect(sessionOf).toHaveBeenCalledWith(element);
    expect(attach.mock.calls.map((call: unknown[]) => call[0])).not.toContain(element);
  });
});

describe('fitting a form to its page', () => {
  it('reaches every form root of the contract once, an outcome among them', () => {
    const outcomeRoot = document.createElement('div');
    outcomeRoot.className = 'jfb-form';
    outcomeRoot.dataset['jfbContract'] = '1';
    const otherContract = document.createElement('div');
    otherContract.className = 'jfb-form';
    otherContract.dataset['jfbContract'] = '9';
    document.body.append(outcomeRoot, otherContract);
    const listen = vi.fn();
    vi.stubGlobal('matchMedia', () => ({ addEventListener: listen }));
    const frame = createFrame(() => Promise.reject(new Error('never')));

    frame.init();
    frame.init();

    expect(listen, 'one form root, once').toHaveBeenCalledTimes(1);
    vi.unstubAllGlobals();
  });
});

describe('counting what visitors do with a form', () => {
  const EVENTS = '/jfb/api/v1/forms/key-jfb-1/events';

  /** The beacons sent, as their bodies read, by the address they went to. */
  function beacons(): [string, unknown][] {
    const sent: [string, unknown][] = [];
    vi.stubGlobal('navigator', {
      ...navigator,
      sendBeacon: (url: string, body: string) => sent.push([url, JSON.parse(body)]) > 0,
    });
    return sent;
  }

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends a view as it enhances a form whose markup names where to count, once in its life', () => {
    const sent = beacons();
    const counted = form('jfb-1');
    counted.dataset['jfbInsights'] = EVENTS;
    document.body.append(counted, form('jfb-2'));
    const frame = createFrame(chunk().load);

    frame.init();
    frame.init();

    expect(sent, 'the form without the attribute sends nothing').toEqual([[EVENTS, { type: 'view' }]]);
  });

  it('sends each page reached and each field that stopped the visitor once, with its first kind, never an answer', () => {
    const sent = beacons();
    const counted = form('jfb-1');
    counted.dataset['jfbInsights'] = EVENTS;
    document.body.append(counted);
    createFrame(chunk().load).init();
    const invalid = (errors: { field: string | null; kind: string }[]) =>
      counted.dispatchEvent(
        new CustomEvent('jfb:invalid', { detail: { key: 'key-jfb-1', alias: 'jfb-1', scope: 'send', errors } }),
      );

    counted.dispatchEvent(new CustomEvent('jfb:page', { detail: { page: 'p2' } }));
    counted.dispatchEvent(new CustomEvent('jfb:page', { detail: { page: 'p2' } }));
    invalid([
      { field: 'email', kind: 'invalid' },
      { field: null, kind: 'empty' },
      { field: 'guests[0].name', kind: 'required' },
    ]);
    invalid([{ field: 'email', kind: 'invalid' }]);
    invalid([{ field: 'email', kind: 'required' }]);

    expect(sent.map(([, body]) => body)).toEqual([
      { type: 'view' },
      { type: 'page', page: 'p2' },
      { type: 'invalid', field: 'email', kind: 'invalid' },
      { type: 'invalid', field: 'guests[0].name', kind: 'required' },
    ]);
  });

  it('sends a beacon the browser could not queue again the next time, and counts nothing at an empty address', () => {
    const tried: unknown[] = [];
    let queues = false;
    vi.stubGlobal('navigator', {
      ...navigator,
      sendBeacon: (_url: string, body: string) => tried.push(JSON.parse(body)) > 0 && queues,
    });
    const counted = form('jfb-1');
    counted.dataset['jfbInsights'] = EVENTS;
    const nowhere = form('jfb-2');
    nowhere.dataset['jfbInsights'] = '';
    document.body.append(counted, nowhere);
    createFrame(chunk().load).init();

    counted.dispatchEvent(new CustomEvent('jfb:page', { detail: { page: 'p2' } }));
    queues = true;
    counted.dispatchEvent(new CustomEvent('jfb:page', { detail: { page: 'p2' } }));
    counted.dispatchEvent(new CustomEvent('jfb:page', { detail: { page: 'p2' } }));

    expect(tried, 'the form with an empty address sends nothing').toEqual([
      { type: 'view' },
      { type: 'page', page: 'p2' },
      { type: 'page', page: 'p2' },
    ]);
  });

  it('counts a start only at a real touch, never at a focus the page or the preview moves', () => {
    const sent = beacons();
    const counted = form('jfb-1');
    counted.dataset['jfbInsights'] = EVENTS;
    document.body.append(counted);
    createFrame(chunk().load).init();

    inputOf(counted).dispatchEvent(new Event('input', { bubbles: true }));
    inputOf(counted).focus();

    expect(
      sent.map(([, body]) => body),
      'an event a script dispatches is not trusted, and a focus a script moves is no touch',
    ).toEqual([{ type: 'view' }]);
  });

  it('works a form whose address for counting the browser refuses, and reports what it threw', () => {
    const refused = new TypeError('Refused to send beacon to invalid URL');
    vi.stubGlobal('navigator', {
      ...navigator,
      sendBeacon: (url: string) => {
        if (url === 'http://[refused') throw refused;
        return true;
      },
    });
    const reported: unknown[] = [];
    vi.stubGlobal('reportError', (error: unknown) => reported.push(error));
    const uncounted = form('jfb-1');
    uncounted.dataset['jfbInsights'] = 'http://[refused';
    document.body.append(uncounted, form('jfb-2'));
    const heard = readies();
    const frame = createFrame(chunk().load);

    frame.init();

    expect(heard.map((detail) => detail.alias)).toEqual(['jfb-1', 'jfb-2']);
    expect(reported).toEqual([refused]);
    expect(frame.get(uncounted)).not.toBeNull();
  });

  it('leaves a page whose browser sends no beacons working, every form on it enhanced', () => {
    vi.stubGlobal('navigator', { ...navigator, sendBeacon: undefined });
    const counted = form('jfb-1');
    counted.dataset['jfbInsights'] = EVENTS;
    const next = form('jfb-2');
    document.body.append(counted, next);
    const heard = readies();

    expect(() => createFrame(chunk().load).init()).not.toThrow();
    expect(heard.map((detail) => detail.alias)).toEqual(['jfb-1', 'jfb-2']);
  });
});
