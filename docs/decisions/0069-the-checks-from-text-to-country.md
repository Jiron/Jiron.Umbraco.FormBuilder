# 0069. The checks from text to country

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the message kind
  `digits`), [ADR-0058](0058-field-answers-and-values.md) (a number reads up to the digits a calculation reads, and a
  ranking's reading keeps a repeated option or position) and [ADR-0062](0062-the-built-in-types-checks.md) (the forms
  of an email address, a phone number and a web address, a grid's required rows, a ranking's positions and the
  stored digits of a number)
- Date: 2026-10-26
- Roadmap step: 5.4a (decided and built in both runtimes); 5.4b stores the answers, 6.4 reads answers in the browser

## Context

Steps 5.2b and 5.2c gave the built-in types their checks of their settings in both runtimes (ADR-0062). For the
types from Text to Country, §4.3.2 and §4.3.3 ask for more than the settings give:

1. **The forms of three types.** §4.3.3 counts a malformed email address as `invalid`, and §4.3.2 asks a phone field
   for a "lenient international format check". Neither exists, and a web address is held only to its scheme, so
   `https://` or `https://exa mple.com` passes.
2. **Long numbers.** A number field keeps at most 15 significant digits and 15 places, so the stored double is exact
   (ADR-0003, ADR-0005). Reading refuses longer numbers (ADR-0058), so a visitor who types sixteen digits reads "not
   a valid number", which is wrong: the number is valid, only too long.
3. **A grid's required rows.** The definition gives each row `required`, and a required grid requires every row
   (ADR-0002). No check reads either, and the vectors' rows have no `required`.
4. **A ranking's positions.** Without script a visitor gives each option a position. Reading refuses a repeated
   position or option (ADR-0058), so the vectors, whose answers must be readable, cannot hold one, and the browser,
   which sends the options in order, has no check that an option comes once.

## Options

1. **Where the forms are checked.**
   - In reading, as unreadable answers: the browser would find nothing until it reads answers itself (6.4), and the
     vectors could not hold such answers, so the two runtimes would not be held to one rule.
   - .NET's `MailAddress` and `Uri` and a phone number library: `MailAddress` takes display names, comments and
     quoted parts, `Uri` repairs and normalises what it is given, a phone library brings country data of its own,
     and none of them exists in the browser.
   - **Each type's check, in both runtimes, as `invalid`, with the forms spelled out code unit by code unit.**
     Chosen: the vectors hold both runtimes to the same forms, and the visitor learns before posting.
2. **What a long number says.**
   - `invalid`, as today.
   - **A message kind of its own, `digits`**: "use at most 15 digits". Chosen. The kind is the number type's; a type
     that stores numbers and lacks the kind (rating, scale, slider, a site's type) reports `invalid`.
3. **Which rows are required.**
   - The marked rows only once any row is answered, as a name's required parts (ADR-0056): an editor who marks a row
     required would see an empty grid pass.
   - **A marked row needs an answer whether or not the grid is required, and a required grid needs every row.**
     Chosen.
4. **Repeated positions and options.**
   - Refused in reading, as today.
   - **Kept by reading, refused by the ranking's check.** Chosen: the vectors hold the rule, and the browser checks
     the order it sends.

## Decision

### The forms

The checks read the cleaned answer (ADR-0005) code unit by code unit, the same in C# and TypeScript. "A character
outside ASCII" is a UTF-16 code unit from U+00A0 on that cleaning does not trim, so no space and no control
character counts as one; letters of every script do.

**An email address** is `local@domain`, with one `@`:

- the local part holds 1 to 64 code units: atoms apart by single dots (no dot first, last or twice), each of ASCII
  letters and digits, ``!#$%&'*+-/=?^_`{|}~`` and characters outside ASCII;
- the domain is a host name.

Quoted local parts, comments, IP literals and display names are refused. Addresses in other scripts (RFC 6531) are
taken; whether a mail server delivers to one is the mail's concern (Phase 8). An email's check reports, in this order,
`invalid` for the form, `confirmation`, and on the server `invalid` for a domain the field does not take.

**A host name** (an email's domain, a web address's host) holds at most 253 code units in at least two labels apart
by dots. A label holds 1 to 63 code units of ASCII letters, digits, hyphens and characters outside ASCII, and starts
and ends with no hyphen. The last label is not only digits. So `localhost`, IP addresses and a trailing dot are
refused: a visitor's address is a public one.

**A phone number** is lenient and international:

- digits, spaces (the characters cleaning trims), `-`, `.` and `/`, an optional `+` first, and at most one pair of
  brackets holding digits and spaces, at least one digit, such as the trunk prefix in `+41 (0)44 668 18 00`;
- it starts with `+`, `(` or a digit and ends with a digit, with 4 to 17 digits: the 15 of E.164 and room for a trunk
  prefix or an international prefix written `00`;
- an extension may follow: optional spaces, then `ext`, `ext.` or `x` (in any case) or `#`, optional spaces, and 1
  to 6 digits.

The number is kept as typed. The field's `defaultCountry` is the renderer's (6.2, 6.4): the code shown before the
input.

**A web address** is `scheme://host[:port]` followed by an optional path, query or fragment:

- the scheme, the text before the first colon in ASCII lowercase, is one the field allows (ADR-0062);
- the host is a host name; the port, after a colon, is 1 to 5 digits from 1 to 65535; a user name or password (an
  `@` before the path) is refused, since it hides the host a reader sees;
- the path, query and fragment hold no control character and none that cleaning trims.

### Numbers

Reading takes a number of up to 60 digits in all and 60 places, the most a calculation reads (ADR-0063), converted
exactly to the wire form as before, an exponent included; a longer one is unreadable (`invalid`). Then, before the
type's checks, a value of a field that stores numbers and has more than 15 digits in all or more than 15 places
fails `digits` when its type declares that kind and `invalid` otherwise, alone: neither the type's checks nor the
field's own checks run. In TypeScript the same holds for a field of the number kind. The stored double is therefore
always exact (ADR-0003); a calculation's result is the server's and keeps its rule (ADR-0063).

### A grid's rows

The vectors' rows are `{ "id", "required" }`, as the projection writes them. A grid without answers is `required`
when the field is required or any row is marked required. A grid with answers fails, after its columns' `invalid`,
`required` when a row it needs has none: every row when the field is required, otherwise the marked ones.

### A ranking's positions

Reading takes the options in the order given, or, without script, each option's position as a whole number from 1
and the options ordered by it, options that share a position in the order of their values. The check fails
`invalid` for an option given twice and for a position given to two options, as for a count other than `top` or the
options. The browser reads positions as Core does, so the vectors hold them in both runtimes.

### What 5.4a finds in place

Word limits and excluded dates are checked in both runtimes since 5.2b, the camera option is a public setting of the
file type, and an option's places are refused on types without places and outside their range at publish (ADR-0035);
counting them is the pipeline's (6.1). Country names come from .NET's culture data since 5.3d (ADR-0066).

## Consequences

- A visitor gets the same message for the same answer in the browser and on the server, the email's domains, the
  uploads' host limits and `uniquePerForm` aside.
- The message kinds grow by `digits`, in the model, the schema and the field types endpoint; statistics count it as
  any kind (`invalid:{fieldKey}:digits`).
- Answers that were unreadable read now: a repeated ranking option or position, and a number of 16 to 60 digits.
  Rules compare them as read; the checks refuse them, so none is stored.
- **Revisit** the forms when visitors meet a refusal that a mail server or browser would take (a new top-level
  domain of digits, quoted local parts), and the phone's digits if a numbering plan grows past E.164.
