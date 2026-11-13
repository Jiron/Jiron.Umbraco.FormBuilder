// The form workspace's frame (docs/ux/information-architecture.md, section 5): Umbraco's detail workspace editor, with the
// back arrow to the form's folder, the name, which renames the form for who may design it (ADR-0166) and is a heading for
// everyone else (ADR-0182), and the state chip in Umbraco's words, which goes under the name where the two do not fit
// side by side and wraps rather than cuts (ADR-0206, ADR-0213). The views draw the rest, and the footer holds Save
// draft and Publish....
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens } from '../styles/tokens';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import type { FormState } from './form-state';

/** How each state's chip looks: the live form stands out, a draft and an offline form stay quiet. */
const CHIP: Record<FormState, { color: string; look: string }> = {
  draft: { color: 'default', look: 'secondary' },
  published: { color: 'positive', look: 'primary' },
  pending: { color: 'warning', look: 'primary' },
  unpublished: { color: 'default', look: 'secondary' },
};

/** The name's room in rem, below which the header is cramped (the name's min-width in the styles). */
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
    _cramped: { state: true },
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
         width first, ADR-0250), the name takes what there is, and the state, under it, its own width at most, cut
         with an ellipsis; its words stay its title and are read in full. The header clips its parts to its box, so
         nothing of it lies over Umbraco's menu or the tabs, also where Umbraco leaves it no room at all. */
      .cramped {
        overflow: hidden;
      }

      .cramped .name,
      .cramped h1 {
        min-width: 0;
      }

      .cramped uui-tag {
        min-width: 0;
        max-width: 100%;
      }

      .cramped .state {
        display: block;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
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
  /** Whether Umbraco's row leaves the header less than the name's 6rem, so the name and the state take what there is. */
  declare private _cramped: boolean;

  /** The header's own size follows the window and the chip's words; each change reads again where the chip lies. */
  #resize = new ResizeObserver(() => this.#measure());

  constructor() {
    super();
    this._name = '';
    this._stacked = false;
    this._cramped = false;
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
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

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#resize.disconnect();
  }

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
    const header = this.shadowRoot?.querySelector<HTMLElement>('.header');
    const name = this.shadowRoot?.querySelector<HTMLElement>('.name, h1');
    const chip = this.shadowRoot?.querySelector<HTMLElement>('uui-tag');
    const stacked = !!name && !!chip && chip.offsetTop >= name.offsetTop + name.offsetHeight;
    if (stacked !== this._stacked) this._stacked = stacked;
    // The header's width is what Umbraco's row leaves it, whatever its own parts ask, so the two never take turns.
    const cramped = !!header && header.clientWidth < NAME_ROOM * rem();
    if (cramped !== this._cramped) this._cramped = cramped;
  }

  #chip() {
    const state = this._state;
    if (!state) return nothing;
    const label = this.localize.term(`jfbDesigner_state_${state}`);
    return html`<uui-tag color=${CHIP[state].color} look=${CHIP[state].look} title=${this._cramped ? label : nothing}
      ><span class="state">${label}</span></uui-tag
    >`;
  }

  override render() {
    return html`<umb-entity-detail-workspace-editor .backPath=${this._backPath}>
      <div
        class=${['header', this._stacked ? 'stacked' : '', this._cramped ? 'cramped' : ''].join(' ').trim()}
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
    </umb-entity-detail-workspace-editor>`;
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
