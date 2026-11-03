// The section's names that several areas share, without importing their manifests.

/** The section's alias (D12), which user groups store. */
export const SECTION_ALIAS = 'Jiron.FormBuilder.Section';

/** The section's address, as the server's links name it (`BackofficeLinks.SectionPathname`, ADR-0067). */
export const SECTION_PATHNAME = 'jfb-forms';

/** The entity type of the tree's root, which the sidebar header's "+" and "..." act on. */
export const LIBRARY_ROOT_ENTITY_TYPE = 'jfb-library-root';

/** The entity types with a screen of their own that no menu item opens: a form, a folder, an entry. */
export const FORM_ENTITY_TYPE = 'jfb-form';
export const FOLDER_ENTITY_TYPE = 'jfb-folder';
export const ENTRY_ENTITY_TYPE = 'jfb-entry';

/** The Recycle Bin's own tree (ADR-0166): its root, and the forms and folders it holds, whose menus are not the library's. */
export const RECYCLE_BIN_ENTITY_TYPE = 'jfb-recycle-bin';
export const TRASHED_FORM_ENTITY_TYPE = 'jfb-trashed-form';
export const TRASHED_FOLDER_ENTITY_TYPE = 'jfb-trashed-folder';

/** The address of a screen of the section, relative to the backoffice. */
export const screenPath = (entityType: string, key?: string): string =>
  `section/${SECTION_PATHNAME}/workspace/${entityType}${key ? `/edit/${key}` : ''}`;

/** A form's screen with something in it selected, which the designer (9.3a) and the Actions view (10.2) honour (ADR-0166). */
export const formPath = (key: string, select?: { field?: string; action?: string }): string => {
  const query = select?.field ? `?field=${select.field}` : select?.action ? `?action=${select.action}` : '';
  return screenPath(FORM_ENTITY_TYPE, key) + query;
};
