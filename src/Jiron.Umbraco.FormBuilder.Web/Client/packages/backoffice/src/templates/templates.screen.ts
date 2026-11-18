// Library > Templates (ROADMAP 12.1, IA 7, ADR-0217): the built-in templates, the site's saved form templates and its
// saved groups, as cards in one radio group beside the chosen one's preview. A form template's preview has Use
// template, for who may design forms at the top of the forms, which creates the form there and opens it; a group's says
// the field picker adds it. A saved template's "..." renames or deletes it, and the focus stays on the "..." or moves
// to the card that takes the deleted one's place. There is no create button: templates come from Save as template on a
// form or a group.
import '../section/place-tabs.element';
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  formExchangeFromTemplate,
  templateDelete,
  templateList,
  templateRename,
  type TemplateResponseModel,
} from '../api';
import '../designer/preview/form-preview.element';
import { say } from '../library/items/library-items';
import { changed, open, placeOf, sayRefusal } from '../library/library-changes';
import { TEMPLATE_NAME_MODAL } from '../library/modals/tokens';
import { screenPath, FORM_ENTITY_TYPE } from '../section/entity-types';
import { myPermissions } from '../section/my-permissions';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { confirmText } from '../ui/confirm';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';

const quiet = { disableNotifications: true } as const;

/** The three parts of the screen, in their order. */
type Part = 'builtIn' | 'forms' | 'groups';

export class TemplatesScreenElement extends UmbLitElement {
  static override properties = {
    _templates: { state: true },
    _chosen: { state: true },
    _busy: { state: true },
    _mayUse: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    css`
      :host {
        display: block;
        block-size: 100%;
        container-type: inline-size;
      }

      .page {
        display: grid;
        grid-template-columns: minmax(240px, 2fr) 3fr;
        gap: var(--uui-size-layout-1);
        align-items: start;
      }

      @container (max-width: 860px) {
        .page {
          grid-template-columns: 1fr;
        }
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
        min-inline-size: 0;
      }

      h3 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-3);
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      h3:first-of-type {
        margin-top: 0;
      }

      .cards {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      .card {
        display: grid;
        grid-template-columns: auto 1fr;
        gap: var(--uui-size-space-1) var(--uui-size-space-3);
        padding: var(--uui-size-space-4);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        cursor: pointer;
        transition:
          border-color 120ms ease,
          background-color 120ms ease,
          transform 120ms ease-out;
      }

      .card:active {
        transform: scale(0.99);
      }

      @media (hover: hover) and (pointer: fine) {
        .card:hover {
          border-color: var(--uui-color-border-emphasis);
        }
      }

      .card:has(input:checked) {
        border-color: var(--uui-color-selected);
        background: var(--uui-color-surface-alt);
      }

      .card:has(input:focus-visible) {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      /* Beside the name, as the gallery's: a native radio stretched over both lines would sit between them. */
      .card input {
        grid-row: span 2;
        align-self: start;
        margin: 3px 0 0;
        accent-color: var(--uui-color-selected);
      }

      .name {
        font-weight: 700;
      }

      .line {
        color: var(--jfb-bo-color-text-muted);
      }

      .preview {
        position: sticky;
        top: 0;
      }

      .head {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-3);
      }

      .head h3 {
        flex: 1;
        margin: 0;
        font-size: var(--uui-type-h5-size);
      }

      .muted {
        margin: 0 0 var(--uui-size-space-4);
        color: var(--jfb-bo-color-text-muted);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }

      @media (prefers-reduced-motion: reduce) {
        .card {
          transition: none;
        }

        .card:active {
          transform: none;
        }
      }
    `,
  ];

  declare private _templates: readonly TemplateResponseModel[] | 'failed' | undefined;
  declare private _chosen: string | undefined;
  declare private _busy: boolean;
  /** Whether the user may design forms at the top of the forms, where Use template creates one. */
  declare private _mayUse: boolean;

  constructor() {
    super();
    this._templates = undefined;
    this._chosen = undefined;
    this._busy = false;
    this._mayUse = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
    void myPermissions(this).then((held) => (this._mayUse = held?.global.includes('design') ?? false));
  }

  async #read() {
    const [forms, groups] = await Promise.all([
      tryExecute(this, templateList({ query: { take: 500 } }), quiet),
      tryExecute(this, templateList({ query: { kind: 'group', take: 500 } }), quiet),
    ]);
    if (!forms.data || !groups.data) {
      this._templates = 'failed';
      return;
    }
    this._templates = [...forms.data.items, ...groups.data.items];
    if (!this._templates.some((one) => one.key === this._chosen)) this._chosen = this._templates[0]?.key;
  }

  #partOf(template: TemplateResponseModel): Part {
    if (template.builtIn) return 'builtIn';
    return template.kind === 'group' ? 'groups' : 'forms';
  }

  #name(template: TemplateResponseModel): string {
    return template.builtIn && template.alias
      ? this.localize.termOrDefault(`jfbLibrary_template_${template.alias}`, template.name)
      : template.name;
  }

  #line(template: TemplateResponseModel): string {
    if (template.builtIn && template.alias)
      return this.localize.termOrDefault(`jfbLibrary_template_${template.alias}Description`, '');
    return template.description ?? '';
  }

  async #use(template: TemplateResponseModel) {
    if (this._busy) return;
    this._busy = true;
    try {
      const { data: form, error } = await tryExecute(
        this,
        formExchangeFromTemplate({ body: { templateKey: template.key, name: this.#name(template), folderKey: null } }),
        quiet,
      );
      if (!form) {
        await sayRefusal(this, error);
        return;
      }
      await changed(this, placeOf(null));
      open(screenPath(FORM_ENTITY_TYPE, form.key));
    } finally {
      this._busy = false;
    }
  }

  async #rename(template: TemplateResponseModel) {
    const value = await umbOpenModal(this, TEMPLATE_NAME_MODAL, {
      data: {
        headline: this.localize.term('jfbLibrary_renameTemplateHeadline', template.name),
        confirmLabel: this.localize.term('jfbLibrary_renameTemplate'),
        name: template.name,
        description: template.description ?? null,
      },
    }).catch(() => undefined);
    if (!value) return;
    const { data, error } = await tryExecute(
      this,
      templateRename({ path: { key: template.key }, body: { name: value.name, description: value.description } }),
      quiet,
    );
    if (!data) {
      await sayRefusal(this, error);
      return;
    }
    await say(this, 'positive', this.localize.term('jfbLibrary_templateRenamed', data.name));
    await this.#read();
    await this.updateComplete;
    this.shadowRoot?.querySelector<HTMLElement>('.head uui-button[popovertarget]')?.focus();
  }

  async #delete(template: TemplateResponseModel) {
    try {
      await confirmText(this, {
        headline: this.localize.term('jfbLibrary_deleteTemplateHeadline', template.name),
        content: this.localize.term(
          template.kind === 'group' ? 'jfbLibrary_deleteGroupTemplateText' : 'jfbLibrary_deleteTemplateText',
        ),
        color: 'danger',
        confirmLabel: this.localize.term('jfbLibrary_deleteTemplate'),
      });
    } catch {
      return;
    }
    const { error } = await tryExecute(this, templateDelete({ path: { key: template.key } }), quiet);
    if (error) {
      await sayRefusal(this, error);
      return;
    }
    await say(this, 'positive', this.localize.term('jfbLibrary_templateDeleted', template.name));
    // The card after it in its part takes its place, else the one before, else the first; the focus goes with the choice.
    const part = Array.isArray(this._templates)
      ? this._templates.filter((one) => this.#partOf(one) === this.#partOf(template))
      : [];
    const at = part.findIndex((one) => one.key === template.key);
    this._chosen = (part[at + 1] ?? part[at - 1])?.key;
    await this.#read();
    await this.updateComplete;
    this.shadowRoot?.querySelector<HTMLInputElement>('.card input:checked')?.focus();
  }

  #card(template: TemplateResponseModel): TemplateResult {
    const line = this.#line(template);
    return html`<label class="card">
      <input
        type="radio"
        name="template"
        .value=${template.key}
        .checked=${template.key === this._chosen}
        @change=${() => (this._chosen = template.key)}
      />
      <span class="name">${this.#name(template)}</span>
      <span class="line">${line || nothing}</span>
    </label>`;
  }

  #more(template: TemplateResponseModel): TemplateResult {
    return html`<uui-button
        compact
        look="secondary"
        popovertarget="template-more"
        label=${this.localize.term('jfbLibrary_templateMore', template.name)}
      >
        <uui-symbol-more></uui-symbol-more>
      </uui-button>
      <uui-popover-container id="template-more" placement="bottom-end">
        <umb-popover-layout>
          <uui-menu-item
            label=${this.localize.term('jfbLibrary_actionRenameTemplate')}
            @click-label=${() => void this.#rename(template)}
          ></uui-menu-item>
          <uui-menu-item
            label=${this.localize.term('jfbLibrary_actionDeleteTemplate')}
            @click-label=${() => void this.#delete(template)}
          ></uui-menu-item>
        </umb-popover-layout>
      </uui-popover-container>`;
  }

  #preview(template: TemplateResponseModel): TemplateResult {
    const group = template.kind === 'group';
    return html`<section class="preview" aria-labelledby="preview-heading">
      <div class="head">
        <h3 id="preview-heading">${this.#name(template)}</h3>
        ${template.builtIn ? nothing : this.#more(template)}
        ${
          group || !this._mayUse
            ? nothing
            : html`<uui-button
                look="primary"
                color="positive"
                label=${this.localize.term('jfbLibrary_useTemplate')}
                .state=${this._busy ? 'waiting' : undefined}
                @click=${() => void this.#use(template)}
              ></uui-button>`
        }
      </div>
      <p class="muted">
        ${group ? this.localize.term('jfbLibrary_groupTemplateHelp') : this.localize.term('jfbLibrary_holds', template.holds.pages, template.holds.fields)}
      </p>
      <jfb-form-preview .definition=${template.definition}></jfb-form-preview>
    </section>`;
  }

  #body(): TemplateResult {
    const templates = this._templates;
    if (templates === undefined)
      return html`<uui-box>${skeletonLines(this.localize.term('jfbLibrary_templatesLoading'))}</uui-box>`;
    if (templates === 'failed')
      return html`<uui-box
        ><p class="alert" role="alert">${this.localize.term('jfbLibrary_templatesFailed')}</p></uui-box
      >`;
    const chosen = templates.find((one) => one.key === this._chosen);
    const parts = (['builtIn', 'forms', 'groups'] as const)
      .map((part) => ({ part, items: templates.filter((one) => this.#partOf(one) === part) }))
      .filter((each) => each.items.length > 0);
    return html`<div class="page">
      <fieldset>
        <legend class="visually-hidden">${this.localize.term('jfbLibrary_templatesLegend')}</legend>
        ${parts.map(
          ({ part, items }) =>
            html`<h3>${this.localize.term(`jfbLibrary_templatesPart_${part}`)}</h3>
              <div class="cards">${items.map((one) => this.#card(one))}</div>`,
        )}
      </fieldset>
      ${chosen ? this.#preview(chosen) : nothing}
    </div>`;
  }

  override render() {
    return html`<umb-body-layout><jfb-place-tabs tab="templates"></jfb-place-tabs>${this.#body()}</umb-body-layout>`;
  }
}

if (!customElements.get('jfb-templates-screen')) customElements.define('jfb-templates-screen', TemplatesScreenElement);

export { TemplatesScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-templates-screen': TemplatesScreenElement;
  }
}
