// An action's details in the audit log (ROADMAP 12.4b, IA 9, ADR-0041, ADR-0219), in a sidebar over the list: its sentence,
// who, when (with the zone the time is written in) and where; a grant's permissions before and after; the settings a save
// changed, by their names, as the log never keeps their values; and what else its details hold. A form's save, publish or
// restore opens its History. "Show only their actions" narrows the log to the person who did it.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { FORM_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { dateOf, fullTimeOf } from './activity-words';
import type { AuditEntryModalData, AuditEntryModalValue } from './audit-entry.modal.token';
import {
  DETAIL_ROWS,
  detailText,
  factsOf,
  grantChangeOf,
  saysAlready,
  sentenceOf,
  whereOf,
  whoOf,
} from './audit-words';

/** The actions on a form whose version its History lists. */
const VERSIONED = new Set([
  'form.saved',
  'form.published',
  'form.versionRestored',
  'form.versionPinned',
  'form.versionUnpinned',
  'form.versionCommented',
]);

export class AuditEntryModalElement extends UmbModalBaseElement<AuditEntryModalData, AuditEntryModalValue> {
  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      .body {
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        gap: var(--uui-size-space-5);
      }

      dl {
        display: grid;
        grid-template-columns: max-content minmax(0, 1fr);
        gap: var(--uui-size-space-3) var(--uui-size-space-5);
        margin: 0;
      }

      dt {
        font-weight: 600;
      }

      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }

      table {
        width: 100%;
        border-collapse: collapse;
      }

      th,
      td {
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border-bottom: 1px solid var(--uui-color-divider);
        text-align: start;
        vertical-align: top;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      p {
        margin: 0;
      }

      a {
        color: var(--uui-color-interactive);
        text-decoration: underline;
      }

      a:hover {
        color: var(--uui-color-interactive-emphasis);
      }
    `,
  ];

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActivity_${key}`, ...args);

  #section = (key: string) => this.localize.term(`jfbSection_${key}`);

  #setting = (section: string, field: string) => {
    const key = `jfbGlobalSettings_${section}_${field}`;
    const said = this.localize.term(key);
    return said === key ? '' : said;
  };

  #permission = (name: string) => this.localize.term(`jfbLibrary_permission_${name}`);

  // A grant's permissions before and after, as a table: every permission it had or has.
  #grants(): TemplateResult | typeof nothing {
    const data = this.data;
    const entry = data?.entry;
    if (!data || !entry || entry.action !== 'access.grantChanged') return nothing;
    const rows = grantChangeOf(entry);
    if (rows.length === 0) return nothing;
    const mark = (on: boolean) => this.#term(on ? 'granted' : 'notGranted');
    return html`<table>
      <caption class="visually-hidden">
        ${sentenceOf(entry, factsOf(entry, data.names, this.#term, this.#setting), this.#term)}
      </caption>
      <thead>
        <tr>
          <th scope="col">${this.#term('permission')}</th>
          <th scope="col">${this.#term('before')}</th>
          <th scope="col">${this.#term('after')}</th>
        </tr>
      </thead>
      <tbody>
        ${rows.map(
          (row) =>
            html`<tr>
              <th scope="row">${this.#permission(row.name)}</th>
              <td class=${row.before ? '' : 'muted'}>${mark(row.before)}</td>
              <td class=${row.after ? '' : 'muted'}>${mark(row.after)}</td>
            </tr>`,
        )}
      </tbody>
    </table>`;
  }

  // The settings a save changed, by their names; the log keeps no value, which one line says.
  #settings(): TemplateResult | typeof nothing {
    const entry = this.data?.entry;
    if (!entry || entry.action !== 'settings.saved') return nothing;
    const details = (entry.details ?? {}) as Record<string, unknown>;
    const section = typeof details['section'] === 'string' ? details['section'] : '';
    const fields = Array.isArray(details['fields'])
      ? details['fields'].filter((field): field is string => typeof field === 'string')
      : [];
    const names = fields.map((field) => this.#setting(section, field.split('.')[0] ?? '') || field);
    return html`${
        names.length > 0
          ? html`<dl>
              <dt>${this.#term('changed')}</dt>
              <dd>${[...new Set(names)].join(', ')}</dd>
            </dl>`
          : nothing
      }
      <p class="muted">${this.#term('valuesNotKept')}</p>`;
  }

  // What else the details hold, each with its words.
  #rest(): TemplateResult | typeof nothing {
    const data = this.data;
    if (!data || data.entry.action === 'settings.saved' || data.entry.action === 'access.grantChanged') return nothing;
    const details = (data.entry.details ?? {}) as Record<string, unknown>;
    const rows = DETAIL_ROWS.filter((name) => !saysAlready(data.entry.action, name))
      .map((name) => [name, detailText(details[name], data.names, this.#term)] as const)
      .filter(([, text]) => text !== '');
    if (rows.length === 0) return nothing;
    return html`<dl>
      ${rows.map(
        ([name, text]) =>
          html`<dt>${this.#term(`detail_${name}`)}</dt>
            <dd>${text}</dd>`,
      )}
    </dl>`;
  }

  #pickUser() {
    const entry = this.data?.entry;
    if (!entry?.userKey || !entry.userName) return;
    this.value = { user: { key: entry.userKey, name: entry.userName } };
    this._submitModal();
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    const { entry, names } = data;
    const facts = factsOf(entry, names, this.#term, this.#setting);
    const at = dateOf(entry.occurredUtc);
    const full = at ? fullTimeOf(at, this.localize.lang()) : undefined;
    const history =
      VERSIONED.has(entry.action) && entry.entityKey && names[entry.entityKey]
        ? `${screenPath(FORM_ENTITY_TYPE, entry.entityKey)}/view/history`
        : undefined;
    return html`<umb-body-layout headline=${sentenceOf(entry, facts, this.#term)}>
      <div class="body">
        <dl>
          <dt>${this.#term('detailWho')}</dt>
          <dd>${whoOf(entry, this.#term)}</dd>
          <dt>${this.#term('detailWhen')}</dt>
          <dd>${full ? this.#term('whenWithZone', full.time, full.zone) : ''}</dd>
          <dt>${this.#term('detailWhere')}</dt>
          <dd>${whereOf(entry, names, this.#term, this.#section)}</dd>
        </dl>
        ${this.#grants()} ${this.#settings()} ${this.#rest()}
        ${history ? html`<p><a href=${history} @click=${() => this._rejectModal()}>${this.#term('openHistory')}</a></p>` : nothing}
      </div>
      <div slot="actions">
        ${
          entry.userKey && entry.userName
            ? html`<uui-button
                look="secondary"
                label=${this.#term('showOnly', entry.userName)}
                @click=${() => this.#pickUser()}
              ></uui-button>`
            : nothing
        }
        <uui-button label=${this.#term('close')} @click=${this._rejectModal}></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-audit-entry-modal'))
  customElements.define('jfb-audit-entry-modal', AuditEntryModalElement);

export { AuditEntryModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-audit-entry-modal': AuditEntryModalElement;
  }
}
