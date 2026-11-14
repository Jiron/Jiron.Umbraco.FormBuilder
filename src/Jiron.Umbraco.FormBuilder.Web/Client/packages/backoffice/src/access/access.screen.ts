// Settings > Access (ROADMAP 12.4a, IA 8, ADR-0013, ADR-0218): what every user group may do with every form, as a
// matrix of the groups and the permissions in three parts, the administrators ticked and fixed, with one line on why;
// a group that can't open the section yet named once with a link to its user group, where the section is added; and
// below it, the folders and forms with access of their own, each with one sentence on what is special about it and
// opening its Permissions sidebar, which Add folder or form... opens for another. A box ticked brings the permission it
// needs, and one unticked takes those that need it, each said once to screen readers. Save in the footer saves the
// changed groups in one unit of work; a change made meanwhile is said, with the grants now saved.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_TREE_PICKER_MODAL, type UmbTreeItemModel } from '@umbraco-cms/backoffice/tree';
import { UMB_EDIT_USER_GROUP_WORKSPACE_PATH_PATTERN } from '@umbraco-cms/backoffice/user-group';
import type { AccessItemResponseModel, AccessResponseModel } from '../api';
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { LIBRARY_TREE_ALIAS } from '../tree/aliases';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readAccess, readTarget, saveAccess, type AccessTarget } from './access-api';
import { accessMatrix, matrixStyles, type MatrixPart } from './access-matrix';
import { changedGrants, type Grants, grantsOf, sentenceOf, withPending, withPermission } from './access-model';
import { loadAccessWords } from './access-words';
import { LeaveGuard } from '../ui/leave-guard';
import { PERMISSIONS_MODAL } from './permissions.modal.token';
import { jfbPhoneColumn } from '../styles/phone';

/** What the screen says once, at its top, about the last save. */
type Alert = '' | 'conflict' | 'lockedNow' | 'invalid';

export class AccessScreenElement extends UmbLitElement {
  static override properties = {
    _answer: { state: true },
    _failed: { state: true },
    _grants: { state: true },
    _saving: { state: true },
    _alert: { state: true },
    _said: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    matrixStyles,
    css`
      :host {
        display: block;
        block-size: 100%;
      }

      /* One column no wider than the page, so a matrix of many groups scrolls inside its box. */
      .page {
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        gap: var(--uui-size-space-5);
        max-inline-size: 960px;
        margin-inline: auto;
      }

      /* Umbraco's link colour, as the Overview's links: the browser's own blue is 1.4:1 on the dark theme's box. */
      a {
        color: var(--uui-color-interactive);
        text-decoration: underline;
      }

      a:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .lead,
      .note,
      .alert {
        margin: 0 0 var(--uui-size-space-4);
      }

      .lead,
      .note {
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      .groups-without {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1) var(--uui-size-space-3);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .items {
        display: grid;
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .items li {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        column-gap: var(--uui-size-space-3);
        align-items: center;
        padding: var(--uui-size-space-3) 0;
        border-block-end: 1px solid var(--uui-color-divider);
      }

      .items li:last-child {
        border-block-end: 0;
      }

      .item-name {
        justify-self: start;
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        font-weight: 600;
        text-align: start;
        cursor: pointer;
      }

      .item-name:hover {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      .item-name:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .item-line {
        grid-column: 2;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }
    `,
    jfbPhoneColumn,
  ];

  declare private _answer: AccessResponseModel | undefined;
  /** Why access couldn't be read: no answer, which Try again may fix, or a missing permission, which it won't. */
  declare private _failed: false | 'failed' | 'forbidden';
  /** Each group's global grant as the boxes hold it, changes not saved among them. */
  declare private _grants: Grants;
  declare private _saving: boolean;
  declare private _alert: Alert;
  /** What a box ticked or unticked changed besides itself, said to screen readers. */
  declare private _said: string;

  constructor() {
    super();
    // Ticks not saved yet are asked about before the backoffice leaves the screen.
    new LeaveGuard(
      this,
      () =>
        this._answer !== undefined &&
        changedGrants(grantsOf(this._answer.groups), this._grants, this.#order()).length > 0,
    );
    this._answer = undefined;
    this._failed = false;
    this._grants = new Map();
    this._saving = false;
    this._alert = '';
    this._said = '';
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #words = (key: string, ...args: unknown[]) => this.localize.term(`jfbAccess_${key}`, ...args);

  #permission = (name: string) => this.localize.term(`jfbLibrary_permission_${name}`);

  // ---- Reading and saving -------------------------------------------------------------------------------------------

  async #read() {
    this._failed = false;
    const [words, answer] = await Promise.all([
      loadAccessWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readAccess(this),
    ]);
    if (!words || answer === undefined || answer === 'forbidden') {
      this._failed = answer === 'forbidden' ? 'forbidden' : 'failed';
      return;
    }
    this.#show(answer);
  }

  #show(answer: AccessResponseModel) {
    this._answer = answer;
    this._grants = grantsOf(answer.groups);
  }

  #order(): string[] {
    return (this._answer?.parts ?? []).flatMap((part) => part.permissions);
  }

  async #save() {
    const answer = this._answer;
    if (!answer || this._saving) return;
    this._alert = '';
    this._saving = true;
    const result = await saveAccess(this, changedGrants(grantsOf(answer.groups), this._grants, this.#order()));
    this._saving = false;
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    switch (result.kind) {
      case 'saved':
        this.#show(result.answer);
        notifications?.peek('positive', { data: { message: this.#words('saved') } });
        return;
      case 'conflict':
        this._alert = 'conflict';
        await this.#read();
        return;
      case 'locked':
        this._alert = 'lockedNow';
        await this.#read();
        return;
      case 'invalid':
        this._alert = 'invalid';
        return;
      default:
        notifications?.peek('danger', { data: { message: this.#words('saveFailed') } });
    }
  }

  #toggle(group: string, permission: string, on: boolean) {
    const needs = this._answer?.needs ?? [];
    const { granted, besides } = withPermission(this._grants.get(group) ?? new Set(), permission, on, needs);
    const next = new Map(this._grants);
    next.set(group, granted);
    this._grants = next;
    this._said =
      besides.length === 0
        ? ''
        : this.#words(on ? 'alsoOn' : 'alsoOff', this.#permission(permission), besides.map(this.#permission));
  }

  /** Opens a folder's or form's Permissions sidebar; the list is read again once it saved. */
  async #open(target: AccessTarget, key: string) {
    const value = await umbOpenModal(this, PERMISSIONS_MODAL, { data: { target, key } }).catch(() => undefined);
    if (!value?.saved) return;
    // The ticks not saved yet stay as they stand: the sidebar saved who sees one folder or form, not the grants.
    const before = this._answer;
    const mine = this._grants;
    await this.#read();
    if (before && this._answer && !this._failed)
      this._grants = withPending(grantsOf(before.groups), mine, grantsOf(this._answer.groups));
  }

  /** Picks a folder or form from the library and opens its sidebar. */
  async #add() {
    const { selection } = await umbOpenModal(this, UMB_TREE_PICKER_MODAL, {
      data: {
        treeAlias: LIBRARY_TREE_ALIAS,
        hideTreeRoot: true,
        pickableFilter: (item: UmbTreeItemModel) =>
          item.entityType === FOLDER_ENTITY_TYPE || item.entityType === FORM_ENTITY_TYPE,
      },
    }).catch(() => ({ selection: [] as (string | null)[] }));
    const key = selection[0];
    if (!key) return;
    // The picker answers the key alone; a folder's read answers for a folder.
    const target: AccessTarget = (await readTarget(this, 'folder', key)) === 'gone' ? 'form' : 'folder';
    await this.#open(target, key);
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #parts(answer: AccessResponseModel): MatrixPart[] {
    return answer.parts.map((part) => ({
      label: this.#words(`part_${part.alias}`),
      permissions: part.permissions.map((name) => ({ name, label: this.#permission(name) })),
    }));
  }

  #withoutSection(answer: AccessResponseModel): TemplateResult | typeof nothing {
    const groups = answer.groups.filter((group) => !group.hasSection && !group.isAdministrators);
    if (groups.length === 0) return nothing;
    return html`<p class="note">${this.#words('noSection', groups.length)}</p>
      <ul class="groups-without note">
        ${groups.map(
          (group) =>
            html`<li>
              <a href=${UMB_EDIT_USER_GROUP_WORKSPACE_PATH_PATTERN.generateAbsolute({ unique: group.key })}
                >${group.name}</a
              >
            </li>`,
        )}
      </ul>`;
  }

  #item(item: AccessItemResponseModel, nameOf: (group: string) => string): TemplateResult {
    const sentence = sentenceOf(item, nameOf);
    const line =
      sentence.kind === 'only'
        ? this.#words('only', sentence.groups)
        : sentence.kind === 'onlyAdministrators'
          ? this.#words('onlyAdministrators')
          : sentence.groups.length === 0
            ? this.#words('alsoNone')
            : this.#words('also', sentence.groups);
    const target = item.type === 'form' ? 'form' : 'folder';
    const id = `item-${item.key}`;
    return html`<li>
      <uui-icon name=${target === 'form' ? 'icon-document' : 'icon-folder'} aria-hidden="true"></uui-icon>
      <button
        type="button"
        class="item-name"
        aria-describedby=${`${id}-line`}
        aria-label=${this.#words('open', item.name)}
        @click=${() => void this.#open(target, item.key)}
      >
        ${item.name}
      </button>
      <span class="item-line" id=${`${id}-line`}>${line}</span>
    </li>`;
  }

  #body(): TemplateResult {
    if (this._failed === 'forbidden')
      return html`<uui-box>
        <p class="lead" role="alert">
          ${this.localize.term(
            'jfbLibrary_missingPermission',
            this.localize.term('jfbLibrary_permission_managePermissions'),
          )}
        </p>
      </uui-box>`;
    if (this._failed)
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.#words('failed')}</span>
          <uui-button look="outline" label=${this.#words('retry')} @click=${() => void this.#read()}></uui-button>
        </div>
      </uui-box>`;
    const answer = this._answer;
    if (!answer) return html`<uui-box>${skeletonLines(this.#words('loading'), [64, 48, 56])}</uui-box>`;
    const names = new Map(answer.groups.map((group) => [group.key, group.name]));
    const nameOf = (group: string) => names.get(group) ?? group;
    const alert =
      this._alert === 'conflict'
        ? this.#words('conflict')
        : this._alert === 'lockedNow'
          ? this.#words('lockedNow')
          : this._alert === 'invalid'
            ? this.#words('invalid')
            : '';
    const lock = answer.lock;
    return html`${alert ? html`<p class="alert" role="alert">${alert}</p>` : nothing}
      <uui-box headline-variant="h2" headline=${this.#words('everyForm')}>
        <p class="note">${this.#words('administrators')}</p>
        ${this.#withoutSection(answer)}
        ${
          lock
            ? html`<p class="note" role="note">
                ${lock.field === '*' ? this.#words('lockedAll', lock.configurationKey) : this.#words('lockedGrants', lock.configurationKey)}
              </p>`
            : nothing
        }
        ${accessMatrix({
          id: 'matrix',
          caption: this.#words('matrixCaption'),
          permissionHeader: this.#words('permission'),
          columns: answer.groups.map((group) => ({ key: group.key, name: group.name, fixed: group.isAdministrators })),
          parts: this.#parts(answer),
          granted: (group, permission) => this._grants.get(group)?.has(permission) ?? false,
          readOnly: lock !== null && lock !== undefined,
          toggle: (group, permission, on) => this.#toggle(group, permission, on),
          cellLabel: (permission, group) => this.#words('cell', permission, group),
        })}
      </uui-box>
      <uui-box headline-variant="h2" headline=${this.#words('items')}>
        ${
          lock?.field === '*'
            ? nothing
            : html`<uui-button
                slot="header-actions"
                look="outline"
                label=${this.#words('addItem')}
                @click=${() => void this.#add()}
              ></uui-button>`
        }
        ${
          answer.items.length === 0
            ? html`<p class="note">${this.#words('itemsNone')}</p>`
            : html`<ul class="items">
                ${answer.items.map((item) => this.#item(item, nameOf))}
              </ul>`
        }
      </uui-box>`;
  }

  override render() {
    const savable = this._answer !== undefined && !this._failed && !this._answer.lock;
    return html`<umb-body-layout>
      <div class="page">${this.#body()}</div>
      <p class="visually-hidden" role="status" aria-live="polite">${this._said}</p>
      ${
        savable
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.#words('save')}
              .state=${this._saving ? 'waiting' : undefined}
              @click=${() => void this.#save()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-access-screen')) customElements.define('jfb-access-screen', AccessScreenElement);

export { AccessScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-access-screen': AccessScreenElement;
  }
}
