// The element itself (ROADMAP 6.6e, ADR-0128): its attributes and the endpoint's address, the instance ids, the
// contract check and the failures it says, the stylesheets and the runtime added once per document and awaited, the
// events, renders again and their order, a field of the site's own type, and the post it never makes as a page would.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { JfbElementAnswer } from './answer';
import { ANSWER_CONTRACT, answerUrl, defineElement, JfbFormElement, nextInstance, STYLES_WAIT } from './jfb-element';
import { LANGUAGES } from './testing';

/** A value a test needs, or the test fails saying which. */
function must<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) throw new Error(`the test needs ${what}`);
  return value;
}

const paged = must(
  LANGUAGES.flatMap(([, cases]) => cases).find((each) => each.name === 'paged'),
  "the fixture's paged form",
);

/** The instance id a request asks for. */
const instanceOf = (url: URL | undefined): string => must(url?.searchParams.get('instance'), 'an instance id');

// What the fake runtime saw: each root it was handed. Its module runs once, as a module does, and sets the global.
interface Seen {
  __jfbRuntimeRuns?: number;
  __jfbInits?: ParentNode[];
}
const seen = globalThis as unknown as Seen;
const RUNTIME = `data:text/javascript,${encodeURIComponent(
  'globalThis.__jfbRuntimeRuns = (globalThis.__jfbRuntimeRuns ?? 0) + 1;' +
    'globalThis.JironFormBuilder ??= { version: "test", init(root) { globalThis.__jfbInits.push(root); } };',
)}`;

let run = 0;

/** Stylesheet addresses of this test alone, since each document keeps the ones it has. */
function styles(): string[] {
  return [`/jfb/assets/themes/base.css?run=${run}`, `/jfb/assets/themes/default.css?run=${run}`];
}

/** The fixture's answer for an instance, with this test's assets and the changes a test makes. */
function answerFor(instance: string, change: (answer: JfbElementAnswer) => void = () => undefined): JfbElementAnswer {
  const answer = JSON.parse(JSON.stringify(paged.answer).replace(/jfb-1(?=[-"\s])/g, instance)) as JfbElementAnswer;
  answer.assets = { styles: styles(), script: RUNTIME };
  change(answer);
  return answer;
}

const json = (body: unknown, status = 200): Response =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

/** The element endpoint as a fake: each request's address, and an answer for its instance unless a test gives one. */
function serve(answer: (url: URL) => Response | Promise<Response> = (url) => json(answerFor(instanceOf(url)))) {
  const requests: URL[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      const url = new URL(input);
      requests.push(url);
      return answer(url);
    }),
  );
  return requests;
}

/** Fires `load` on each stylesheet added to the head, as a browser does once it has it. */
function loadStylesheets(): MutationObserver {
  const observer = new MutationObserver((records) => {
    for (const node of records.flatMap((record) => [...record.addedNodes])) {
      if (node instanceof HTMLLinkElement) queueMicrotask(() => node.dispatchEvent(new Event('load')));
    }
  });
  observer.observe(document.head, { childList: true });
  return observer;
}

/** A `<jfb-form>` for the fixture's form, added to the body. */
function element(attributes: Record<string, string> = {}): JfbFormElement {
  const form = document.createElement('jfb-form') as JfbFormElement;
  for (const [name, value] of Object.entries({
    api: 'https://cms.example/jfb/api/v1',
    form: paged.answer.form.key,
    ...attributes,
  }))
    form.setAttribute(name, value);
  document.body.append(form);
  return form;
}

/** The next event of a type on the document. */
const next = (type: string): Promise<CustomEvent> =>
  new Promise((resolve) => document.addEventListener(type, (event) => resolve(event as CustomEvent), { once: true }));

/** Every event of a type on the document from now on. */
function collect(type: string): CustomEvent[] {
  const events: CustomEvent[] = [];
  document.addEventListener(type, (event) => events.push(event as CustomEvent));
  return events;
}

const linksTo = (href: string): number =>
  [...document.head.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')].filter(
    (link) => link.href === new URL(href, 'https://cms.example').href,
  ).length;

let observer: MutationObserver;

beforeEach(() => {
  run++;
  seen.__jfbInits = [];
  observer = loadStylesheets();
});

afterEach(() => {
  observer.disconnect();
  document.body.replaceChildren();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  vi.useRealTimers();
});

describe('<jfb-form>', () => {
  it('is registered by the script', () => {
    expect(customElements.get('jfb-form')).toBe(JfbFormElement);
    expect(document.createElement('jfb-form')).toBeInstanceOf(JfbFormElement);
  });

  it('is registered once, however often the script runs', () => {
    expect(() => defineElement()).not.toThrow();
    expect(customElements.get('jfb-form')).toBe(JfbFormElement);
  });

  it("asks the element endpoint for its attributes' form, culture, content, theme and context", () => {
    const form = document.createElement('jfb-form');
    for (const [name, value] of Object.entries({
      api: 'https://cms.example/jfb/api/v1//',
      form: ' 30000000-0000-4000-8000-0000006f0012 ',
      culture: 'de-DE',
      content: '9b7e1c52-4c33-4bde-8f0e-5d2a8c1f6a10',
      theme: 'bootstrap5',
      'context-campaign': 'spring & summer',
      'context-': 'no name',
      lang: 'en',
    })) {
      form.setAttribute(name, value);
    }

    expect(answerUrl(form, 'jfb-4')).toBe(
      'https://cms.example/jfb/api/v1/forms/30000000-0000-4000-8000-0000006f0012/element?instance=jfb-4&culture=de-DE&contentKey=9b7e1c52-4c33-4bde-8f0e-5d2a8c1f6a10&theme=bootstrap5&context-campaign=spring+%26+summer',
    );
    form.setAttribute('culture', '');
    form.setAttribute('form', 'a/b');
    expect(answerUrl(form, 'jfb-1')).toMatch(/\/forms\/a%2Fb\/element\?instance=jfb-1&contentKey=/);
  });

  it('asks nothing without its api and its form, and says why', async () => {
    const requests = serve();
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failed = next('jfb:error');

    element({ form: '  ' });

    expect((await failed).detail).toEqual({ code: 'invalid_request' });
    expect(requests).toEqual([]);
    expect(error).toHaveBeenCalledWith('jfb-form: the attributes api and form are both needed.');
  });

  it('takes the next instance id no form of the document has', () => {
    const page = document.implementation.createHTMLDocument();
    page.body.innerHTML = '<form id="jfb-1"></form><div id="jfb-3"></div><div id="jfb-02"></div><div id="jfb-x"></div>';

    expect([nextInstance(page), nextInstance(page), nextInstance(page)]).toEqual(['jfb-2', 'jfb-4', 'jfb-5']);
    page.body.innerHTML = '';
    expect(nextInstance(page)).toBe('jfb-6');
  });

  it('writes the form, adds the stylesheets and the runtime once per document, and says it rendered', async () => {
    const requests = serve();
    const rendered = collect('jfb:rendered');
    document.body.innerHTML = '<form id="jfb-1"></form>';

    const first = element();
    const second = element({ 'context-campaign': 'spring' });
    await vi.waitFor(() => expect(rendered).toHaveLength(2));

    const instances = requests.map(instanceOf);
    expect(instances).toHaveLength(2);
    expect(new Set([...instances, 'jfb-1']).size, 'each its own instance, none the page has').toBe(3);
    expect(instances.every((instance) => /^jfb-[1-9][0-9]*$/.test(instance))).toBe(true);
    expect(requests[1]?.searchParams.get('context-campaign')).toBe('spring');
    expect([first.querySelector('form')?.id, second.querySelector('form')?.id]).toEqual(instances);
    expect(first.querySelector('form')?.dataset['jfbApi']).toBe('https://cms.example/jfb/api/v1');
    expect(first.querySelector('form')?.getAttribute('action')).toBeNull();
    expect(styles().map(linksTo)).toEqual([1, 1]);
    expect(seen.__jfbRuntimeRuns).toBe(1);
    expect(seen.__jfbInits).toEqual([first, second]);
    expect(rendered.map((event) => [event.target, event.detail, event.bubbles])).toEqual([
      [first, { form: paged.answer.form.key, instance: instances[0] }, true],
      [second, { form: paged.answer.form.key, instance: instances[1] }, true],
    ]);
  });

  it('shows the form once its stylesheets loaded, so it never shows unstyled', async () => {
    observer.disconnect();
    serve();
    const form = element();

    await vi.waitFor(() => expect(styles().map(linksTo)).toEqual([1, 1]));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(form.childElementCount).toBe(0);
    const shown = next('jfb:rendered');
    for (const link of document.head.querySelectorAll('link'))
      link.dispatchEvent(new Event(link.href.includes('base') ? 'load' : 'error'));
    await shown;
    expect(form.querySelector('form')).not.toBeNull();
  });

  it('shows the form without its stylesheets once it waited for them a while', async () => {
    observer.disconnect();
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    serve();
    const form = element();

    // A wait's check moves the fake clock by its interval (50 ms) too.
    await vi.waitFor(() => expect(styles().map(linksTo)).toEqual([1, 1]));
    await vi.advanceTimersByTimeAsync(STYLES_WAIT - 100);
    expect(form.childElementCount).toBe(0);
    const shown = next('jfb:rendered');
    await vi.advanceTimersByTimeAsync(100);
    await shown;
    expect(form.querySelector('form')).not.toBeNull();
  });

  it('leaves itself empty for an answer of another contract, says why and tells the page', async () => {
    serve((url) =>
      json(answerFor(instanceOf(url), (answer) => (answer.contract = (ANSWER_CONTRACT + 1) as typeof answer.contract))),
    );
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failed = next('jfb:error');
    const form = document.createElement('jfb-form');
    form.innerHTML = '<p>The form loads here.</p>';
    form.setAttribute('api', 'https://cms.example/jfb/api/v1');
    form.setAttribute('form', paged.answer.form.key);
    document.body.append(form);

    expect((await failed).detail).toEqual({ code: 'contract_mismatch' });
    expect(form.childNodes).toHaveLength(0);
    expect(error).toHaveBeenCalledOnce();
    expect(error.mock.calls[0]?.[0]).toContain(
      `the answer's contract is ${ANSWER_CONTRACT + 1}, this element reads ${ANSWER_CONTRACT}`,
    );
    expect(styles().map(linksTo)).toEqual([0, 0]);
    expect(seen.__jfbInits).toEqual([]);
  });

  it("says the endpoint's refusal with its problem's code", async () => {
    serve(() => json({ status: 404, code: 'not_found' }, 404));
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failed = next('jfb:error');

    element();

    expect((await failed).detail).toEqual({ code: 'not_found' });
    expect(error).toHaveBeenCalledWith('jfb-form: the form could not be loaded (404).');
  });

  it('says a refusal without a problem as a server error', async () => {
    serve(() => new Response('<h1>Bad gateway</h1>', { status: 502 }));
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failed = next('jfb:error');

    element();

    expect((await failed).detail).toEqual({ code: 'server_error' });
  });

  it("says when the API cannot be reached or does not allow the page's origin", async () => {
    serve(() => Promise.reject(new TypeError('Failed to fetch')));
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const failed = next('jfb:error');

    element();

    expect((await failed).detail).toEqual({ code: 'network_error' });
    expect(error).toHaveBeenCalledWith(
      "jfb-form: the form could not be loaded from https://cms.example/jfb/api/v1: the API is not reachable, or it does not allow this page's origin.",
    );
  });

  it('gives the form as it is now, under the same id and not yet on the page, for the runtime to put in place', async () => {
    const requests = serve();
    const rendered = collect('jfb:rendered');
    const made = element() as JfbFormElement;
    await vi.waitFor(() => expect(rendered).toHaveLength(1));
    const shown = made.querySelector('form');

    const fresh = await made.fresh();

    expect(fresh?.localName).toBe('form');
    expect(fresh?.id).toBe(shown?.id);
    expect(fresh?.isConnected).toBe(false);
    expect(made.querySelector('form'), 'the one on the page stays until the runtime has put the answers over').toBe(
      shown,
    );
    expect(requests).toHaveLength(2);
    expect(rendered, 'nothing rendered yet').toHaveLength(1);
  });

  it('gives none when it cannot be had', async () => {
    let answering = true;
    serve((url) => (answering ? json(answerFor(instanceOf(url))) : json({ code: 'form_not_found' }, 404)));
    const rendered = collect('jfb:rendered');
    const made = element() as JfbFormElement;
    await vi.waitFor(() => expect(rendered).toHaveLength(1));
    answering = false;

    expect(await made.fresh()).toBeNull();
    expect(made.querySelector('form'), 'what shows stays').not.toBeNull();
  });

  it('gives none of another contract', async () => {
    let contract = ANSWER_CONTRACT;
    serve((url) =>
      json(
        answerFor(instanceOf(url), (answer) => {
          (answer as { contract: number }).contract = contract;
        }),
      ),
    );
    const rendered = collect('jfb:rendered');
    const made = element() as JfbFormElement;
    await vi.waitFor(() => expect(rendered).toHaveLength(1));
    contract = ANSWER_CONTRACT + 1;

    expect(await made.fresh()).toBeNull();
  });

  it('gives none of a form that takes no entry now', async () => {
    const closed = must(
      LANGUAGES.flatMap(([, cases]) => cases).find((each) => each.name === 'closed'),
      "the fixture's closed form",
    );
    let open = true;
    serve((url) =>
      json(
        answerFor(instanceOf(url), (answer) => {
          if (!open) answer.form = closed.answer.form;
        }),
      ),
    );
    const rendered = collect('jfb:rendered');
    const made = element() as JfbFormElement;
    await vi.waitFor(() => expect(rendered).toHaveLength(1));
    open = false;

    expect(await made.fresh()).toBeNull();
  });

  it('gives none and asks for nothing before it was ever on a page', async () => {
    const requests = serve();
    const made = document.createElement('jfb-form') as JfbFormElement;
    made.setAttribute('api', 'https://cms.example/jfb/api/v1');
    made.setAttribute('form', paged.answer.form.key);

    expect(await made.fresh()).toBeNull();
    expect(requests).toEqual([]);
  });

  it('renders again when an attribute changes, with its own instance id, and drops the answers that came too late', async () => {
    const answers: ((response: Response) => void)[] = [];
    const requests = serve(() => new Promise<Response>((resolve) => answers.push(resolve)));
    const rendered = collect('jfb:rendered');
    const failed = collect('jfb:error');
    const form = element();
    await vi.waitFor(() => expect(answers).toHaveLength(1));

    form.setAttribute('culture', 'de-DE');
    form.setAttribute('theme', 'bootstrap5');
    await vi.waitFor(() => expect(answers).toHaveLength(3));
    const instance = instanceOf(requests[0]);
    must(answers[2], 'a third answer')(json(answerFor(instance, (answer) => (answer.form.culture = 'de-DE'))));
    await vi.waitFor(() => expect(rendered).toHaveLength(1));
    must(answers[0], 'a first answer')(json({ status: 500, code: 'server_error' }, 500));
    must(answers[1], 'a second answer')(json(answerFor(instance)));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(
      requests.map((url) => [
        url.searchParams.get('instance'),
        url.searchParams.get('culture'),
        url.searchParams.get('theme'),
      ]),
    ).toEqual([
      [instance, null, null],
      [instance, 'de-DE', null],
      [instance, 'de-DE', 'bootstrap5'],
    ]);
    expect(form.querySelector('form')?.getAttribute('lang')).toBe('de-DE');
    expect(form.querySelector('form')?.id).toBe(instance);
    expect([rendered.length, failed.length]).toEqual([1, 0]);
  });

  it('drops a render still waiting for its stylesheets once a newer one started', async () => {
    observer.disconnect();
    const requests = serve();
    const rendered = collect('jfb:rendered');
    const form = element();
    await vi.waitFor(() => expect(styles().map(linksTo)).toEqual([1, 1]));

    form.setAttribute('culture', 'de-DE');
    await vi.waitFor(() => expect(requests).toHaveLength(2));
    await new Promise((resolve) => setTimeout(resolve, 20));
    for (const link of document.head.querySelectorAll('link')) link.dispatchEvent(new Event('load'));
    await vi.waitFor(() => expect(rendered).toHaveLength(1));
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(seen.__jfbInits, 'the older form never written and enhanced').toEqual([form]);
    expect(rendered).toHaveLength(1);
  });

  it('keeps its form when it moves to another place of the page', async () => {
    const requests = serve();
    const form = element();
    await next('jfb:rendered');
    const written = form.querySelector('form');

    const aside = document.createElement('aside');
    document.body.append(aside);
    aside.append(form);
    await new Promise((resolve) => setTimeout(resolve, 20));

    expect(requests).toHaveLength(1);
    expect(form.querySelector('form')).toBe(written);
  });

  it("frames a field of the site's own type and says once per document that the site gives its control", async () => {
    serve((url) =>
      json(
        answerFor(instanceOf(url), (answer) => {
          for (const field of must(answer.form.pages[0]?.groups[0], 'a first group').fields.slice(0, 2)) {
            field.type = 'site-stars';
            delete (field as { control?: unknown }).control;
          }
        }),
      ),
    );
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const rendered = collect('jfb:rendered');

    const first = element();
    element();
    await vi.waitFor(() => expect(rendered).toHaveLength(2));

    const frames = first.querySelectorAll('[data-jfb-type="site-stars"]');
    expect(frames).toHaveLength(2);
    expect(
      [...frames].map((frame) => [
        frame.querySelector('label') !== null,
        frame.querySelector('input, select, textarea'),
      ]),
    ).toEqual([
      [true, null],
      [true, null],
    ]);
    expect(warn.mock.calls.filter(([message]) => String(message).includes("'site-stars'"))).toEqual([
      ["jfb-form: a field of type 'site-stars' has no control in the element, so it shows no answer (ADR-0128)."],
    ]);
  });

  it('shows the form without the runtime when it cannot be loaded, and says so', async () => {
    serve((url) =>
      json(
        answerFor(
          instanceOf(url),
          (answer) => (answer.assets.script = 'data:text/javascript,throw new Error("no runtime")'),
        ),
      ),
    );
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    const form = element();
    await next('jfb:rendered');

    expect(form.querySelector('form')).not.toBeNull();
    expect(warn).toHaveBeenCalledWith('jfb-form: the website runtime could not be loaded; the form shows without it.');
  });

  it("never posts its form as a page would, to the page's own address", async () => {
    serve();
    const form = element();
    await next('jfb:rendered');

    const submit = new Event('submit', { bubbles: true, cancelable: true });
    must(form.querySelector('form'), 'the form').dispatchEvent(submit);

    expect(submit.defaultPrevented).toBe(true);
  });
});
