// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-audit-suppressions.mjs (ADR-0024). Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, test } from 'node:test';
import { AI_CONDITION, findViolations, HOST_CONDITION, isHostOnly, readReferences, readSuppressions, run } from '../check-audit-suppressions.mjs';

const props = (hostEntries, sharedEntries = '') => `<Project>
  <!-- <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-in-a-comment" Package="Commented.Out" /> -->
  <ItemGroup>
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-dddd-eeee-ffff" Package="MessagePack" />
    ${sharedEntries}
  </ItemGroup>
  <ItemGroup Condition="${HOST_CONDITION}">
    ${hostEntries}
  </ItemGroup>
</Project>`;

const imageSharp = '<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-aaaa-bbbb-cccc" Package="SixLabors.ImageSharp" />';

describe('readSuppressions', () => {
  test('reads every suppression with its package and the condition of its group, and ignores comments', () => {
    assert.deepEqual(readSuppressions(props(imageSharp)), [
      { url: 'https://github.com/advisories/GHSA-dddd-eeee-ffff', packageId: 'MessagePack', condition: '' },
      { url: 'https://github.com/advisories/GHSA-aaaa-bbbb-cccc', packageId: 'SixLabors.ImageSharp', condition: HOST_CONDITION },
    ]);
  });

  test('reads the attributes in either order', () => {
    const [entry] = readSuppressions('<ItemGroup><NuGetAuditSuppress Package="A" Include="https://x.test/1" /></ItemGroup>');
    assert.deepEqual(entry, { url: 'https://x.test/1', packageId: 'A', condition: '' });
  });
});

describe('readReferences', () => {
  test('reads package references and central pins, not those in comments', () => {
    const ids = readReferences(
      '<PackageReference Include="Umbraco.Cms.Core" /><PackageVersion Include="MessagePack" Version="3.1.9" /><PackageReference Update="Foo" /><!-- <PackageReference Include="Hidden" /> -->',
    );
    assert.deepEqual(
      ids.map((i) => `${i.kind}:${i.packageId}`),
      ['PackageReference:Umbraco.Cms.Core', 'PackageVersion:MessagePack', 'PackageReference:Foo'],
    );
  });
});

describe('findViolations', () => {
  test('a direct reference to a suppressed package fails, case-insensitively', () => {
    const problems = findViolations(readSuppressions(props(imageSharp)), [
      { file: 'src/A/A.csproj', text: '<PackageReference Include="messagepack" />' },
      { file: 'Directory.Packages.props', text: '<PackageVersion Include="MessagePack" Version="3.1.9" />' },
      { file: 'src/B/B.csproj', text: '<PackageReference Include="Umbraco.Cms.Core" />' },
    ]);
    assert.equal(problems.length, 2);
    assert.match(problems[0], /^src\/A\/A\.csproj has a PackageReference for messagepack, whose advisories/);
    assert.match(problems[1], /^Directory\.Packages\.props has a PackageVersion for MessagePack/);
  });

  test('a direct ImageSharp reference fails even when nothing suppresses it', () => {
    const problems = findViolations(readSuppressions(props('')), [{ file: 'tests/T/T.csproj', text: '<PackageReference Include="SixLabors.ImageSharp.Web" />' }]);
    assert.deepEqual(problems, ['tests/T/T.csproj has a PackageReference for SixLabors.ImageSharp.Web, which only Umbraco sites may bring (CLAUDE.md rule 4)']);
    assert.equal(isHostOnly('SixLabors.ImageSharp'), true);
    assert.equal(isHostOnly('SixLabors.Fonts'), false);
  });

  test('an ImageSharp suppression that leaves the host-only group fails', () => {
    const moved = findViolations(readSuppressions(props('', imageSharp)), []);
    assert.equal(moved.length, 1);
    assert.match(moved[0], /SixLabors\.ImageSharp's https:\/\/github\.com\/advisories\/GHSA-aaaa-bbbb-cccc left the host-only group/);
    const otherCondition = findViolations(readSuppressions(`<ItemGroup Condition="'$(Configuration)' == 'Release'">${imageSharp}</ItemGroup>`), []);
    assert.equal(otherCondition.length, 1);
    assert.deepEqual(findViolations(readSuppressions(props(imageSharp)), []), []);
  });

  test("an ImageSharp suppression may stand in the AI add-on's group, which only the add-on sets (ADR-0018)", () => {
    const inAddOn = readSuppressions(`<ItemGroup Condition="${AI_CONDITION}">${imageSharp}</ItemGroup>`);
    assert.equal(inAddOn[0]?.condition, AI_CONDITION);
    assert.deepEqual(findViolations(inAddOn, []), []);
  });

  test('a suppression outside any ItemGroup fails', () => {
    const problems = findViolations(readSuppressions(`<Project>${imageSharp}</Project>`), []);
    assert.match(problems[0], /is outside an ItemGroup$/);
  });
});

describe('run', () => {
  test('the repository has no direct reference to a suppressed package, and its ImageSharp entries are host-only', () => {
    const root = resolve(fileURLToPath(new URL('../..', import.meta.url)));
    const result = run(root);
    assert.ok(result.suppressions.length > 0, 'NuGetAudit.props lists suppressions');
    assert.ok(result.suppressions.some((s) => isHostOnly(s.packageId) && s.condition === HOST_CONDITION));
    assert.ok(result.checked > 0);
    assert.deepEqual(result.problems, []);
  });
});
