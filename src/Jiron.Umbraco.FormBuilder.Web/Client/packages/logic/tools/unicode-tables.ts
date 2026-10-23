// The tables check's TypeScript half (ADR-0005): what this runtime's clean, fold and NFC recomposition do to every
// code point, written to tests/vectors/tables/typescript.json for the C# suite to compare. Only code points that
// change are listed, one per line, so a change in the runtime's Unicode data shows as a short diff.
//   node tools/unicode-tables.ts           write the file
//   node tools/unicode-tables.ts --check   exit 1 when the file differs from what this runtime does
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { clean, fold } from '../src/text.ts';
import { tablesFile } from './repository.ts';

/** The format the file declares. */
export const TABLES_FORMAT = 'jfb-unicode-tables/1';

/** Code point to its folded code point, its cleaned code points, and the NFC of its canonical decomposition. */
export interface UnicodeTables {
  readonly fold: readonly (readonly [number, number])[];
  readonly clean: readonly (readonly [number, readonly number[]])[];
  readonly recompose: readonly (readonly [number, readonly number[]])[];
}

const codePoints = (text: string): number[] => Array.from(text, (ch) => ch.codePointAt(0) ?? 0);
const NEWLINE = String.fromCharCode(10);

/** What this runtime does to every code point; a surrogate code point is cleaned as that lone code unit. */
export function unicodeTables(): UnicodeTables {
  const foldTable: [number, number][] = [];
  const cleanTable: [number, number[]][] = [];
  // The NFC of each character's canonical decomposition: the compositions both runtimes must make alike.
  const recomposeTable: [number, number[]][] = [];
  for (let cp = 0; cp <= 0x10ffff; cp++) {
    const surrogate = cp >= 0xd800 && cp <= 0xdfff;
    const ch = surrogate ? String.fromCharCode(cp) : String.fromCodePoint(cp);
    const cleaned = clean(ch);
    if (cleaned !== ch) cleanTable.push([cp, codePoints(cleaned)]);
    if (surrogate) continue;
    const folded = fold(ch);
    if (folded !== ch) foldTable.push([cp, folded.codePointAt(0) ?? 0]);
    const nfd = ch.normalize('NFD');
    if (nfd !== ch) recomposeTable.push([cp, codePoints(nfd.normalize('NFC'))]);
  }
  return { fold: foldTable, clean: cleanTable, recompose: recomposeTable };
}

/** The file's text: the runtime's versions, then each table with one entry per line. */
export function tablesText(tables: UnicodeTables): string {
  const { node, icu, unicode } = process.versions;
  const table = (name: string, rows: readonly unknown[]): string =>
    `  "${name}": [` + NEWLINE + rows.map((row) => '    ' + JSON.stringify(row)).join(',' + NEWLINE) + NEWLINE + '  ]';
  const header = { format: TABLES_FORMAT, node, icu, unicode };
  const head = Object.entries(header).map(([name, value]) => `  "${name}": ${JSON.stringify(value)}`);
  const body = [table('fold', tables.fold), table('clean', tables.clean), table('recompose', tables.recompose)];
  return '{' + NEWLINE + [...head, ...body].join(',' + NEWLINE) + NEWLINE + '}' + NEWLINE;
}

/** The tables of a written file, without the versions of the runtime that wrote it. */
export function readTables(text: string): UnicodeTables {
  const parsed = JSON.parse(text) as UnicodeTables & { readonly format: string };
  if (parsed.format !== TABLES_FORMAT) throw new Error(`Not a ${TABLES_FORMAT} file`);
  return { fold: parsed.fold, clean: parsed.clean, recompose: parsed.recompose };
}

const isMain = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const path = tablesFile();
  const tables = unicodeTables();
  const summary = `${tables.fold.length} folds, ${tables.clean.length} cleaned, ${tables.recompose.length} recompositions`;
  if (process.argv.includes('--check')) {
    const current = existsSync(path) ? JSON.stringify(readTables(readFileSync(path, 'utf8'))) : '';
    if (current !== JSON.stringify(tables)) {
      console.error(`tables: ${path} differs from what Node ${process.versions.node} does; run npm run tables`);
      process.exit(1);
    }
    console.log(`tables: ${path} matches Node ${process.versions.node} (${summary})`);
  } else {
    writeFileSync(path, tablesText(tables));
    const { node, icu, unicode } = process.versions;
    console.log(`tables: wrote ${path} from Node ${node}, ICU ${icu}, Unicode ${unicode}: ${summary}`);
  }
}
