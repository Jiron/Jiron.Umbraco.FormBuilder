// The preview frame's side in the backoffice (ADR-0203). Once the frame's document has loaded, it is handed a port of a
// channel of their own, with the one message posted to the frame's window; over that port the frame asks for a new
// ticket for its form when its own runs short, and is told the colour scheme. A page the frame goes on to never gets
// the port, which closes as the frame loads another. A new ticket is asked for only when the last one is about to run
// out, one at a time, so a frame that keeps asking cannot keep the backoffice's session alive (section 4.10).
import {
  isTicketAsk,
  PORT_MESSAGE,
  RENEW_BEFORE,
  SCHEME_MESSAGE,
  TICKET_MESSAGE,
  type PortMessage,
  type SchemeMessage,
  type TicketAnswer,
} from '@jfb/runtime/preview-protocol';
import type { PreviewTicket } from './preview-read';

/** How much sooner than the frame's own count the host renews a ticket, for the moments between the two counts. */
export const RENEW_SLACK = 30_000;

/** The frame as the host reads it: its window, which the port goes to. */
export interface HostedFrame {
  readonly contentWindow: {
    postMessage(message: unknown, targetOrigin: string, transfer?: Transferable[]): void;
  } | null;
}

/** One document in one frame: the version it shows, its form, its ticket's time, and where new tickets come from. */
export class FrameHost {
  readonly #frame: HostedFrame;
  readonly #form: string;
  readonly #version: number;
  readonly #ticket: (version: number) => Promise<PreviewTicket | undefined>;
  readonly #now: () => number;
  #until: number;
  #loads = 0;
  #port: MessagePort | null = null;
  #asking = false;

  /**
   * @param lifetime The seconds the document's own ticket counts from now.
   */
  constructor(
    frame: HostedFrame,
    form: string,
    version: number,
    lifetime: number,
    ticket: (version: number) => Promise<PreviewTicket | undefined>,
    now: () => number = Date.now,
  ) {
    this.#frame = frame;
    this.#form = form;
    this.#version = version;
    this.#ticket = ticket;
    this.#now = now;
    this.#until = now() + lifetime * 1000;
  }

  /** Whether the frame still shows the document it was given, and holds its port. */
  get showsItsDocument(): boolean {
    return this.#port !== null;
  }

  /** The frame loaded a document: the one given first gets the port; any later one is a page it went on to. */
  loaded(): void {
    this.#loads++;
    const target = this.#frame.contentWindow;
    if (this.#loads !== 1 || target === null) {
      this.close();
      return;
    }
    const channel = new MessageChannel();
    channel.port1.onmessage = (event) => void this.#heard(channel.port1, event.data);
    this.#port = channel.port1;
    const message: PortMessage = { jfb: PORT_MESSAGE, form: this.#form };
    target.postMessage(message, '*', [channel.port2]);
  }

  /** Closes the port: the frame is gone or went on. */
  close(): void {
    this.#port?.close();
    this.#port = null;
  }

  /** Tells the frame to show the page light or dark, which keeps the answers given. */
  scheme(dark: boolean): void {
    const message: SchemeMessage = { jfb: SCHEME_MESSAGE, dark };
    this.#port?.postMessage(message);
  }

  // The frame's ask: answered with a new ticket for the version shown when the last one is about to run out, over the
  // port it came on while that is still open.
  async #heard(port: MessagePort, data: unknown): Promise<void> {
    if (port !== this.#port || this.#asking || !isTicketAsk(data, this.#form)) return;
    if (this.#until - this.#now() > RENEW_BEFORE + RENEW_SLACK) return;
    this.#asking = true;
    try {
      const answer = await this.#ticket(this.#version);
      if (!answer || port !== this.#port) return;
      this.#until = this.#now() + answer.lifetime * 1000;
      const message: TicketAnswer = {
        jfb: TICKET_MESSAGE,
        form: this.#form,
        ticket: answer.ticket,
        lifetime: answer.lifetime,
      };
      port.postMessage(message);
    } finally {
      this.#asking = false;
    }
  }
}
