// The Keyboard shortcuts dialog's token (ROADMAP 9.3c).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export const SHORTCUTS_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Shortcuts';

export const SHORTCUTS_MODAL = new UmbModalToken<never, never>(SHORTCUTS_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
