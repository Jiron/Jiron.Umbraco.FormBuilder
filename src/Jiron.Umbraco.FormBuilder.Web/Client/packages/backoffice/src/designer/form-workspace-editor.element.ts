// The form workspace's frame (docs/ux/information-architecture.md, section 5): Umbraco's detail workspace editor, with the
// back arrow to the form's folder, the name, which renames the form for who may design it (ADR-0166) and is a heading for
// everyone else (ADR-0182), and the state chip in Umbraco's words, which goes under the name where the two do not fit
// side by side and wraps rather than cuts (ADR-0206, ADR-0213). The views draw the rest. The footer holds Preview, Save
// draft and Publish... on the views that change the form, for who may design it; where it would hold none of them it is
// not drawn at all (ADR-0258). So this is Umbraco's workspace editor, which can leave its footer out, drawn here as
// Umbraco's detail workspace editor draws it (the form's "..." where the form is saved, and Umbraco's own words for a
// form that can't be opened or isn't there), since that one draws its footer on every view. A form put in the Recycle
// Bin leaves its screen by the action that put it there (library/actions/trash-form.action.ts).
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens } from '../styles/tokens';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import type { FormState } from './form-state';
import { changesForm, shownView } from './form-views';

/** How each state's chip looks: the live form stands out, a draft and an offline form stay quiet. */
const CHIP: Record<FormState, { color: string; look: string }> = {
  draft: { color: 'default', look: 'secondary' },
  published: { color: 'positive', look: 'primary' },
  pending: { color: 'warning', look: 'primary' },
  unpublished: { color: 'default', look: 'secondary' },
};

/** The name's room in rem: its least width in the styles, below which the header's parts wait for room. */
const NAME_ROOM = 6;

/** The root's font size in pixels, which rem stands for. */
const rem = () => Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;

export class FormWorkspaceEditorElement extends UmbLitElement {
  static override properties = {
    _state: { state: true },
    _backPath: { state: true },
    _name: { state: true },
    _canDesign: { state: true },
    _stacked: { state: true },
    _waiting: { state: true },
    _loading: { state: true },
    _forbidden: { state: true },
    _exists: { state: true },
    _isNew: { state: true },
    _footer: { state: true },
  };

  // The tokens give the footer's filled buttons, Umbraco's own, the section's white labels (ADR-0182).
  static override styles = [
    jfbBackofficeTokens,
    css`
      /* Umbraco's header shares its row with the views' tabs, and with six views it leaves the name and the chip about
         166 px at its narrowest: where the two do not fit on one line, the chip goes under the name (ADR-0213). */
      .header {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-1) var(--uui-size-space-4);
        flex: 1 1 auto;
        min-width: 0;
      }

      /* On a narrow window the name gives way first, to 6rem. Umbraco's name box lays out as its input alone
         (display: contents), so a box of ours holds the name's room. */
      .name,
      h1 {
        flex: 1 1 0;
        min-width: 6rem;
      }

      /* One under the other, the name's box and the chip keep inside Umbraco's 70 px header, the longest state's two
         lines too: the box at UUI's smaller height, the chip with less air above and below its words. */
      .stacked .name {
        --uui-input-height: var(--uui-size-9);
      }

      .stacked uui-tag {
        --uui-tag-padding: 1px calc(var(--uui-size-space-1) + 0.5em);
        line-height: 1.1;
      }

      .name {
        display: flex;
      }

      /* Read, not changed: the name as the screen's heading, in the size of the box it stands for. */
      h1 {
        margin: 0;
        overflow-wrap: anywhere;
        font-size: var(--jfb-bo-type-title);
        font-weight: 700;
        line-height: 1.3;
      }

      /* A state is never cut: under the name, the longest, "Published (pending changes)", takes a second line inside its
         chip where even the header's whole width is too short for it. */
      uui-tag {
        flex: 0 1 auto;
        min-width: min-content;
        white-space: normal;
        text-align: center;
        line-height: 1.2;
      }

      /* Where Umbraco's row leaves the header less than the name's 6rem (Umbraco 18.3 gives the views' tabs their whole
         width first, ADR-0250), a sliver of the name would be no box to type in and crowd Umbraco's back button, and a
         cut state would say less than nothing: both wait out of sight and reach, in their place, until the window
         leaves them room. */
      .waiting > * {
        visibility: hidden;
      }

      /* As Umbraco's detail workspace editor keeps itself in place while there is no form to show. */
      umb-workspace-editor.hide {
        visibility: hidden;
      }
    `,
  ];

  declare private _state: FormState | undefined;
  declare private _backPath: string | undefined;
  declare private _name: string;
  /** Whether the user may design the form, and so rename it; until it is read, the editable name is not shown. */
  declare private _canDesign: boolean | undefined;
  /** Whether the chip went under the name, which then both take their smaller height. */
  declare private _stacked: boolean;
  /** Whether Umbraco's row leaves the header less than the name's 6rem, so the name and the state wait for room. */
  declare private _waiting: boolean;
  declare private _loading: boolean;
  declare private _forbidden: boolean;
  declare private _exists: boolean;
  declare private _isNew: boolean | undefined;
  /** Whether the view shown changes the form, so the footer with its buttons is drawn for who may design it. */
  declare private _footer: boolean;

  /** The header's own size follows the window and the chip's words; each change reads again where the chip lies. */
  #resize = new ResizeObserver(() => this.#measure());

  constructor() {
    super();
    this._name = '';
    this._stacked = false;
    this._waiting = false;
    this._loading = false;
    this._forbidden = false;
    this._exists = false;
    this._footer = true;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.observe(context?.loading.isOn, (loading) => (this._loading = loading ?? false), 'loading');
      this.observe(context?.forbidden.isOn, (forbidden) => (this._forbidden = forbidden ?? false), 'forbidden');
      this.observe(context?.data, (data) => (this._exists = !!data), 'data');
      this.observe(context?.isNew, (isNew) => (this._isNew = isNew), 'isNew');
      this.observe(context?.state, (state) => (this._state = state), 'state');
      this.observe(
        context?.form,
        (form) => {
          this._name = form?.name ?? '';
          this._canDesign = form ? form.permissions.includes('design') : undefined;
          this._backPath = form
            ? screenPath(form.folderKey ? FOLDER_ENTITY_TYPE : LIBRARY_ROOT_ENTITY_TYPE, form.folderKey ?? undefined)
            : undefined;
        },
        'form',
      );
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener('navigationend', this.#viewShown);
    this.#viewShown();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    window.removeEventListener('navigationend', this.#viewShown);
    this.#resize.disconnect();
  }

  readonly #viewShown = () => {
    this._footer = changesForm(shownView(window.location.pathname));
  };

  protected override firstUpdated(changed: PropertyValues<this>) {
    super.firstUpdated(changed);
    // The header stays the same element; a chip that moves to or from a line of its own changes its height.
    const header = this.shadowRoot?.querySelector('.header');
    if (header) this.#resize.observe(header);
  }

  /**
   * Whether the chip lies on a line of its own. The flex line decides it by the widths alone, which the smaller height
   * leaves as they are, so the two never take turns.
   */
  #measure() {
    const name = this.shadowRoot?.querySelector<HTMLElement>('.name, h1');
    const chip = this.shadowRoot?.querySelector<HTMLElement>('uui-tag');
    const stacked = !!name && !!chip && chip.offsetTop >= name.offsetTop + name.offsetHeight;
    if (stacked !== this._stacked) this._stacked = stacked;
    // The header's width is what Umbraco's row leaves it; its parts keep their place while they wait, so the two never
    // take turns.
    const header = this.shadowRoot?.querySelector<HTMLElement>('.header');
    const waiting = !!header && header.clientWidth < NAME_ROOM * rem();
    if (waiting !== this._waiting) this._waiting = waiting;
  }

  #chip() {
    const state = this._state;
    if (!state) return nothing;
    const label = this.localize.term(`jfbDesigner_state_${state}`);
    return html`<uui-tag color=${CHIP[state].color} look=${CHIP[state].look}>${label}</uui-tag>`;
  }

  override render() {
    const missing = !this._loading && !this._exists;
    const forbidden =
      !this._loading && this._forbidden
        ? html`<umb-entity-detail-forbidden entity-type=${FORM_ENTITY_TYPE}></umb-entity-detail-forbidden>`
        : nothing;
    const gone = missing
      ? html`<umb-entity-detail-not-found entity-type=${FORM_ENTITY_TYPE}></umb-entity-detail-not-found>`
      : nothing;
    const menu = html`<umb-workspace-entity-action-menu slot="action-menu"></umb-workspace-entity-action-menu>`;
    return html`${forbidden}${gone}
      <umb-workspace-editor
        class=${missing ? 'hide' : ''}
        ?loading=${this._loading}
        .enforceNoFooter=${!this._footer || this._canDesign === false}
        .backPath=${this._backPath}
      >
        <div
          class=${['header', this._stacked ? 'stacked' : '', this._waiting ? 'waiting' : ''].filter(Boolean).join(' ')}
          slot="header"
        >
          ${
            this._canDesign === false
              ? html`<h1>${this._name}</h1>`
              : html`<div class="name">
                  <umb-workspace-header-name-editable
                    label=${this.localize.term('jfbDesigner_formName')}
                  ></umb-workspace-header-name-editable>
                </div>`
          }
          ${this.#chip()}
        </div>
        ${this._isNew ? nothing : menu}
      </umb-workspace-editor>`;
  }
}

if (!customElements.get('jfb-form-workspace-editor'))
  customElements.define('jfb-form-workspace-editor', FormWorkspaceEditorElement);

export { FormWorkspaceEditorElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-workspace-editor': FormWorkspaceEditorElement;
  }
}
