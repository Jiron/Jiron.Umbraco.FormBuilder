// The picked rows the form picker and its settings share (ADR-0134, ADR-0135): Umbraco's reference rows by name, each
// with Remove, rows in their final shape while the names load, a folder or form the endpoint left out marked as not
// available in the look Umbraco gives an item it cannot read, and a failed lookup said once, with Try again. The
// lookup asks only for the keys it does not know yet, and drops the answer to an older one.
import {
  css,
  html,
  nothing,
  repeat,
  type ReactiveControllerHost,
  type TemplateResult,
} from '@umbraco-cms/backoffice/external/lit';
import type { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { PickerItem } from './picker-data';

/** Reads the items of these keys, in the order asked, leaving out those it may not show; undefined on a failure. */
export type ReadItems = (keys: readonly string[]) => Promise<PickerItem[] | undefined>;

/** The icon and the texts of a kind of row: a form's or a folder's. */
export interface PickedKind {
  icon: string;
  /** Whether the row shows a form's alias below its name. */
  alias: boolean;
  notAvailable: string;
  notAvailableDetail: string;
  removeNotAvailable: string;
}

/** The rows of picked forms. */
export const FORM_ROWS: PickedKind = {
  icon: 'icon-document',
  alias: true,
  notAvailable: 'jfbFormPicker_notAvailable',
  notAvailableDetail: 'jfbFormPicker_notAvailableDetail',
  removeNotAvailable: 'jfbFormPicker_removeNotAvailable',
};

/** The rows of allowed folders. */
export const FOLDER_ROWS: PickedKind = {
  icon: 'icon-folder',
  alias: false,
  notAvailable: 'jfbFormPicker_folderNotAvailable',
  notAvailableDetail: 'jfbFormPicker_folderNotAvailableDetail',
  removeNotAvailable: 'jfbFormPicker_removeFolderNotAvailable',
};

/** The picked items' names, looked up when the host's keys change; the host renders them and asks after each render. */
export class PickedLookup {
  /** The items the endpoint answered with, by key; null for one it left out. */
  found: ReadonlyMap<string, PickerItem | null> = new Map();
  /** Whether the last lookup failed. */
  failed = false;

  readonly #host: ReactiveControllerHost;
  /** What the keys were last looked up within, such as a data type; another scope looks every key up again. */
  #scope: string | undefined = undefined;
  #keys: string | undefined = undefined;
  /** Counts the lookups, so that an answer to an older one is dropped. */
  #id = 0;

  constructor(host: ReactiveControllerHost) {
    this.#host = host;
  }

  /**
   * Looks up the keys not known yet, once for each set of keys.
   * @param scope What the answer depends on besides the keys; a new one forgets every name.
   * @param keys The picked keys, in order.
   * @param read The endpoint's read.
   */
  sync(scope: string, keys: readonly string[], read: ReadItems) {
    const joined = keys.join(',');
    if (scope === this.#scope && joined === this.#keys) return;
    if (scope !== this.#scope) this.found = new Map();
    this.#scope = scope;
    this.#keys = joined;
    void this.#lookUp(keys, read);
  }

  /** Looks the missing keys up again on the host's next render. */
  retry() {
    this.#keys = undefined;
    this.#host.requestUpdate();
  }

  /** Keeps items the host learnt elsewhere, such as the sidebar's answer. */
  remember(items: readonly PickerItem[]) {
    const found = new Map(this.found);
    for (const item of items) found.set(item.key, item);
    this.found = found;
    this.#host.requestUpdate();
  }

  async #lookUp(keys: readonly string[], read: ReadItems) {
    const id = ++this.#id;
    const missing = keys.filter((key) => !this.found.has(key));
    if (missing.length === 0) {
      if (this.failed) {
        this.failed = false;
        this.#host.requestUpdate();
      }
      return;
    }
    const items = await read(missing);
    if (id !== this.#id) return;
    this.failed = items === undefined;
    if (items) {
      const found = new Map(this.found);
      for (const key of missing) found.set(key, items.find((item) => item.key === key) ?? null);
      this.found = found;
    }
    this.#host.requestUpdate();
  }
}

/** What the rows show and do. */
export interface PickedRows {
  host: UmbLitElement;
  keys: readonly string[];
  lookup: PickedLookup;
  kind: PickedKind;
  /** Whether a single row stands alone, as Umbraco shows a picker's one item. */
  standalone: boolean;
  readonly: boolean;
  remove: (key: string) => void;
}

/** The picked items as reference rows, or nothing when none is picked. */
export function renderPickedRows(rows: PickedRows): TemplateResult | typeof nothing {
  if (rows.keys.length === 0) return nothing;
  return html`<uui-ref-list>
    ${repeat(
      rows.keys,
      (key) => key,
      (key) => renderRow(rows, key),
    )}
  </uui-ref-list>`;
}

function renderRow(rows: PickedRows, key: string) {
  const { host, kind, lookup, standalone } = rows;
  const item = lookup.found.get(key);
  if (item === undefined) {
    return lookup.failed
      ? nothing
      : html`<div class="placeholder" aria-hidden="true">
          <span class="block"></span>
          <span class="lines">
            <span class="line"><span class="bar"></span></span>
            <span class="line detail"><span class="bar short"></span></span>
          </span>
        </div>`;
  }
  if (item === null) {
    return html`<uui-ref-node
      readonly
      ?standalone=${standalone}
      class="not-available"
      detail=${host.localize.term(kind.notAvailableDetail)}
    >
      <umb-icon slot="icon" name="icon-alert"></umb-icon>
      <span slot="name">${host.localize.term(kind.notAvailable)}</span>
      ${renderRemove(rows, key, host.localize.term(kind.removeNotAvailable))}
    </uui-ref-node>`;
  }
  return html`<uui-ref-node
    readonly
    ?standalone=${standalone}
    name=${item.name}
    detail=${kind.alias ? (item.alias ?? '') : ''}
  >
    <umb-icon slot="icon" name=${kind.icon}></umb-icon>
    ${renderRemove(rows, key, host.localize.term('jfbFormPicker_remove', item.name))}
  </uui-ref-node>`;
}

function renderRemove(rows: PickedRows, key: string, label: string) {
  if (rows.readonly) return nothing;
  return html`<uui-action-bar slot="actions">
    <uui-button class="remove" label=${label} @click=${() => rows.remove(key)}
      >${rows.host.localize.term('general_remove')}</uui-button
    >
  </uui-action-bar>`;
}

/** A failed lookup, said once below the rows, with Try again. */
export function renderPickedFailure(host: UmbLitElement, lookup: PickedLookup, message: string) {
  if (!lookup.failed) return nothing;
  return html`<div class="failure" role="alert">
    <span>${host.localize.term(message)}</span>
    <uui-button
      look="secondary"
      compact
      label=${host.localize.term('jfbFormPicker_tryAgain')}
      @click=${() => lookup.retry()}
    ></uui-button>
  </div>`;
}

/** The dashed Choose below the rows. */
export function renderChoose(host: UmbLitElement, choose: () => void) {
  return html`<uui-button
    id="choose"
    look="placeholder"
    label=${host.localize.term('general_choose')}
    @click=${choose}
  ></uui-button>`;
}

/**
 * Puts the focus where a removed row's Remove was once the host has rendered without it: on the next row's Remove, or
 * the one before, or Choose when no row is left.
 * @param host The element whose rows these are.
 * @param index Where the removed row stood.
 */
export async function focusAfterRemove(host: UmbLitElement, index: number) {
  await host.updateComplete;
  const removes = [...(host.shadowRoot?.querySelectorAll<HTMLElement>('.remove') ?? [])];
  const next = removes[Math.min(index, removes.length - 1)] ?? host.shadowRoot?.querySelector<HTMLElement>('#choose');
  next?.focus();
}

export const pickedRowsStyles = css`
  :host {
    display: block;
  }

  #choose {
    display: block;
  }

  uui-ref-list:not(:empty) + #choose,
  .failure + #choose {
    margin-top: var(--uui-size-space-2);
  }

  /* The name and the icon carry the state; the reason stays in the detail's own colour, which keeps its contrast. */
  .not-available umb-icon,
  .not-available [slot='name'] {
    color: var(--jfb-bo-color-danger-text);
  }

  /* The shape of uui-ref-node: a pixel around a padded box with a line of name and a line of detail. */
  .placeholder {
    position: relative;
    display: flex;
    align-items: center;
    gap: var(--uui-size-space-3);
    padding: calc(var(--uui-size-space-3) + 1px);
  }

  .block,
  .bar {
    display: block;
    border-radius: var(--uui-border-radius);
    background-color: var(--uui-color-surface-alt);
  }

  .block {
    flex: none;
    width: 1.25em;
    height: 1.25em;
  }

  .lines {
    display: flex;
    flex: 1;
    flex-direction: column;
  }

  .line {
    display: flex;
    align-items: center;
    height: 1.2em;
  }

  .line.detail {
    height: calc(1.2 * var(--uui-type-small-size));
  }

  .bar {
    width: 40%;
    height: 0.75em;
  }

  .bar.short {
    width: 25%;
  }

  .failure {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-3);
    padding-block: var(--uui-size-space-2);
    color: var(--jfb-bo-color-danger-text);
  }
`;
