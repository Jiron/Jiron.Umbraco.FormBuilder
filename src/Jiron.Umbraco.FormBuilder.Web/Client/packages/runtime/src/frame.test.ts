// The frame (ROADMAP 6.4b, ADR-0110): the forms of the markup contract found and enhanced once, later ones through
// init(root), others left to the post without JavaScript, `jfb:ready` once per form, and the chunk with the logic
// loaded on a form's first interaction only, what the visitor answered and left until it woke handed to it, or at once for a form with a default the page fills in (6.4h, ADR-0118),
// the print button of the copy of the answers below an outcome revealed and bound once (6.5b, ADR-0123), every form
// root fitted to its page once, a form to work with through `get` (6.4l, section 4.9), and what visitors do with a form
// counted where its markup names where to (ROADMAP 11.5, ADR-0211), and a press before the logic woke held for it (27.1).
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createFrame, followResumeLinks, HOLD_MS, type Chunk, type ReadyDetail } from './frame';
import type { FormModel } from './model';
import { stubMembers } from './testing-cover';

const model = (alias: string, settings?: Record<string, unknown>): string =>
  JSON.stringify({
    alias,
    revision: 1,
    culture: 'en-US',
    timeZone: 'UTC',
    open: true,
    form: {
      key: 'f',
      ...(settings === undefined ? {} : { settings }),
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

  it('loads the logic at once for a form that saves for later, when a link or the tab names a saved form (ADR-0163)', async () => {
    try {
      history.replaceState(null, '', '#jfb-resume=t');
      const linked = chunk();
      document.body.append(form('jfb-1', { data: model('jfb-1', { resume: true }) }), form('jfb-2'));
      createFrame(linked.load).init();
      await settled();
      expect(linked.attached.map(([element]) => element.id)).toEqual(['jfb-1']);

      history.replaceState(null, '', location.pathname);
      document.body.replaceChildren();
      sessionStorage.setItem('jfb-resume:key-jfb-3', 't');
      const kept = chunk();
      document.body.append(
        form('jfb-3', { data: model('jfb-3', { resume: true }) }),
        form('jfb-4', { data: model('jfb-4', { resume: true }) }),
      );
      createFrame(kept.load).init();
      await settled();
      expect(kept.attached.map(([element]) => element.id)).toEqual(['jfb-3']);
    } finally {
      history.replaceState(null, '', location.pathname);
      sessionStorage.clear();
    }
  });

  it('loads the page again when only the fragment changed to name a saved form, so its form takes the link up', () => {
    const heard = new Map<string, () => void>();
    const target = {
      location: { hash: '#top', reload: vi.fn() },
      addEventListener: (type: string, listener: () => void) => heard.set(type, listener),
    };
    followResumeLinks(target);
    heard.get('hashchange')?.();
    expect(target.location.reload).not.toHaveBeenCalled();
    target.location.hash = '#jfb-resume=t';
    heard.get('hashchange')?.();
    expect(target.location.reload).toHaveBeenCalledTimes(1);
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
    stubMembers(first, {
      querySelector: () => {
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

describe('what the visitor does before the logic woke', () => {
  it('hands the logic each answer and leaving of the visitor until it woke, in order, and none a script dispatched', async () => {
    const host = form('jfb-1');
    const box = document.createElement('input');
    box.type = 'checkbox';
    box.name = 'agree';
    host.append(box);
    document.body.append(host);
    const text = inputOf(host);
    let arrive: (loaded: Chunk) => void = () => undefined;
    let woken: (session: unknown) => void = () => undefined;
    const handed: [HTMLFormElement, string, readonly Event[]][] = [];
    createFrame(() => new Promise<Chunk>((resolve) => (arrive = resolve))).init();

    text.focus();
    box.click();
    // As a restored draft or a default's filler tells the page: no answer of the visitor's.
    box.dispatchEvent(new Event('change', { bubbles: true }));
    box.focus();
    arrive({
      attach: (element, data, heard) => {
        handed.push([element, data.alias, heard]);
        return new Promise((resolve) => (woken = resolve));
      },
    });
    await settled();
    box.blur();
    woken({});
    await settled();
    box.focus();
    text.focus();

    const named = (target: EventTarget | null): string => (target === text ? 'text' : target === box ? 'box' : '?');
    expect(handed.map(([element, alias]) => [element, alias])).toEqual([[host, 'jfb-1']]);
    expect(
      handed[0]?.[2].map((event) => `${event.type} ${named(event.target)}`),
      "the browser's events from the first touch until the logic woke, the leaving while it woke among them",
    ).toEqual(['input box', 'change box', 'focusout text', 'focusout box']);
  });
});

describe('a press before the logic woke (27.1)', () => {
  /**
   * A form with a Send button whose requestSubmit does what a browser's does: it fires a submit event pressed by the
   * button, and the form posts when no listener prevents it. Each press and each post is written down.
   */
  function sending() {
    const host = form('jfb-1');
    host.setAttribute('action', '/contact');
    host.insertAdjacentHTML('beforeend', '<button type="submit" name="jfb-nav" value="submit">Send</button>');
    document.body.append(host);
    const button = host.querySelector('button');
    if (button === null) throw new Error('no button');
    const happened: string[] = [];
    const press = (submitter: HTMLElement | null = button): SubmitEvent => {
      const event = new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter });
      host.dispatchEvent(event);
      if (!event.defaultPrevented)
        happened.push(`posted ${submitter instanceof HTMLButtonElement ? submitter.value : ''}`);
      return event;
    };
    stubMembers(host, {
      requestSubmit: (submitter?: HTMLElement | null) => {
        happened.push(`handed ${submitter instanceof HTMLButtonElement ? submitter.value : 'none'}`);
        press(submitter ?? null);
      },
    });
    // The logic as the chunk attaches it: it answers every press itself, with the button pressed.
    const logic: Chunk = {
      attach: (element) => {
        element.addEventListener('submit', (event) => {
          event.preventDefault();
          happened.push(`logic ${(event.submitter as HTMLButtonElement | null)?.value ?? 'none'}`);
        });
        return {};
      },
    };
    return { host, button, happened, press, logic };
  }

  afterEach(() => {
    vi.useRealTimers();
  });

  it('is held while the logic loads, and the logic takes that same press with the button pressed, once', async () => {
    const { button, happened, press, logic } = sending();
    let arrive: (loaded: Chunk) => void = () => undefined;
    const load = vi.fn(() => new Promise<Chunk>((resolve) => (arrive = resolve)));
    createFrame(load).init();

    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    const first = press();
    const second = press();
    expect([first.defaultPrevented, second.defaultPrevented, button.hasAttribute('data-jfb-busy')]).toEqual([
      true,
      true,
      true,
    ]);
    expect(happened, 'nothing posted while the logic loads').toEqual([]);

    arrive(logic);
    await settled();
    await settled();
    await settled();
    expect(happened, 'the one press, handed to the logic once; the second press was the same press').toEqual([
      'handed submit',
      'logic submit',
    ]);
    expect([load.mock.calls.length, button.hasAttribute('data-jfb-busy')]).toEqual([1, false]);

    press();
    expect(happened.slice(2), 'the logic answers a later press itself').toEqual(['logic submit']);
  });

  it('starts the logic loading when a press comes before any touch', async () => {
    const { happened, press, logic } = sending();
    const load = vi.fn(() => Promise.resolve(logic));
    createFrame(load).init();
    expect(press().defaultPrevented).toBe(true);
    await settled();
    await settled();
    await settled();
    expect([load.mock.calls.length, happened]).toEqual([1, ['handed submit', 'logic submit']]);
  });

  it('posts as without JavaScript, once, when the logic cannot load', async () => {
    const { button, happened, press } = sending();
    createFrame(() => Promise.reject(new Error('offline'))).init();
    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    expect(press().defaultPrevented).toBe(true);
    press();
    await settled();
    await settled();
    await settled();
    expect(happened).toEqual(['handed submit', 'posted submit']);
    expect(press().defaultPrevented, 'a later press posts as before').toBe(false);
  });

  it('posts as without JavaScript, once, when the logic does not come in time, and the logic that comes later sends nothing', async () => {
    vi.useFakeTimers();
    const { button, happened, press, logic } = sending();
    let arrive: (loaded: Chunk) => void = () => undefined;
    createFrame(() => new Promise<Chunk>((resolve) => (arrive = resolve))).init();
    button.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    press();
    await vi.advanceTimersByTimeAsync(HOLD_MS - 1);
    expect(happened, 'still held').toEqual([]);
    await vi.advanceTimersByTimeAsync(1);
    // The press goes on in a task of its own.
    await vi.runOnlyPendingTimersAsync();
    expect(happened).toEqual(['handed submit', 'posted submit']);

    arrive(logic);
    await vi.advanceTimersByTimeAsync(HOLD_MS);
    expect(happened).toEqual(['handed submit', 'posted submit']);
  });

  it('goes on waiting where the form has no address to post to, and the logic takes the press whenever it comes', async () => {
    vi.useFakeTimers();
    const { host, button, happened, press, logic } = sending();
    host.removeAttribute('action');
    let arrive: (loaded: Chunk) => void = () => undefined;
    createFrame(() => new Promise<Chunk>((resolve) => (arrive = resolve))).init();
    press();
    await vi.advanceTimersByTimeAsync(HOLD_MS * 3);
    expect([happened, button.hasAttribute('data-jfb-busy')], 'still held: there is no post to fall back to').toEqual([
      [],
      true,
    ]);

    arrive(logic);
    await vi.advanceTimersByTimeAsync(1);
    await vi.runOnlyPendingTimersAsync();
    expect([happened, button.hasAttribute('data-jfb-busy')]).toEqual([['handed submit', 'logic submit'], false]);
  });
});

describe('a form that cannot post, as the element writes it', () => {
  it('says so at a press when its logic could not load, tells the site, and tries again', async () => {
    const attached: HTMLFormElement[] = [];
    const load = vi
      .fn<() => Promise<Chunk>>()
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValue({
        attach: (woken) => {
          attached.push(woken);
          return {};
        },
      });
    const lame = form('jfb-1');
    lame.insertAdjacentHTML(
      'beforeend',
      '<template data-jfb-templates><div data-jfb-notice="server_error" role="alert" tabindex="-1"><p>Something went wrong.</p></div></template>' +
        '<div class="jfb-navigation"><button type="submit">Send</button></div>',
    );
    document.body.append(lame);
    const errors: unknown[] = [];
    lame.addEventListener('jfb:error', (event) => errors.push((event as CustomEvent).detail));
    createFrame(load).init();
    inputOf(lame).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await settled();
    await settled();

    const press = new Event('submit', { bubbles: true, cancelable: true });
    lame.dispatchEvent(press);
    const notice = lame.querySelector<HTMLElement>(':scope > [data-jfb-notice="server_error"]');
    expect([press.defaultPrevented, notice?.nextElementSibling?.className, document.activeElement]).toEqual([
      true,
      'jfb-navigation',
      notice,
    ]);
    expect(errors).toEqual([{ key: 'key-jfb-1', alias: 'jfb-1', code: 'server_error' }]);
    await settled();
    await settled();
    expect([load.mock.calls.length, attached]).toEqual([2, [lame]]);

    // With its logic there, a press is the logic's to answer: nothing of the frame's stays in its way.
    const again = new Event('submit', { bubbles: true, cancelable: true });
    lame.dispatchEvent(again);
    expect([again.defaultPrevented, errors.length]).toEqual([false, 1]);
  });

  it('leaves a form that can post to its post, as before', async () => {
    const posting = form('jfb-1');
    posting.setAttribute('action', '/contact');
    document.body.append(posting);
    createFrame(() => Promise.reject(new Error('offline'))).init();
    inputOf(posting).dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await settled();
    await settled();
    const press = new Event('submit', { bubbles: true, cancelable: true });
    posting.dispatchEvent(press);
    expect(press.defaultPrevented).toBe(false);
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

  it('sends a field that stopped the visitor whatever its alias, the names every object has among them', () => {
    const sent = beacons();
    const counted = form('jfb-1');
    counted.dataset['jfbInsights'] = EVENTS;
    document.body.append(counted);
    createFrame(chunk().load).init();
    const names = ['constructor', 'toString', 'valueOf', 'hasOwnProperty', 'isPrototypeOf'];

    counted.dispatchEvent(
      new CustomEvent('jfb:invalid', {
        detail: {
          key: 'key-jfb-1',
          alias: 'jfb-1',
          scope: 'send',
          errors: names.map((field) => ({ field, kind: 'required' })),
        },
      }),
    );

    expect(sent.map(([, body]) => body)).toEqual([
      { type: 'view' },
      ...names.map((field) => ({ type: 'invalid', field, kind: 'required' })),
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
