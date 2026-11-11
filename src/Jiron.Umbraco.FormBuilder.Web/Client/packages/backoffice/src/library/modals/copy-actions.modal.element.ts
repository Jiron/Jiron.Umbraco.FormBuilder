// "Copy actions to..." (docs/ux/information-architecture.md, section 3; ADR-0046, ADR-0166): the target, picked from the forms whose
// actions the user may manage, and which of the source's actions go, all to begin with. The copies land in the target's draft;
// the result lists what each copy names that the target lacks, with a link to fix it in the target's Actions view.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement, umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { UMB_TREE_PICKER_MODAL, type UmbTreeItemModel } from '@umbraco-cms/backoffice/tree';
import {
  formDraft,
  formExchangeCopyActions,
  formGet,
  type ActionsCopiedResponseModel,
  type CopiedActionResponseModel,
  type FormResponseModel,
} from '../../api';
import { formPath, FORM_ENTITY_TYPE, screenPath } from '../../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import { LIBRARY_TREE_ALIAS } from '../../tree/aliases';
import type { LibraryTreeItem } from '../../tree/library-tree.repository';
import { changed, placeOf, sayRefusal } from '../library-changes';
import type { CopyActionsModalData, CopyActionsModalValue } from './tokens';

/** An action of the source's draft, as the sidebar lists it. */
interface SourceAction {
  key: string;
  name: string;
}

export class CopyActionsModalElement extends UmbModalBaseElement<CopyActionsModalData, CopyActionsModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _actions: { state: true },
    _chosen: { state: true },
    _target: { state: true },
    _copying: { state: true },
    _result: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    css`
      /* It takes the focus only as the result takes the place of the button pressed; it does nothing. */
      .result:focus {
        outline: none;
      }

      uui-box + uui-box {
        margin-top: var(--uui-size-space-5);
      }

      p {
        margin: 0 0 var(--uui-size-space-4);
      }

      .target {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .target strong {
        flex: 1 1 12rem;
        min-width: 0;
        overflow-wrap: anywhere;
      }

      ul {
        margin: 0;
        padding: 0;
        list-style: none;
      }

      li + li {
        margin-top: var(--uui-size-space-4);
      }

      .issues {
        margin: var(--uui-size-space-2) 0;
        padding-inline-start: var(--uui-size-space-5);
        list-style: disc;
      }

      .issues li + li {
        margin-top: var(--uui-size-space-1);
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      /* Umbraco's link colour, as the Overview's links: the browser's own blue is 1.4:1 on the dark theme's box. */
      a {
        color: var(--uui-color-interactive);
        text-decoration: underline;
      }

      a:hover {
        color: var(--uui-color-interactive-emphasis);
      }
    `,
  ];

  /** The source's actions; undefined while they load. */
  declare private _actions: SourceAction[] | undefined;
  declare private _chosen: ReadonlySet<string>;
  declare private _target: FormResponseModel | undefined;
  declare private _copying: boolean;
  declare private _result: ActionsCopiedResponseModel | undefined;

  override connectedCallback() {
    super.connectedCallback();
    this._chosen = new Set();
    this._copying = false;
    void this.#loadActions();
  }

  async #loadActions() {
    const key = this.data?.sourceKey;
    if (!key) return;
    const { data } = await tryExecute(this, formDraft({ path: { key } }), { disableNotifications: true });
    const actions = (
      (data?.definition as { actions?: { key: string; name?: string; type: string }[] } | null | undefined)?.actions ??
      []
    ).map((action) => ({ key: action.key, name: action.name || action.type }));
    this._actions = actions;
    this._chosen = new Set(actions.map((action) => action.key));
  }

  async #pickTarget() {
    const source = this.data?.sourceKey;
    // A picker closed without a choice leaves the target as it was.
    const { selection } = await umbOpenModal(this, UMB_TREE_PICKER_MODAL, {
      data: {
        treeAlias: LIBRARY_TREE_ALIAS,
        hideTreeRoot: true,
        pickableFilter: (item: UmbTreeItemModel) => {
          const permissions = (item as LibraryTreeItem).permissions ?? [];
          return (
            item.entityType === FORM_ENTITY_TYPE &&
            item.unique !== source &&
            permissions.includes('design') &&
            permissions.includes('manageActions')
          );
        },
      },
    }).catch(() => ({ selection: [] as (string | null)[] }));
    const key = selection[0];
    if (!key) return;
    const { data } = await tryExecute(this, formGet({ path: { key } }));
    if (data) this._target = data;
  }

  #toggle(key: string, on: boolean) {
    const chosen = new Set(this._chosen);
    if (on) chosen.add(key);
    else chosen.delete(key);
    this._chosen = chosen;
  }

  async #copy() {
    const source = this.data?.sourceKey;
    const target = this._target;
    const actions = this._actions ?? [];
    if (!source || !target || this._chosen.size === 0 || this._copying) return;
    this._copying = true;
    const all = this._chosen.size === actions.length;
    const { data, error } = await tryExecute(
      this,
      formExchangeCopyActions({
        path: { key: source },
        body: {
          targetKey: target.key,
          concurrencyVersion: target.concurrencyVersion,
          actionKeys: all ? null : actions.filter((action) => this._chosen.has(action.key)).map((action) => action.key),
        },
      }),
      { disableNotifications: true },
    );
    this._copying = false;
    if (!data) {
      await sayRefusal(this, error);
      return;
    }

    this._result = data;
    this.updateValue({ targetKey: target.key });
    // The result takes the place of the choice, and of the button pressed: the focus goes to what it says.
    void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('.result')?.focus());
    await changed(this, placeOf(target.folderKey));
  }

  // Each sentence once: the server reports every place a missing field is named, and one action may name it twice.
  #issues(copy: CopiedActionResponseModel): string[] {
    const said = [
      ...copy.unmapped.map((reference) =>
        reference.alias
          ? this.localize.term('jfbLibrary_copyLacksField', reference.alias)
          : this.localize.term('jfbLibrary_copyLacksElement'),
      ),
      ...copy.mergeTags.map((tag) => this.localize.term('jfbLibrary_copyLacksTag', `{{ ${tag.tag} }}`)),
    ];
    return [...new Set(said)];
  }

  #renderResult(result: ActionsCopiedResponseModel, target: FormResponseModel) {
    const names = new Map((this._actions ?? []).map((action) => [action.key, action.name]));
    const troubled = result.actions.filter((copy) => copy.unmapped.length + copy.mergeTags.length > 0);
    return html`<uui-box>
      <p class="result" role="status" tabindex="-1">
        ${this.localize.term('jfbLibrary_copied', result.actions.length, target.name)}
      </p>
      ${
        troubled.length === 0
          ? html`<p class="muted">${this.localize.term('jfbLibrary_copiedClean', result.actions.length)}</p>`
          : html`<p>${this.localize.term('jfbLibrary_copiedNeedsFixing', result.actions.length, troubled.length)}</p>
              <ul>
                ${troubled.map(
                  (copy) =>
                    html`<li>
                      <strong>${names.get(copy.sourceKey) ?? ''}</strong>
                      <ul class="issues">
                        ${this.#issues(copy).map((issue) => html`<li>${issue}</li>`)}
                      </ul>
                      <a href=${formPath(target.key, { action: copy.key })} @click=${this._submitModal}
                        >${this.localize.term('jfbLibrary_fixIn')}</a
                      >
                    </li>`,
                )}
              </ul>`
      }
    </uui-box>`;
  }

  #renderChoice() {
    const actions = this._actions;
    const target = this._target;
    return html`<uui-box headline-variant="h4" headline=${this.localize.term('jfbLibrary_copyTo')}>
        <div class="target">
          ${target ? html`<strong>${target.name}</strong>` : html`<span class="muted">${this.localize.term('jfbLibrary_noTargetYet')}</span>`}
          <uui-button
            look="outline"
            label=${this.localize.term(target ? 'jfbLibrary_chooseAnotherForm' : 'jfbLibrary_chooseForm')}
            @click=${this.#pickTarget}
          ></uui-button>
        </div>
      </uui-box>
      <uui-box
        headline-variant="h4"
        headline=${this.localize.term('jfbLibrary_whichActions', this.data?.sourceName ?? '')}
      >
        ${
          actions === undefined
            ? skeletonLines(this.localize.term('jfbLibrary_loading'), [52, 40])
            : actions.length === 0
              ? html`<p>${this.localize.term('jfbLibrary_noActionsToCopy')}</p>`
              : html`<ul>
                  ${actions.map(
                    (action) =>
                      html`<li>
                        <uui-checkbox
                          label=${action.name}
                          ?checked=${this._chosen.has(action.key)}
                          @change=${(event: Event) => this.#toggle(action.key, (event.target as HTMLInputElement).checked)}
                        ></uui-checkbox>
                      </li>`,
                  )}
                </ul>`
        }
      </uui-box>`;
  }

  override render() {
    const result = this._result;
    const target = this._target;
    // The headline stays short, as Umbraco's header cuts it to one line; the source's name heads its actions below.
    return html`<umb-body-layout headline=${this.localize.term('jfbLibrary_copyActionsHeadline')}>
      ${result && target ? this.#renderResult(result, target) : this.#renderChoice()}
      <div slot="actions">
        ${
          result && target
            ? html`<uui-button label=${this.localize.term('general_close')} @click=${this._submitModal}></uui-button>
                <uui-button
                  look="primary"
                  color="positive"
                  href=${screenPath(FORM_ENTITY_TYPE, target.key)}
                  label=${this.localize.term('jfbLibrary_openForm')}
                  @click=${this._submitModal}
                ></uui-button>`
            : html`<uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
                <uui-button
                  look="primary"
                  color="positive"
                  label=${this.localize.term('jfbLibrary_copyCount', this._chosen.size)}
                  ?disabled=${!target || this._chosen.size === 0}
                  .state=${this._copying ? 'waiting' : undefined}
                  @click=${this.#copy}
                ></uui-button>`
        }
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-copy-actions-modal'))
  customElements.define('jfb-copy-actions-modal', CopyActionsModalElement);

export { CopyActionsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-copy-actions-modal': CopyActionsModalElement;
  }
}
