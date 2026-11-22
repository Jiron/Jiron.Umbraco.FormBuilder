import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { deliveryApiOn, hostEnvironment, INSTRUCTION_PRUNING, INSTRUCTION_SYNC } from './host-env.ts';
import { MARKER_NOT_LAST, STARTUP_LINES } from './host-health.ts';
import { checkPort, demoDll, FAILED_LINES, hostProcessEnvironment, READY_LINES } from './host.ts';
import {
  DEMO_URL,
  demoProject,
  E2E_PORT,
  REFUSAL_PORTS,
  STUB_PORT,
  TARGET_FRAMEWORK,
  testingAIProject,
  testingProject,
} from './paths.ts';
import {
  MEMBERS,
  PEOPLE,
  RESTRICTED_FOLDERS,
  ROLES,
  USER_GROUPS,
  type Account,
  type E2ESecrets,
  type Role,
} from './users.ts';

// The test host's start and settings: the ports it may use, an environment that only the harness configures, the
// people it creates, and log lines that are the ones the C# code writes.

const source = (...path: string[]): string => readFileSync(join(...path), 'utf8');

test('a test host uses a port of its own range and never the demo', () => {
  assert.doesNotThrow(() => checkPort(E2E_PORT));
  for (const port of Object.values(REFUSAL_PORTS)) assert.doesNotThrow(() => checkPort(port));
  assert.equal(
    new Set([E2E_PORT, STUB_PORT, ...Object.values(REFUSAL_PORTS)]).size,
    4,
    'every host and the stub have a port of their own',
  );
  assert.throws(() => checkPort(5420), /outside/);
  assert.throws(() => checkPort(5600), /outside/);
  assert.throws(() => checkPort(Number(new URL(DEMO_URL).port)), /demo's own/);
});

test('the host gets no setting from the shell that starts the tests', () => {
  const parent = {
    PATH: '/usr/bin',
    Umbraco__CMS__Global__Id: 'from-the-shell',
    umbraco__cms__deliveryapi__enabled: 'true',
    JironFormBuilderTesting__Clock: 'false',
    ConnectionStrings__umbracoDbDSN: 'Server=elsewhere',
    ASPNETCORE_ENVIRONMENT: 'Production',
  };

  const env = hostProcessEnvironment(parent, {
    ASPNETCORE_ENVIRONMENT: 'Development',
    Umbraco__CMS__Global__Id: 'harness',
  });

  assert.deepEqual(env, {
    PATH: '/usr/bin',
    ASPNETCORE_ENVIRONMENT: 'Development',
    Umbraco__CMS__Global__Id: 'harness',
  });
});

test('the host runs the demo build of the repository target framework', () => {
  assert.equal(
    demoDll('Release'),
    join(demoProject, 'bin', 'Release', TARGET_FRAMEWORK, 'Jiron.Umbraco.FormBuilder.Demo.dll'),
  );
});

function usersOfTheTest(): Record<Role, Account> {
  const users = {} as Record<Role, Account>;
  for (const role of ROLES)
    users[role] = { name: PEOPLE[role].name, email: PEOPLE[role].email, password: `pw-${role}` };
  return users;
}

const secrets: E2ESecrets = {
  imagingHmacSecretKey: 'imaging-key-of-the-test',
  users: usersOfTheTest(),
  members: MEMBERS.map((member, index) => ({ ...member, password: `pw-member-${index}` })),
};

test('the host environment creates every persona but the admin, with its groups, and the two members', () => {
  const env = hostEnvironment(secrets, { deliveryApi: false });

  assert.equal(env.Umbraco__CMS__Unattended__UnattendedUserEmail, PEOPLE.admin.email);
  const users = ROLES.filter((role) => role !== 'admin');
  users.forEach((role, u) => {
    assert.equal(env[`JironFormBuilderTesting__Users__${u}__Email`], PEOPLE[role].email);
    assert.equal(env[`JironFormBuilderTesting__Users__${u}__Password`], `pw-${role}`);
    PEOPLE[role].groups.forEach((group, i) =>
      assert.equal(env[`JironFormBuilderTesting__Users__${u}__Groups__${i}`], group),
    );
    // Only a twin keeps a language of its own (ROADMAP 16.6); the others take Umbraco's default.
    assert.equal(env[`JironFormBuilderTesting__Users__${u}__Language`], PEOPLE[role].language);
  });
  assert.equal(env[`JironFormBuilderTesting__Users__${users.indexOf('adminDe')}__Language`], 'de-DE');
  assert.equal(env[`JironFormBuilderTesting__Users__${users.length}__Email`], undefined);
  USER_GROUPS.forEach((group, g) => {
    assert.equal(env[`JironFormBuilderTesting__UserGroups__${g}__Alias`], group.alias);
    group.permissions.forEach((permission, p) =>
      assert.equal(env[`JironFormBuilderTesting__UserGroups__${g}__Permissions__${p}`], permission),
    );
    assert.equal(env[`JironFormBuilderTesting__UserGroups__${g}__Permissions__${group.permissions.length}`], undefined);
    assert.equal(
      env[`JironFormBuilderTesting__UserGroups__${g}__MediaRootAccess`],
      group.alias === 'jfbDesigners' ? 'true' : undefined,
    );
    assert.equal(
      env[`JironFormBuilderTesting__UserGroups__${g}__ContentRootAccess`],
      group.alias === 'jfbDesigners' ? 'true' : undefined,
    );
    assert.equal(
      env[`JironFormBuilderTesting__UserGroups__${g}__ContentEditing`],
      group.alias === 'jfbDesigners' ? 'true' : undefined,
    );
  });
  RESTRICTED_FOLDERS.forEach((folder, f) => {
    assert.equal(env[`JironFormBuilderTesting__RestrictedFolders__${f}__Name`], folder.name);
    assert.equal(env[`JironFormBuilderTesting__RestrictedFolders__${f}__Group`], folder.group);
    folder.permissions.forEach((permission, p) =>
      assert.equal(env[`JironFormBuilderTesting__RestrictedFolders__${f}__Permissions__${p}`], permission),
    );
  });
  MEMBERS.forEach((member, m) => assert.equal(env[`Demo__Members__${m}__Email`], member.email));
});

test('the host environment turns on what the browser tests need, and the Delivery API as asked', () => {
  const off = hostEnvironment(secrets, { deliveryApi: false });
  const on = hostEnvironment(secrets, { deliveryApi: true });

  for (const name of ['Endpoints', 'Clock', 'StartupMarker', 'StartupWorkAtOnce'])
    assert.equal(off[`JironFormBuilderTesting__${name}`], 'true', name);
  assert.equal(off.JironFormBuilderTesting__FakeAI, 'Chosen');
  assert.equal(off.JironFormBuilderTesting__StubPort, String(STUB_PORT));
  assert.equal(off.Umbraco__CMS__DeliveryApi__Enabled, 'false');
  assert.equal(on.Umbraco__CMS__DeliveryApi__Enabled, 'true');
});

test("a host of one server reads and prunes cache instructions once a day, and several servers keep Umbraco's intervals", () => {
  const one = hostEnvironment(secrets, { deliveryApi: false });
  const several = hostEnvironment(secrets, { deliveryApi: false, severalServers: true });

  assert.equal(INSTRUCTION_SYNC, 'Umbraco__CMS__Global__DatabaseServerMessenger__TimeBetweenSyncOperations');
  assert.equal(INSTRUCTION_PRUNING, 'Umbraco__CMS__Global__DatabaseServerMessenger__TimeBetweenPruneOperations');
  assert.equal(one[INSTRUCTION_SYNC], '1.00:00:00');
  assert.equal(one[INSTRUCTION_PRUNING], '1.00:00:00');
  assert.equal(INSTRUCTION_SYNC in several, false);
  assert.equal(INSTRUCTION_PRUNING in several, false);
});

test("every host does Umbraco's start-up work after its seeding, one server or several, with the option the C# code reads", () => {
  for (const severalServers of [false, true]) {
    const env = hostEnvironment(secrets, { deliveryApi: false, severalServers });
    assert.equal(env.JironFormBuilderTesting__StartupWorkAtOnce, 'true');
    // The one switch sets the jobs' delay too; Umbraco's own setting would start them before the seeding has finished.
    assert.equal(
      Object.keys(env).some((name) => name.startsWith('Umbraco__CMS__DistributedJobs__')),
      false,
    );
  }
  const options = source(testingProject, 'FormBuilderTestingOptions.cs');
  assert.match(options, /public bool StartupWorkAtOnce \{ get; set; \}/);
  const composer = source(testingProject, 'Startup', 'StartupMarkerComposer.cs');
  assert.match(composer, /PostConfigure<DistributedJobSettings>\(settings => settings\.Delay = TimeSpan\.Zero\)/);
});

test('JFB_E2E_DELIVERY_API is on, off or unset', () => {
  assert.equal(deliveryApiOn({}), false);
  assert.equal(deliveryApiOn({ JFB_E2E_DELIVERY_API: 'ON' }), true);
  assert.equal(deliveryApiOn({ JFB_E2E_DELIVERY_API: 'off' }), false);
  assert.throws(() => deliveryApiOn({ JFB_E2E_DELIVERY_API: 'yes' }), /"on" or "off"/);
});

test('every log line the harness waits for is one the C# code writes', () => {
  const marker = source(testingProject, 'Startup', 'StartupMarkerPopulator.cs');
  assert.ok(marker.includes(`Marker = "${STARTUP_LINES[1]}"`), 'the start-up marker');
  assert.ok(marker.includes(`NotLast = "${MARKER_NOT_LAST}"`), 'the misplaced marker');

  const seeders = [
    source(testingProject, 'People', 'TestPeopleSeeder.cs'),
    source(demoProject, 'Seeding', 'DemoSeeder.cs'),
    source(testingAIProject, 'FakeAI', 'FakeAISetup.cs'),
  ].join('\n');
  for (const line of [...READY_LINES, ...FAILED_LINES]) assert.ok(seeders.includes(`Message = "${line}`), line);
});

test("the stub's port is the Testing project's default", () => {
  assert.match(source(testingProject, 'Stub', 'StubServer.cs'), new RegExp(`DefaultPort = ${STUB_PORT};`));
});
