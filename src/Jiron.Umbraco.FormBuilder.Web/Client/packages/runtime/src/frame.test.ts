// The frame (ROADMAP 6.4b, ADR-0110): the forms of the markup contract found and enhanced once, later ones through
// init(root), others left to the post without JavaScript, `jfb:ready` once per form, and the chunk with the logic
// loaded on a form's first interaction only, or at once for a form with a default the page fills in (6.4h, ADR-0118).
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

  it.each(['focusin', 'pointerdown', 'input', 'change'])('loads the logic on %s', async (type) => {
    const loaded = chunk();
    document.body.append(form('jfb-1'));
    createFrame(loaded.load).init();
    inputOf(document).dispatchEvent(new Event(type, { bubbles: true }));
    await settled();
    expect(loaded.attached).toHaveLength(1);
  });

  // Vitest fails the run on an unhandled rejection, so a failed load that leaked one would fail here.
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
