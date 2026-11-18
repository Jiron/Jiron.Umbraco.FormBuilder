// The language a form's views show, on a site with more than one (ADR-0258): a button that shows its culture and opens
// the site's languages by name, the same on the Design view and the Settings view, so the form has one control for it.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import type { SiteLanguage } from '../preview/look';
import { popoverMenu } from './canvas-field.element';

/**
 * The button and its menu of languages, with the culture shown marked; nothing on a site of one language. `pick` hears
 * another language chosen.
 */
export function languageButton(
  localize: UmbLocalizationController,
  id: string,
  languages: readonly SiteLanguage[],
  shown: string,
  pick: (culture: string) => void,
): TemplateResult | typeof nothing {
  if (languages.length < 2) return nothing;
  const current = languages.find((language) => language.culture.toLowerCase() === shown.toLowerCase());
  const culture = current?.culture ?? shown;
  const label = localize.term('jfbDesigner_languageButton', culture, current?.name ?? culture);
  return html`<button type="button" class="bo-language" popovertarget=${id} aria-label=${label} title=${label}>
      <span>${culture}</span><uui-icon name="icon-navigation-down" aria-hidden="true"></uui-icon>
    </button>
    ${popoverMenu(
      id,
      languages.map((language) => ({
        label: language.name,
        current: language === current,
        act: () => {
          if (language !== current) pick(language.culture);
        },
      })),
    )}`;
}

/** The language button's look: the culture in a box of the controls' border, which a hover fills. */
export const languageButtonStyles = css`
  .bo-language {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-1);
    min-height: 36px;
    padding: 0 var(--uui-size-space-3);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
    white-space: nowrap;
    cursor: pointer;
  }

  @media (hover: hover) {
    .bo-language:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .bo-language:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }
`;
