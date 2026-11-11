// The site's own extensions of the designer (ROADMAP 15.1, ADR-0233, ADR-0018): the custom setting editors, field
// previews, designer actions and field text actions its manifests register, kept as they are now, so each is known as
// the designer draws. A
// site's bundle may register them after Jiron Form Builder's, so a change is said on the window, which the screens that
// draw settings hear.
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import type {
  JfbFieldText,
  ManifestJfbDesignerAction,
  ManifestJfbFieldPreview,
  ManifestJfbFieldTextAction,
  ManifestJfbSettingEditor,
} from '../../public/types/jiron-form-builder';

/** The event said on the window when the site's editors or previews change. */
export const EXTENSIONS_CHANGED = 'jfb-extensions-changed';

let editors: ReadonlyMap<string, ManifestJfbSettingEditor> = new Map();
let previews: ReadonlyMap<string, ManifestJfbFieldPreview> = new Map();
let actions: readonly ManifestJfbDesignerAction[] = [];
let textActions: readonly ManifestJfbFieldTextAction[] = [];
let watching = false;

// Reads the registry once and follows it for as long as the backoffice runs.
function watch(): void {
  if (watching) return;
  watching = true;
  umbExtensionsRegistry.byType('jfbSettingEditor').subscribe((manifests) => {
    editors = byKey(manifests, (manifest) => manifest.meta?.editor);
    window.dispatchEvent(new Event(EXTENSIONS_CHANGED));
  });
  umbExtensionsRegistry.byType('jfbFieldPreview').subscribe((manifests) => {
    previews = byKey(manifests, (manifest) => manifest.meta?.fieldType);
    window.dispatchEvent(new Event(EXTENSIONS_CHANGED));
  });
  umbExtensionsRegistry.byType('jfbDesignerAction').subscribe((manifests) => {
    actions = byWeight(manifests.filter((manifest) => typeof manifest.meta?.label === 'string'));
    window.dispatchEvent(new Event(EXTENSIONS_CHANGED));
  });
  umbExtensionsRegistry.byType('jfbFieldTextAction').subscribe((manifests) => {
    textActions = byWeight(manifests.filter((manifest) => Array.isArray(manifest.meta?.texts)));
    window.dispatchEvent(new Event(EXTENSIONS_CHANGED));
  });
}

// The manifests as Umbraco orders a type's: the higher weight first.
const byWeight = <T extends { readonly weight?: number }>(manifests: readonly T[]): readonly T[] =>
  [...manifests].sort((a, b) => (b.weight ?? 0) - (a.weight ?? 0));

// The manifests by the name each gives, the one of the highest weight for a name, as Umbraco orders a type's manifests.
function byKey<T extends { readonly weight?: number }>(manifests: readonly T[], key: (manifest: T) => unknown) {
  const found = new Map<string, T>();
  for (const manifest of manifests) {
    const name = key(manifest);
    if (typeof name !== 'string' || name.length === 0) continue;
    const kept = found.get(name);
    if (kept === undefined || (manifest.weight ?? 0) > (kept.weight ?? 0)) found.set(name, manifest);
  }
  return found;
}

/** The site's editor for the editor alias a setting names, or undefined for none. */
export function editorFor(alias: string): ManifestJfbSettingEditor | undefined {
  watch();
  return editors.get(alias);
}

/** The designer actions of the toolbar's "...", in their order. */
export function designerActions(): readonly ManifestJfbDesignerAction[] {
  watch();
  return actions;
}

/** The field text actions that sit beside one of a field's texts, in their order. */
export function fieldTextActionsFor(text: JfbFieldText): readonly ManifestJfbFieldTextAction[] {
  watch();
  return textActions.filter((manifest) => manifest.meta.texts.includes(text));
}

/** The site's preview of a field type, by its alias, or undefined for none. */
export function previewFor(type: string): ManifestJfbFieldPreview | undefined {
  watch();
  return previews.get(type);
}
