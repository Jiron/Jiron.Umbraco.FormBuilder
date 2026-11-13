// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-marketplace.mjs. Run: node --test "scripts/tests/*.test.mjs"
// The schema comes from the copy in .local/marketplace/, which `node scripts/check-marketplace.mjs` downloads first
// (the gate and CI run it before these tests). In CI a schema the run cannot reach skips the tests that need it, as
// `check-marketplace --ci` only warns then (18.3's review); locally it fails.
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { checkListing, LICENCE_WORDS, LISTED, listingFile, pinned, schema, SCHEMA_COPY, SCHEMA_SHA256, screenshotsOnDisk, validate } from '../check-marketplace.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const MAIN = 'Jiron.Umbraco.FormBuilder';
const AI = 'Jiron.Umbraco.FormBuilder.AI';
const listingOf = (id) => JSON.parse(readFileSync(join(root, listingFile(id)), 'utf8'));
let marketplace = null;
try {
  marketplace = await schema();
} catch (error) {
  if (!process.env.GITHUB_ACTIONS) throw error;
}
const UNREACHABLE = "the Marketplace's schema is not reachable from this run; check-marketplace --ci warned";

describe('the listings', () => {
  test('are one per package with the umbraco-marketplace tag, the main one under the plain name', () => {
    assert.deepEqual(LISTED, [MAIN, AI]);
    assert.equal(listingFile(MAIN), 'umbraco-marketplace.json');
    assert.equal(listingFile(AI), 'umbraco-marketplace-jiron.umbraco.formbuilder.ai.json');
  });

  test("pass the Marketplace's schema and Jiron Form Builder's rules", async () => {
    for (const id of LISTED) {
      if (marketplace) assert.deepEqual(await validate(listingOf(id), marketplace), [], id);
      assert.deepEqual(checkListing(id, listingOf(id)), [], id);
    }
  });

  test('show every picture of docs/screenshots on the main listing, the add-on none', () => {
    const shown = listingOf(MAIN).Screenshots.map((shot) => shot.ImageUrl.split('/').at(-1)).sort();
    assert.deepEqual(shown, screenshotsOnDisk());
    assert.ok(shown.length >= 19);
    assert.equal(listingOf(AI).Screenshots, undefined);
  });
});

describe("the Marketplace's schema", () => {
  test('refuses an unknown field, a category it does not know and a title that is too long', async (t) => {
    if (!marketplace) return t.skip(UNREACHABLE);
    const listing = listingOf(MAIN);
    assert.ok((await validate({ ...listing, Forms: true }, marketplace)).some((error) => /additional properties/.test(error)));
    assert.ok((await validate({ ...listing, Category: 'Forms' }, marketplace)).some((error) => /^\/Category /.test(error)));
    assert.ok((await validate({ ...listing, Title: 'x'.repeat(257) }, marketplace)).some((error) => /^\/Title /.test(error)));
  });

  test('is used only with the pinned hash', () => {
    assert.throws(() => pinned(Buffer.from('{}')), /not the one the listings were checked against/);
    assert.match(SCHEMA_SHA256, /^[0-9a-f]{64}$/);
  });

  test('is downloaded when the copy is missing, and a changed one is kept beside the reviewed copy, which stays', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'jfb-marketplace-'));
    try {
      const copy = join(dir, 'umbraco-marketplace-schema.json');
      const reviewed = Buffer.from('{"type":"object"}');
      const hash = createHash('sha256').update(reviewed).digest('hex');
      let downloads = 0;
      const serve = (bytes) => async () => {
        downloads++;
        return bytes;
      };
      assert.deepEqual(await schema({ copy, fetchSchema: serve(reviewed), expected: hash }), { type: 'object' });
      assert.equal(downloads, 1);
      assert.deepEqual(await schema({ copy, fetchSchema: serve(reviewed), expected: hash }), { type: 'object' }, 'the copy, without a download');
      assert.equal(downloads, 1);
      const changed = Buffer.from('{"type":"array"}');
      await assert.rejects(schema({ refresh: true, copy, fetchSchema: serve(changed), expected: hash }), /not the one the listings were checked against/);
      assert.deepEqual(readFileSync(copy), reviewed, 'the reviewed copy stays');
      assert.deepEqual(readFileSync(join(dir, 'umbraco-marketplace-schema.new.json')), changed, 'the changed one waits for review');
      await assert.rejects(schema({ refresh: true, copy, fetchSchema: async () => Buffer.from('not json'), expected: hash }), SyntaxError);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  test('the copy in .local/ is the one with the pinned hash', (t) => {
    if (!existsSync(SCHEMA_COPY)) return t.skip(UNREACHABLE);
    assert.equal(createHash('sha256').update(readFileSync(SCHEMA_COPY)).digest('hex'), SCHEMA_SHA256);
  });
});

describe("Jiron Form Builder's rules for a listing", () => {
  const main = () => structuredClone(listingOf(MAIN));
  const ai = () => structuredClone(listingOf(AI));

  test('name a wrong title, category, licence type or a listing of the add-on that stands alone', () => {
    assert.deepEqual(checkListing(MAIN, { ...main(), Title: 'Forms' }), ['the title is not "Jiron Form Builder"']);
    assert.deepEqual(checkListing(MAIN, { ...main(), Category: 'Developer Tools' }), ['the category is not "Editor Tools"']);
    assert.deepEqual(checkListing(MAIN, { ...main(), LicenseTypes: ['Purchase'] }), ['the licence types are not ["Free"]']);
    const alone = ai();
    delete alone.IsSubPackageOf;
    assert.deepEqual(checkListing(AI, alone), [`it is not a sub-package of ${MAIN}`]);
    const unrelated = main();
    delete unrelated.RelatedPackages;
    assert.deepEqual(checkListing(MAIN, unrelated), [`the related packages do not name ${AI}`]);
  });

  test('keep the licence in its plain words and never call it open source', () => {
    const plain = main();
    assert.deepEqual(checkListing(MAIN, { ...plain, Description: plain.Description.replace(LICENCE_WORDS, '') }), [
      'the description does not give the licence in its plain words',
    ]);
    assert.deepEqual(checkListing(MAIN, { ...plain, Description: `An open source form builder. ${plain.Description}` }), [
      'the description calls Jiron Form Builder open source',
    ]);
  });

  test('need links into the repository that name files it holds, and the author with the owner and Ligata', () => {
    assert.deepEqual(checkListing(MAIN, main(), { exists: () => false }), ['docs/README.md is not in the repository']);
    assert.deepEqual(checkListing(MAIN, { ...main(), IssueTrackerUrl: 'https://example.test/issues' }), [
      'the issue tracker is not https://github.com/Jiron/Jiron.Umbraco.FormBuilder/issues',
    ]);
    const author = main();
    author.AuthorDetails.Description = 'Made by Jiron.';
    assert.deepEqual(checkListing(MAIN, author), [
      "the author's description does not name Manass\u00e9s Z\u00e4hnler",
      "the author's description does not name Ligata",
      "the author's description does not name jiron.dev",
      "the author's description does not name ligata.ch",
    ]);
  });

  test('need every picture once, with its caption, at the raw GitHub address', () => {
    const listing = main();
    const [first, second] = listing.Screenshots;
    const file = (shot) => shot.ImageUrl.split('/').at(-1);
    assert.deepEqual(checkListing(MAIN, { ...listing, Screenshots: listing.Screenshots.slice(1) }), [
      `docs/screenshots/${file(first)} is missing from the screenshots`,
    ]);
    assert.deepEqual(checkListing(MAIN, { ...listing, Screenshots: [...listing.Screenshots, second] }), [`${file(second)} is listed twice`]);
    const elsewhere = { ImageUrl: 'https://example.test/docs/screenshots/designer.png', Caption: 'x' };
    assert.deepEqual(checkListing(MAIN, { ...listing, Screenshots: [...listing.Screenshots, elsewhere] }), [
      'https://example.test/docs/screenshots/designer.png is not a picture of docs/screenshots',
    ]);
    const silent = structuredClone(listing);
    silent.Screenshots[0].Caption = ' ';
    assert.deepEqual(checkListing(MAIN, silent), [`${file(first)} has no caption`]);
  });
});
