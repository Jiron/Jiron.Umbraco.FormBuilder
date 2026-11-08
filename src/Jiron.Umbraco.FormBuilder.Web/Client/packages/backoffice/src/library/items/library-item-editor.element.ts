// A library item's workspace frame (ROADMAP 12.1, IA 7, ADR-0214, ADR-0215): Umbraco's detail workspace editor with the
// back arrow to the list, the name with its alias beside it in Umbraco's name-and-alias box, then the forms that use the
// item, a button that opens them as links. The "..." holds the item's entity actions, the footer Save, and the view the
// rest. The kind's words say what the item is.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UmbWorkspaceContext } from '@umbraco-cms/backoffice/workspace';
import type { LibraryUsersResponseModel } from '../../api';
import { FORM_ENTITY_TYPE, screenPath } from '../../section/entity-types';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { LibraryItemWorkspaceContext } from './library-item-workspace';
import { countOf, type LibraryItemModel } from './library-items';

type Workspace = LibraryItemWorkspaceContext<LibraryItemModel>;

/** The open item's workspace, whichever kind it is. */
export const LIBRARY_ITEM_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, Workspace>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is Workspace => 'refreshUsers' in context && 'libraryKind' in context,
);

export class LibraryItemEditorElement extends UmbLitElement {
  static override properties = {
    _name: { state: true },
    _alias: { state: true },
    _isNew: { state: true },
    _users: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      .header {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-1) var(--uui-size-space-4);
        flex: 1 1 auto;
        min-width: 0;
      }

      /* Umbraco's box lays out as its input alone (display: contents), so a box of ours holds the name's room. */
      .name {
        display: flex;
        flex: 1 1 0;
        min-width: 8rem;
      }

      .used {
        flex: none;
        color: var(--jfb-bo-color-text-muted);
      }

      .users {
        display: grid;
        gap: var(--uui-size-space-3);
        min-width: 16rem;
        max-width: 24rem;
        padding: var(--uui-size-space-5);
        background: var(--uui-color-surface);
        border-radius: var(--uui-border-radius);
        box-shadow: var(--uui-shadow-depth-3);
      }

      .users h2 {
        margin: 0;
        font-size: var(--jfb-bo-type-body);
        font-weight: 700;
      }

      .users ul {
        display: grid;
        gap: var(--uui-size-space-2);
        margin: 0;
        padding: 0;
        list-style: none;
      }

      .users a {
        color: var(--uui-color-interactive);
        overflow-wrap: anywhere;
      }

      .users a:hover,
      .users a:focus-visible {
        color: var(--uui-color-interactive-emphasis);
      }

      .note,
      .others {
        color: var(--jfb-bo-color-text-muted);
      }

      .others {
        margin: 0;
      }
    `,
  ];

  declare private _name: string;
  declare private _alias: string;
  declare private _isNew: boolean | undefined;
  declare private _users: LibraryUsersResponseModel | undefined;

  #context: Workspace | undefined;

  constructor() {
    super();
    this._name = '';
    this._alias = '';
    this._isNew = undefined;
    this._users = undefined;
    this.consumeContext(LIBRARY_ITEM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.name, (name) => (this._name = name ?? ''), 'name');
      this.observe(context?.alias, (alias) => (this._alias = alias ?? ''), 'alias');
      this.observe(context?.isNew, (isNew) => (this._isNew = isNew), 'isNew');
      this.observe(context?.users, (users) => (this._users = users), 'users');
    });
  }

  #term = (key: string, ...args: unknown[]) =>
    this.#context ? this.localize.term(`${this.#context.libraryKind.words}_${key}`, ...args) : '';

  // A name and an alias as Umbraco's box hands them on: an alias that breaks the rule is made from what was typed.
  #onChange(event: Event) {
    const kind = this.#context?.libraryKind;
    if (!kind) return;
    const box = event.target as HTMLElement & { value?: string; alias?: string };
    const name = box.value ?? '';
    const typed = box.alias ?? '';
    const alias = kind.isAlias(typed) ? typed : kind.aliasOf(typed || name);
    if (alias !== typed) box.alias = alias;
    this.#context?.setName(name);
    this.#context?.setAlias(alias);
  }

  #users(): TemplateResult | typeof nothing {
    const users = this._users;
    const kind = this.#context?.libraryKind;
    if (this._isNew !== false || !users || !kind) return nothing;
    if (users.forms.length + users.others === 0) return html`<span class="used">${this.#term('notUsed')}</span>`;
    return html`<uui-button
        class="used"
        look="outline"
        compact
        popovertarget="library-item-users"
        label=${this.#term('usedByButton', countOf(kind, users))}
        @click=${() => void this.#context?.refreshUsers()}
      ></uui-button>
      <uui-popover-container id="library-item-users" placement="bottom-end">
        <section class="users" aria-labelledby="library-item-users-heading">
          <h2 id="library-item-users-heading">${this.#term('usersHeading')}</h2>
          ${
            users.forms.length > 0
              ? html`<ul>
                  ${users.forms.map((form) => {
                    const notes = [
                      kind.counts === 'uses' ? this.#term('userUses', form.uses) : '',
                      form.inDraft && !form.inLive ? this.#term('userDraftOnly') : '',
                      form.isTrashed ? this.#term('userTrashed') : '',
                    ].filter(Boolean);
                    return html`<li>
                      <a href=${screenPath(FORM_ENTITY_TYPE, form.key)}>${form.name}</a>${
                        notes.length > 0 ? html` <span class="note">${notes.join(', ')}</span>` : nothing
                      }
                    </li>`;
                  })}
                </ul>`
              : nothing
          }
          ${users.others > 0 ? html`<p class="others">${this.#term('usersOthers', users.others)}</p>` : nothing}
        </section>
      </uui-popover-container>`;
  }

  override render() {
    const kind = this.#context?.libraryKind;
    if (!kind) return nothing;
    return html`<umb-entity-detail-workspace-editor .backPath=${screenPath(kind.listEntityType)}>
      <div class="header" slot="header">
        <div class="name">
          <umb-input-with-alias
            label=${this.#term('name')}
            placeholder=${this.#term('namePlaceholder')}
            .value=${this._name}
            .alias=${this._alias}
            alias-pattern=${kind.aliasPattern}
            .aliasReadonly=${kind.aliasFixed === true && this._isNew === false}
            ?auto-generate-alias=${this._isNew === true}
            required
            @change=${this.#onChange}
          ></umb-input-with-alias>
        </div>
        ${this.#users()}
      </div>
    </umb-entity-detail-workspace-editor>`;
  }
}

if (!customElements.get('jfb-library-item-editor'))
  customElements.define('jfb-library-item-editor', LibraryItemEditorElement);

export { LibraryItemEditorElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-library-item-editor': LibraryItemEditorElement;
  }
}
