// The form workspace's frame (docs/ux/information-architecture.md, section 5): Umbraco's detail workspace editor, with the
// back arrow to the form's folder, the name, which renames the form for who may design it (ADR-0166), and the state chip
// in Umbraco's words. The views draw the rest, and the footer holds Save draft and Publish....
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import type { FormState } from './form-state';

/** How each state's chip looks: the live form stands out, a draft and an offline form stay quiet. */
const CHIP: Record<FormState, { color: string; look: string }> = {
  draft: { color: 'default', look: 'secondary' },
  published: { color: 'positive', look: 'primary' },
  pending: { color: 'warning', look: 'primary' },
  unpublished: { color: 'default', look: 'secondary' },
};

export class FormWorkspaceEditorElement extends UmbLitElement {
  static override properties = {
    _state: { state: true },
    _backPath: { state: true },
  };

  static override styles = css`
    .header {
      display: flex;
      align-items: center;
      gap: var(--uui-size-space-4);
      flex: 1 1 auto;
      min-width: 0;
    }

    umb-workspace-header-name-editable {
      flex: 1 1 auto;
      min-width: 8rem;
    }

    /* On a narrow window the chip gives way to the name: it shortens, and its title keeps the whole state. */
    uui-tag {
      flex: 0 1 auto;
      min-width: 0;
      max-width: 50%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `;

  declare private _state: FormState | undefined;
  declare private _backPath: string | undefined;

  constructor() {
    super();
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.observe(context?.state, (state) => (this._state = state), 'state');
      this.observe(
        context?.form,
        (form) => {
          this._backPath = form
            ? screenPath(form.folderKey ? FOLDER_ENTITY_TYPE : LIBRARY_ROOT_ENTITY_TYPE, form.folderKey ?? undefined)
            : undefined;
        },
        'form',
      );
    });
  }

  #chip() {
    const state = this._state;
    if (!state) return nothing;
    const label = this.localize.term(`jfbDesigner_state_${state}`);
    return html`<uui-tag color=${CHIP[state].color} look=${CHIP[state].look} title=${label}>${label}</uui-tag>`;
  }

  override render() {
    return html`<umb-entity-detail-workspace-editor .backPath=${this._backPath}>
      <div class="header" slot="header">
        <umb-workspace-header-name-editable
          label=${this.localize.term('jfbDesigner_formName')}
        ></umb-workspace-header-name-editable>
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
