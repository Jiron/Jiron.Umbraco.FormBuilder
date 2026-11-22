# 0262. A group's access on its user group screen: shown and linked, not edited

- Status: accepted; adds to [ADR-0013](0013-permissions.md) (permissions per user group) and
  [ADR-0218](0218-the-settings-screens-and-access.md) (Settings › Access); follows
  [ADR-0231](0231-members-entries.md) in putting a view of ours on a workspace Umbraco owns
- Date: 2026-11-22
- Roadmap step: 25.1

## Context

On the first site the owner made a user group for the person who will run it, gave it the section and found every
place refused. Umbraco's user group screen, where a group gets its sections, said nothing about Form Builder, and the
refusals named "Settings › Access" to a user whose section shows no Settings place (WORKLOG, findings 11 and 12). A
group needs two steps, the section in Umbraco and its permissions in Form Builder, and nothing on the first step's
screen leads to the second.

A view of ours on Umbraco's user group workspace (`Umb.Workspace.UserGroup`) can say what the group may do there.
The question is whether it should also change it.

- The workspace's footer has one **Save**, Umbraco's own workspace action, which submits the group through Umbraco's
  repository. A workspace view of a package has no part in that submit: no hook runs our save with Umbraco's, inside
  its validation or after it.
- The permissions have rules the matrix on Settings › Access already keeps: a box ticked brings the permission it
  needs and one unticked takes those that need it, the server refuses a grant without its dependency, a change made
  meanwhile refuses the whole save (`409`), the host's `Access:Grants` locks the grants, and the administrators hold
  everything.
- Settings › Access shows every group side by side, so an administrator compares a group with the others while giving
  it permissions; one group's screen shows one.

## Options

1. **Tick boxes on the user group screen, saved by a Save of the view's own** beside Umbraco's footer. Two Saves on one
   screen: a user ticks, presses the footer's Save, which saves the group and not the ticks, and loses them without
   being told, or leaves with Umbraco's question about unsaved changes, which knows nothing of ours. The dependency
   handling, the lock and the conflict would be drawn a second time.
2. **Tick boxes saved with Umbraco's Save,** by reaching into the workspace context's submit. Umbraco's API offers a
   package no such part; it would rest on Umbraco's internals and break with them.
3. **A view that shows and links:** what the group may do with every form, by part and by the permissions' names; the
   folders and forms that give it more or are restricted to groups including it; that its users see an empty section
   while it holds nothing; that a group without the section adds it under Details first; that the administrators hold
   everything; whether the host configuration sets the grants; and one link to Settings › Access. Nothing on it saves.

## Decision

Option 3. The view `Jiron.FormBuilder.WorkspaceView.UserGroupAccess`, labelled with the section's name, comes after
Umbraco's Details, on a group that exists, for a user who has the Form Builder section and may manage permissions;
others never see it, and the server refuses its read to anyone else. It reads `GET access`, the answer Settings ›
Access reads, and finds the group by the workspace's unique; whether the group has the section it takes from the
workspace's own Sections, so it follows the Details beside it before they are saved. Its element loads only when the
view opens.

The refusals that send a user to an administrator name the place whole, "Form Builder › Settings › Access", in every
language, so that an administrator reading them over a user's shoulder looks in the right section; a test of the words
holds it.

## Consequences

- An administrator who adds the section to a group sees on the same screen that the group still needs permissions,
  and one click opens the screen that gives them.
- Changing a group's permissions stays in one place, with its dependencies, conflicts and locks handled once.
- The view adds no words to the backoffice's start: its words are Access's, loaded with it.
- **Revisit** if Umbraco gives packages a part in a workspace's submit, so that a view's changes save with the
  workspace's Save and are asked about on leaving.
