// The create dialog of themes (ROADMAP 12.1, IA 7, ADR-0215), as a token the list screen opens without loading the
// element.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { ThemeBaseResponseModel } from '../api';
import { NEW_THEME_MODAL_ALIAS } from './constants';

/** The themes in code a new theme may build on. */
export interface NewThemeModalData {
  readonly bases: readonly Pick<ThemeBaseResponseModel, 'alias' | 'label' | 'darkScheme'>[];
}

/** The base theme chosen. */
export interface NewThemeModalValue {
  readonly baseTheme: string;
}

export const NEW_THEME_MODAL = new UmbModalToken<NewThemeModalData, NewThemeModalValue>(NEW_THEME_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
