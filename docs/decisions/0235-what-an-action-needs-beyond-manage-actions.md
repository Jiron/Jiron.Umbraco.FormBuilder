# 0235. What an action needs beyond `manageActions`

- Status: accepted
- Date: 2026-11-12
- Roadmap step: 16.1 (the security review)

## Context

The security review of 16.1 read every management endpoint against what the person calling it holds. Two paths let a
user with `manageActions` do more than their permissions allow:

- **The content action** (ADR-0162) saves and publishes Umbraco content as the site itself. Only `manageActions` gated it,
  so a form designer without Umbraco's Content section, or with a narrow start node, could point an action at any
  parent, update an existing page of the same name, publish it, and send the form once. The media folder for uploaded
  files had the same gap.
- **Copying sensitive answers.** The email, webhook and content actions copy a sensitive field only when the action names
  it (`includeSensitive`, ADR-0014). Naming it needed only `manageActions`, so a user without `viewSensitive` could add an
  email action addressed to themselves, or point an existing action that already copies a salary at a new recipient, and
  read every later entry's sensitive answers. ADR-0150 closed the same hole for lowering a field's sensitivity.

## Options

1. **Hold the action against the people who decide it** where it is saved, published and run from the preview: the
   user's own Umbraco rights for the content action, `viewSensitive` for an action that copies sensitive answers.
2. **Check only at publish.** A draft runs nowhere until it is live, but the preview's test entries run the draft's
   actions at once, so a content action would still write content.
3. **Make the content action a host opt-in** (configuration only). Safe, but sites that want it lose the designer's
   control over it, and the sensitive copy would still be open.

## Decision

Option 1, in one check (`ActionGrantChecks`) that the management API calls wherever actions change or run.

- **Which actions count:** those a definition adds, or whose settings, switches or rule it changes, against the one it
  replaces (`DefinitionDiff.ChangedActions`). Moving an action in the run order, and any other change to the form,
  leave an action that was already there alone, so a designer without the rights can still edit a form whose content
  action someone else set up.
- **Where:** a draft save, a version restore and a replace from a file (against the draft they replace); a publish
  (against the version that is live, as that is what changes for visitors); and the preview when its test entries run
  their actions, a ticket that lets them, and Run actions… on a test entry (against nothing, as everything the draft holds
  runs). A duplicate, copied actions, an import and a new form from a template make a draft that runs nothing; its
  publish is held.
- **The content action** needs, in Umbraco: the Content section; create on the picked parent (or at the root), or, for
  the visitor's page or its site, on the item the reach starts at and every item below it; update when it updates an
  item of the same name, and publish when it publishes; every language of the site when its document type varies by
  culture; and, when it stores uploaded files, the Media section and the media folder (or the media root). Umbraco's own
  `IContentPermissionService` and `IMediaPermissionService` decide, so start nodes, granular permissions and language
  access count as they do in Umbraco. A refusal is `403 content_rights` with `gap`
  (`contentSection`, `content`, `languages`, `mediaSection` or `media`), which the backoffice says in its own words.
- **An action that copies sensitive answers** needs `viewSensitive` on the form when it is new or changed: adding a field
  to copy, and changing where an action that copies one sends it, alike. Test entries are left out, as their answers
  are the user's own. A refusal is `403 forbidden` naming `viewSensitive`, as every missing permission is.

## Consequences

- A content action now has an owner Umbraco recognises: someone who may write where it writes. A site that wants form
  designers to set them up gives their user group those rights; the browser tests' designer persona has create,
  update and publish on the whole tree for that reason (`TestUserGroup.ContentEditing`).
- Re-running an entry's actions (Deliveries, the entries list) runs what a publish already held, so it needs only
  `manageActions`, as before.
- Extension action types get the sensitive check for free: any setting of theirs that picks sensitive fields counts.
  An extension type that writes to Umbraco itself is the site's own code and decides its own rights.
- Revisit if Umbraco adds a permission of its own for content created by a process rather than a user.
