import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { manifests } from '../bundle.manifests';
import { FORM_PICKER_UI_ALIAS } from '../picker/manifests';
import jfb from './icon-jfb';
import icons from './icons';

// Jiron Form Builder's backoffice icon (ROADMAP 17.1): the brand's own file, as a line icon in Umbraco's style,
// registered for Umbraco's icon registry and shown beside the form picker.

/** The repository's assets/icon-section.svg. */
function asset(): string {
  let folder = import.meta.dirname;
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) folder = dirname(folder);
  return readFileSync(join(folder, 'assets', 'icon-section.svg'), 'utf8');
}

const plain = (svg: string) =>
  svg
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/\s+/g, ' ')
    .replace(/> </g, '><')
    .trim();

describe('the backoffice icon', () => {
  it('is assets/icon-section.svg', () => {
    expect(plain(jfb)).toBe(plain(asset()));
  });

  it("draws in the backoffice's colour, as Umbraco's own icons do", () => {
    expect(jfb).toMatch(/viewBox="0 0 24 24"/);
    expect(jfb).toMatch(/stroke="currentColor"/);
    expect(jfb).not.toMatch(/#[0-9a-f]{3,6}/i);
  });

  it('is registered as icon-jfb, its file loaded when it is first drawn', async () => {
    expect(icons.map((icon) => icon.name)).toEqual(['icon-jfb']);
    const [icon] = icons;
    if (!icon) throw new Error('no icon');
    expect((await icon.path()).default).toBe(jfb);
  });

  it("is the bundle's icons extension, and the form picker's icon in Umbraco's list of property editors", async () => {
    const registered = manifests.filter((manifest) => manifest.type === 'icons') as unknown as {
      alias: string;
      js: () => Promise<{ default: unknown }>;
    }[];
    expect(registered.map((manifest) => manifest.alias)).toEqual(['Jiron.FormBuilder.Icons']);
    const [extension] = registered;
    if (!extension) throw new Error('no icons extension');
    expect((await extension.js()).default).toBe(icons);
    const picker = manifests.find((manifest) => manifest.alias === FORM_PICKER_UI_ALIAS) as unknown as {
      meta: { icon: string };
    };
    expect(picker.meta.icon).toBe('icon-jfb');
  });
});
