// The webhook's editors (ROADMAP 10.2d, ADR-0191). Where it goes is one box: the target, then the address, whose help
// says what it takes for that target. What it sends is another: for its own address the format and one choice of what
// goes (every answer, the names and values chosen, a body written, or only that something happened), with the metadata
// where it applies; for Slack and Teams, a message or a body. The draft holds only the chosen way's values, and the
// sidebar keeps the others while it is open, so choosing back brings them back. The headers are rows of a name and a
// value, and the signing secret takes only a reference: a credential typed as itself is said under its box and never
// reaches the draft. Each box has the id the form check's Fix it lands on.
import { css, html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import type { FieldSettingResponseModel } from '../../api';
import { boxValue } from '../inspector/box-value';
import { boxId, settingEditor, settingFrame } from '../inspector/setting-editor';
import { parseSetting } from '../inspector/setting-values';
import type { ActionEditorContext, Within } from './action-editors';
import { foundAt, typedLine } from './editor-lines';
import {
  MODES,
  everyAnswer,
  formatOf,
  headerProblem,
  holdsASecret,
  isChat,
  isReference,
  mappingProblem,
  modeOf,
  objectOf,
  rowsOf,
  starterBody,
  starterChatBody,
  targetOf,
  type BodyMode,
  type Row,
  type RowProblem,
  type WebhookTarget,
} from './webhook-model';

/** The webhook's editors by their aliases, for the inspector's `editors`. */
export function webhookEditors(context: ActionEditorContext) {
  return {
    webhookAddress: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      address(context, setting, value, path),
    webhookUrl: (setting: FieldSettingResponseModel, value: unknown) =>
      addressBox(context, { setting, value }, targetOf(context.value('target')), true),
    webhookBody: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      body(context, setting, value, path),
    headerMap: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      rowsEditor(context, setting, value, path, HEADERS),
    secretReference: (setting: FieldSettingResponseModel, value: unknown, path: string) =>
      secret(context, setting, value, path),
  };
}

/** The look of the webhook's editors, beside the action editors'. */
export const webhookEditorStyles = css`
  /* A box's settings within it, under its first, as one item, set in from it as what a choice brings. */
  .within {
    display: grid;
    gap: var(--uui-size-space-5);
    min-inline-size: 0;
  }

  .inner,
  .mode {
    display: grid;
    gap: var(--uui-size-space-3);
    min-inline-size: 0;
    padding-inline-start: var(--uui-size-space-4);
    border-inline-start: 2px solid var(--uui-color-border);
  }

  /* The chosen way's box under its own choice, from the choice's words on: the radio, its gap and the row's padding. */
  .choices > .mode {
    margin-block: var(--uui-size-space-1) var(--uui-size-space-4);
    margin-inline-start: calc(18px + 2 * var(--uui-size-space-3));
  }

  textarea.body {
    min-block-size: 10rem;
  }

  /* Names and values: two columns and the remove button; one under the other where the sidebar is narrow. */
  .pairs {
    display: grid;
    gap: var(--uui-size-space-2);
    min-inline-size: 0;
  }

  .pairs-head,
  .pair-row {
    display: grid;
    grid-template-columns: minmax(0, 2fr) minmax(0, 3fr) 32px;
    align-items: center;
    gap: var(--uui-size-space-2);
  }

  .pairs-head {
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
    font-weight: 600;
  }

  .pair-row > .why {
    grid-column: 1 / -1;
  }

  /* The columns' heads name the boxes; where they go, the boxes' own placeholders do. */
  .pair-row > input::placeholder {
    color: transparent;
  }

  @container (max-width: 480px) {
    .pairs-head {
      display: none;
    }

    .pair-row {
      grid-template-columns: minmax(0, 1fr) 32px;
      padding-block-end: var(--uui-size-space-3);
      border-block-end: 1px solid var(--uui-color-divider-standalone);
    }

    .pair-row > input::placeholder {
      color: var(--jfb-bo-color-text-muted);
    }

    .pair-row > .value {
      grid-column: 1;
    }

    .pair-row > .remove {
      grid-row: 1 / span 2;
      grid-column: 2;
    }
  }
`;

// ---- Values -------------------------------------------------------------------------------------------------------

/** The settings within a box by their names, as the webhook type names them. */
function parts(context: ActionEditorContext, box: string): Map<string, Within> {
  return new Map(context.within(box).map((part) => [part.setting.name, part]));
}

const textOf = (value: unknown) => (typeof value === 'string' ? value : '');

const isObject = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

/** The keys of the sensitive fields the action may send. */
function included(context: ActionEditorContext): string[] {
  const keys = context.value('includeSensitive');
  return Array.isArray(keys) ? keys.map(String) : [];
}

/** Sets a setting within a box. */
function put(context: ActionEditorContext, part: Within | undefined, value: unknown, done = true) {
  if (part) context.editor.set(part.setting.name, part.setting, { ok: true, value }, done);
}

/** Hands a typed text on as the server's rules for its setting read it, its length above all, or why it can't be kept. */
function typedText(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  path: string,
  raw: string,
  done: boolean,
) {
  context.editor.set(path, setting, parseSetting(setting, raw), done);
}

/** The settings of a chat's ways, which a host's lock on one draws as they are. */
const CHAT = new Set(['template', 'message']);

/** The settings What it sends draws within it. */
const BODY = ['format', 'withoutAnswers', 'metadata', 'template', 'message'] as const;

/** The values of What it sends, by their settings' names: the box's own, the mapping, and those within it. */
type BodyRecord = Partial<Record<(typeof BODY)[number] | 'fields', unknown>>;

/** The values of the settings within What it sends now. */
function bodyValues(within: Map<string, Within>): BodyRecord {
  return Object.fromEntries(BODY.map((name) => [name, within.get(name)?.value])) as BodyRecord;
}

// ---- Where it goes ------------------------------------------------------------------------------------------------

// The target, then the address within it, whose help says what it takes there. Another target keeps the address and
// What it sends of the one before while the sidebar is open, and brings back its own, or for a chat the preset's message.
function address(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const id = boxId(path);
  const target = targetOf(value);
  const url = parts(context, setting.name).get('url');
  const choose = (next: WebhookTarget) => {
    if (next === target) return;
    const body = parts(context, 'fields');
    const mapping = context.settingOf('fields');
    const signing = context.settingOf('secret');
    context.kept.set(`webhook:${target}`, {
      url: url?.value,
      fields: context.value('fields'),
      secret: context.value('secret'),
      ...bodyValues(body),
    });
    const back = context.kept.get(`webhook:${next}`) as Record<string, unknown> | undefined;
    const start: Record<string, unknown> = back ?? {
      message: isChat(next) ? context.presetMessage(next) : undefined,
    };
    context.forget('setting-url');
    context.forget('setting-fields');
    context.forget('setting-template');
    context.forget('setting-message');
    context.forget('setting-secret');
    context.kept.delete('rows:fields');
    context.editor.set(path, setting, { ok: true, value: next }, true);
    put(context, url, start['url']);
    if (mapping) context.editor.set(mapping.name, mapping, { ok: true, value: start['fields'] }, true);
    // A chat checks no signature: its secret is kept for its own address, and comes back with it.
    if (signing && !signing.lock) context.editor.set(signing.name, signing, { ok: true, value: start['secret'] }, true);
    for (const name of BODY) put(context, body.get(name), start[name]);
  };
  const own = foundAt(context.editor, id);
  return html`<div class="item within">
    <div class="item">
      <label class="label" for=${id}>${context.editor.say(setting.label)}</label>
      <select
        id=${id}
        aria-describedby=${own.ids.join(' ') || nothing}
        aria-invalid=${own.invalid ? 'true' : 'false'}
        .value=${live(target)}
        @change=${(event: Event) => choose(targetOf((event.target as HTMLSelectElement).value))}
      >
        ${setting.choices.map(
          (choice) =>
            html`<option value=${choice.value} ?selected=${choice.value === target}>
              ${context.editor.say(choice.label)}
            </option>`,
        )}
      </select>
      ${own.lines}
    </div>
    ${url ? addressBox(context, url, target) : nothing}
  </div>`;
}

// The address: an https address or a reference for its own; for a chat, empty for the site's, or a reference, as a
// chat's address holds its key. Within the target's box, or alone where a host locks the target.
function addressBox(context: ActionEditorContext, url: Within, target: WebhookTarget, alone = false) {
  if (url.setting.lock) return settingEditor(context.editor, url.setting, url.value, url.setting.name);
  const id = boxId(url.setting.name);
  const help = `${id}-help`;
  const keep = (raw: string, done: boolean) => {
    const text = raw.trim();
    const wrong =
      text.startsWith('$') && !isReference(text)
        ? context.words('referenceWrong')
        : isChat(target) && text !== '' && !isReference(text)
          ? context.words(`addressReference_${target}`)
          : null;
    if (wrong !== null) {
      if (done) context.problem(id, wrong);
      return;
    }
    typedText(context, url.setting, url.setting.name, raw, done);
  };
  const own = foundAt(context.editor, id);
  return html`<div class=${alone ? 'item' : 'item inner'}>
    <label class="label" for=${id}>${context.editor.say(url.setting.label)}</label>
    <p class="help" id=${help}>${context.words(`addressHelp_${target}`)}</p>
    <input
      type="text"
      id=${id}
      inputmode="url"
      autocomplete="off"
      spellcheck="false"
      aria-describedby=${[help, ...own.ids].join(' ')}
      aria-invalid=${own.invalid ? 'true' : 'false'}
      .value=${boxValue(textOf(url.value), context.editor.problems.has(id))}
      @input=${(event: Event) => keep((event.target as HTMLInputElement).value, false)}
      @change=${(event: Event) => keep((event.target as HTMLInputElement).value, true)}
    />
    ${own.lines}
  </div>`;
}

// ---- What it sends ------------------------------------------------------------------------------------------------

// The format, the ways of sending with what each means, the chosen way's own box, and the metadata where it applies.
// A host's lock on one of the ways' settings leaves the choice out: each is drawn as it is.
function body(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  const within = parts(context, setting.name);
  const target = targetOf(context.value('target'));
  const values = { ...bodyValues(within), fields: value };
  const mode = modeOf(target, values);
  const locked = ['template', 'withoutAnswers', 'message'].some((name) => within.get(name)?.setting.lock);
  const say = context.editor.say;
  const id = boxId(path);
  if (locked)
    return html`<div class="item within">
      <p class="label" id=${`${id}-label`}>${say(setting.label)}</p>
      ${isChat(target) ? nothing : html`<div class="mode">${rowsEditor(context, setting, value, path, MAPPING, `${id}-label`)}</div>`}
      ${[...within.values()]
        .filter((part) => (isChat(target) ? CHAT.has(part.setting.name) : part.setting.name !== 'message'))
        .map((part) => settingEditor(context.editor, part.setting, part.value, part.setting.name))}
    </div>`;
  const modes = MODES[isChat(target) ? 'chat' : 'own'];
  const choose = (next: BodyMode) => {
    if (next === mode) return;
    context.kept.set(`webhook-mode:${target}:${mode}`, values);
    const back = context.kept.get(`webhook-mode:${target}:${next}`) as Record<string, unknown> | undefined;
    context.forget(`${id}-`);
    context.forget('setting-template');
    context.forget('setting-message');
    // Leaving no answers, the metadata's switch comes back as it was before.
    const metadata = mode === 'none' ? { metadata: context.kept.get(`webhook-metadata:${target}`) } : {};
    switch (next) {
      case 'answers':
        settle(
          context,
          within,
          { fields: undefined, withoutAnswers: false, template: undefined, ...metadata },
          path,
          setting,
        );
        break;
      case 'mapping': {
        const fields =
          back?.['fields'] ??
          (isObject(value) ? value : objectOf(everyAnswer(context.fields, included(context)), mappingProblem));
        context.kept.delete('rows:fields');
        settle(context, within, { fields, withoutAnswers: false, template: undefined, ...metadata }, path, setting);
        break;
      }
      case 'none':
        // Without answers the metadata says what happened, so it goes; its switch comes back after.
        context.kept.set(`webhook-metadata:${target}`, values.metadata);
        settle(
          context,
          within,
          { fields: undefined, withoutAnswers: true, template: undefined, metadata: true },
          path,
          setting,
        );
        break;
      case 'body': {
        const rows = rowsOf(value).length > 0 ? rowsOf(value) : everyAnswer(context.fields, included(context));
        const message = textOf(within.get('message')?.value) || context.presetMessage(target) || '';
        const template =
          back?.['template'] ??
          (isChat(target) ? starterChatBody(target, message) : starterBody(formatOf(values.format), rows));
        settle(
          context,
          within,
          { fields: undefined, withoutAnswers: false, template, message: undefined },
          path,
          setting,
        );
        break;
      }
      case 'message':
        settle(
          context,
          within,
          { template: undefined, message: back?.['message'] ?? context.presetMessage(target) },
          path,
          setting,
        );
        break;
    }
  };
  const format = within.get('format');
  const metadata = within.get('metadata');
  const drawn = new Set<string>([
    ...(isChat(target) ? [] : ['format']),
    ...(mode === 'mapping' ? ['fields'] : []),
    ...(mode === 'body' ? ['template'] : []),
    ...(mode === 'message' ? ['message'] : []),
    ...(mode === 'answers' || mode === 'mapping' ? ['metadata'] : []),
    'withoutAnswers',
  ]);
  // A problem the check found at a setting this way does not draw is said under the choices, so none is lost.
  const elsewhere = [...within.keys(), setting.name]
    .filter((name) => !drawn.has(name))
    .map((name) => context.editor.found?.(boxId(name))?.lines ?? nothing);
  const none = boxId('withoutAnswers');
  const noneFound = foundAt(context.editor, none);
  return html`<fieldset class="item" id=${`${id}-box`}>
    <legend class="label">${say(setting.label)}</legend>
    ${!isChat(target) && format ? settingEditor(context.editor, format.setting, format.value, format.setting.name) : nothing}
    <div class="choices">
      ${modes.map(
        (each) =>
          html`<label class="choice">
              <input
                type="radio"
                name="webhook-body"
                value=${each}
                id=${each === 'none' ? none : nothing}
                aria-labelledby=${`webhook-body-${each}-name`}
                aria-describedby=${[`webhook-body-${each}`, ...(each === 'none' ? noneFound.ids : [])].join(' ')}
                .checked=${live(mode === each)}
                @change=${() => choose(each)}
              />
              <span class="choice-name" id=${`webhook-body-${each}-name`}>${context.words(`mode_${each}`)}</span>
              <span class="choice-line" id=${`webhook-body-${each}`}>${context.words(modeLine(each, target))}</span>
            </label>
            ${each === mode ? modeBox(context, setting, value, path, within, mode, target) : nothing}`,
      )}
    </div>
    ${noneFound.lines} ${elsewhere}
    ${(mode === 'answers' || mode === 'mapping') && metadata ? metadataSwitch(context, metadata) : nothing}
  </fieldset>`;
}

/** The chosen way's own box, under its choice: the table, the body or the message; none for the others. */
function modeBox(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
  path: string,
  within: Map<string, Within>,
  mode: BodyMode,
  target: WebhookTarget,
) {
  switch (mode) {
    case 'mapping':
      return html`<div class="mode">
        ${rowsEditor(context, setting, value, path, MAPPING, 'webhook-body-mapping-name')}
      </div>`;
    case 'body':
      return textBox(context, within.get('template'), 'body');
    case 'message':
      return textBox(context, within.get('message'), 'message', context.presetMessage(target));
    default:
      return nothing;
  }
}

/** The words under a way of sending: a body's and a message's say what they are written in. */
function modeLine(mode: BodyMode, target: WebhookTarget): string {
  if (mode === 'message') return `modeLine_message_${target}`;
  if (mode === 'body' && isChat(target)) return 'modeLine_body_chat';
  return `modeLine_${mode}`;
}

/** Writes a way's values: the fields at the box's own setting, the others within it; a value not given stays. */
function settle(
  context: ActionEditorContext,
  within: Map<string, Within>,
  values: Record<string, unknown>,
  path: string,
  setting: FieldSettingResponseModel,
) {
  for (const [name, value] of Object.entries(values)) {
    if (name === 'fields') context.editor.set(path, setting, { ok: true, value }, true);
    else put(context, within.get(name), value);
  }
}

// A body or a message to write, with Insert merge tag: a body that is cleared says so, as no body is no way of sending;
// an empty message sends the form's name and the answers.
function textBox(
  context: ActionEditorContext,
  part: Within | undefined,
  kind: 'body' | 'message',
  placeholder?: string | undefined,
) {
  if (!part) return nothing;
  if (part.setting.lock) return settingEditor(context.editor, part.setting, part.value, part.setting.name);
  const id = boxId(part.setting.name);
  const help = kind === 'message' ? `${id}-help` : undefined;
  const keep = (raw: string, done: boolean) => {
    if (kind === 'body' && raw.trim() === '') {
      if (done) context.problem(id, context.words('bodyEmpty'));
      return;
    }
    typedText(context, part.setting, part.setting.name, raw, done);
  };
  const own = foundAt(context.editor, id);
  return html`<div class="mode">
    <textarea
      id=${id}
      class=${kind === 'body' ? 'code body' : nothing}
      rows=${kind === 'body' ? 10 : 5}
      spellcheck=${kind === 'body' ? 'false' : nothing}
      aria-labelledby=${`webhook-body-${kind}-name`}
      placeholder=${placeholder ?? nothing}
      aria-describedby=${[help, ...own.ids].filter(Boolean).join(' ') || nothing}
      aria-invalid=${own.invalid ? 'true' : 'false'}
      .value=${boxValue(textOf(part.value), context.editor.problems.has(id))}
      @input=${(event: Event) => keep((event.target as HTMLTextAreaElement).value, false)}
      @change=${(event: Event) => keep((event.target as HTMLTextAreaElement).value, true)}
    ></textarea>
    ${help ? html`<p class="help" id=${help}>${context.words('messageHelp')}</p>` : nothing} ${own.lines}
    ${context.tags(() => id)}
  </div>`;
}

// Send metadata too, with what it adds.
function metadataSwitch(context: ActionEditorContext, metadata: Within) {
  if (metadata.setting.lock)
    return settingEditor(context.editor, metadata.setting, metadata.value, metadata.setting.name);
  const id = boxId(metadata.setting.name);
  const own = foundAt(context.editor, id);
  return html`<div class="item">
    <div class="item-switch">
      <input
        type="checkbox"
        role="switch"
        id=${id}
        aria-describedby=${[`${id}-help`, ...own.ids].join(' ')}
        .checked=${live(metadata.value !== false)}
        @change=${(event: Event) => put(context, metadata, (event.target as HTMLInputElement).checked)}
      />
      <label class="label" for=${id}>${context.editor.say(metadata.setting.label)}</label>
    </div>
    <p class="help" id=${`${id}-help`}>${context.words('metadataHelp')}</p>
    ${own.lines}
  </div>`;
}

// ---- Rows of names and values -------------------------------------------------------------------------------------

/** What a table of rows is: its words, its rule, and whether its values take merge tags. */
interface RowKind {
  readonly key: string;
  readonly words: string;
  readonly problem: (rows: readonly Row[], at: number, done: boolean) => RowProblem | null;
  readonly tags: boolean;
}

const MAPPING: RowKind = { key: 'fields', words: 'pair', problem: mappingProblem, tags: true };
const HEADERS: RowKind = { key: 'headers', words: 'header', problem: headerProblem, tags: false };

/** The rows a table shows as they are typed, and the setting's value they were last in step with. */
interface Typed {
  readonly rows: Row[];
  readonly from: string;
}

/** The rows a table shows: those typed while the sidebar is open, while the setting holds what they last wrote. */
function rowsIn(context: ActionEditorContext, kind: RowKind, value: unknown): Row[] {
  const now = JSON.stringify(rowsOf(value));
  const typed = context.kept.get(`rows:${kind.key}`) as Typed | undefined;
  if (typed && typed.from === now) return typed.rows;
  const rows = rowsOf(value);
  context.kept.set(`rows:${kind.key}`, { rows, from: now });
  return rows;
}

// A row's problem in words, under the box it is at.
function rowWords(context: ActionEditorContext, kind: RowKind, problem: RowProblem, row: Row): string {
  return context.words(`${kind.words}_${problem.at}_${problem.kind}`, row.name);
}

// The names and values, each row with its remove button, then Add, and for a mapping Insert merge tag, which puts a tag
// into the value last in focus. What can't be kept of a row is said under it as it is typed, a row without its name once
// it is left, and the setting holds the rows that can be kept.
function rowsEditor(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
  path: string,
  kind: RowKind,
  labelledBy = `${boxId(path)}-label`,
): TemplateResult {
  const id = boxId(path);
  const words = (key: string, ...args: unknown[]) => context.words(`${kind.words}${key}`, ...args);
  const rows = rowsIn(context, kind, value);
  const nameId = (at: number) => `${id}-name-${at}`;
  const valueId = (at: number) => `${id}-value-${at}`;
  const write = (next: Row[], done: boolean) => {
    const kept = objectOf(next, kind.problem);
    const written = Object.keys(kept).length === 0 && kind.key === 'headers' ? undefined : kept;
    context.kept.set(`rows:${kind.key}`, {
      rows: next,
      from: done ? JSON.stringify(rowsOf(written)) : JSON.stringify(rowsOf(value)),
    });
    const shown = new Set([...context.editor.problems.keys()]);
    context.forget(`${id}-`);
    next.forEach((row, at) => {
      const problem = kind.problem(next, at, done || shown.has(nameId(at)));
      if (problem)
        context.problem(problem.at === 'name' ? nameId(at) : valueId(at), rowWords(context, kind, problem, row));
    });
    // What is typed reaches the setting once its box is left (`done`); a credential's row is no row while its name is
    // half typed.
    if (done) context.editor.set(path, setting, { ok: true, value: written }, true);
    else context.redraw();
  };
  const change = (at: number, row: Row, done: boolean) =>
    write(
      rows.map((each, i) => (i === at ? row : each)),
      done,
    );
  const own = foundAt(context.editor, id);
  const help = setting.description && kind.key === 'headers' ? `${id}-help` : undefined;
  const last = () => (context.kept.get(`last:${kind.key}`) as string | undefined) ?? valueId(rows.length - 1);
  const table = html`<div
    class="pairs"
    id=${id}
    role="group"
    aria-labelledby=${labelledBy}
    aria-describedby=${[help, ...own.ids].filter(Boolean).join(' ') || nothing}
  >
    ${
      rows.length > 0
        ? html`<div class="pairs-head" aria-hidden="true">
            <span>${words('Name')}</span><span>${words('Value')}</span>
          </div>`
        : nothing
    }
    ${rows.map((row, at) => {
      const label = row.name || context.words('rowNumber', at + 1);
      const nameWhy = context.editor.problems.has(nameId(at));
      const valueWhy = context.editor.problems.has(valueId(at));
      return html`<div class="pair-row">
        <input
          type="text"
          id=${nameId(at)}
          class="name"
          autocomplete="off"
          spellcheck="false"
          aria-label=${words('NameOf', at + 1)}
          placeholder=${words('Name')}
          aria-describedby=${nameWhy ? `${nameId(at)}-why` : nothing}
          aria-invalid=${nameWhy ? 'true' : 'false'}
          .value=${live(row.name)}
          @input=${(event: Event) => change(at, { ...row, name: (event.target as HTMLInputElement).value }, false)}
          @change=${(event: Event) => change(at, { ...row, name: (event.target as HTMLInputElement).value.trim() }, true)}
        />
        <input
          type="text"
          id=${valueId(at)}
          class="value"
          autocomplete="off"
          spellcheck="false"
          aria-label=${words('ValueOf', label)}
          placeholder=${words('Value')}
          aria-describedby=${valueWhy ? `${valueId(at)}-why` : nothing}
          aria-invalid=${valueWhy ? 'true' : 'false'}
          .value=${live(row.value)}
          @focus=${() => context.kept.set(`last:${kind.key}`, valueId(at))}
          @input=${(event: Event) => change(at, { ...row, value: (event.target as HTMLInputElement).value }, false)}
          @change=${(event: Event) => change(at, { ...row, value: (event.target as HTMLInputElement).value }, true)}
        />
        <button
          type="button"
          class="remove"
          aria-label=${words('Remove', label)}
          @click=${() => {
            context.kept.delete(`last:${kind.key}`);
            write(
              rows.filter((_, i) => i !== at),
              true,
            );
            context.focus(rows.length > 1 ? nameId(Math.min(at, rows.length - 2)) : `${id}-add`);
          }}
        >
          <uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
        </button>
        ${typedLine(context.editor, nameId(at))} ${typedLine(context.editor, valueId(at))}
      </div>`;
    })}
    ${rows.length === 0 && kind.key === 'fields' ? html`<p class="help">${words('sNone')}</p>` : nothing}
    <button
      type="button"
      class="add-kind"
      id=${`${id}-add`}
      @click=${() => {
        // A blank row holds nothing the setting keeps: the sidebar draws it anew to show it.
        context.kept.set(`rows:${kind.key}`, {
          rows: [...rows, { name: '', value: '' }],
          from: JSON.stringify(rowsOf(value)),
        });
        context.redraw();
        context.focus(nameId(rows.length));
      }}
    >
      <uui-icon name="icon-add" aria-hidden="true"></uui-icon>${words('Add')}
    </button>
    ${kind.tags && rows.length > 0 ? context.tags(last) : nothing} ${own.lines}
  </div>`;
  if (kind.key === 'fields') return table;
  return html`<div class="item">
    <p class="label" id=${`${id}-label`}>${context.editor.say(setting.label)}</p>
    ${help ? html`<p class="help" id=${help}>${context.editor.say(setting.description)}</p>` : nothing} ${table}
  </div>`;
}

// ---- The signing secret -------------------------------------------------------------------------------------------

// A reference to the host's secret, never the secret itself, which would be saved with the form.
function secret(context: ActionEditorContext, setting: FieldSettingResponseModel, value: unknown, path: string) {
  if (isChat(targetOf(context.value('target')))) return nothing;
  const id = boxId(path);
  const keep = (raw: string, done: boolean) => {
    const text = raw.trim();
    if (text !== '' && !isReference(text)) {
      if (done) context.problem(id, context.words('secretReference'));
      return;
    }
    typedText(context, setting, path, raw, done);
  };
  return settingFrame(
    context.editor,
    setting,
    path,
    (described, invalid, held) =>
      html`<input
        type="text"
        id=${id}
        autocomplete="off"
        spellcheck="false"
        aria-describedby=${described ?? nothing}
        aria-invalid=${invalid ? 'true' : 'false'}
        .value=${boxValue(textOf(value), held)}
        @input=${(event: Event) => keep((event.target as HTMLInputElement).value, false)}
        @change=${(event: Event) => keep((event.target as HTMLInputElement).value, true)}
      />`,
  );
}

// ---- Read-only ----------------------------------------------------------------------------------------------------

/** A webhook setting's value in words, for who may not change it; undefined for a setting of another editor. */
export function webhookSettingText(
  context: ActionEditorContext,
  setting: FieldSettingResponseModel,
  value: unknown,
): string | undefined {
  const say = context.editor.say;
  // A value where only a reference belongs is a secret written as itself: it is said to be there, never shown.
  const shown = (text: string, secret: boolean) =>
    secret && text !== '' && !isReference(text) ? context.words('secretHidden') : text;
  const choice = (part: FieldSettingResponseModel | undefined, chosen: unknown) =>
    say(part?.choices.find((each) => each.value === chosen)?.label ?? '');
  switch (setting.editor) {
    case 'webhookAddress': {
      const url = textOf(parts(context, setting.name).get('url')?.value);
      return [choice(setting, targetOf(value)), shown(url, isChat(targetOf(value)))].filter(Boolean).join('\n');
    }
    case 'webhookBody': {
      const within = parts(context, setting.name);
      const target = targetOf(context.value('target'));
      const values = { ...bodyValues(within), fields: value };
      const mode = modeOf(target, values);
      if (mode === 'message') return textOf(values.message) || context.words('sends_message');
      if (mode === 'body' && isChat(target)) return context.words('mode_body');
      const what =
        mode === 'body'
          ? context.words('sends_body')
          : mode === 'mapping'
            ? rowsOf(value)
                .map((row) => row.name)
                .join(', ') || context.words('sends_answers')
            : context.words(`sends_${mode}`);
      const format = choice(within.get('format')?.setting, formatOf(values.format));
      const withMetadata = (mode === 'answers' || mode === 'mapping') && values.metadata !== false;
      return context.words('sends', format, withMetadata ? context.words('sends_withMetadata', what) : what);
    }
    case 'headerMap':
      return rowsOf(value)
        .map((row) => `${row.name}: ${shown(row.value, holdsASecret(row.name))}`)
        .join('\n');
    case 'secretReference':
      return shown(textOf(value), true);
    default:
      return undefined;
  }
}
