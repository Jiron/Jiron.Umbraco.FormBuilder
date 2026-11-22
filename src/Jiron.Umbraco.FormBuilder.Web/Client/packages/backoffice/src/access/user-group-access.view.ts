// The Form Builder view on Umbraco's user group screen (ROADMAP 25.1, ADR-0262): what the group may do with every form,
// by part and by the permissions' names; the folders and forms that give it more or are restricted to groups including
// it; that its users see an empty section while it holds nothing; that it can't open the section yet, taken from the
// screen's own Sections so it follows them; and for the administrators, that they hold everything. It reads Settings >
// Access's answer and saves nothing: the footer's Save is Umbraco's and saves the group. One link opens Settings >
// Access, where the group's permissions are changed beside every other group's.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { UMB_USER_GROUP_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/user-group';
import type { AccessResponseModel } from '../api';
import { screenPath, SECTION_ALIAS } from '../section/entity-types';
import { PLACES } from '../section/sidebar';
import { jfbPhoneColumn } from '../styles/phone';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readAccess } from './access-api';
import { groupAccessOf, type GroupAccess, type GroupItem } from './access-model';
import { loadAccessWords } from './access-words';

/** Settings > Access's tab in the sidebar's table; a table without it is a mistake found as the view loads. */
const ACCESS_TAB = PLACES.flatMap((place) => place.tabs).find((tab) => tab.id === 'access');
if (!ACCESS_TAB) throw new Error('The sidebar has no Access tab for the user group view to link to.');

/** Settings > Access, the screen the view links to. */
const ACCESS_SCREEN = screenPath(ACCESS_TAB.entityType);

/** What the view shows: the read on its way, a read that failed or was refused, or the answer. */
type State = 'loading' | 'failed' | 'forbidden' | AccessResponseModel;

export class UserGroupAccessViewElement extends UmbLitElement {
  static override properties = {
    _state: { state: true },
    _unique: { state: true },
    _sections: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    css`
      /* As Umbraco's own views on this screen: the box keeps the workspace's padding (members/member-entries.view). */
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      /* One column, as wide as the details beside it read; on a phone it keeps to the window (jfbPhoneColumn). */
      .page {
        max-inline-size: 720px;
      }

      .stack {
        display: grid;
        gap: var(--uui-size-space-5);
      }

      p,
      h2,
      dl,
      dd,
      ul {
        margin: 0;
      }

      section {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      h2 {
        font-size: var(--uui-type-h5-size, 15px);
        font-weight: 700;
        line-height: 1.4;
      }

      .note {
        color: var(--jfb-bo-color-text-muted);
      }

      /* Each part's name, then its permissions under it, one a line: a definition list of lists. */
      dl {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      dt {
        font-weight: 600;
      }

      ul {
        padding: 0;
        list-style: none;
      }

      dd ul {
        display: grid;
        gap: var(--uui-size-space-1);
        padding-inline-start: var(--uui-size-space-4);
      }

      .items {
        display: grid;
      }

      .items li {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        column-gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-3) 0;
        border-block-end: 1px solid var(--uui-color-divider);
      }

      .items li:first-child {
        padding-block-start: 0;
      }

      .items li:last-child {
        border-block-end: 0;
      }

      .items uui-icon {
        margin-block-start: 0.15em;
      }

      .item-name {
        font-weight: 600;
        overflow-wrap: anywhere;
      }

      .item-line {
        grid-column: 2;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      /* A link that is a target of its own: 44 px tall on a touch screen (WCAG 2.5.8 asks 24, D18 asks 44). */
      .open {
        display: inline-flex;
        align-items: center;
        min-block-size: 44px;
        color: var(--uui-color-interactive);
        text-decoration: underline;
        font-weight: 600;
      }

      .open:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .open:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
        border-radius: var(--uui-border-radius);
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

  declare private _state: State;
  /** The group the screen shows, once it exists. */
  declare private _unique: string | undefined;
  /** The sections the screen's Details hold now, saved or not. */
  declare private _sections: readonly string[];
  /** Which read is the last asked, so an older answer never replaces a newer one. */
  #asked = 0;

  constructor() {
    super();
    this._state = 'loading';
    this._unique = undefined;
    this._sections = [];
    this.consumeContext(UMB_USER_GROUP_WORKSPACE_CONTEXT, (workspace) => {
      this.observe(workspace?.sections, (sections) => (this._sections = sections ?? []), '_jfbSections');
      // A group that is new has nothing to read yet; the view is not offered there, and asks nothing if shown.
      this.observe(workspace?.isNew, (isNew) => this.#follow(isNew === false ? workspace?.getUnique() : undefined));
      this.observe(workspace?.unique, (unique) => this.#follow(workspace?.getIsNew() === false ? unique : undefined));
    });
  }

  #follow(unique: string | null | undefined) {
    if (!unique || unique === this._unique) return;
    this._unique = unique;
    void this.#read();
  }

  #words = (key: string, ...args: unknown[]) => this.localize.term(`jfbAccess_${key}`, ...args);

  #permission = (name: string) => this.localize.term(`jfbLibrary_permission_${name}`);

  async #read() {
    const asked = ++this.#asked;
    this._state = 'loading';
    const [words, answer] = await Promise.all([
      loadAccessWords(this.localize.lang()).then(
        () => true,
        () => false,
      ),
      readAccess(this),
    ]);
    if (asked !== this.#asked) return;
    this._state = answer === 'forbidden' ? 'forbidden' : !words || answer === undefined ? 'failed' : answer;
  }

  // ---- Drawing ------------------------------------------------------------------------------------------------------

  #list(names: readonly string[]): string {
    return new Intl.ListFormat(this.localize.lang(), { type: 'conjunction' }).format(names);
  }

  #everyForm(access: GroupAccess, hasSection: boolean): TemplateResult {
    let said: TemplateResult;
    if (access.parts.length > 0)
      said = html`<dl>
        ${access.parts.map(
          (part) =>
            html`<dt>${this.#words(`part_${part.alias}`)}</dt>
              <dd>
                <ul>
                  ${part.permissions.map((name) => html`<li>${this.#permission(name)}</li>`)}
                </ul>
              </dd>`,
        )}
      </dl>`;
    else if (access.holdsAny) said = html`<p class="note">${this.#words('groupNoneEveryForm')}</p>`;
    // Its users see an empty section only if the group has the section; without it they see none.
    else said = html`<p>${this.#words(hasSection ? 'groupNoPermission' : 'groupNoPermissionYet')}</p>`;
    return html`<section aria-labelledby="every-form">
      <h2 id="every-form">${this.#words('groupEveryForm')}</h2>
      ${said}
    </section>`;
  }

  #item(item: GroupItem): TemplateResult {
    const may = item.permissions.length > 0 ? this.#list(item.permissions.map(this.#permission)) : '';
    return html`<li>
      <uui-icon name=${item.type === 'form' ? 'icon-document' : 'icon-folder'} aria-hidden="true"></uui-icon>
      <span class="item-name">${item.name}</span>
      <span class="item-line">${may || this.#words('groupItemNothing')}</span>
      ${
        item.isRestricted
          ? html`<span class="item-line"
              >${item.others.length === 0 ? this.#words('groupItemOwn') : this.#words('groupItemShared', item.others)}</span
            >`
          : nothing
      }
    </li>`;
  }

  #items(access: GroupAccess): TemplateResult | typeof nothing {
    if (access.items.length === 0) return nothing;
    return html`<section aria-labelledby="items">
      <h2 id="items">${this.#words('items')}</h2>
      <ul class="items">
        ${access.items.map((item) => this.#item(item))}
      </ul>
    </section>`;
  }

  #lock(access: GroupAccess): TemplateResult | typeof nothing {
    const lock = access.lock;
    if (!lock) return nothing;
    return html`<p class="note" role="note">
      ${lock.field === '*' ? this.#words('groupLockedAll', lock.configurationKey) : this.#words('groupLockedGrants', lock.configurationKey)}
    </p>`;
  }

  #open(): TemplateResult {
    return html`<p><a class="open" href=${ACCESS_SCREEN}>${this.#words('groupOpen')}</a></p>`;
  }

  #body(): TemplateResult {
    const state = this._state;
    if (state === 'loading') return skeletonLines(this.localize.term('general_loading'), [48, 64, 40]);
    if (state === 'forbidden')
      return html`<p role="alert">
        ${this.localize.term('jfbLibrary_missingPermission', this.#permission('managePermissions'))}
      </p>`;
    if (state === 'failed')
      return html`<div class="failed" role="alert">
        <span>${this.#words('failed')}</span>
        <uui-button look="outline" label=${this.#words('retry')} @click=${() => void this.#read()}></uui-button>
      </div>`;
    const access = groupAccessOf(state, this._unique ?? '');
    if (access.isAdministrators)
      return html`<div class="stack">
        <p>${this.#words('administrators')}</p>
        ${this.#open()}
      </div>`;
    const hasSection = this._sections.includes(SECTION_ALIAS);
    return html`<div class="stack">
      ${hasSection ? nothing : html`<p>${this.#words('groupNoSection')}</p>`} ${this.#lock(access)}
      ${this.#everyForm(access, hasSection)} ${this.#items(access)} ${this.#open()}
    </div>`;
  }

  override render() {
    return html`<div class="page"><uui-box>${this.#body()}</uui-box></div>`;
  }
}

export default UserGroupAccessViewElement;

if (!customElements.get('jfb-user-group-access-view'))
  customElements.define('jfb-user-group-access-view', UserGroupAccessViewElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-user-group-access-view': UserGroupAccessViewElement;
  }
}
