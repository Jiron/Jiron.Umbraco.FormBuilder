// A folder's or form's Permissions sidebar (ROADMAP 12.4a, IA 8, ADR-0013, ADR-0218): "Only these groups", with one
// sentence on what restricting does; the groups with access of their own here, as a matrix of their grants that a group
// is added to and taken off; and Save, which saves the grants and the restriction in one unit of work. Restricting with
// no group asks first, as only administrators would see the forms then. A box ticked brings the permission it needs, and
// one unticked takes those that need it. A folder or form in the Recycle Bin, or one the host locks, reads only.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { AccessTargetResponseModel } from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { confirmText } from '../ui/confirm';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readTarget, saveTarget } from './access-api';
import { accessMatrix, matrixStyles } from './access-matrix';
import { changedGrants, grantsOf, withPermission, type Grants } from './access-model';
import { loadAccessWords } from './access-words';
import type { PermissionsModalData, PermissionsModalValue } from './permissions.modal.token';

/** What the sidebar says once, at its top. */
type Alert = '' | 'conflict' | 'lockedNow' | 'invalid' | 'trashed' | 'gone';

export class PermissionsModalElement extends UmbModalBaseElement<PermissionsModalData, PermissionsModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _target: { state: true },
    _failed: { state: true },
    _grants: { state: true },
    _shown: { state: true },
    _restricted: { state: true },
    _saving: { state: true },
    _alert: { state: true },
    _said: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    controlStyles,
    matrixStyles,
    css`
      /* One column no wider than the sidebar, so a matrix of many groups scrolls inside it. */
      .body {
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        gap: var(--uui-size-space-5);
      }

      h3 {
        margin: 0;
        font-size: var(--uui-type-h5-size);
      }

      .note,
      .alert {
        margin: 0;
      }

      .note {
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      .add {
        max-inline-size: 22rem;
      }
    `,
  ];

  declare private _target: AccessTargetResponseModel | undefined;
  declare private _failed: boolean;
  /** Each group's own grant here as the boxes hold it, changes not saved among them. */
  declare private _grants: Grants;
  /** The groups the matrix shows, in their order: those with a grant here, then those added. */
  declare private _shown: readonly string[];
  declare private _restricted: boolean;
  declare private _saving: boolean;
  declare private _alert: Alert;
  declare private _said: string;

  constructor() {
    super();
    this._target = undefined;
    this._failed = false;
    this._grants = new Map();
    this._shown = [];
    this._restricted = false;
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

  async #read() {
    const data = this.data;
    if (!data) return;
    this._failed = false;
    const [words, target] = await Promise.all([
      loadAccessWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readTarget(this, data.target, data.key),
    ]);
    if (!words || target === undefined) {
      this._failed = true;
      return;
    }
    if (target === 'gone') {
      this._alert = 'gone';
      return;
    }
    this.#show(target);
  }

  #show(target: AccessTargetResponseModel) {
    this._target = target;
    this._grants = grantsOf(target.groups);
    this._shown = target.groups.filter((group) => group.permissions.length > 0).map((group) => group.key);
    this._restricted = target.isRestricted;
  }

  #toggle(group: string, permission: string, on: boolean) {
    const { granted, besides } = withPermission(
      this._grants.get(group) ?? new Set(),
      permission,
      on,
      this._target?.needs ?? [],
    );
    const next = new Map(this._grants);
    next.set(group, granted);
    this._grants = next;
    this._said =
      besides.length === 0
        ? ''
        : this.#words(on ? 'alsoOn' : 'alsoOff', this.#permission(permission), besides.map(this.#permission));
  }

  #addGroup(key: string) {
    if (!key || this._shown.includes(key)) return;
    this._shown = [...this._shown, key];
  }

  #removeGroup(key: string) {
    this._shown = this._shown.filter((shown) => shown !== key);
    const next = new Map(this._grants);
    next.set(key, new Set());
    this._grants = next;
  }

  async #save() {
    const target = this._target;
    const data = this.data;
    if (!target || !data || this._saving) return;
    const kind = data.target;
    const anyGrant = this._shown.some((key) => (this._grants.get(key)?.size ?? 0) > 0);
    if (this._restricted && !target.isRestricted && !anyGrant) {
      const restrict = await confirmText(this, {
        headline: this.#words('restrictHeadline', target.name),
        content: this.#words('restrictText'),
        confirmLabel: this.#words(kind === 'folder' ? 'restrictFolder' : 'restrictForm'),
        color: 'danger',
      }).then(
        () => true,
        () => false,
      );
      if (!restrict) return;
    }
    this._alert = '';
    this._saving = true;
    const result = await saveTarget(this, kind, data.key, {
      grants: changedGrants(grantsOf(target.groups), this._grants, target.permissions),
      isRestricted: this._restricted,
      version: target.version ?? null,
    });
    this._saving = false;
    switch (result.kind) {
      case 'saved': {
        const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
        notifications?.peek('positive', { data: { message: this.#words('savedTarget', result.answer.name) } });
        this.value = { saved: true };
        this._submitModal();
        return;
      }
      case 'conflict':
      case 'locked':
        this._alert = result.kind === 'conflict' ? 'conflict' : 'lockedNow';
        await this.#read();
        return;
      case 'trashed':
      case 'gone':
      case 'invalid':
        this._alert = result.kind;
        return;
      default: {
        const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
        notifications?.peek('danger', { data: { message: this.#words('saveFailed') } });
      }
    }
  }

  #alertText(target: AccessTargetResponseModel | undefined): string {
    switch (this._alert) {
      case 'conflict':
        return this.#words('conflict');
      case 'lockedNow':
        return this.#words('lockedNow');
      case 'invalid':
        return this.#words('invalid');
      case 'trashed':
        return this.#words(target?.type === 'form' ? 'trashedForm' : 'trashedFolder');
      case 'gone':
        return this.#words('gone');
      default:
        return '';
    }
  }

  #body(target: AccessTargetResponseModel): TemplateResult {
    const readOnly = target.lock !== null && target.lock !== undefined;
    const names = new Map(target.groups.map((group) => [group.key, group.name]));
    const others = target.groups.filter((group) => !this._shown.includes(group.key));
    const noneHere = this._restricted
      ? this.#words('noneHereRestricted')
      : this.#words(target.type === 'form' ? 'noneHereForm' : 'noneHereFolder');
    return html`<div class="body">
      ${
        readOnly && target.lock
          ? html`<p class="note" role="note">${this.#words('lockedAll', target.lock.configurationKey)}</p>`
          : nothing
      }
      <div class="item">
        <div class="item-switch">
          <input
            type="checkbox"
            role="switch"
            id="restrict"
            aria-describedby="restrict-help"
            .checked=${this._restricted}
            ?disabled=${readOnly}
            @change=${(event: Event) => (this._restricted = (event.target as HTMLInputElement).checked)}
          />
          <label class="label" for="restrict">${this.#words('restrict')}</label>
        </div>
        <p class="help" id="restrict-help">
          ${this.#words(target.type === 'form' ? 'restrictHelpForm' : 'restrictHelpFolder')}
        </p>
      </div>
      <h3>${this.#words('groupsHere')}</h3>
      ${
        readOnly || others.length === 0
          ? nothing
          : html`<div class="item add">
              <label class="label" for="add-group">${this.#words('addGroup')}</label>
              <select
                id="add-group"
                @change=${(event: Event) => {
                  const select = event.target as HTMLSelectElement;
                  this.#addGroup(select.value);
                  select.value = '';
                }}
              >
                <option value="" selected>${this.#words('addGroupNone')}</option>
                ${others.map((group) => html`<option value=${group.key}>${group.name}</option>`)}
              </select>
            </div>`
      }
      ${
        this._shown.length === 0
          ? html`<p class="note">${noneHere}</p>`
          : accessMatrix({
              id: 'grants',
              caption: this.#words('sidebarCaption', target.name),
              permissionHeader: this.#words('permission'),
              columns: this._shown.map((key) => ({
                key,
                name: names.get(key) ?? key,
                fixed: false,
                remove: () => this.#removeGroup(key),
              })),
              parts: [
                {
                  label: this.#words('part_forms'),
                  permissions: ['design', 'manageActions']
                    .filter((name) => target.permissions.includes(name))
                    .map((name) => ({ name, label: this.#permission(name) })),
                },
                {
                  label: this.#words('part_entries'),
                  permissions: target.permissions
                    .filter((name) => name !== 'design' && name !== 'manageActions')
                    .map((name) => ({ name, label: this.#permission(name) })),
                },
              ],
              granted: (group, permission) => this._grants.get(group)?.has(permission) ?? false,
              readOnly,
              toggle: (group, permission, on) => this.#toggle(group, permission, on),
              cellLabel: (permission, group) => this.#words('cell', permission, group),
              removeLabel: (group) => this.#words('removeGroup', group),
            })
      }
    </div>`;
  }

  override render() {
    const target = this._target;
    const alert = this.#alertText(target);
    const savable = target !== undefined && !target.lock && this._alert !== 'gone' && this._alert !== 'trashed';
    return html`<umb-body-layout headline=${target ? this.#words('headline', target.name) : ''}>
      ${alert ? html`<p class="alert" role="alert">${alert}</p>` : nothing}
      ${
        this._failed
          ? html`<p class="alert" role="alert">${this.#words('failed')}</p>`
          : target
            ? this.#body(target)
            : this._alert === 'gone'
              ? nothing
              : skeletonLines(this.#words('loading'), [64, 48])
      }
      <p class="visually-hidden" role="status" aria-live="polite">${this._said}</p>
      <div slot="actions">
        <uui-button label=${this.#words('cancel')} @click=${this._rejectModal}></uui-button>
        ${
          savable
            ? html`<uui-button
                look="primary"
                color="positive"
                label=${this.#words('save')}
                .state=${this._saving ? 'waiting' : undefined}
                @click=${() => void this.#save()}
              ></uui-button>`
            : nothing
        }
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-permissions-modal'))
  customElements.define('jfb-permissions-modal', PermissionsModalElement);

export { PermissionsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-permissions-modal': PermissionsModalElement;
  }
}
