// The preview frame's side in the backoffice (ADR-0203, ADR-0204). Once the frame's document has loaded, it is handed a
// port of a channel of their own, with the one message posted to the frame's window; over that port the frame asks for
// a new ticket for its form when its own runs short and says which submission it sent, and is told the colour scheme
// and handed a new ticket unasked when the test entries' actions are switched. A page the frame goes on to never gets
// the port, which closes as the frame loads another. A new ticket is asked for only when the last one is about to run
// out, one at a time, so a frame that keeps asking cannot keep the backoffice's session alive (section 4.10). For the
// same reason it hears of each submission once, and of a few for each document.
import {
  isSentMessage,
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

/** How many submissions the host hears of for one document; Start again gives a new one. */
export const SENT_LIMIT = 10;

/** The frame as the host reads it: its window, which the port goes to. */
export interface HostedFrame {
  readonly contentWindow: {
    postMessage(message: unknown, targetOrigin: string, transfer?: Transferable[]): void;
  } | null;
}

/** Where the host gets its tickets and tells of what the frame sent. */
export interface FrameHostOptions {
  /** A new ticket for the version, whose test entries run their actions or not; undefined without an answer. */
  readonly ticket: (version: number, runActions: boolean) => Promise<PreviewTicket | undefined>;
  /** Whether the document's own ticket's test entries run their actions. */
  readonly runsActions?: boolean;
  /** Told of each submission the frame sent, by its submission id. */
  readonly sent?: (submissionId: string) => void;
  /** Told, once a switch of the test entries' actions settles, whether the frame's ticket runs them: the switch's, or the one it kept. */
  readonly held?: (runsActions: boolean) => void;
  /** The clock. */
  readonly now?: () => number;
}

/** One document in one frame: the version it shows, its form, its ticket's time, and where new tickets come from. */
export class FrameHost {
  readonly #frame: HostedFrame;
  readonly #form: string;
  readonly #version: number;
  readonly #options: FrameHostOptions;
  readonly #now: () => number;
  #until: number;
  #loads = 0;
  #port: MessagePort | null = null;
  #asking = false;
  // Whether the frame's ticket runs the test entries' actions, whether it should, and the switches so far.
  #holdsRuns: boolean;
  #runs: boolean;
  #switches = 0;
  // The submissions told of.
  readonly #sent = new Set<string>();

  /**
   * @param lifetime The seconds the document's own ticket counts from now.
   */
  constructor(frame: HostedFrame, form: string, version: number, lifetime: number, options: FrameHostOptions) {
    this.#frame = frame;
    this.#form = form;
    this.#version = version;
    this.#options = options;
    this.#now = options.now ?? Date.now;
    this.#until = this.#now() + lifetime * 1000;
    this.#holdsRuns = options.runsActions ?? false;
    this.#runs = this.#holdsRuns;
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
    // Switched before the document had its port: it gets its ticket now.
    if (this.#runs !== this.#holdsRuns) void this.runActions(this.#runs);
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

  /**
   * Whether the test entries the frame sends from now on run their actions (ADR-0204): the frame is handed a ticket that
   * says so, keeping the answers given, once it has its port. Without a ticket, or when the frame went on meanwhile, the
   * frame keeps the ticket it holds. A later switch decides over one whose ticket is still on its way; the one that
   * settles tells what the frame holds.
   */
  async runActions(on: boolean): Promise<void> {
    this.#runs = on;
    const port = this.#port;
    if (port === null) return;
    const switched = ++this.#switches;
    const answer = await this.#options.ticket(this.#version, on);
    if (switched !== this.#switches) return;
    if (answer && port === this.#port) {
      this.#hand(port, answer);
      this.#holdsRuns = on;
    }
    this.#runs = this.#holdsRuns;
    this.#options.held?.(this.#holdsRuns);
  }

  // The frame's ask, answered with a new ticket for the version shown when the last one is about to run out, over the
  // port it came on while that is still open; and the submission it sent, which the preview is told of.
  async #heard(port: MessagePort, data: unknown): Promise<void> {
    if (port !== this.#port) return;
    if (isSentMessage(data, this.#form)) {
      const id = data.submissionId.toLowerCase();
      if (this.#sent.has(id) || this.#sent.size >= SENT_LIMIT) return;
      this.#sent.add(id);
      this.#options.sent?.(data.submissionId);
      return;
    }
    if (this.#asking || !isTicketAsk(data, this.#form)) return;
    if (this.#until - this.#now() > RENEW_BEFORE + RENEW_SLACK) return;
    this.#asking = true;
    try {
      const runs = this.#holdsRuns;
      const answer = await this.#options.ticket(this.#version, runs);
      // A switch meanwhile handed the frame a ticket of its own, which this one would take back.
      if (!answer || port !== this.#port || runs !== this.#holdsRuns) return;
      this.#hand(port, answer);
    } finally {
      this.#asking = false;
    }
  }

  // Hands the frame a new ticket, which counts from now.
  #hand(port: MessagePort, answer: PreviewTicket): void {
    this.#until = this.#now() + answer.lifetime * 1000;
    const message: TicketAnswer = {
      jfb: TICKET_MESSAGE,
      form: this.#form,
      ticket: answer.ticket,
      lifetime: answer.lifetime,
    };
    port.postMessage(message);
  }
}
