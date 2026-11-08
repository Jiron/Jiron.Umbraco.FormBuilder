// The global settings' own editors (ROADMAP 12.4a, IA 8, ADR-0218): the settings the inspector's editors draw none of,
// each named by its section's schema. The time zone is a list grouped by area, never typed (ADR-0043); the theme and
// the layout are the site's own; the protection level is one list of choices with each captcha's key status, a captcha
// without keys not to be chosen; the captcha's consent greys out the mode the host doesn't allow, with its key; a new
// form's consent field is a switch with its label and wording under it; a secret is only ever a reference, said found or
// not; and the files' types and largest size say what the host configuration sets besides.
import { html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import type { FieldSettingResponseModel, SettingsSectionResponseModel } from '../api';
import type { LocalizedText } from '../designer/definition';
import { boxValue } from '../designer/inspector/box-value';
import '../designer/inspector/rich-text.element';
import { boxId, settingEditor, settingFrame, type EditorContext } from '../designer/inspector/setting-editor';
import { parseChoices } from '../designer/inspector/setting-values';
import { textIn } from '../designer/preview/field-models';
import { browserZones, bytesOf, isSecretReference, megabytesOf, zoneGroups, zoneName } from './settings-values';

/** What the screen gives its editors. */
export interface SettingsEditorHost {
  readonly section: SettingsSectionResponseModel;
  /** The values as the screen holds them now, changes not yet saved among them. */
  readonly values: Readonly<Record<string, unknown>>;
  /** Whether the screen shows one site's values, rather than every site's. */
  readonly site: boolean;
  /** The inspector's context, whose problems and words the editors share. */
  readonly context: EditorContext;
  /** A word of the global settings' own area, without its `jfbGlobalSettings_`. */
  readonly words: (key: string, ...args: unknown[]) => string;
  /** Hands on a value that can be kept, for the setting at a path; null leaves the setting to its default. */
  readonly put: (path: string, value: unknown) => void;
  /** Says why a box's value can't be kept, by the box's id; null once it can. */
  readonly problem: (id: string, text: string | null) => void;
}

type Editor = (setting: FieldSettingResponseModel, value: unknown, path: string) => TemplateResult | typeof nothing;

const text = (value: unknown): string => (typeof value === 'string' ? value : '');

/** A reference's example name from its setting's: Signing for signingSecret, Slack for slackWebhook. */
const exampleOf = (name: string): string => {
  const first = /^[a-z]+/.exec(name)?.[0] ?? name;
  return first.charAt(0).toUpperCase() + first.slice(1);
};

/** The editors of the global settings, by the aliases their schemas name. */
export function settingsEditors(host: SettingsEditorHost): Readonly<Record<string, Editor>> {
  const { context, words } = host;
  const facts = host.section.facts;

  // A select of a list's choices, the default's choice where the section names none; one the site no longer has stays
  // chosen, said as such, until another is picked. All sites store the default as none, a site its own choice.
  const choiceOf = (
    setting: FieldSettingResponseModel,
    value: unknown,
    path: string,
    choices: readonly { readonly value: string; readonly label: string }[],
    fallback: string,
  ) => {
    const chosen = text(value) || fallback;
    const known = choices.some((choice) => choice.value === chosen);
    return settingFrame(
      context,
      setting,
      path,
      (described, invalid) =>
        html`<select
          id=${boxId(path)}
          .value=${live(chosen)}
          aria-describedby=${described ?? nothing}
          aria-invalid=${invalid ? 'true' : 'false'}
          @change=${(event: Event) => {
            const picked = (event.target as HTMLSelectElement).value;
            host.put(path, picked === fallback && !host.site ? null : picked);
          }}
        >
          ${choices.map(
            (choice) =>
              html`<option value=${choice.value} ?selected=${choice.value === chosen}>
                ${context.say(choice.label)}
              </option>`,
          )}
          ${known ? nothing : html`<option value=${chosen} selected disabled>${chosen}</option>`}
        </select>`,
    );
  };

  // A radio for each choice, its name and its line under it, the whole row a target; one that can't be chosen is greyed
  // and says why on its line.
  const radios = (
    setting: FieldSettingResponseModel,
    path: string,
    rows: readonly {
      readonly value: string;
      readonly name: string;
      readonly line: string;
      readonly disabled?: boolean;
      readonly checked: boolean;
      readonly pick: () => void;
    }[],
    below: TemplateResult | typeof nothing = nothing,
  ) => {
    const id = boxId(path);
    const help = setting.description ? `${id}-help` : undefined;
    const problem = context.problems.get(id);
    return html`<fieldset
      class="item"
      id=${id}
      aria-describedby=${[help, problem ? `${id}-why` : undefined].filter(Boolean).join(' ') || nothing}
    >
      <legend class="label">${context.say(setting.label)}</legend>
      ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
      <div class="choices">
        ${rows.map((row) => {
          const rowId = `${id}-${row.value.replace(/[^a-zA-Z0-9]/g, '-')}`;
          return html`<label class="choice" ?data-dimmed=${row.disabled}>
            <input
              type="radio"
              name=${id}
              value=${row.value}
              .checked=${live(row.checked)}
              ?disabled=${row.disabled}
              aria-labelledby=${`${rowId}-name`}
              aria-describedby=${`${rowId}-line`}
              @change=${row.pick}
            />
            <span class="choice-name" id=${`${rowId}-name`}>${row.name}</span>
            <span class="choice-line" id=${`${rowId}-line`}>${row.line}</span>
          </label>`;
        })}
      </div>
      ${below} ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
    </fieldset>`;
  };

  return {
    theme: (setting, value, path) =>
      facts.general ? choiceOf(setting, value, path, facts.general.themes, facts.general.defaultTheme) : nothing,

    emailLayout: (setting, value, path) =>
      facts.email ? choiceOf(setting, value, path, facts.email.layouts, facts.email.defaultLayout) : nothing,

    // The browser's zones, by area in a native select, which keeps its type-ahead and is said as a list.
    timeZone: (setting, value, path) => {
      const chosen = text(value);
      return settingFrame(
        context,
        setting,
        path,
        (described, invalid) =>
          html`<select
            id=${boxId(path)}
            .value=${live(chosen)}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            @change=${(event: Event) => host.put(path, (event.target as HTMLSelectElement).value)}
          >
            ${zoneGroups(browserZones(), chosen).map(
              (group) =>
                html`<optgroup label=${group.area || words('zonesWithoutArea')}>
                  ${group.zones.map(
                    (zone) => html`<option value=${zone} ?selected=${zone === chosen}>${zoneName(zone)}</option>`,
                  )}
                </optgroup>`,
            )}
          </select>`,
      );
    },

    // The address the scope's links start with; empty, the one the site gets without it, which the box shows.
    baseUrl: (setting, value, path) =>
      settingFrame(
        context,
        setting,
        path,
        (described, invalid) =>
          html`<input
            type="url"
            id=${boxId(path)}
            inputmode="url"
            spellcheck="false"
            autocomplete="off"
            placeholder=${facts.general?.defaultBaseUrl ?? nothing}
            aria-describedby=${described ?? nothing}
            aria-invalid=${invalid ? 'true' : 'false'}
            .value=${live(text(value))}
            @change=${(event: Event) => host.put(path, (event.target as HTMLInputElement).value.trim() || null)}
          />`,
      ),

    // Addresses, one a line, as the inspector's list of texts takes them; the server checks each.
    addresses: (setting, value, path) => settingEditor(context, { ...setting, editor: 'list' }, value, path),

    // The level and the captcha are one list: the levels, then each captcha with its key status.
    protectionLevel: (setting, value, path) => {
      const captchas = facts.protection?.captchas ?? [];
      const level = text(value);
      const provider = text(host.values['captchaProvider']);
      const pick = (next: string, alias: string | null) => () => {
        host.put(path, next);
        host.put('captchaProvider', alias);
      };
      const rows = [
        ...setting.choices
          .filter((choice) => choice.value !== 'captcha')
          .map((choice) => ({
            value: choice.value,
            name: context.say(choice.label),
            line: context.say(`${choice.label}Line`),
            checked: level === choice.value,
            pick: pick(choice.value, null),
          })),
        ...captchas.map((captcha) => {
          const checked = level === 'captcha' && provider === captcha.alias;
          return {
            value: `captcha:${captcha.alias}`,
            name: captcha.name,
            line: captcha.hasKeys ? words('captchaKeys') : words('captchaNoKeys', captcha.keysAt),
            disabled: !captcha.hasKeys && !checked,
            checked,
            pick: pick('captcha', captcha.alias),
          };
        }),
        // A captcha the site no longer has stays chosen until another choice, said as such.
        ...(level === 'captcha' && !captchas.some((captcha) => captcha.alias === provider)
          ? [
              {
                value: `captcha:${provider}`,
                name: provider,
                line: words('captchaGone'),
                disabled: true,
                checked: true,
                pick: pick('captcha', provider),
              },
            ]
          : []),
      ];
      return radios(setting, path, rows);
    },

    // Folded into the level's list.
    captchaProvider: () => nothing,

    // Each mode with its line; the implicit one, which the host must allow, greyed with the key that allows it.
    captchaConsent: (setting, value, path) => {
      const chosen = text(value);
      const allowed = facts.protection?.implicitConsent ?? false;
      const key = host.section.locks.find((lock) => lock.field === setting.name)?.configurationKey ?? '';
      return radios(
        setting,
        path,
        setting.choices.map((choice) => {
          const refused = choice.value === 'implicit' && !allowed;
          return {
            value: choice.value,
            name: context.say(choice.label),
            line: refused ? words('implicitLocked', key) : context.say(`${choice.label}Line`),
            disabled: refused && chosen !== choice.value,
            checked: chosen === choice.value,
            pick: () => host.put(path, choice.value),
          };
        }),
      );
    },

    // A switch, and once it is on the field's label and wording under it, in the language the screen edits.
    newFormConsent: (setting, value, path) => {
      const consent =
        value && typeof value === 'object' ? (value as { label?: LocalizedText; text?: LocalizedText }) : null;
      const id = boxId(path);
      const help = setting.description ? `${id}-help` : undefined;
      const inner = (name: 'label' | 'text', kind: 'localizedText' | 'localizedHtml'): FieldSettingResponseModel => ({
        ...setting,
        name,
        kind,
        editor: kind,
        label: words(name === 'label' ? 'consentLabel' : 'consentText'),
        description: null,
        rules: { ...setting.rules, maximumLength: name === 'label' ? 500 : 50_000 },
        unit: null,
      });
      return html`<div class="item with-detail">
        <div class="item">
          <div class="item-switch">
            <input
              type="checkbox"
              role="switch"
              id=${id}
              aria-describedby=${help ?? nothing}
              .checked=${live(consent !== null)}
              @change=${(event: Event) =>
                host.put(path, (event.target as HTMLInputElement).checked ? { label: {}, text: {} } : null)}
            />
            <label class="label" for=${id}>${context.say(setting.label)}</label>
          </div>
          ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
        </div>
        ${
          consent
            ? html`<div class="detail">
                ${settingEditor(context, inner('label', 'localizedText'), consent.label, `${path}.label`)}
                ${settingEditor(context, inner('text', 'localizedHtml'), consent.text, `${path}.text`)}
              </div>`
            : nothing
        }
      </div>`;
    },

    // A reference to a secret in the host configuration, never the secret itself, said found or not once it is saved.
    secretReference: (setting, value, path) => {
      const id = boxId(path);
      const shown = text(value);
      const saved = text((host.section.values as Record<string, unknown>)[setting.name]);
      const found = facts.integrations?.found.includes(setting.name) ?? false;
      const status =
        shown !== '' && shown === saved && isSecretReference(shown)
          ? found
            ? words('referenceFound')
            : words('referenceMissing', shown.slice(1))
          : '';
      const typed = (raw: string, done: boolean) => {
        const reference = raw.trim();
        if (reference !== '' && !isSecretReference(reference)) {
          if (done) host.problem(id, words('referenceInvalid'));
          return;
        }
        host.problem(id, null);
        host.put(path, reference || null);
      };
      return settingFrame(
        context,
        setting,
        path,
        (described, invalid, held) =>
          html`<input
              type="text"
              id=${id}
              class="code"
              spellcheck="false"
              autocomplete="off"
              placeholder=${words('referencePlaceholder', exampleOf(setting.name))}
              aria-describedby=${described ?? nothing}
              aria-invalid=${invalid ? 'true' : 'false'}
              .value=${boxValue(shown, held)}
              @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
              @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
            />
            ${status ? html`<p class="help" id=${`${id}-status`} data-result=${found ? 'found' : 'missing'}>${status}</p>` : nothing}`,
        status ? `${id}-status` : undefined,
      );
    },

    // The types a site takes, each with its extensions; what the host never takes is said once under them.
    fileTypes: (setting, value, path) => {
      const id = boxId(path);
      const chosen = Array.isArray(value) ? value.map(String) : [];
      const groups = facts.files?.groups ?? [];
      const problem = context.problems.get(id);
      const help = setting.description ? `${id}-help` : undefined;
      return html`<fieldset
        class="item"
        id=${id}
        aria-describedby=${[help, `${id}-blocked`, problem ? `${id}-why` : undefined].filter(Boolean).join(' ')}
      >
        <legend class="label">${context.say(setting.label)}</legend>
        ${help ? html`<p class="help" id=${help}>${context.say(setting.description)}</p>` : nothing}
        <div class="choices">
          ${setting.choices.map((choice) => {
            const extensions = groups.find((group) => group.value === choice.value)?.extensions ?? [];
            const rowId = `${id}-${choice.value}`;
            return html`<label class="choice">
              <input
                type="checkbox"
                value=${choice.value}
                .checked=${live(chosen.includes(choice.value))}
                aria-labelledby=${`${rowId}-name`}
                aria-describedby=${`${rowId}-line`}
                @change=${(event: Event) => {
                  const on = (event.target as HTMLInputElement).checked;
                  const next = setting.choices
                    .map((each) => each.value)
                    .filter((each) => (each === choice.value ? on : chosen.includes(each)));
                  const parsed = parseChoices(setting, next);
                  context.set(path, setting, parsed, true);
                }}
              />
              <span class="choice-name" id=${`${rowId}-name`}>${context.say(choice.label)}</span>
              <span class="choice-line" id=${`${rowId}-line`}
                >${extensions.map((one) => one.toUpperCase()).join(', ')}</span
              >
            </label>`;
          })}
        </div>
        <p class="help" id=${`${id}-blocked`}>${words('blockedExtensions', facts.files?.blockedExtensionsKey ?? '')}</p>
        ${problem ? html`<p class="why" id=${`${id}-why`}>${problem}</p>` : nothing}
      </fieldset>`;
    },

    // The largest file in megabytes; empty, the host's cap, which the box shows and the line under it names.
    fileSize: (setting, value, path) => {
      const id = boxId(path);
      const cap = facts.files?.hostMaxFileBytes ?? 0;
      const most = megabytesOf(cap);
      const typed = (raw: string, done: boolean) => {
        const bytes = bytesOf(raw);
        if (bytes === 'invalid' || (bytes !== null && bytes > cap)) {
          if (done) host.problem(id, words('sizeInvalid', most));
          return;
        }
        host.problem(id, null);
        host.put(path, bytes);
      };
      const shown = typeof value === 'number' ? megabytesOf(value) : '';
      return settingFrame(
        context,
        setting,
        path,
        (described, invalid, held) =>
          html`<div class="with-unit">
              <input
                type="text"
                id=${id}
                inputmode="decimal"
                autocomplete="off"
                placeholder=${most}
                aria-describedby=${described ?? nothing}
                aria-invalid=${invalid ? 'true' : 'false'}
                .value=${boxValue(shown, held)}
                @input=${(event: Event) => typed((event.target as HTMLInputElement).value, false)}
                @change=${(event: Event) => typed((event.target as HTMLInputElement).value, true)}
              />
              <span id=${`${id}-unit`}>${context.say(setting.unit)}</span>
            </div>
            <p class="help" id=${`${id}-cap`}>${words('hostCap', most, facts.files?.maxFileBytesKey ?? '')}</p>`,
        `${id}-unit ${id}-cap`,
      );
    },
  };
}

/** The consent field's label in the site's default language, which a new form's consent field needs. */
export const consentLabelOf = (consent: unknown): string =>
  consent && typeof consent === 'object' ? textIn((consent as { label?: LocalizedText }).label, '') : '';
