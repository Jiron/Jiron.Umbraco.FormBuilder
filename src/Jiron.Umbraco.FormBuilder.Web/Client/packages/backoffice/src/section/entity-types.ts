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

/** The address of a screen of the section, relative to the backoffice. */
export const screenPath = (entityType: string, key?: string): string =>
  `section/${SECTION_PATHNAME}/workspace/${entityType}${key ? `/edit/${key}` : ''}`;
