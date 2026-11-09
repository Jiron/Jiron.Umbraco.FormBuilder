// The erase dialog of Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0223): what the erase takes, per form, with the
// files and mail that go and the entries a legal hold keeps; whether the entries go or are anonymised; and Cancel,
// focused, beside the red button that names the count. Nothing changes until that button is pressed; the dialog then
// erases and answers what it took.
import { css, html, nothing, type PropertyValues, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { peopleErase, type PersonEraseFormResponseModel } from '../api';
import { jfbBackofficeTokens } from '../styles/tokens';
import { bodyOf } from './people-api';
import type { PeopleEraseModalData, PeopleEraseModalValue } from './people-erase.modal.token';

type Mode = 'delete' | 'anonymise';

export class PeopleEraseModalElement extends UmbModalBaseElement<PeopleEraseModalData, PeopleEraseModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _mode: { state: true },
    _erasing: { state: true },
    _failed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-dialog-layout {
        inline-size: min(560px, calc(100vw - 2 * var(--uui-size-space-6)));
        box-sizing: border-box;
      }

      p {
        margin: 0 0 var(--uui-size-space-4);
      }

      .forms {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: var(--uui-size-space-2) var(--uui-size-space-5);
        margin: 0 0 var(--uui-size-space-5);
      }

      .forms dt,
      .forms dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      .forms dd {
        color: var(--jfb-bo-color-text-muted);
        text-align: end;
      }

      /* On a phone, a form's counts go under its name, so neither is squeezed into a sliver. */
      @media (max-width: 480px) {
        .forms {
          grid-template-columns: minmax(0, 1fr);
          gap: 0;
        }

        .forms dd {
          margin-block-end: var(--uui-size-space-3);
          text-align: start;
        }
      }

      fieldset {
        margin: 0;
        padding: 0;
        border: 0;
      }

      legend {
        margin-block-end: var(--uui-size-space-3);
        padding: 0;
        font-weight: 700;
      }

      .choice {
        display: grid;
        grid-template-columns: auto minmax(0, 1fr);
        gap: 0 var(--uui-size-space-3);
        align-items: baseline;
        margin-block-end: var(--uui-size-space-3);
        cursor: pointer;
      }

      .choice input {
        margin: 0;
      }

      .choice .help {
        grid-column: 2;
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .alert {
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _mode: Mode;
  declare private _erasing: boolean;
  declare private _failed: boolean;

  constructor() {
    super();
    this._mode = 'delete';
    this._erasing = false;
    this._failed = false;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbGlobalSettings_${key}`, ...args);

  #count = (key: string, count: number) => this.#term(key, this.localize.number(count), count === 1);

  override firstUpdated(changed: PropertyValues) {
    super.firstUpdated(changed);
    // Cancel takes the focus: the red button is never the one Enter presses by chance.
    void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('#cancel')?.focus());
  }

  async #erase() {
    const data = this.data;
    if (!data || this._erasing) return;
    this._erasing = true;
    this._failed = false;
    const { data: erased } = await tryExecute(
      this,
      peopleErase({ body: { ...bodyOf(data.person), mode: this._mode } }),
      {
        disableNotifications: true,
      },
    );
    this._erasing = false;
    if (!erased) {
      this._failed = true;
      return;
    }
    this.value = { erased };
    this._submitModal();
  }

  #form(form: PersonEraseFormResponseModel): TemplateResult {
    const parts = [
      ...(form.entries > 0 ? [this.#count('people_entries', form.entries)] : []),
      ...(form.drafts > 0 ? [this.#count('people_drafts', form.drafts)] : []),
      ...(form.files > 0 ? [this.#count('people_files', form.files)] : []),
      ...(form.emails > 0 ? [this.#count('people_emails', form.emails)] : []),
      ...(form.held > 0 ? [this.#term('people_eraseFormHeld', this.localize.number(form.held))] : []),
    ];
    return html`<dt>${form.name}</dt>
      <dd>${parts.join(', ')}</dd>`;
  }

  #choice(mode: Mode, label: string, help: string): TemplateResult {
    return html`<label class="choice">
      <input
        type="radio"
        name="mode"
        .checked=${this._mode === mode}
        aria-describedby=${`${mode}-help`}
        @change=${() => (this._mode = mode)}
      />
      <span>${label}</span>
      <span class="help" id=${`${mode}-help`}>${help}</span>
    </label>`;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const { preview } = data;
    const taken = preview.entries + preview.drafts;
    const forms = preview.forms.filter((form) => form.entries + form.drafts > 0).length;
    // The entries and their forms, then the files and the mail where there are any.
    const what = [
      this.#term('people_found', this.#count('people_entries', taken), this.#count('people_forms', forms)),
      ...(preview.files > 0 ? [this.#count('people_files', preview.files)] : []),
      ...(preview.emails > 0 ? [this.#count('people_emails', preview.emails)] : []),
    ];
    return html`<uui-dialog-layout headline=${this.#term('people_eraseHeadline', data.who)}>
      ${
        taken === 0
          ? html`<p>${this.#term('people_eraseNothing')}</p>`
          : html`<p id="what">${this.#term('people_eraseText', what, what.length === 1 && taken === 1)}</p>`
      }
      ${
        preview.held > 0
          ? html`<p>
              ${this.#term('people_eraseHeld', this.#count('people_entries', preview.held), preview.held === 1)}
            </p>`
          : nothing
      }
      ${preview.more ? html`<p>${this.#term('people_eraseMore')}</p>` : nothing}
      <dl class="forms">${preview.forms.map((form) => this.#form(form))}</dl>
      ${
        preview.entries > 0
          ? html`<fieldset>
              <legend>${this.#term('people_eraseHow')}</legend>
              ${this.#choice('delete', this.#term('people_eraseDelete'), this.#term('people_eraseDeleteHelp'))}
              ${this.#choice('anonymise', this.#term('people_eraseAnonymise'), this.#term('people_eraseAnonymiseHelp'))}
            </fieldset>`
          : nothing
      }
      ${this._failed ? html`<p class="alert" role="alert">${this.#term('people_eraseFailed')}</p>` : nothing}
      <uui-button
        id="cancel"
        slot="actions"
        label=${this.localize.term('general_cancel')}
        @click=${this._rejectModal}
      ></uui-button>
      ${
        taken > 0
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="danger"
              label=${this.#term('people_eraseConfirm', this.#count('people_entries', taken))}
              .state=${this._erasing ? 'waiting' : undefined}
              @click=${() => void this.#erase()}
            ></uui-button>`
          : nothing
      }
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-people-erase-modal'))
  customElements.define('jfb-people-erase-modal', PeopleEraseModalElement);

export { PeopleEraseModalElement as element };
