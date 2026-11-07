// What the preview's frame and the backoffice say to each other (ADR-0203, ADR-0204), on the `jfb` key of each message.
// Once the frame's document has loaded, the backoffice hands it a port of a channel of their own, with the one message
// it posts to the frame's window; everything else goes over that port, which a page the frame goes on to never gets.
// Over it the frame asks for a new ticket for its form and says which submission it sent; the backoffice answers with a
// ticket, hands one over unasked when the test entries' actions are switched, and tells it the colour scheme.

/** The backoffice hands the frame its port. */
export const PORT_MESSAGE = 'preview-port';

/** The frame asks for a new ticket; the backoffice's answer carries it. */
export const TICKET_MESSAGE = 'preview-ticket';

/** The backoffice tells the frame to show the page light or dark. */
export const SCHEME_MESSAGE = 'preview-scheme';

/** The frame says which submission the public API took, a test entry the backoffice may run the actions of. */
export const SENT_MESSAGE = 'preview-sent';

/** A ticket is renewed once less than this is left of it, in milliseconds; the backoffice answers no sooner. */
export const RENEW_BEFORE = 120_000;

/** The port's message, for the frame's form. */
export interface PortMessage {
  readonly jfb: typeof PORT_MESSAGE;
  readonly form: string;
}

/** The frame's ask for a new ticket for its form. */
export interface TicketAsk {
  readonly jfb: typeof TICKET_MESSAGE;
  readonly form: string;
}

/** A new ticket for the form, which counts for so many seconds from now. */
export interface TicketAnswer extends TicketAsk {
  readonly ticket: string;
  readonly lifetime: number;
}

/** The submission the frame sent, by the submission id it sent it with. */
export interface SentMessage {
  readonly jfb: typeof SENT_MESSAGE;
  readonly form: string;
  readonly submissionId: string;
}

/** The page's colour scheme. */
export interface SchemeMessage {
  readonly jfb: typeof SCHEME_MESSAGE;
  readonly dark: boolean;
}

const record = (data: unknown): data is Record<string, unknown> => typeof data === 'object' && data !== null;

/** Whether a message hands the port for this form. */
export const isPortMessage = (data: unknown, form: string): data is PortMessage =>
  record(data) && data['jfb'] === PORT_MESSAGE && data['form'] === form;

/** Whether a message is the frame's ask for a new ticket for this form. */
export const isTicketAsk = (data: unknown, form: string): data is TicketAsk =>
  record(data) && data['jfb'] === TICKET_MESSAGE && data['form'] === form && !('ticket' in data);

/** Whether a message is a new ticket for this form. */
export const isTicketAnswer = (data: unknown, form: string): data is TicketAnswer =>
  record(data) &&
  data['jfb'] === TICKET_MESSAGE &&
  data['form'] === form &&
  typeof data['ticket'] === 'string' &&
  data['ticket'] !== '' &&
  typeof data['lifetime'] === 'number' &&
  data['lifetime'] > 0;

/** A submission id as the public API makes one: a UUID. */
const SUBMISSION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Whether a message says which submission the frame sent for this form, by an id as the public API makes one. */
export const isSentMessage = (data: unknown, form: string): data is SentMessage =>
  record(data) &&
  data['jfb'] === SENT_MESSAGE &&
  data['form'] === form &&
  typeof data['submissionId'] === 'string' &&
  SUBMISSION_ID.test(data['submissionId']);

/** Whether a message tells the page's colour scheme. */
export const isSchemeMessage = (data: unknown): data is SchemeMessage =>
  record(data) && data['jfb'] === SCHEME_MESSAGE && typeof data['dark'] === 'boolean';
