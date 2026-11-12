// The webhook's settings as its editors read and write them (ROADMAP 10.2d, ADR-0191): where it goes, which of the ways
// of What it sends its values say, the names and values of a mapping or of the headers as rows, what can't be kept of a
// row, and the body an editor starts from. The rules are the server's (WebhookMaps, FormBuilderSecrets), said where the
// editor types, so a value the check would refuse, a credential above all, never reaches the draft. Pure, so it is tested
// without the DOM.
import type { PickField } from './action-editors';

/** Where a webhook goes: its own address, or the site's Slack or Teams address. */
export type WebhookTarget = 'url' | 'slack' | 'teams';

/**
 * What a webhook sends: for its own address, every answer by its field's alias, the names and values chosen, a body of
 * its own, or only that something happened; for Slack and Teams, a message or a body of its own.
 */
export type BodyMode = 'answers' | 'mapping' | 'body' | 'none' | 'message';

/** What a body is written in. */
export type WebhookFormat = 'json' | 'form' | 'xml';

/** The ways a target offers, in their order. */
export const MODES: Readonly<Record<'own' | 'chat', readonly BodyMode[]>> = {
  own: ['answers', 'mapping', 'body', 'none'],
  chat: ['message', 'body'],
};

/** The beginning every reference to the host's secrets has. */
export const REFERENCE = '$JironFormBuilder:Secrets:';

const isText = (value: unknown): value is string => typeof value === 'string';

const isObject = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

/** A setting's target; its own address for anything else. */
export const targetOf = (value: unknown): WebhookTarget => (value === 'slack' || value === 'teams' ? value : 'url');

/** A setting's format; JSON for anything else. */
export const formatOf = (value: unknown): WebhookFormat => (value === 'form' || value === 'xml' ? value : 'json');

/** Whether the target is a chat, whose message the server writes. */
export const isChat = (target: WebhookTarget): boolean => target !== 'url';

/** The values of What it sends, by their settings' names. */
export interface BodyValues {
  readonly fields?: unknown;
  readonly template?: unknown;
  readonly withoutAnswers?: unknown;
  readonly message?: unknown;
}

/** The way the values say, as the server reads them: a body of its own wins, then no answers, then the mapping. */
export function modeOf(target: WebhookTarget, values: BodyValues): BodyMode {
  if (isText(values.template)) return 'body';
  if (isChat(target)) return 'message';
  if (values.withoutAnswers === true) return 'none';
  return isObject(values.fields) ? 'mapping' : 'answers';
}

/** Whether a text names a secret of the host's configuration: the beginning, then 1 to 64 letters, digits and `_`. */
export function isReference(text: string): boolean {
  return text.startsWith(REFERENCE) && /^[A-Za-z0-9_]{1,64}$/.test(text.slice(REFERENCE.length));
}

/** The name a reference gives its secret. */
export const referenceName = (text: string): string => text.slice(REFERENCE.length);

// ---- Rows ---------------------------------------------------------------------------------------------------------

/** A name and its value, as a row shows them while it is typed. */
export interface Row {
  readonly name: string;
  readonly value: string;
}

/** The rows of an object of names and texts; none for anything else. */
export function rowsOf(value: unknown): Row[] {
  if (!isObject(value)) return [];
  return Object.entries(value).flatMap(([name, text]) => (isText(text) ? [{ name, value: text }] : []));
}

/** Why a row can't be kept, at its name or its value. */
export type RowProblem =
  | { readonly at: 'name'; readonly kind: 'empty' | 'wrong' | 'twice' | 'own' }
  | { readonly at: 'value'; readonly kind: 'credential' | 'wrong' };

/** What the server takes as a mapping's name: 1 to 100 letters, digits, `_`, `-` and `.`. */
export const isMappingName = (name: string): boolean => /^[A-Za-z0-9_.-]{1,100}$/.test(name);

// The headers the request sets itself, which a site may not (WebhookMaps.OwnHeaders).
const OWN_HEADERS = new Set([
  'allow',
  'connection',
  'expect',
  'expires',
  'host',
  'keep-alive',
  'last-modified',
  'proxy-connection',
  'te',
  'trailer',
  'transfer-encoding',
  'upgrade',
  'user-agent',
  'via',
]);

/** Whether a header's name is one the request or Jiron Form Builder sets itself. */
export function isOwnHeader(name: string): boolean {
  const lower = name.toLowerCase();
  return lower.startsWith('x-jfb-') || lower.startsWith('content-') || OWN_HEADERS.has(lower);
}

/** Whether a header's name says it carries a credential, which only a reference may give. */
export const holdsASecret = (name: string): boolean =>
  /auth|bearer|cookie|credential|key|passphrase|passwd|password|secret|session|signature|token/i.test(name);

/** Whether HTTP carries a header's value as it is: printable ASCII. */
export const isHeaderValue = (value: string): boolean => /^[\x20-\x7e]*$/.test(value);

/** A row that holds nothing yet, which is kept as no row. */
const isBlank = (row: Row): boolean => row.name.trim() === '' && row.value === '';

/**
 * Why a mapping's row can't be kept: a value without a name, a name the server refuses, or one an earlier row has. A
 * row without its name is said only once `done`, when its box was left.
 */
export function mappingProblem(rows: readonly Row[], at: number, done: boolean): RowProblem | null {
  const row = rows[at];
  if (!row || isBlank(row)) return null;
  if (row.name === '') return done ? { at: 'name', kind: 'empty' } : null;
  if (!isMappingName(row.name)) return { at: 'name', kind: 'wrong' };
  if (rows.slice(0, at).some((one) => one.name === row.name)) return { at: 'name', kind: 'twice' };
  return null;
}

/**
 * Why a header's row can't be kept: as a mapping's, a name the request sets itself, one an earlier row has in any letter
 * case, a credential given as itself, or a value HTTP can't carry.
 */
export function headerProblem(rows: readonly Row[], at: number, done: boolean): RowProblem | null {
  const row = rows[at];
  if (!row || isBlank(row)) return null;
  if (row.name === '') return done ? { at: 'name', kind: 'empty' } : null;
  if (!/^[A-Za-z0-9-]{1,100}$/.test(row.name)) return { at: 'name', kind: 'wrong' };
  if (isOwnHeader(row.name)) return { at: 'name', kind: 'own' };
  const lower = row.name.toLowerCase();
  if (rows.slice(0, at).some((one) => one.name.toLowerCase() === lower)) return { at: 'name', kind: 'twice' };
  if (!isHeaderValue(row.value)) return { at: 'value', kind: 'wrong' };
  if (holdsASecret(row.name) && !isReference(row.value)) return { at: 'value', kind: 'credential' };
  return null;
}

/** The object of the rows that can be kept, in their order; a blank row or one with a problem is left out. */
export function objectOf(
  rows: readonly Row[],
  problem: (rows: readonly Row[], at: number, done: boolean) => RowProblem | null,
): Record<string, string> {
  const kept: Record<string, string> = {};
  rows.forEach((row, at) => {
    if (row.name !== '' && !isBlank(row) && problem(rows, at, true) === null) kept[row.name] = row.value;
  });
  return kept;
}

/** Whether two objects of names and texts hold the same, in the same order. */
export function sameRows(a: unknown, b: unknown): boolean {
  return JSON.stringify(rowsOf(a)) === JSON.stringify(rowsOf(b)) && isObject(a) === isObject(b);
}

// ---- Where an editor starts ----------------------------------------------------------------------------------------

/** The tag of a field's answer. */
const answerTag = (alias: string) => `{{ field.${alias} }}`;

/**
 * The rows of every answer a webhook sends by itself, by its field's alias, as the server finds them: a field in a group,
 * not inside a repeatable one, and a sensitive one only where the action names it.
 */
export function everyAnswer(fields: readonly PickField[], included: readonly string[] = []): Row[] {
  return fields
    .filter((field) => field.top && (!field.sensitive || included.includes(field.key)) && isMappingName(field.alias))
    .map((field) => ({ name: field.alias, value: answerTag(field.alias) }));
}

const jsonText = (text: string) => JSON.stringify(text).slice(1, -1);

const xmlText = (text: string) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** A value with what lies outside its merge tags written for the format, the tags as they are, which the server writes. */
function outsideTags(value: string, write: (text: string) => string): string {
  return value
    .split(/(\{\{[\s\S]*?\}\})/)
    .map((part, at) => (at % 2 === 1 ? part : write(part)))
    .join('');
}

/** A body in the format's own language with the rows' names and values, for an editor to start from. */
export function starterBody(format: WebhookFormat, rows: readonly Row[]): string {
  switch (format) {
    case 'form':
      return rows
        .map((row) => `${encodeURIComponent(row.name)}=${outsideTags(row.value, encodeURIComponent)}`)
        .join('&');
    case 'xml':
      return [
        '<submission>',
        ...rows.map((row) => `  <${xmlName(row.name)}>${outsideTags(row.value, xmlText)}</${xmlName(row.name)}>`),
        '</submission>',
      ].join('\n');
    default:
      return rows.length === 0
        ? '{}'
        : [
            '{',
            rows.map((row) => `  "${jsonText(row.name)}": "${outsideTags(row.value, jsonText)}"`).join(',\n'),
            '}',
          ].join('\n');
  }
}

// An element's name from a mapping's: one that starts with a digit, a dot or a hyphen gets `_` before it.
const xmlName = (name: string) => (/^[A-Za-z_]/.test(name) ? name : `_${name}`);

/**
 * A chat's own body to start from: its message as the chat reads it, in JSON; for Teams a text block a line, a blank
 * line the space before the next, as a card breaks lines only between blocks (as the server writes a message).
 */
export function starterChatBody(target: WebhookTarget, message: string): string {
  const text = jsonText(message);
  const blocks: string[] = [];
  let apart = false;
  for (const line of message.split('\n')) {
    if (line.trim() === '') {
      apart = blocks.length > 0;
      continue;
    }
    blocks.push(
      `{ "type": "TextBlock", "wrap": true, "spacing": "${apart ? 'Medium' : 'None'}", "text": "${jsonText(line)}" }`,
    );
    apart = false;
  }
  return target === 'teams'
    ? [
        '{',
        '  "type": "message",',
        '  "attachments": [',
        '    {',
        '      "contentType": "application/vnd.microsoft.card.adaptive",',
        '      "content": {',
        '        "type": "AdaptiveCard",',
        '        "version": "1.4",',
        '        "body": [',
        blocks.map((block) => `          ${block}`).join(',\n'),
        '        ]',
        '      }',
        '    }',
        '  ]',
        '}',
      ].join('\n')
    : ['{', `  "text": "${text}"`, '}'].join('\n');
}
