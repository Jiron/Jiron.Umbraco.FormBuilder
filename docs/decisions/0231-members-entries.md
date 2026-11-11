# 0231. Members' entries: the gate, one entry per member, the anonymous form and a member's entries

- Status: accepted; completes [ADR-0004](0004-rendering-and-submission-paths.md) (the frame headers), [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (members-only
  forms and their posts without JavaScript), [ADR-0086](0086-the-options-endpoint.md) (a members-only form's options),
  [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md) (the gate),
  [ADR-0095](0095-visitor-texts-members-only-forms-and-the-pending-note.md),
  [ADR-0100](0100-the-post-without-javascript.md) (`member_required` without JavaScript),
  [ADR-0124](0124-prefill.md) (the rules' member on the server, the member link) and
  [ADR-0138](0138-the-challenge.md); builds what [ADR-0184](0184-the-form-settings-view.md) and
  [ADR-0226](0226-the-phase-13-privacy-review.md) (item 8) left to this step
- Date: 2026-11-11
- Roadmap step: 14.1 (part 14.3, members)

## Context

Since 7.1 a form for members shows only to the members it admits, gives its challenge only to them and binds it to the
member signed in, but takes no entry: every submission meets `member_required`. ADR-0012 designed the rest: the challenge
a post without JavaScript carries, the origin first, no framing by another origin. "One entry per member" is stored
and read by the form check, but nothing at submission holds it, and the Settings view has no switch for it. The options
endpoint answers a members-only form's options to anyone. The server's rules always see nobody signed in, though the page
learns the member from the challenge. Entries keep the member's key, but nothing in the backoffice shows it, a member
deleted in Umbraco leaves their entries as they were, and a form cannot leave the member out (ADR-0226).

## Decision

1. **The gate admits a form's members, on both paths.** A transport hands the pipeline the member signed in and a member
   context (`IFormMemberContext`, which the website's `FormMembers` is) that says whether the form's access admits them
   and which of a set of groups they are in. On a form for members the gate then asks, in this order: no server key and
   a transport with challenges (otherwise `member_required`); a member signed in whom the access admits now, groups
   compared at the submission as on the page, not only when the challenge was issued (otherwise `member_required`); a
   challenge that names that member (otherwise `challenge_required`, so the runtime fetches a fresh one). The preview's
   ticket still stands in for a test member (ADR-0203).
2. **The server's rules see the member as the page does.** "Member signed in" is true when a member is on the request,
   and the member's groups are those the form's rules name that the member is in: what the challenge tells the page
   (ADR-0138), so both runtimes decide alike, on a form for anyone too.
3. **A post without JavaScript to a form for members** carries a challenge rendered into its page (`jfb-challenge`),
   issued to the member for that render, which is per member and `no-store` already:
   - its submission id is derived from the challenge's id (the first 16 bytes of SHA-256 over a fixed label and the id,
     as a version-8 GUID), so a double click or a reload of the post finds the entry and redirects to its outcome;
   - a missing, expired, used or another member's challenge renders the page again with the answers, a fresh challenge
     and the notice that the answers were not sent; Next checks the page with the challenge too;
   - the origin comes first: on a form for members, and on any form while a member is signed in, a post whose `Origin`
     is not the site's own, or that has no `Origin` or `null` without `Sec-Fetch-Site: same-origin`, answers a bare
     `403` that shows none of the posted answers, before a rate limit's or any other re-render;
   - the minimum fill time does not apply, as for every post without JavaScript (a re-render would restart it).
4. **The receipt is the member's.** The outcome after a post without JavaScript, the copy of the answers and the public
   receipt of a form for members answer only the member who made the entry (and, for the receipt, only with the
   challenge that made it); anyone else gets the same miss as for an unknown submission.
5. **No framing.** A page that renders a form for members, or a form with a member's values in it, and the render
   endpoint's answer for such a form send, as the response starts, a separate `Content-Security-Policy: frame-ancestors
   'self'` and `X-Frame-Options: SAMEORIGIN`, so no other origin can show the member's page in a frame (ADR-0004, as
   ADR-0104 planned for `render`). A response that already says how it may be framed keeps the site's word. No Jiron
   Form Builder response sets a cookie.
6. **One entry per member** sits under "Who can fill it in" in the Availability group, shown once the form is for
   members (progressive disclosure); the structure check refuses it on a form for anyone (`property_not_supported`),
   where it could only hold back the visitors who signed in, and choosing "Anyone" takes it away with the member groups. Every entry of the form that is not a draft or a test entry
   counts, in any state and of any version; a deleted, erased or anonymised entry no longer does. The pipeline checks it
   with the availability (section 4.4, step 5) and holds it in the same write unit as the entry limit, so two posts at
   once still make one entry. The refusal is `409 already_submitted`. A member who has sent sees the notice "You've
   already sent this form" in the form's place, in the page's language; the runtime hands the refusal to the page's own
   post, which shows that notice. A draft is no entry and may still be saved.
7. **The options of a form for members** answer only a member the form admits, and a server key, as the form endpoint
   does (ADR-0150); anyone else gets `403 member_required` and no option.
8. **The anonymous form** (`privacy.anonymous`, a switch in the Privacy group): its entries keep no member, whoever is
   signed in, and the member's values reach nothing the entry keeps. The form check refuses it (errors) together with a
   form for members, one entry per member and a field that takes a value from the member, all of which need the member
   or store them. Rules may still look at whether a member is signed in, which stores nothing. The privacy facts sheet
   and the checklist say what the switch does.
9. **A deleted member's entries** are anonymised (the default) or deleted, per a setting in Settings › Privacy ("When a
   member is deleted"), for all sites at once, since a member belongs to no site. A handler of Umbraco's member deletion
   runs the privacy tools' erase for that member (ADR-0223): legal holds keep their entries as they are, drafts go, and
   the audit log records `people.erased` with the mode and the user who deleted the member.
10. **A member's entries are one click away.** An entry's details name the member who sent it, linked to the member's
    editor for a user with the Members section. The member's editor gains an "Entries" view (for a user with the Form
    Builder section) that lists that member's entries of the forms the user may read entries of, newest first, each
    opening in the Form Builder section, from `GET members/{key}/entries`.

## Alternatives

- **Admission only when the challenge is issued:** a member taken out of a group would keep sending for as long as a
  challenge lives. Comparing groups at the submission costs one read of the member's roles.
- **A random submission id in the page without JavaScript:** a reload of the post would be a second post with the same
  id and a fresh challenge, refused as a duplicate with no way to the outcome. Deriving the id from the challenge makes
  the reload a retry.
- **One entry per member on a form for anyone:** visitors who are not signed in could send any number, so the switch
  would mostly hold back the honest. Refusing the combination keeps the setting's meaning.
- **Counting only some states:** a rejected or spam entry is still the member's one entry; a privacy officer who
  deletes it gives the member their turn back.
- **Keeping a deleted member's entries unchanged:** their key would point nowhere and their answers would stay, which
  privacy by default rules out; deleting them by default would lose counts the site may rely on, so anonymising is the
  default and deleting the choice.

## Consequences

- Members-only forms work end to end, with and without JavaScript, and every refusal on another origin shows nothing of
  the post.
- `already_submitted` joins the public API's codes in use; the runtime needs no words for it.
- The member deletion handler runs inside Umbraco's delete, in chunks; a member with many entries makes the delete take
  longer.
- **Revisit** if sites ask to keep a deleted member's entries untouched, or to count only some states.
