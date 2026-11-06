// Restore an earlier version... (ROADMAP 10.3b, IA 5.6, ADR-0198): Umbraco's medium sidebar listing the form's earlier
// versions by date, newest first, with Show draft saves; choosing one opens its comparison with the current draft on top.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FormVersionEntryResponseModel } from '../../api';
import { VERSION_PICKER_MODAL_ALIAS } from '../constants';
import type { VersionPage } from './versions';

export interface VersionPickerModalData {
  /** A page of the form's versions, newest first, the draft saves too when asked; undefined without an answer. */
  readonly read: (includeDrafts: boolean, skip: number) => Promise<VersionPage | undefined>;
  /** Opens a version compared with the current draft; true once it was restored there, which closes the sidebar. */
  readonly choose: (entry: FormVersionEntryResponseModel) => Promise<boolean>;
}

export const VERSION_PICKER_MODAL = new UmbModalToken<VersionPickerModalData, never>(VERSION_PICKER_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'medium' },
});
