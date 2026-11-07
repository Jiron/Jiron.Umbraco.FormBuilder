// The preview frame's host (ADR-0203): the frame's first loaded document is handed a port, with the one message to its
// window; an ask over it for its form gets a new ticket for the version shown, but only once the last ticket is about to
// run out and one at a time, so a frame that keeps asking gets nothing; a later load closes the port, and so does the
// host; the scheme goes over the port too.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PORT_MESSAGE, RENEW_BEFORE, SCHEME_MESSAGE, TICKET_MESSAGE } from '@jfb/runtime/preview-protocol';
import { FrameHost, RENEW_SLACK } from './frame-host';
import type { PreviewTicket } from './preview-read';

const FORM = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';
const ASK = { jfb: TICKET_MESSAGE, form: FORM };
const ports: MessagePort[] = [];

/** A host for a frame whose document's ticket counts 600 seconds from a clock at 0. */
function hosted(answer: PreviewTicket | null = { ticket: 'new-ticket', lifetime: 600 }) {
  const posted: { message: unknown; origin: string; transfer: Transferable[] | undefined }[] = [];
  const window = {
    postMessage: (message: unknown, origin: string, transfer?: Transferable[]) => {
      posted.push({ message, origin, transfer });
      for (const port of (transfer ?? []) as MessagePort[]) ports.push(port);
    },
  };
  const frame = { contentWindow: window as typeof window | null };
  const asked: number[] = [];
  const clock = { now: 0 };
  let resolve: (value: PreviewTicket | undefined) => void = () => undefined;
  const ticket = vi.fn((version: number) => {
    asked.push(version);
    return new Promise<PreviewTicket | undefined>((done) => (resolve = done));
  });
  const host = new FrameHost(frame, FORM, 7, 600, ticket, () => clock.now);
  /** The frame's end of the port it was handed, with what came over it. */
  const portOfFrame = () => {
    const port = posted[0]?.transfer?.[0] as MessagePort | undefined;
    if (!port) throw new Error('the frame has no port');
    const heard: unknown[] = [];
    port.onmessage = (event) => heard.push(event.data);
    return { port, heard };
  };
  return { host, frame, posted, asked, clock, portOfFrame, answer: () => resolve(answer ?? undefined) };
}

/** Lets the port's messages go across. */
const across = () => new Promise((done) => setTimeout(done, 20));

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
    await vi.waitFor(() => expect(asked).toEqual([7]));
    answer();
    await vi.waitFor(() =>
      expect(heard).toEqual([{ jfb: TICKET_MESSAGE, form: FORM, ticket: 'new-ticket', lifetime: 600 }]),
    );

    // The new ticket counts from now: another ask is too soon again.
    port.postMessage(ASK);
    await across();
    expect(asked).toEqual([7]);
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
    expect([asked, heard]).toEqual([[7], []]);
  });

  it('closes the port when the frame goes on, or the host closes, and sends nothing over it after', async () => {
    const meanwhile = hosted();
    meanwhile.host.loaded();
    meanwhile.clock.now = 600_000;
    const asking = meanwhile.portOfFrame();
    asking.port.postMessage(ASK);
    await vi.waitFor(() => expect(meanwhile.asked).toEqual([7]));
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
    await across();
    expect([closed.host.showsItsDocument, end.heard]).toEqual([false, []]);
  });

  it('sends nothing without a ticket, and hands no port to a frame without a window', async () => {
    const none = hosted(null);
    none.host.loaded();
    none.clock.now = 600_000;
    const end = none.portOfFrame();
    end.port.postMessage(ASK);
    await vi.waitFor(() => expect(none.asked).toEqual([7]));
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
});
