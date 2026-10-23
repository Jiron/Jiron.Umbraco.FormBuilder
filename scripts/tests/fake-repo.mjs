// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// A small repository with restore graphs, npm lock files, THIRD-PARTY-NOTICES.md and a NuGet packages folder, for
// the tests of the NuGet helpers, the licence checks and the notices. Not a test file itself.
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

export const SHIPPED_PROJECT = 'src/Pkg/Pkg.csproj';
export const DEV_PROJECT = 'tests/Tool/Tool.csproj';

/** The NuGet packages of the fixture: id, version, nuspec licence element, copyright and dependencies. */
export const PACKAGES = [
  { id: 'Umbraco.Cms.Core', version: '17.5.0', licence: '<license type="expression">MIT</license>', dependencies: { Examine: '3.7.1' } },
  { id: 'Examine', version: '3.7.1', licence: '<license type="expression">MS-PL</license>' },
  { id: 'Shipped.Lib', version: '1.0.0', licence: '<license type="expression">MIT</license>', copyright: 'Shipped | Ltd' },
  { id: 'Dev.Tool', version: '2.0.0', licence: '<license type="expression">BSD-3-Clause</license>' },
];

const graph = (ids) => ({
  version: 3,
  targets: {
    'net10.0': Object.fromEntries(
      PACKAGES.filter((pkg) => ids.includes(pkg.id)).map((pkg) => [`${pkg.id}/${pkg.version}`, { type: 'package', dependencies: pkg.dependencies }]),
    ),
  },
  packageFolders: {},
});

export const NOTICES = `# Third-party notices

## Shipped to sites

<!-- jfb-shipped:start -->
<!-- jfb-shipped:end -->

## Development only (never shipped)

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| \`tool-a\` | 1.0.0 | MIT | a tool (\`scripts/\`) |
| \`Dev.Tool\` | 2.0.0 | BSD-3-Clause | tests (\`tests/\`) |

<!-- jfb-public-domain:start -->
<!-- jfb-public-domain:end -->

## Documents
`;

/** Writes a file below a folder, creating its folders. */
export function put(root, path, content) {
  const file = join(root, path);
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, typeof content === 'string' ? content : JSON.stringify(content, null, 2));
}

/** Writes a package with its nuspec (and any extra files) into a packages folder. */
export function putPackage(cache, { id, version, licence = '', copyright = '', files = {} }) {
  const folder = join(cache, id.toLowerCase(), version.toLowerCase());
  put(folder, `${id.toLowerCase()}.nuspec`, `<?xml version="1.0"?><package><metadata><id>${id}</id><version>${version}</version>${licence}<copyright>${copyright}</copyright></metadata></package>`);
  for (const [name, text] of Object.entries(files)) put(folder, name, text);
}

/**
 * A fresh fake repository and packages folder under the temp folder: { root, cache, cleanup }. The packages folder
 * goes where `cacheOf(base)` says (default: <base>/cache), so a test can put it under a fake home.
 */
export function fakeRepo({ cacheOf = (base) => join(base, 'cache') } = {}) {
  const base = mkdtempSync(join(tmpdir(), 'jfb-fake-repo-'));
  const root = join(base, 'repo');
  const cache = cacheOf(base);
  put(root, 'Jiron.Umbraco.FormBuilder.slnx', `<Solution>\n  <Project Path="${SHIPPED_PROJECT}" />\n  <Project Path="${DEV_PROJECT}" />\n</Solution>\n`);
  put(root, 'src/Jiron.Umbraco.FormBuilder.Packages.slnf', { solution: { path: '../Jiron.Umbraco.FormBuilder.slnx', projects: [SHIPPED_PROJECT] } });
  put(root, SHIPPED_PROJECT, '<Project><ItemGroup><PackageReference Include="Umbraco.Cms.Core" /><PackageReference Include="Shipped.Lib" /></ItemGroup></Project>');
  put(root, 'src/Pkg/obj/project.assets.json', graph(['Umbraco.Cms.Core', 'Examine', 'Shipped.Lib']));
  put(root, DEV_PROJECT, '<Project><ItemGroup><PackageReference Include="Dev.Tool" /></ItemGroup></Project>');
  put(root, 'tests/Tool/obj/project.assets.json', graph(['Umbraco.Cms.Core', 'Examine', 'Shipped.Lib', 'Dev.Tool']));
  put(root, 'THIRD-PARTY-NOTICES.md', NOTICES);

  put(root, 'scripts/package.json', { devDependencies: { 'tool-a': '1.0.0' } });
  put(root, 'scripts/package-lock.json', {
    lockfileVersion: 3,
    packages: {
      '': {},
      'node_modules/tool-a': { version: '1.0.0', license: 'MIT', dev: true },
      'node_modules/pd-lib': { version: '0.1.0', license: 'CC0-1.0', dev: true },
    },
  });
  for (const folder of ['src/Jiron.Umbraco.FormBuilder.Web/Client', 'tests/e2e']) {
    put(root, `${folder}/package.json`, {});
    put(root, `${folder}/package-lock.json`, { lockfileVersion: 3, packages: { '': {} } });
  }
  for (const pkg of PACKAGES) putPackage(cache, pkg);
  return { root, cache, cleanup: () => rmSync(base, { recursive: true, force: true }) };
}
