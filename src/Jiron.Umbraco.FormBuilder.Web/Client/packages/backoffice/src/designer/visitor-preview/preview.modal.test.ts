// The preview (ROADMAP 10.3e, IA 5.7, ADR-0203): the saved draft's document in a frame sandboxed without the
// backoffice's origin, at the designer's widths, starting at the canvas's, which a choice here leaves as it is, with
// "Narrowed to fit" where the room is less; the "..." with the colour scheme, for a theme that offers the dark one,
// which the frame is told over its port, and the site's languages, which render again, an answer that comes late
// dropped; a draft whose rules have errors said, and a failed read tried again; and the frame's ask over its port for a
// new ticket answered for the version shown.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { PORT_MESSAGE, SCHEME_MESSAGE, TICKET_MESSAGE } from '@jfb/runtime/preview-protocol';
import { all, deferred, must, registerEnglish, settle, shadow, text } from '../../testing';
import { readWidth } from '../canvas/widths';
import { FRAME_SANDBOX, PreviewModalElement } from './preview.modal.element';
import type { PreviewModalData } from './preview.modal.token';
import type { PreviewRead, PreviewTicket } from './preview-read';

const FORM = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const LANGUAGES = [
  { culture: 'en-US', name: 'English', isDefault: true },
  { culture: 'de-DE', name: 'German', isDefault: false },
];
const ports: MessagePort[] = [];

const shownIn = (culture: string, darkScheme = true, version = 7, lifetimeSeconds = 600): PreviewRead => ({
  kind: 'shown',
  document: `<!doctype html><html lang="${culture}"><body><p>${culture}</p></body></html>`,
  version,
  culture,
  darkScheme,
  lifetimeSeconds,
});

/** The frame's end of the port the preview hands its window once loaded, with what came over it. */
function portOfFrame(frame: HTMLIFrameElement) {
  const handed: { message: unknown; port: MessagePort }[] = [];
  vi.spyOn(must(frame.contentWindow, 'the frame window'), 'postMessage').mockImplementation(((
    message: unknown,
    _origin: string,
    transfer?: Transferable[],
  ) => {
    for (const port of (transfer ?? []) as MessagePort[]) {
      ports.push(port);
      handed.push({ message, port });
    }
  }) as never);
  const heard: unknown[] = [];
  return {
    handed,
    heard,
    get port() {
      const port = must(handed[0], 'the port handed').port;
      port.onmessage ??= (event) => heard.push(event.data);
      return port;
    },
  };
}

/** Lets the port's messages go across. */
const across = () => new Promise((done) => setTimeout(done, 20));

/** A preview whose first document comes once the test listens at its frame's window, which then loads it. */
async function previewListening(read: (culture: string | null) => PreviewRead) {
  const first = deferred<PreviewRead>();
  let asks = 0;
  const preview = await previewOf({}, (culture) => (++asks === 1 ? first.promise : Promise.resolve(read(culture))));
  first.resolve(read(null));
  await first.promise;
  await preview.element.updateComplete;
  const frame = frameOf(preview.root);
  const port = portOfFrame(frame);
  await loaded(frame);
  return { ...preview, frame, port };
}

async function previewOf(
  data: Partial<PreviewModalData> = {},
  read: PreviewModalData['read'] = () => Promise.resolve(shownIn('en-US')),
) {
  const element = new PreviewModalElement();
  const reads = vi.fn(read);
  const tickets = vi.fn<(version: number) => Promise<PreviewTicket | undefined>>(() =>
    Promise.resolve({ ticket: 'new-ticket', lifetime: 600 }),
  );
  element.modalContext = { reject: vi.fn(), submit: vi.fn() } as never;
  element.data = {
    name: 'Contact us',
    form: FORM,
    languages: LANGUAGES,
    culture: null,
    width: 'desktop',
    read: reads,
    ticket: tickets,
    ...data,
  };
  document.body.append(element);
  await settle(element);
  await vi.waitFor(() => expect(reads).toHaveBeenCalled());
  await settle(element);
  return { element, root: shadow(element), reads, tickets };
}

const frameOf = (root: ShadowRoot) => must(root.querySelector('iframe'), 'the frame');
const buttonsOf = (element: Element, group: string) =>
  all<HTMLButtonElement>(element, `[role="group"][aria-labelledby="${group}"] button`);

/** Waits for the frame's one load, which jsdom, as a browser, fires once its document is there. */
const loaded = async (frame: HTMLIFrameElement) => {
  await vi.waitFor(() => expect(frame.contentDocument?.readyState).toBe('complete'));
  await new Promise((done) => setTimeout(done, 0));
};

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  localStorage.clear();
  for (const port of ports.splice(0)) port.close();
});

describe('the preview', () => {
  it('shows the document in a frame sandboxed without the backoffice origin, under its headline', async () => {
    const { element, root, reads } = await previewOf();
    const frame = frameOf(root);
    expect(reads).toHaveBeenCalledWith(null, false);
    expect(frame.getAttribute('sandbox')).toBe(FRAME_SANDBOX);
    expect(FRAME_SANDBOX.split(' ')).not.toContain('allow-same-origin');
    expect(frame.getAttribute('title')).toBe('The form as visitors see it');
    expect(frame.srcdoc).toContain('<p>en-US</p>');
    expect(frame.getAttribute('style')).toBe('--frame-width: 960px');
    expect(must(root.querySelector('umb-body-layout'), 'layout').getAttribute('headline')).toBe(
      'Preview of Contact us',
    );
    expect(all(element, 'uui-button[slot="actions"]').map((button) => button.getAttribute('label'))).toEqual(['Close']);
  });

  it("says it is loading until the document comes, in the frame's place", async () => {
    const answer = deferred<PreviewRead>();
    const { root } = await previewOf({}, () => answer.promise);
    expect(root.querySelector('iframe')).toBeNull();
    expect(text(root.querySelector('[role="status"]'))).toBe('Loading the preview\u2026');
    answer.resolve(shownIn('en-US'));
    await vi.waitFor(() => expect(root.querySelector('iframe')).not.toBeNull());
    expect(root.querySelector('[role="status"]')).toBeNull();
  });

  it("starts at the width given, narrowed where the room is less, and leaves the canvas's as it is", async () => {
    const { element, root } = await previewOf({ width: 'phone' });
    const widths = all<HTMLButtonElement>(element, '.widths button');
    expect(widths.map((button) => [text(button), button.getAttribute('aria-pressed')])).toEqual([
      ['Phone', 'true'],
      ['Tablet', 'false'],
      ['Desktop', 'false'],
    ]);
    expect(frameOf(root).getAttribute('style')).toBe('--frame-width: 375px');

    const stage = must(root.querySelector<HTMLElement>('.stage'), 'stage');
    Object.defineProperty(stage, 'clientWidth', { configurable: true, value: 900 });
    must(widths[1], 'Tablet').click();
    await settle(element);
    expect(frameOf(root).getAttribute('style')).toBe('--frame-width: 768px');
    expect(root.querySelector('.narrowed')).toBeNull();

    must(widths[2], 'Desktop').click();
    await settle(element);
    await settle(element);
    expect(text(root.querySelector('.narrowed'))).toBe('Narrowed to fit');
    must(widths[0], 'Phone').click();
    await settle(element);
    // The canvas's width, kept in this browser, is what the canvas was left at: Desktop, as nothing was kept.
    expect(readWidth()).toBe('desktop');
  });

  it('turns the page dark through the frame, which keeps its answers, and renders another language anew', async () => {
    const {
      element,
      root,
      reads,
      frame: first,
      port: frame,
    } = await previewListening((culture) => shownIn(culture ?? 'en-US'));
    expect(frame.handed.map((each) => each.message)).toEqual([{ jfb: PORT_MESSAGE, form: FORM }]);
    const port = frame.port;

    expect(text(root.querySelector('#jfb-preview-scheme'))).toBe('Colour scheme');
    const [light, dark] = buttonsOf(element, 'jfb-preview-scheme');
    expect([light?.getAttribute('aria-pressed'), dark?.getAttribute('aria-pressed')]).toEqual(['true', 'false']);
    must(dark, 'Dark').click();
    await settle(element);
    await vi.waitFor(() => expect(frame.heard.at(-1)).toEqual({ jfb: SCHEME_MESSAGE, dark: true }));
    expect(port).toBe(frame.port);
    expect(reads).toHaveBeenCalledTimes(1);
    expect(frameOf(root)).toBe(first);

    expect(text(root.querySelector('#jfb-preview-language'))).toBe('Language');
    const languages = buttonsOf(element, 'jfb-preview-language');
    expect(languages.map((button) => [text(button), button.getAttribute('aria-pressed')])).toEqual([
      ['English', 'true'],
      ['German', 'false'],
    ]);
    must(languages[1], 'German').click();
    await vi.waitFor(() => expect(frameOf(root).srcdoc).toContain('<p>de-DE</p>'));
    expect(reads).toHaveBeenLastCalledWith('de-DE', true);
    expect(frameOf(root)).not.toBe(first);
  });

  it('offers no "..." for a theme without the dark scheme on a site of one language', async () => {
    const { root } = await previewOf({ languages: LANGUAGES.slice(0, 1) }, () =>
      Promise.resolve(shownIn('en-US', false)),
    );
    expect(root.querySelector('.more')).toBeNull();
    expect(root.querySelector('#jfb-preview-more')).toBeNull();
  });

  it('shows the answer to the last ask, an earlier one that comes late dropped', async () => {
    const answers = new Map<string | null, ReturnType<typeof deferred<PreviewRead>>>();
    const { element, root } = await previewOf({}, (culture) => {
      const answer = deferred<PreviewRead>();
      answers.set(culture, answer);
      return answer.promise;
    });
    must(answers.get(null), 'the first ask').resolve(shownIn('en-US'));
    await vi.waitFor(() => expect(root.querySelector('iframe')).not.toBeNull());
    must(buttonsOf(element, 'jfb-preview-language')[1], 'German').click();
    await settle(element);
    must(buttonsOf(element, 'jfb-preview-language')[0], 'English').click();
    await settle(element);
    must(answers.get('en-US'), 'English again').resolve(shownIn('en-US', true, 8));
    must(answers.get('de-DE'), 'German').resolve(shownIn('de-DE'));
    await vi.waitFor(() => expect(root.querySelector('iframe')).not.toBeNull());
    await settle(element);
    expect(frameOf(root).srcdoc).toContain('<p>en-US</p>');
  });

  it("says a draft whose rules have errors isn't shown, and tries a failed read again", async () => {
    const rules = await previewOf({}, () => Promise.resolve({ kind: 'rulesInvalid' }));
    expect(text(rules.root.querySelector('.state p'))).toBe(
      "This draft's rules have errors, so it can't be shown as visitors would get it. The form check on the Design view lists them.",
    );
    expect(rules.root.querySelector('iframe')).toBeNull();
    document.body.replaceChildren();

    let tries = 0;
    const failed = await previewOf({}, () => Promise.resolve(++tries === 1 ? { kind: 'failed' } : shownIn('en-US')));
    expect(text(failed.root.querySelector('[role="alert"]'))).toBe("The preview couldn't be loaded.");
    must(failed.root.querySelector<HTMLElement>('.state uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(failed.root.querySelector('iframe')).not.toBeNull());
    expect(tries).toBe(2);
  });

  it("answers its frame's ask over its port for a new ticket for the version shown, and nothing once closed", async () => {
    // Tickets of 60 seconds are ones with little left, which the preview renews whenever asked.
    const { element, tickets, port: frame } = await previewListening(() => shownIn('en-US', true, 7, 60));
    tickets.mockImplementation(() => Promise.resolve({ ticket: 'new-ticket', lifetime: 60 }));
    const port = frame.port;
    port.postMessage({ jfb: TICKET_MESSAGE, form: FORM });
    await vi.waitFor(() =>
      expect(frame.heard).toContainEqual({ jfb: TICKET_MESSAGE, form: FORM, ticket: 'new-ticket', lifetime: 60 }),
    );
    expect(tickets).toHaveBeenCalledWith(7);

    // Only the port's asks count, not a message to the backoffice's window.
    window.dispatchEvent(new MessageEvent('message', { data: { jfb: TICKET_MESSAGE, form: FORM } }));
    await across();
    expect(tickets).toHaveBeenCalledTimes(1);

    element.remove();
    port.postMessage({ jfb: TICKET_MESSAGE, form: FORM });
    await across();
    expect(tickets).toHaveBeenCalledTimes(1);
  });
});
