// The preview (ROADMAP 10.3e and 10.3f, IA 5.7, ADR-0203, ADR-0204): the saved draft's document in a frame sandboxed
// without the backoffice's origin, at the designer's widths, starting at the canvas's, which a choice here leaves as it
// is, with "Narrowed to fit" where the room is less; the "..." with the colour scheme, for a theme that offers the dark
// one, which the frame is told over its port, and the site's languages, which render again, an answer that comes late
// dropped; a draft whose rules have errors said, and a failed read tried again; and the frame's ask over its port for a
// new ticket answered for the version shown. Fill with sample answers renders the draft with them, Start again afresh
// once sent; the note says what sending does, and once sent which test entry it made, with Run actions... where its
// actions did not run, after the list of what goes out; Run actions for test entries, for who may manage them, hands
// the frame a ticket that says so.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import {
  EXPIRED_MESSAGE,
  PORT_MESSAGE,
  SCHEME_MESSAGE,
  SENT_MESSAGE,
  TICKET_MESSAGE,
} from '@jfb/runtime/preview-protocol';
import { all, deferred, must, registerEnglish, settle, shadow, text } from '../../testing';
import { readWidth } from '../canvas/widths';
import { FRAME_SANDBOX, PreviewModalElement } from './preview.modal.element';
import type { PreviewModalData } from './preview.modal.token';
import type { PreviewAsk, PreviewRead, PreviewTicket, RunAnswer, TestEntryRead } from './preview-read';

/** Umbraco's confirm dialog: whether the user confirms, what it was asked, and what it waits for before it answers. */
const confirming = vi.hoisted(() => ({
  yes: true,
  asked: [] as { headline: string | undefined; confirmLabel: string | undefined }[],
  open: undefined as Promise<void> | undefined,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbConfirmModal: async (_host: unknown, data: { headline?: string; confirmLabel?: string }) => {
    confirming.asked.push({ headline: data.headline, confirmLabel: data.confirmLabel });
    await confirming.open;
    if (!confirming.yes) throw new Error('declined');
  },
}));

const FORM = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const NONE: PreviewAsk = { dark: false, sample: false, actions: false };
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
async function previewListening(read: (culture: string | null) => PreviewRead, data: Partial<PreviewModalData> = {}) {
  const first = deferred<PreviewRead>();
  let asks = 0;
  const preview = await previewOf(data, (culture) => (++asks === 1 ? first.promise : Promise.resolve(read(culture))));
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
  const tickets = vi.fn<(version: number, runActions: boolean) => Promise<PreviewTicket | undefined>>(() =>
    Promise.resolve({ ticket: 'new-ticket', lifetime: 600 }),
  );
  const entries = vi.fn<(submissionId: string) => Promise<TestEntryRead>>(() =>
    Promise.resolve({ kind: 'found', number: 12, actionsRan: false, awaitsConfirmation: false, actions: ['a1', 'a2'] }),
  );
  const runs = vi.fn<(submissionId: string) => Promise<RunAnswer>>(() => Promise.resolve('ran'));
  element.modalContext = { reject: vi.fn(), submit: vi.fn() } as never;
  element.data = {
    name: 'Contact us',
    form: FORM,
    languages: LANGUAGES,
    culture: null,
    width: 'desktop',
    mayRunActions: false,
    read: reads,
    ticket: tickets,
    testEntry: entries,
    runActions: runs,
    describe: (keys) => Promise.resolve(keys.map((key) => ({ name: `Action ${key}`, line: '' }))),
    ...data,
  };
  document.body.append(element);
  await settle(element);
  await vi.waitFor(() => expect(reads).toHaveBeenCalled());
  await settle(element);
  return { element, root: shadow(element), reads, tickets, entries, runs };
}

/** Submission ids as the public API makes them. */
const SUBMISSION = '7c9e6679-7425-40de-944b-e07fc1f90ae7';
const OTHER_SUBMISSION = '9b2f1c0e-3d4a-4e5b-8c6d-7e8f9a0b1c2d';

/** The frame's word that it sent a submission. */
const sentOver = (port: MessagePort, submissionId = SUBMISSION) =>
  port.postMessage({ jfb: SENT_MESSAGE, form: FORM, submissionId });

/** The note above the frame, its words as read. */
const noteOf = (root: ShadowRoot) => text(root.querySelector('.note')).replace(/\s+/g, ' ').trim();

/** The toolbar's buttons by their labels. */
const toolbarButton = (root: ShadowRoot, label: string) =>
  must(root.querySelector<HTMLElement>(`.bar uui-button[label="${label}"]`), label);

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
  confirming.yes = true;
  confirming.asked = [];
  confirming.open = undefined;
});

describe('the preview', () => {
  it('shows the document in a frame sandboxed without the backoffice origin, under its headline', async () => {
    const { element, root, reads } = await previewOf();
    const frame = frameOf(root);
    expect(reads).toHaveBeenCalledWith(null, NONE);
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
    expect(reads).toHaveBeenLastCalledWith('de-DE', { ...NONE, dark: true });
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
    expect(tickets).toHaveBeenCalledWith(7, false);

    // Only the port's asks count, not a message to the backoffice's window.
    window.dispatchEvent(new MessageEvent('message', { data: { jfb: TICKET_MESSAGE, form: FORM } }));
    await across();
    expect(tickets).toHaveBeenCalledTimes(1);

    element.remove();
    port.postMessage({ jfb: TICKET_MESSAGE, form: FORM });
    await across();
    expect(tickets).toHaveBeenCalledTimes(1);
  });

  it('says when the frame can send no more, as the public API refused its ticket, and loads it again', async () => {
    const { element, root, reads, port: frame } = await previewListening(() => shownIn('en-US'));
    frame.port.postMessage({ jfb: EXPIRED_MESSAGE, form: FORM });
    await vi.waitFor(() =>
      expect(text(root.querySelector('.note > span'))).toBe(
        "This preview can't send any more: its access has run out.",
      ),
    );
    expect(must(root.querySelector('.note'), 'note').getAttribute('role')).toBe('alert');
    const again = must(root.querySelector<HTMLElement>('.note uui-button'), 'Load again');
    expect(again.getAttribute('label')).toBe('Load again');
    const before = reads.mock.calls.length;
    again.click();
    await vi.waitFor(() => expect(reads.mock.calls.length).toBe(before + 1));
    await settle(element);
    expect(noteOf(root)).toBe('Entries sent here are test entries. They run no actions.');
  });

  it('fills the draft with the sample answers, and once sent starts again afresh', async () => {
    const filled = await previewOf();
    expect(noteOf(filled.root)).toBe('Entries sent here are test entries. They run no actions.');
    toolbarButton(filled.root, 'Fill with sample answers').click();
    await vi.waitFor(() => expect(filled.reads).toHaveBeenLastCalledWith('en-US', { ...NONE, sample: true }));
    await settle(filled.element);
    expect(filled.root.querySelector('.bar uui-button[label="Fill with sample answers"]')).not.toBeNull();
    document.body.replaceChildren();

    const { element, root, reads, port: frame } = await previewListening(() => shownIn('en-US'));
    sentOver(frame.port);
    await vi.waitFor(() => expect(noteOf(root)).toContain('Saved as test entry #12.'));
    toolbarButton(root, 'Start again').click();
    await vi.waitFor(() => expect(reads).toHaveBeenLastCalledWith('en-US', NONE));
    await settle(element);
    expect(noteOf(root)).toBe('Entries sent here are test entries. They run no actions.');
    expect(root.querySelector('.bar uui-button[label="Fill with sample answers"]')).not.toBeNull();
  });

  it('says which test entry it saved, and runs its actions once confirmed, after the list of what goes out', async () => {
    const {
      element,
      root,
      entries,
      runs,
      port: frame,
    } = await previewListening(() => shownIn('en-US'), { mayRunActions: true });
    sentOver(frame.port, OTHER_SUBMISSION);
    await vi.waitFor(() =>
      expect(text(root.querySelector('.note > span'))).toBe('Saved as test entry #12. No actions ran.'),
    );
    expect(entries).toHaveBeenCalledWith(OTHER_SUBMISSION);
    const run = must(root.querySelector<HTMLElement>('.note uui-button'), 'Run actions');
    expect(run.getAttribute('label')).toBe('Run actions for test entry #12\u2026');
    expect(must(root.querySelector('.note'), 'note').getAttribute('role')).toBe('status');

    // While the dialog asks, the button does not wait yet, and a second press asks nothing more.
    const answered = deferred<undefined>();
    confirming.open = answered.promise;
    confirming.yes = false;
    run.click();
    await vi.waitFor(() => expect(confirming.asked).toHaveLength(1));
    await settle(element);
    expect((run as HTMLElement & { state?: string }).state).toBeUndefined();
    run.click();
    answered.resolve(undefined);
    await across();
    expect(confirming.asked).toHaveLength(1);
    expect(runs).not.toHaveBeenCalled();
    confirming.open = undefined;
    expect(confirming.asked[0]).toEqual({
      headline: 'Run the actions for test entry #12?',
      confirmLabel: 'Run 2 actions',
    });

    confirming.yes = true;
    run.click();
    await vi.waitFor(() => expect(runs).toHaveBeenCalledWith(OTHER_SUBMISSION));
    await vi.waitFor(() => expect(noteOf(root)).toBe('Saved as test entry #12. Its actions are running.'));
    expect(root.querySelector('.note uui-button')).toBeNull();
  });

  it('says when the actions could not be run, and offers Run actions... only where it may and something waits', async () => {
    const failing = await previewListening(() => shownIn('en-US'), {
      mayRunActions: true,
      runActions: () => Promise.resolve('failed'),
    });
    sentOver(failing.port.port);
    await vi.waitFor(() => expect(failing.root.querySelector('.note uui-button')).not.toBeNull());
    must(failing.root.querySelector<HTMLElement>('.note uui-button'), 'Run actions').click();
    await vi.waitFor(() =>
      expect(text(failing.root.querySelector('.note [role="alert"]'))).toBe("The actions couldn't be run."),
    );
    expect(failing.root.querySelector('.note uui-button')).not.toBeNull();
    document.body.replaceChildren();

    const designer = await previewListening(() => shownIn('en-US'));
    sentOver(designer.port.port);
    await vi.waitFor(() => expect(noteOf(designer.root)).toBe('Saved as test entry #12. No actions ran.'));
    document.body.replaceChildren();

    const ran = await previewListening(() => shownIn('en-US'), {
      mayRunActions: true,
      testEntry: () =>
        Promise.resolve({ kind: 'found', number: 3, actionsRan: true, awaitsConfirmation: false, actions: [] }),
    });
    sentOver(ran.port.port);
    await vi.waitFor(() => expect(noteOf(ran.root)).toBe('Saved as test entry #3. Its actions are running.'));
    document.body.replaceChildren();

    const waiting = await previewListening(() => shownIn('en-US'), {
      mayRunActions: true,
      testEntry: () =>
        Promise.resolve({ kind: 'found', number: 4, actionsRan: true, awaitsConfirmation: true, actions: [] }),
    });
    sentOver(waiting.port.port);
    await vi.waitFor(() =>
      expect(noteOf(waiting.root)).toBe('Saved as test entry #4. Its actions run once its email address is confirmed.'),
    );
    expect(waiting.root.querySelector('.note uui-button')).toBeNull();
    document.body.replaceChildren();

    const missing = await previewListening(() => shownIn('en-US'), {
      mayRunActions: true,
      testEntry: () => Promise.resolve({ kind: 'missing' }),
    });
    sentOver(missing.port.port);
    await vi.waitFor(() => expect(noteOf(missing.root)).toBe('Sent as a test entry.'));
  });

  it("runs the test entries' actions through a ticket the frame is handed, for who may manage them", async () => {
    const {
      element,
      root,
      tickets,
      port: frame,
    } = await previewListening(() => shownIn('en-US'), {
      mayRunActions: true,
    });
    const handed = frame.port;
    const coming = deferred<PreviewTicket | undefined>();
    tickets.mockImplementation(() => coming.promise);
    expect(text(root.querySelector('#jfb-preview-tests'))).toBe('Test entries');
    const [toggle] = buttonsOf(element, 'jfb-preview-tests');
    expect([text(toggle), toggle?.getAttribute('aria-pressed')]).toEqual(['Run actions for test entries', 'false']);
    must(toggle, 'Run actions for test entries').click();
    await vi.waitFor(() => expect(tickets).toHaveBeenCalledWith(7, true));
    // Until the frame holds the ticket, the note says what the one it holds does.
    await settle(element);
    expect(noteOf(root)).toBe('Entries sent here are test entries. They run no actions.');
    coming.resolve({ ticket: 'new-ticket', lifetime: 600 });
    await vi.waitFor(() =>
      expect(frame.heard).toContainEqual({ jfb: TICKET_MESSAGE, form: FORM, ticket: 'new-ticket', lifetime: 600 }),
    );
    await settle(element);
    expect(handed).toBe(frame.port);
    expect(noteOf(root)).toBe(
      'Entries sent here are test entries. Their actions run, so their emails and messages really go out.',
    );
    expect(buttonsOf(element, 'jfb-preview-tests')[0]?.getAttribute('aria-pressed')).toBe('true');
  });

  it('keeps the switch where the frame is when no ticket comes, and offers it not to who may not manage actions', async () => {
    const { element, root, tickets } = await previewListening(() => shownIn('en-US'), { mayRunActions: true });
    tickets.mockImplementation(() => Promise.resolve(undefined));
    must(buttonsOf(element, 'jfb-preview-tests')[0], 'Run actions for test entries').click();
    await vi.waitFor(() => expect(tickets).toHaveBeenCalledWith(7, true));
    await across();
    await settle(element);
    expect(buttonsOf(element, 'jfb-preview-tests')[0]?.getAttribute('aria-pressed')).toBe('false');
    expect(noteOf(root)).toBe('Entries sent here are test entries. They run no actions.');
    document.body.replaceChildren();

    const designer = await previewOf();
    expect(designer.root.querySelector('#jfb-preview-tests')).toBeNull();
  });
});
