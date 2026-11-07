// The preview frame's host (ADR-0203, ADR-0204): the frame's first loaded document is handed a port, with the one
// message to its window; an ask over it for its form gets a new ticket for the version shown, but only once the last
// ticket is about to run out and one at a time, so a frame that keeps asking gets nothing; a later load closes the port,
// and so does the host; the scheme goes over the port too. The frame says over it which submission it sent, and a switch
// of the test entries' actions hands it a ticket unasked, which its renewals keep; the latest switch decides, and the
// one that settles tells what the frame holds.
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  EXPIRED_MESSAGE,
  PORT_MESSAGE,
  RENEW_BEFORE,
  SCHEME_MESSAGE,
  SENT_MESSAGE,
  TICKET_MESSAGE,
} from '@jfb/runtime/preview-protocol';
import { FrameHost, RENEW_SLACK, SENT_LIMIT } from './frame-host';
import type { PreviewTicket } from './preview-read';

const FORM = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const ASK = { jfb: TICKET_MESSAGE, form: FORM };
const ports: MessagePort[] = [];

/** A host for a frame whose document's ticket counts 600 seconds from a clock at 0. */
function hosted(answer: PreviewTicket | null = { ticket: 'new-ticket', lifetime: 600 }, runsActions = false) {
  const posted: { message: unknown; origin: string; transfer: Transferable[] | undefined }[] = [];
  const window = {
    postMessage: (message: unknown, origin: string, transfer?: Transferable[]) => {
      posted.push({ message, origin, transfer });
      for (const port of (transfer ?? []) as MessagePort[]) ports.push(port);
    },
  };
  const frame = { contentWindow: window as typeof window | null };
  const asked: [number, boolean][] = [];
  const sent: string[] = [];
  const held: boolean[] = [];
  let expired = 0;
  const clock = { now: 0 };
  const answers: ((value: PreviewTicket | undefined) => void)[] = [];
  const ticket = vi.fn((version: number, runActions: boolean) => {
    asked.push([version, runActions]);
    return new Promise<PreviewTicket | undefined>((done) => answers.push(done));
  });
  const host = new FrameHost(frame, FORM, 7, 600, {
    ticket,
    runsActions,
    sent: (submissionId) => sent.push(submissionId),
    held: (runs) => held.push(runs),
    expired: () => expired++,
    now: () => clock.now,
  });
  /** The frame's end of the port it was handed, with what came over it. */
  const portOfFrame = () => {
    const port = posted[0]?.transfer?.[0] as MessagePort | undefined;
    if (!port) throw new Error('the frame has no port');
    const heard: unknown[] = [];
    port.onmessage = (event) => heard.push(event.data);
    return { port, heard };
  };
  /** Answers an ask for a ticket, the latest unless told which, with the ticket given or none. */
  const reply = (index = answers.length - 1, given: PreviewTicket | null = answer) =>
    answers[index]?.(given ?? undefined);
  return {
    host,
    frame,
    posted,
    asked,
    sent,
    held,
    expired: () => expired,
    clock,
    portOfFrame,
    answer: () => reply(),
    reply,
  };
}

/** Lets the port's messages go across. */
const across = () => new Promise((done) => setTimeout(done, 20));

/** The message that hands the frame a ticket. */
const handed = (ticket = 'new-ticket') => ({ jfb: TICKET_MESSAGE, form: FORM, ticket, lifetime: 600 });

afterEach(() => {
  for (const port of ports.splice(0)) port.close();
});

describe('the frame host', () => {
  it("hands the frame's first document a port, with the one message to its window", () => {
    const { host, posted } = hosted();
    expect(host.showsItsDocument).toBe(false);
    host.loaded();
    expect(host.showsItsDocument).toBe(true);
    expect(posted).toHaveLength(1);
    expect(posted[0]?.message).toEqual({ jfb: PORT_MESSAGE, form: FORM });
    expect(posted[0]?.origin).toBe('*');
    expect(posted[0]?.transfer).toHaveLength(1);
  });

  it('answers an ask over the port with a new ticket for the version shown, once little is left of the last', async () => {
    const { host, asked, clock, portOfFrame, answer } = hosted();
    host.loaded();
    const { port, heard } = portOfFrame();

    // Too soon: the ticket the document holds has plenty left, so the frame goes with it.
    clock.now = 600_000 - RENEW_BEFORE - RENEW_SLACK - 1;
    port.postMessage(ASK);
    await across();
    expect(asked).toEqual([]);

    clock.now += 1;
    port.postMessage(ASK);
    await vi.waitFor(() => expect(asked).toEqual([[7, false]]));
    answer();
    await vi.waitFor(() => expect(heard).toEqual([handed()]));

    // The new ticket counts from now: another ask is too soon again.
    port.postMessage(ASK);
    await across();
    expect(asked).toEqual([[7, false]]);
  });

  it('asks for one ticket at a time, and answers nothing that is no ask for its form', async () => {
    const { host, asked, clock, portOfFrame } = hosted();
    host.loaded();
    const { port, heard } = portOfFrame();
    clock.now = 600_000;
    port.postMessage({ ...ASK, form: 'another' });
    port.postMessage({ ...ASK, ticket: 'given' });
    port.postMessage('preview-ticket');
    await across();
    expect(asked).toEqual([]);
    port.postMessage(ASK);
    port.postMessage(ASK);
    await across();
    expect([asked, heard]).toEqual([[[7, false]], []]);
  });

  it('closes the port when the frame goes on, or the host closes, and sends nothing over it after', async () => {
    const meanwhile = hosted();
    meanwhile.host.loaded();
    meanwhile.clock.now = 600_000;
    const asking = meanwhile.portOfFrame();
    asking.port.postMessage(ASK);
    await vi.waitFor(() => expect(meanwhile.asked).toEqual([[7, false]]));
    // The frame goes on while the ticket is on its way.
    meanwhile.host.loaded();
    expect(meanwhile.host.showsItsDocument).toBe(false);
    meanwhile.answer();
    await across();
    expect(asking.heard).toEqual([]);

    const closed = hosted();
    closed.host.loaded();
    const end = closed.portOfFrame();
    closed.host.close();
    closed.host.scheme(true);
    await closed.host.runActions(true);
    await across();
    expect([closed.host.showsItsDocument, end.heard, closed.asked]).toEqual([false, [], []]);
  });

  it('sends nothing without a ticket, and hands no port to a frame without a window', async () => {
    const none = hosted(null);
    none.host.loaded();
    none.clock.now = 600_000;
    const end = none.portOfFrame();
    end.port.postMessage(ASK);
    await vi.waitFor(() => expect(none.asked).toEqual([[7, false]]));
    none.answer();
    await across();
    expect(end.heard).toEqual([]);

    const gone = hosted();
    gone.frame.contentWindow = null;
    gone.host.loaded();
    expect([gone.host.showsItsDocument, gone.posted]).toEqual([false, []]);
  });

  it('tells the frame the colour scheme over its port', async () => {
    const { host, portOfFrame } = hosted();
    host.scheme(true);
    host.loaded();
    const { heard } = portOfFrame();
    host.scheme(true);
    host.scheme(false);
    await across();
    expect(heard).toEqual([
      { jfb: SCHEME_MESSAGE, dark: true },
      { jfb: SCHEME_MESSAGE, dark: false },
    ]);
  });

  it('tells of each submission its frame sent once, by its id, and of a few for one document', async () => {
    const { host, sent, portOfFrame } = hosted();
    host.loaded();
    const { port } = portOfFrame();
    const id = (n: number) => `7c9e6679-7425-40de-944b-${String(n).padStart(12, '0')}`;
    port.postMessage({ jfb: SENT_MESSAGE, form: FORM, submissionId: id(1) });
    port.postMessage({ jfb: SENT_MESSAGE, form: 'another', submissionId: id(2) });
    port.postMessage({ jfb: SENT_MESSAGE, form: FORM });
    port.postMessage({ jfb: SENT_MESSAGE, form: FORM, submissionId: '../ticket' });
    port.postMessage({ jfb: SENT_MESSAGE, form: FORM, submissionId: id(1).toUpperCase() });
    port.postMessage({ jfb: SENT_MESSAGE, form: FORM, submissionId: id(3) });
    await vi.waitFor(() => expect(sent).toEqual([id(1), id(3)]));

    // A frame that keeps saying it sent is heard for a few submissions only.
    for (let n = 10; n < 30; n++) port.postMessage({ jfb: SENT_MESSAGE, form: FORM, submissionId: id(n) });
    await vi.waitFor(() => expect(sent).toHaveLength(SENT_LIMIT));
    await across();
    expect(sent).toHaveLength(SENT_LIMIT);
  });

  it("hands the frame a ticket unasked when the test entries' actions are switched, which its renewals keep", async () => {
    const { host, asked, held, clock, portOfFrame, answer } = hosted();
    host.loaded();
    const { port, heard } = portOfFrame();
    const switching = host.runActions(true);
    expect(asked).toEqual([[7, true]]);
    answer();
    await switching;
    await vi.waitFor(() => expect(heard).toEqual([handed()]));
    expect(held).toEqual([true]);

    // The ticket handed counts from now; its renewal runs the actions too.
    clock.now = 600_000;
    port.postMessage(ASK);
    await vi.waitFor(() =>
      expect(asked).toEqual([
        [7, true],
        [7, true],
      ]),
    );
  });

  it("starts from the document's own ticket, and hands a switch made before the port once the frame has it", async () => {
    const own = hosted(undefined, true);
    own.host.loaded();
    own.clock.now = 600_000;
    own.portOfFrame().port.postMessage(ASK);
    await vi.waitFor(() => expect(own.asked).toEqual([[7, true]]));

    const early = hosted();
    await early.host.runActions(true);
    expect(early.asked).toEqual([]);
    early.host.loaded();
    const { heard } = early.portOfFrame();
    expect(early.asked).toEqual([[7, true]]);
    early.answer();
    await vi.waitFor(() => expect(heard).toEqual([handed()]));
    expect(early.held).toEqual([true]);
  });

  it('lets the latest switch decide, and keeps the frame its ticket when none comes', async () => {
    const { host, held, portOfFrame, reply } = hosted();
    host.loaded();
    const { heard } = portOfFrame();
    const on = host.runActions(true);
    const off = host.runActions(false);
    // The later switch's ticket comes first; the earlier one's, coming after, is dropped.
    reply(1, { ticket: 'off-ticket', lifetime: 600 });
    reply(0, { ticket: 'on-ticket', lifetime: 600 });
    await Promise.all([on, off]);
    await vi.waitFor(() => expect(heard).toEqual([handed('off-ticket')]));
    expect(held).toEqual([false]);

    const failed = host.runActions(true);
    reply(2, null);
    await failed;
    await across();
    expect([heard.length, held]).toEqual([1, [false, false]]);
  });

  it("tells that the public API refused its frame's ticket, for its form only", async () => {
    const { host, expired, portOfFrame } = hosted();
    host.loaded();
    const { port } = portOfFrame();
    port.postMessage({ jfb: EXPIRED_MESSAGE, form: 'another' });
    port.postMessage({ jfb: EXPIRED_MESSAGE, form: FORM });
    await vi.waitFor(() => expect(expired()).toBe(1));
    await across();
    expect(expired()).toBe(1);
  });

  it("drops a renewal's ticket when a switch handed the frame one meanwhile", async () => {
    const { host, asked, clock, portOfFrame, reply } = hosted();
    host.loaded();
    const { port, heard } = portOfFrame();
    clock.now = 600_000;
    port.postMessage(ASK);
    await vi.waitFor(() => expect(asked).toEqual([[7, false]]));
    const switching = host.runActions(true);
    reply(1, { ticket: 'on-ticket', lifetime: 600 });
    await switching;
    reply(0, { ticket: 'renewed', lifetime: 600 });
    await across();
    expect(heard).toEqual([handed('on-ticket')]);
  });
});
