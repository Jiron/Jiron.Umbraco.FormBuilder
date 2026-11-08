# 0218. The settings screens and access

- Status: accepted; builds on [ADR-0008](0008-settings-model.md) (the settings model),
  [ADR-0043](0043-backoffice-settings-sections.md) (sections, scopes, the time zone),
  [ADR-0013](0013-permissions.md) (permissions), [ADR-0170](0170-the-inspector.md) (the inspector's setting editors),
  [ADR-0198](0198-restoring-a-version-and-how-long-draft-saves-stay.md) (Settings › General's History) and
  [ADR-0199](0199-a-captcha-is-a-necessary-cookie-for-cookiebot.md) (the Cookiebot category); amends IA 8 and 9
  (where the captcha consent mode is set) and replaces the typed endpoints of Settings › General and Privacy › Data
  collection
- Date: 2026-11-08
- Roadmap step: 12.4, its part 12.4a (ADR-0197)

## Context

Six sections are stored (`general`, `email`, `protection`, `privacy`, `integrations`, and `access` in `jfbPermission`),
and two screens edit a part of them: Settings › General its History box (10.3b) and Privacy › Data collection the
insights switch (11.5). Part 12.4a asks for every section of §4.8 with host locks, for the features that exist by now:
the site switch, a site scope that must name a root content node, the time zone from a list, the Cookiebot category as
a switch, and a permissions editor of groups and permissions with folder and form overrides. A test must enumerate
every setting and fail unless each has a screen (or is host-only) and is read by product code outside the settings
module. Files has an alias but no section yet, although file fields exist since Phase 7.

Open questions:

- **One endpoint and screen per section, or one for all.** Each section has its own fields, and later phases add more
  (retention, IP handling, sensitive defaults in 13.x).
- **How a screen draws a setting:** the inspector already draws every kind of value a setting holds (ADR-0170), with
  its help below, locks and problems.
- **Where the captcha consent mode is set:** IA 9 puts it on Data collection, a global screen, but it is stored with
  spam protection, per site, and matters only for a captcha.
- **What Files holds,** and who reads it.
- **How the matrix saves** many grants at once without leaving half of them.

## Decision

### One endpoint pair and one screen for every section

1. **`GET settings/{section}?site={key}`** answers the section's values in the scope (the global values without a
   site), the version of the scope's own row, the fields a site sets of its own, the host's locks, for a section a site
   can set the sites (Umbraco's content roots, by name, each marked when it has values of its own), and the section's
   **fields**: the schema the inspector's editors draw, in the shape the field types endpoint serves (ADR-0055), so one
   renderer draws both. **`PUT settings/{section}`** saves the fields it sends, with the version read: for the global
   scope those fields change in the stored values and every other field stays as stored (ADR-0198's rule for a screen
   that shows part of a section); for a site, the fields it names as its own. Both need `manageSettings`. The typed
   endpoints of General and Data collection go; their screens use these.
2. **The schema comes from the section records.** Each property carries `[SettingsField]` (the box it sits in, its
   order, its editor, the choices of a text or the ones of an enum it leaves out, and whether a number has a unit) and
   is read by `SettingsSchema`: its label, help and unit are the backoffice's keys `jfbGlobalSettings_<section>_<field>`,
   `…Help` and `…Unit`, each choice's `…_<choice>`, and a box's headline `<section>Box_<box>`. The area is the global
   settings' own, loaded with their screens, as `jfbSettings` is the form workspace's (ADR-0201); a test fails for a
   served key without words in English or German. Its limits come from the section's constants (`[Range]`,
   `[MaxLength]` on a text or a list), an enum is a choice. A property the backoffice never edits is `[HostOnly]`
   (none so far). A property with neither stops the schema with a message, as the field settings reader does, and a
   test reads every section. Only a field the host **locks** reads only; one it restricts or clamps stays editable,
   and its screen names the key where it refuses a value.
3. **The screen** (`settings-screen.element.ts`) is one element for every section, as IA 8 describes: the boxes of the
   section's fields, each with at most seven settings before **More options** (none has more so far), each setting
   with its help below it, a box of one object (History, New forms) holding the object's settings under the box's
   headline, a field that matters only with another's value shown with it (the puzzle's strength with proof of work, the
   Cookiebot category with Cookiebot), a locked one read-only with the key that locks it, a section the host locks
   whole with one line and no Save, and **Save** in the footer. It checks what it can before it sends, says the server's refusal under the field it names,
   and says a save made meanwhile with the values now saved, as General's History box did. What a section shows
   besides its fields (the line that Umbraco can't send mail, Send test email, the captcha keys, the files status, the
   hosts webhooks may call, the form block type) comes from the section's own module beside the boxes, as its
   **facts**; the choices that are the site's own (the themes, the email layouts) come with them, so a user who may
   manage settings but not the library still picks one. The inspector's editors draw what they can; the screen's own
   draw the rest. A user with unsaved changes who picks another site is asked first.
4. **The site switch** heads every section a site can set (General, Email, Spam protection), set to **All sites**. Its
   choices are Umbraco's content roots, which are the sites (ADR-0043). With a site chosen, each field shows the value
   that site gets; a field it sets itself says so once under it, with **Use the value for all sites**; a field every site
   shares (`[GlobalOnly]`) is left out, and one line above the boxes says those are under All sites. A site save whose
   key names no root content node is refused (`400 site_unknown`) with nothing written.
5. **The time zone** is a list, never typed: the browser's `Intl.supportedValuesOf('timeZone')` with UTC first, grouped
   by their area in a native select, which keeps its type-ahead and is said as a list. A zone the server can't resolve is
   refused with the server's words under the box; a stored zone the browser doesn't list stays offered as it is.

### The sections

6. **General:** the default theme (the code's themes and the library's), which fields get a mark, the three default
   button labels in every language of the site, the new-form defaults (the consent field and its wording, the
   notify-the-creator action), History (10.3b), and per site the base URL (its placeholder the address the site gets
   without one) and the time zone. The form block type keeps its box (IA 8): **Create a form block type…** while there
   is none, for a user with Umbraco's Settings section.
7. **Email:** sender name and address, the default layout (a select of the library's layouts and the built-in card),
   the team recipients and the forward addresses as lists of addresses. While Umbraco can't send mail, one line at the
   top names the key that sets it up, and **Send test email** is not offered; otherwise it sends the saved settings'
   mail to the user's own address, saving first when the screen has changes.
8. **Spam protection:** the level and the captcha as one list, each captcha provider with its key status: a provider
   without keys can't be chosen and names the keys that add it; the proof-of-work strength with proof of work; the
   content checks (how strict, each strictness saying what sends an entry to Spam, blocked words, blocked email
   domains, the most links) and the blocked address ranges. **The captcha consent mode moves here** from IA 9's Data
   collection, in a box of its own after the level, beside the captcha it governs: it is stored per site with spam
   protection and means nothing without a captcha, and a global screen could not set a site's. While the host refuses
   the implicit mode, the choice stays in the list, greyed, with the key that allows it. Its Cookiebot category is a
   choice of two, `necessary` (the default) and `marketing`, whose help says that Cookiebot files a captcha's cookie as
   necessary (ADR-0199).
9. **Files** becomes a section every site shares (`files`, schema version 1): **the file types a site allows** (the
   file field's type groups; all by default) and **the largest file any field may take** (by default, and at most, the
   host's `Files:MaxFileBytes`, a clamp the screen names). The upload refuses a file of a type the site no longer
   allows or above the limit; an extension of no group stays the field's to allow, and the host's blocked extensions
   refuse it still. The form check's hint on a file field without a size names the site's limit, and it warns about a
   file field whose own limit is higher or that allows a type the site no longer takes. The size is written in
   megabytes. A status box says the type check, whether a scanner is registered and where files are stored.
10. **Integrations:** the signing secret, Slack's and Teams' addresses as references (a text that is no reference is
    refused before it is sent, so a secret never reaches a request), each saved one said found in the host configuration
    or not, with the key a developer adds; the Umbraco webhook events switch; and the hosts webhooks may call, read-only
    from the host configuration.
11. **Data collection** keeps the insights switch on the same screen element; 13.1 and 13.3 add retention and IP
    handling there as fields.

### Access

12. **Settings › Access** (`managePermissions`): `GET access` answers every Umbraco user group (name, icon, whether it
    has the Form Builder section, whether it is the administrators'), the permissions in three parts (forms; entries;
    the library, settings and privacy tools), each group's global grant, whether the host configuration sets the grants
    (`Access:Grants`, then read-only with its key), and the folders and forms with grants of their own or a
    restriction. `PUT access` saves the changed groups' global grants in **one unit of work**, each with the grant it
    replaces, so a change made meanwhile refuses the whole save (`409`) and nothing half-saved remains. A grant without
    its dependency, or a global-only permission on a folder or form, is refused as the Core service does. The
    administrators are ticked and can't be changed, as they hold everything (ADR-0013).
13. **Permissions…** in a folder's and a form's "…" opens a sidebar of that folder's or form's grants per group, and its
    restriction, saved the same way (`GET`/`PUT access/folder/{key}`, `access/form/{key}`), each with its own
    sentence on what restricting does. The Access screen's list of special folders and forms opens the same sidebar.

### The wiring test

14. **Every setting has a screen and a reader:** a unit test reads every section's schema and fails for a property
    without `[SettingsField]` or `[HostOnly]`; it then looks for each property's name read outside the settings module
    (`Core/Settings`, the settings endpoints) in the product's sources, and fails for one nothing reads.

## Consequences

- A setting added later appears on its screen with its attribute and two words, and the wiring test fails until
  something reads it.
- The schema the inspector draws is now served for the settings too: a new editor kind serves both.
- The captcha consent mode is on one screen, per site, beside the captcha; Data collection has no captcha setting.
- The matrix never leaves some groups saved and others not.
- **Revisit** if a section needs a layout the boxes can't give, or if sites ask for the time zone typed.
