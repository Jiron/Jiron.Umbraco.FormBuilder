// The Change type dialog (ROADMAP 9.4d, ADR-0174): the types the selected field may become, in the browser's own select
// by palette group, and what the type chosen takes from the field and what it then needs, before anything changes. A
// type the form or the site refuses is marked unavailable and, chosen, says why, so the keys and screen readers reach
// the reason too. Change type stays off until a type it may become is chosen.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { ChangeTypeModalData, ChangeTypeModalValue, ChangeTypeTarget } from './change-type.modal.token';

export class ChangeTypeModalElement extends UmbModalBaseElement<ChangeTypeModalData, ChangeTypeModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _chosen: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      /* One width for every type chosen, so the dialog never jumps as the preview changes. */
      uui-dialog-layout {
        inline-size: min(520px, calc(100vw - 2 * var(--uui-size-space-6)));
        box-sizing: border-box;
      }

      label {
        display: block;
        margin-block-end: var(--uui-size-space-2);
        font-weight: 700;
      }

      select {
        box-sizing: border-box;
        inline-size: 100%;
        min-height: 36px;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
      }

      select:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 0;
        border-color: var(--jfb-bo-color-focus);
      }

      .now {
        margin: var(--uui-size-space-2) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      /*
       * Room for what most types take and need, so the centred dialog and its list keep their place while the arrow
       * keys go through the types; a longer answer still grows it.
       */
      .preview {
        min-block-size: 10rem;
        margin-block-start: var(--uui-size-space-5);
      }

      h3 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-2);
        font-size: var(--uui-type-default-size);
      }

      .preview > h3:first-child {
        margin-block-start: 0;
      }

      ul {
        display: grid;
        gap: var(--uui-size-space-1);
        margin: 0;
        padding-inline-start: var(--uui-size-space-5);
      }

      li {
        overflow-wrap: anywhere;
      }

      p {
        margin: 0;
      }

      .help {
        margin-block-start: var(--uui-size-space-5);
        color: var(--jfb-bo-color-text-muted);
      }

      .why {
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _chosen: string | null;

  override connectedCallback() {
    super.connectedCallback();
    this._chosen = null;
  }

  protected override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    // The dialog takes the focus itself as it opens, so the list takes it in the frame after.
    requestAnimationFrame(() => this.shadowRoot?.querySelector<HTMLSelectElement>('select')?.focus());
  }

  #target(): ChangeTypeTarget | undefined {
    return this.data?.targets.find((target) => target.alias === this._chosen);
  }

  /** Changes to the type chosen; false while none is, or the one chosen is refused. */
  #change(): boolean {
    const target = this.#target();
    if (!target || target.refusal !== null) return false;
    this.updateValue({ to: target.alias });
    this._submitModal();
    return true;
  }

  #groups(): { readonly name: string; readonly targets: ChangeTypeTarget[] }[] {
    const groups: { name: string; targets: ChangeTypeTarget[] }[] = [];
    for (const target of this.data?.targets ?? []) {
      const last = groups.at(-1);
      if (last?.name === target.group) last.targets.push(target);
      else groups.push({ name: target.group, targets: [target] });
    }
    return groups;
  }

  #list(heading: string, items: readonly string[]) {
    return items.length > 0
      ? html`<h3>${heading}</h3>
          <ul>
            ${items.map((item) => html`<li>${item}</li>`)}
          </ul>`
      : nothing;
  }

  #preview(target: ChangeTypeTarget | undefined) {
    if (!target) return html`<p class="help">${this.localize.term('jfbDesigner_changeTypeChoose')}</p>`;
    if (target.refusal !== null) return html`<p class="why">${target.refusal}</p>`;
    const { goes, locked, needs, entries } = target.words;
    const nothingGoes = goes.length === 0 && locked.length === 0 && needs.length === 0;
    return html`${this.#list(this.localize.term('jfbDesigner_changeTypeGoes'), goes)}
    ${this.#list(this.localize.term('jfbDesigner_changeTypeLocked'), locked)}
    ${this.#list(this.localize.term('jfbDesigner_changeTypeNeeds'), needs)}
    ${nothingGoes ? html`<p>${this.localize.term('jfbDesigner_changeTypeKeepsAll')}</p>` : nothing}
    ${entries ? html`<p class="help">${entries}</p>` : nothing}`;
  }

  override render() {
    const data = this.data;
    const name = data?.name ?? '';
    const target = this.#target();
    return html`<uui-dialog-layout headline=${this.localize.term('jfbDesigner_changeTypeHeadline', name)}>
      <label for="type">${this.localize.term('jfbDesigner_changeTypeLabel')}</label>
      <select
        id="type"
        aria-describedby="now"
        @change=${(event: Event) => (this._chosen = (event.target as HTMLSelectElement).value || null)}
        @keydown=${(event: KeyboardEvent) => {
          if (event.key === 'Enter' && this.#change()) event.preventDefault();
        }}
      >
        <option value="" ?selected=${this._chosen === null}>${this.localize.term('jfbDesigner_changeTypeNone')}</option>
        ${this.#groups().map(
          (group) =>
            html`<optgroup label=${group.name}>
              ${group.targets.map(
                (each) =>
                  html`<option value=${each.alias} ?selected=${this._chosen === each.alias}>
                    ${each.refusal === null ? each.name : this.localize.term('jfbDesigner_changeTypeUnavailable', each.name)}
                  </option>`,
              )}
            </optgroup>`,
        )}
      </select>
      <p class="now" id="now">${this.localize.term('jfbDesigner_changeTypeNow', data?.type ?? '')}</p>
      <div class="preview" aria-live="polite">${this.#preview(target)}</div>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        ?disabled=${!target || target.refusal !== null}
        label=${this.localize.term('jfbDesigner_changeTypeConfirm')}
        @click=${() => this.#change()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-change-type-modal'))
  customElements.define('jfb-change-type-modal', ChangeTypeModalElement);

export { ChangeTypeModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-change-type-modal': ChangeTypeModalElement;
  }
}
