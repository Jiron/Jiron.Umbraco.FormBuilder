# Members

How forms work with Umbraco's members, the visitors who sign in on the website: forms only members may fill in, one
entry per member, answers filled in from the member, and what happens to a member's entries.

## Contents

- [Forms for members](#forms-for-members)
- [One entry per member](#one-entry-per-member)
- [Filling in answers from the member](#filling-in-answers-from-the-member)
- [A member's entries](#a-members-entries)
- [Anonymous forms](#anonymous-forms)
- [When a member is deleted](#when-a-member-is-deleted)

## Forms for members

**Who can fill it in**, in a form's Settings view under Availability:

| Choice | Visitors who aren't allowed see |
| --- | --- |
| Anyone | the form, as everyone does |
| Members only | "This form is for members", and that they need to sign in |
| Members of chosen groups | not signed in: that they need to sign in; signed in to another group: "This form is not open to your account" |

The site's own sign-in page does the signing in; the form only checks. Member groups are listed from Umbraco's Members
section, and a group deleted later admits nobody until the form chooses another, which the form check points out.

The check is made again when the entry arrives, so a page cached or left open can't get round it, and a headless site
can't send a members-only form on a member's behalf.

## One entry per member

On a form for members, **One entry per member** lets each member send it once. A member who has sent it sees "You've
already sent this form" in its place. Every entry counts, whatever its state; deleting or erasing the member's entry
gives them their turn back.

## Filling in answers from the member

A field's **Prefill** (Advanced, in the inspector) can take a value from the signed-in member: `name`, `email`, or a
property of the member type that members may see and that isn't marked sensitive.

- **Shown, and can be changed:** the field starts with the member's value, which they may change.
- **Not shown, taken when the entry is sent:** the field is left out of the page for a member who has a value, and the
  member's value is stored whatever the page sends. Visitors who aren't signed in see and answer it as usual.

Merge tags read the member too, such as `{{ member.name }}` in a thank-you message ([merge-tags.md](merge-tags.md)). A
page with a form that fills in from the member isn't cached for signed-in members; for everyone else it stays as
cacheable as before.

## A member's entries

An entry sent by a signed-in member remembers the member. Its panel names the member under **More details**, with a link
to the member for users who may open Umbraco's Members section.

In Umbraco's **Members** section, each member has a **Form entries** view, for users who may open Form Builder: the
member's entries in the forms the user may see, newest first, each opening in Form Builder.

## Anonymous forms

**Anonymous**, in a form's Settings view under Privacy, keeps no member on its entries, even when a member is signed in,
for surveys whose answers must not be traced to anyone. An anonymous form can't be for members only, take one entry per
member or fill in answers from the member; the form check says so.

## When a member is deleted

**Privacy › Data collection › Members** says what happens to a member's entries when the member is deleted in Umbraco,
in every form:

- **Anonymise their entries,** the default: the entries stay with their form, number and state, so counts and insights
  keep them, but every answer and everything else about the member goes;
- **Delete their entries:** the entries go, with their files and mail.

Entries on a legal hold stay as they are. The audit log records the erasure ([privacy.md](privacy.md)).
