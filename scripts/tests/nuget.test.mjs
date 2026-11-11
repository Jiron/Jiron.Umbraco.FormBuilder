// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lib/nuget.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { join, resolve } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  filterProjects,
  graphPackages,
  isUmbracoCms,
  licenceTextHash,
  packageMetadata,
  packagesFolder,
  readNuspec,
  solutionPackages,
  solutionProjects,
  stricter,
} from '../lib/nuget.mjs';
import { fakeRepo, putPackage } from './fake-repo.mjs';

describe('packagesFolder', () => {
  test('is NUGET_PACKAGES when it is set, as CI sets it', () => {
    assert.equal(packagesFolder({ NUGET_PACKAGES: '/work/.nuget/packages' }, '/home/runner'), resolve('/work/.nuget/packages'));
  });

  test("is the default cache under the user's home otherwise", () => {
    assert.equal(packagesFolder({}, '/home/me'), join('/home/me', '.nuget', 'packages'));
    assert.equal(packagesFolder({ NUGET_PACKAGES: '  ' }, '/home/me'), join('/home/me', '.nuget', 'packages'));
  });
});

describe('the solution and its filter', () => {
  test('list their projects with forward slashes', () => {
    assert.deepEqual(solutionProjects('<Solution><Folder Name="/src/"><Project Path="src\\A\\A.csproj" /></Folder><Project Path="tests/B/B.csproj" /></Solution>'), [
      'src/A/A.csproj',
      'tests/B/B.csproj',
    ]);
    assert.deepEqual(filterProjects(JSON.stringify({ solution: { projects: ['src\\A\\A.csproj'] } })), ['src/A/A.csproj']);
  });
});

describe('graphPackages', () => {
  test("marks what the Umbraco CMS packages bring as the platform, and nothing else", () => {
    const assets = {
      targets: {
        'net10.0': {
          'Umbraco.Cms.Core/17.5.0': { type: 'package', dependencies: { examine: '3.7.1' } },
          'Examine/3.7.1': { type: 'package', dependencies: { 'Lucene.Net': '4.8.0' } },
          'Lucene.Net/4.8.0': { type: 'package' },
          'Own.Lib/1.0.0': { type: 'package' },
          'Jiron.Umbraco.FormBuilder.Core/1.0.0': { type: 'project' },
        },
      },
    };
    const byId = Object.fromEntries(graphPackages(assets).map((pkg) => [pkg.id, pkg.platform]));
    assert.deepEqual(byId, { 'Umbraco.Cms.Core': true, Examine: true, 'Lucene.Net': true, 'Own.Lib': false });
  });

  test('a package is platform only when Umbraco brings it in every target framework', () => {
    const assets = {
      targets: {
        'net10.0': { 'Umbraco.Cms.Core/17.5.0': { type: 'package', dependencies: { Shared: '1.0.0' } }, 'Shared/1.0.0': { type: 'package' } },
        'net11.0': { 'Shared/1.0.0': { type: 'package' } },
      },
    };
    assert.equal(graphPackages(assets).find((pkg) => pkg.id === 'Shared').platform, false);
  });

  test('knows the Umbraco CMS packages and the stricter tier', () => {
    assert.equal(isUmbracoCms('Umbraco.Cms'), true);
    assert.equal(isUmbracoCms('umbraco.cms.tests.integration'), true);
    assert.equal(isUmbracoCms('Umbraco.Community.Thing'), false);
    assert.equal(stricter('platform', 'dev'), 'dev');
    assert.equal(stricter('shipped', 'dev'), 'shipped');
  });
});

describe('solutionPackages', () => {
  const repo = fakeRepo();
  after(repo.cleanup);

  test('gives each package the strictest tier of the graphs it is in', () => {
    const tiers = Object.fromEntries(solutionPackages(repo.root).map((pkg) => [pkg.id, pkg.tier]));
    assert.deepEqual(tiers, { 'Dev.Tool': 'dev', Examine: 'platform', 'Shipped.Lib': 'shipped', 'Umbraco.Cms.Core': 'platform' });
  });

  test('names the projects each package comes from', () => {
    assert.deepEqual(solutionPackages(repo.root).find((pkg) => pkg.id === 'Shipped.Lib').projects, ['Pkg', 'Tool']);
  });

  test('asks for a restore when a graph is missing', () => {
    assert.throws(
      () => solutionPackages(repo.root, (path) => {
        if (path.endsWith('project.assets.json')) throw new Error('missing');
        return { 'Jiron.Umbraco.FormBuilder.slnx': '<Project Path="src/A/A.csproj" />', 'src/Jiron.Umbraco.FormBuilder.Packages.slnf': '{"solution":{"projects":[]}}' }[path];
      }),
      /run `dotnet restore` first/,
    );
  });
});

describe('the nuspec', () => {
  test('gives the licence as an expression, a file, a URL or none, with the copyright', () => {
    assert.deepEqual(readNuspec('<license type="expression">MIT</license><copyright> (c) X </copyright>'), { kind: 'expression', value: 'MIT', copyright: '(c) X' });
    assert.equal(readNuspec('<license type="file">LICENSE.txt</license>').kind, 'file');
    assert.deepEqual(readNuspec('<licenseUrl>https://example.test/l</licenseUrl>'), { kind: 'url', value: 'https://example.test/l', copyright: '' });
    assert.equal(readNuspec('<metadata></metadata>').kind, 'none');
  });

  test("is read from the package's folder, with the text of its licence file", () => {
    const repo = fakeRepo();
    try {
      putPackage(repo.cache, { id: 'With.File', version: '1.0.0-Beta', licence: '<license type="file">LICENSE.txt</license>', files: { 'LICENSE.txt': 'the terms' } });
      const meta = packageMetadata(repo.cache, { id: 'With.File', version: '1.0.0-Beta' });
      assert.equal(meta.text, 'the terms');
      // A nuspec that names no licence: the licence file the package carries, for a review (ADR-0018).
      putPackage(repo.cache, { id: 'Carries.One', version: '2.0.0', files: { 'LICENSE.md': 'the MIT text' } });
      assert.deepEqual(packageMetadata(repo.cache, { id: 'Carries.One', version: '2.0.0' }), {
        kind: 'none',
        value: '',
        copyright: '',
        bundled: 'LICENSE.md',
        text: 'the MIT text',
      });
      putPackage(repo.cache, { id: 'Carries.None', version: '2.0.0' });
      assert.equal(packageMetadata(repo.cache, { id: 'Carries.None', version: '2.0.0' }).text, undefined);
      assert.throws(() => packageMetadata(repo.cache, { id: 'Not.There', version: '1.0.0' }), /NUGET_PACKAGES/);
    } finally {
      repo.cleanup();
    }
  });

  test('hashes a licence text the same with either line end and outer white space', () => {
    assert.equal(licenceTextHash('a\r\nb\n'), licenceTextHash('  a\nb'));
    assert.notEqual(licenceTextHash('a\nb'), licenceTextHash('a\nc'));
    assert.match(licenceTextHash('x'), /^[0-9a-f]{64}$/);
  });
});
