import { readFileSync } from 'node:fs';
import { demoContentFile } from './paths.ts';

// The texts the demo's seeder writes (demo/Jiron.Umbraco.FormBuilder.Demo/Seeding/DemoContent.json), so the specs expect
// what the host was seeded with instead of a copy.

export type Language = 'en' | 'de';

export const LANGUAGES: readonly Language[] = ['en', 'de'];

interface PageTexts {
  name: string;
  heading: string;
}

interface DemoContent {
  dictionary: Record<string, Record<Language, string>>;
  home: Record<Language, PageTexts>;
  pages: ({ key: string; type: string } & Record<Language, PageTexts>)[];
}

const content = JSON.parse(readFileSync(demoContentFile, 'utf8')) as DemoContent;

/** The home page's texts in a language. */
export const homeOf = (language: Language): PageTexts => content.home[language];

/** The texts of the first page of a document type in a language. */
export function pageOf(type: string, language: Language): PageTexts {
  const page = content.pages.find((candidate) => candidate.type === type);
  if (!page) throw new Error(`The demo content has no page of type ${type}.`);
  return page[language];
}

/** A dictionary item of the demo's views in a language. */
export function textOf(key: string, language: Language): string {
  const item = content.dictionary[key];
  if (!item) throw new Error(`The demo content has no dictionary item ${key}.`);
  return item[language];
}
