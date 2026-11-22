// One drawing of a field in the backoffice (ROADMAP 9.3b, ADR-0166, ADR-0168): the backoffice writes a field's markup
// only through the headless element's views, which the server's markup holds them to. The views are imported by the
// preview and the canvas alone (a site's own type's writer names only their types, ADR-0233); a field's classes are named only where the preview builds its view models and where
// the canvas finds a label it edits in place; and the template gallery shows a template with the preview.
import { readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { sourceFiles } from '../source-files';

const src = join(dirname(fileURLToPath(import.meta.url)), '..');
// A folder whose name starts with a dot is another test's scratch (the API check's), which may go while this one reads.
const sources = sourceFiles(src)
  .filter((file) => !file.split(/[\\/]/).some((part) => part.startsWith('.')))
  .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts') && !file.startsWith('api'))
  .map((file) => ({
    file: relative(src, join(src, file)).split('\\').join('/'),
    text: readFileSync(join(src, file), 'utf8'),
  }));

/** The classes a field's own markup carries: its wrapper, its label and the parts of its controls. */
const FIELD_CLASSES =
  /jfb-(?:field|label|control|choices?|select|affix|counter|upload|signature|parts?|matrix|ranking|range|scale|repeater|switch|output|heading|text|divider|image|mark|other|ends)(?![\w-])/;

describe('the drawing of a field', () => {
  it("goes through the element's views, which only the preview and the canvas import", () => {
    const importers = sources
      .filter((source) => /^import (?!type )[^;]*['"]@jfb\/element\/views['"]/m.test(source.text))
      .map((source) => source.file);
    expect(importers.sort()).toEqual([
      'designer/canvas/canvas-field.element.ts',
      'designer/canvas/form-canvas.element.ts',
      'designer/preview/form-preview.element.ts',
    ]);
  });

  it("names a field's classes only where the preview builds its models and the canvas finds a label to edit", () => {
    const naming = sources.filter((source) => FIELD_CLASSES.test(source.text)).map((source) => source.file);
    expect(naming.sort()).toEqual(['designer/canvas/canvas-field.element.ts', 'designer/preview/field-models.ts']);
  });

  it('is what the template gallery shows a template with', () => {
    const gallery = sources.find((source) => source.file === 'library/modals/template-gallery.modal.element.ts');
    expect(gallery?.text).toContain('<jfb-form-preview .definition=');
  });
});
