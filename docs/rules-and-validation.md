# Rules and validation

Rules make a form respond to its answers: they show, hide or require questions, show a message, decide which outcome a
visitor gets and whether an action runs. Validation checks each answer. Both run in the visitor's browser as they type,
and again on the server, which always has the last word.

## Contents

- [What a rule can do](#what-a-rule-can-do)
- [Writing a rule](#writing-a-rule)
- [How conditions compare](#how-conditions-compare)
- [A hidden field is gone](#a-hidden-field-is-gone)
- [Checking answers](#checking-answers)
- [Patterns](#patterns)
- [What visitors see](#what-visitors-see)
- [The form check](#the-form-check)
- [One logic, in the browser and on the server](#one-logic-in-the-browser-and-on-the-server)

## What a rule can do

A rule sits on what it affects, and each element takes the rules that make sense for it:

| Element | Its rules | In words |
| --- | --- | --- |
| A page | one, to show or hide it; a hidden page is skipped | "Shown when …", "Hidden when …" |
| A group, a content element | one, to show or hide it | "Shown when …", "Hidden when …" |
| A field | one to show or hide it, and one to require it | "Required when …" |
| A field's messages | any number, each showing its message on the field | "Shows "…" when …" |
| An outcome | one: whether visitors get it | "Used when …" |
| An action | one: whether it runs for an entry | "Runs when …" |

There is no rule that sets a value: values come from [default values, prefill](field-types.md#default-values-and-prefill)
and [calculations](field-types.md#calculation).

## Writing a rule

Select the element and open **Rules** in the inspector, then **Add a rule**. For an outcome, the rule is in its
editor under Settings › After sending; for an action, in the action's editor. The **Rules overview** in the canvas's
**…** menu lists every rule of the form by page, and **Add rule** there starts by asking what the rule should affect.

A rule reads as a sentence:

1. **What the rule does:** **Show**, **Hide**, **Require** or **Show a message on**.
2. **What it affects:** the element, a page or a group.
3. **When:** one or more conditions, and whether **all of these are true** or **any of these is true**. **Add condition
   group** nests a set of conditions with its own all-or-any, as in "the country is Switzerland, and the age is under 18
   or the visitor is a member", up to 10 levels deep.

Each condition looks at something, compares it a certain way, and compares it with a value, a day counted from today,
or another answer of the same kind ("is after the answer to Arrival").

A condition can look at:

- **any earlier or later answer** of a type rules can compare, each row of a grid included;
- **the language** of the page, on sites with more than one;
- **the visitor:** signed in as a member or not, and their **member groups**;
- **a query value** from the page's address, if the form lists it under Settings › Developer › Query values;
- **today's date.**

It can't look at the element itself, at fields inside the element it shows or hides, at hidden fields, or at a member
property that is taken without being shown, because the page doesn't know those values.

**Try it** in the rule's editor lets you give answers and see what the rule does; nothing you give is kept. **Use this
rule** turns a rule off without deleting it: the element then behaves as if it had no rule.

The designer refuses a rule that would make something depend on itself, such as a field whose visibility depends on its
own answer, and says why.

## How conditions compare

What a condition can do depends on what it looks at:

| Looks at | Can compare with |
| --- | --- |
| Text: short and long text, email, phone, web address, query values | is, is not, contains, doesn't contain, starts with, ends with, is one of, is none of, matches a pattern, is empty, is not empty |
| One choice: radio buttons, dropdown, yes or no, country, a grid row | is, is not, is one of, is none of, is empty, is not empty |
| Several choices: checkboxes, ranking, a dropdown with several options | has ticked, hasn't ticked, is one of, is none of, has at least, has at most (a number ticked), is empty, is not empty |
| A box: checkbox, consent | is ticked, is not ticked |
| Files: file upload, signature | has at least, has at most (a number of files), is empty, is not empty |
| A number: number, rating, scale, slider, calculation | is, is not, is more than, is less than, is between, is empty, is not empty |
| A date, a time, a date and time | is, is not, is after, is before, is between, is empty, is not empty |
| The language | is, is not, is one of, is none of |
| The visitor | is signed in, is not signed in |
| Member groups | include, don't include, include one of, include none of |
| Today's date | is, is after, is before, is between |

Names, addresses, grids as a whole and repeatable groups can't be compared; their parts and rows can be.

How values compare:

- **Text** ignores letter case and spaces at either end.
- **Choices** compare the option's stored value exactly, so a rule keeps working when the label is reworded. A rule
  that compares a choice with a value none of its options has gets a warning from the form check.
- **Numbers and dates** compare by value; "between" includes both ends; "today plus 2 days" moves with the date.
- **An answer that can't be read,** such as a date that isn't one, never matches.
- **Each "not" is the exact opposite** of its pair: "is not" matches whenever "is" doesn't, an empty answer included.

## A hidden field is gone

A field hidden by a rule, its own or its group's, page's or repeatable group's, is treated as if it weren't on the
form:

- it is never required and never checked;
- it is never stored, even if a browser posts it;
- other rules and calculations see it as empty;
- merge tags write it as empty, and lists of the answers leave it out.

So a rule that requires a field which another rule can hide is flagged by the form check: when it is hidden, visitors
can send without it.

Inside a repeatable group, rules look at the answers of their own row.

## Checking answers

Each field type brings its own checks, set under **Validation** in the inspector: a length, a range of numbers or
dates, the file types, how many options may be ticked ([field-types.md](field-types.md)). Three more checks are there for
text-like fields:

- **Must match a pattern:** the answer must have a certain shape, such as two letters and four digits ([Patterns](#patterns)).
- **Must match:** the answer must be the same as another field's, as when visitors type a customer number twice.
- **Each answer only once:** a second entry with the same answer is refused, as for a membership number. Entries that
  are spam, rejected or test entries don't count.

**Messages on a field** go further: **Show a message on** a field when its conditions hold, as "Shows "We deliver only
within Switzerland" on Postcode when Country is not Switzerland". The visitor must change the answer before sending.

Every check has a standard message in each visitor language. A field's own message box replaces it for that field;
**Visitor texts** in Settings rewords it for a form or the whole site.

## Patterns

A pattern describes the shape of the whole answer, letter case included. `[A-Z]{2}[0-9]{4}` takes `AB1234` but not
`ab1234` or `AB12345`. **Try it** under the pattern box shows whether it takes an answer you type.

| Part | Means |
| --- | --- |
| `a`, `7`, `-` | that character |
| `.` | any character |
| `[abc]`, `[a-z]`, `[^0-9]` | one of these, one in this range, one that isn't |
| `\d`, `\w`, `\s` | a digit, a letter or digit or `_`, a space; `\D`, `\W`, `\S` the opposite |
| `x?`, `x*`, `x+` | none or one, any number, at least one |
| `x{3}`, `x{2,}`, `x{2,4}` | exactly 3, at least 2, 2 to 4 |
| `(ab)`, `a\|b` | a group, either one |
| `\.`, `\(` | the character itself |

Patterns run in Jiron Form Builder's own matcher, never in a regular expression engine, so no pattern can make the
site slow. A pattern may be up to 500 characters; one it can't take is refused when you type it, with the reason.

## What visitors see

- A problem shows at the field as soon as the visitor leaves it, and goes away as they fix it.
- On **Send** or **Next**, every problem on the page shows at once. With **List of problems** on (Settings ›
  Appearance; on by default), a list above the form names each one and links to it, and takes the focus so screen
  readers read it.
- Without JavaScript, the server checks the post and shows the form again with the same messages.

## The form check

The form check looks at the rules as well as the fields. **Errors**, which block publishing:

- a rule names a field the form doesn't have, or a condition isn't complete;
- a condition compares in a way the field's answers can't be compared, often after a field's type changed;
- rules or calculations depend on each other in a circle;
- a pattern the matcher can't take.

**Warnings**, which don't:

- a rule requires a field that another rule can hide;
- no answers a visitor can give make a page show, so a visitor going forward never reaches it;
- a rule compares a choice with a value that is none of its options;
- the last outcome has a rule, so visitors who match none get the standard thank-you message;
- a rule names a member group that no longer exists;
- every option of a required field has places, so the form can fill up.

**Fix it** beside each problem takes you to the rule.

## One logic, in the browser and on the server

Rules, validation, merge tags and calculations are written twice: in C# on the server, which decides, and in
TypeScript in the browser, which answers at once. Both must give the same result for a shared set of more than 2,000
test cases, so what a visitor sees in the browser is what the server accepts. The rule editor's **Try it** uses the
same logic.

The server works the whole form out in one pass when an entry arrives: which pages, groups and fields are shown, what
is required, which messages apply, which actions run and which outcome the visitor gets.
