// What the designer's canvas and preview write in a site's own field type's place (ROADMAP 15.1, ADR-0233): the host of
// the site's `jfbFieldPreview`, given the field as the preview's public type names it, with the type's effective
// settings the field's model carries.
import type { FieldViewModel } from '@jfb/element/answer';
import type { ControlWriter } from '@jfb/element/views';
import type { JfbPreviewField } from '../../public/types/jiron-form-builder';
import './field-preview-extension.element';

/** The field as the site's preview gets it. */
export function previewField(field: FieldViewModel): JfbPreviewField {
  const settings = field.control?.kind === 'custom' ? field.control.settings : undefined;
  return {
    key: field.key,
    type: field.type,
    ...(field.alias ? { alias: field.alias } : {}),
    id: field.id,
    label: field.label ?? '',
    required: field.isRequired,
    settings:
      settings !== null && typeof settings === 'object' && !Array.isArray(settings)
        ? (settings as Readonly<Record<string, unknown>>)
        : {},
  };
}

const write: ControlWriter = (field, document) => {
  const host = document.createElement('jfb-field-preview-extension');
  host.field = previewField(field);
  return host;
};

/** The writer of every site's own type: its host draws the site's preview once one is registered. */
export const previewControl = (): ControlWriter => write;
