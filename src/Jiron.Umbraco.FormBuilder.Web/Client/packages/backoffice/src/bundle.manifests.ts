/**
 * Every extension of the Forms section (ADR-0021), registered through the one bundle that umbraco-package.json names.
 * Umbraco's backoffice provides Lit and its own modules at run time, so the bundle imports them and never ships them.
 * The section arrives in 9.1.
 */
export const manifests: UmbExtensionManifest[] = [];
