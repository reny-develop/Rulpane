# For an AI asked to make a Rulpane link

People see a short guide on the page; this part is for an AI reading the page as text.

Rulpane (<https://reny-develop.github.io/Rulpane/>) opens a working screen from one link. The link
carries a **paneset**: one JSON document holding

- `"$schema": "rulpane/paneset/v1"` — required;
- `r`, the rules: a [Rulealize](https://github.com/reny-develop/Rulealize) rule set, written in place — required;
- `v`, the view: what the screen shows and where, with `"$schema": "rulealize/view/v1"` — optional; without it the screen is made from the rules;
- `t`, the theme, with `"$schema": "rulealize/theme/v1"` — optional;
- `s`, the state's `data` to start from — optional; the rules' own `state.initial` without it;
- `b`, which of the rules' names the view reads under other names — optional.

## What to do

1. Work out what can be done in the system, by whom, and when it is finished.
2. Start from a whole example and change it. Each start-page sample is a rule set and a view that
   open as they are:
   [rules](https://reny-develop.github.io/Rulpane/rulesets/split-bill.json) and
   [view](https://reny-develop.github.io/Rulpane/views/split-bill.json) of a split bill, and the
   same for `packing-list` and `scoreboard`. Write them into the paneset as `r` and `v`.
3. Look up every operation before using it. The
   [Rulealize Registry](https://reny-develop.github.io/Rulealize.Registry/) has a page for each,
   such as `https://reny-develop.github.io/Rulealize.Registry/op/seq.where.html`, which leads to
   its plugin's specification: that gives every key it takes. How a rule set is laid out is in
   the [quick guide](https://github.com/reny-develop/Rulealize/blob/main/doc/ruleset-quickguide.md).
   Use only the plugins listed at the end of this page.
4. The view's blocks and keys are in [Rulpane's README](https://github.com/reny-develop/Rulpane#views).
5. Give the person the link: `https://reny-develop.github.io/Rulpane/#p=` followed by the paneset
   as JSON on one line, with every space written as `%20`, inside strings too — a chat cuts a link
   at the first space. Nothing else needs encoding.
6. Give the JSON itself too, in a code block, and say: if the link does not open, paste the JSON
   at <https://reny-develop.github.io/Rulpane/#make>, press "Make link" and tell me the reason it
   shows. The reason names the key that is wrong.
7. When the request needs it, say plainly what a link cannot do: it keeps no secrets, anybody
   holding it can take any step, and two people working from one link make two separate histories.

## Before you answer

- [ ] The paneset, and the view and the theme when written, each have their `$schema`.
- [ ] Every operation is one a listed plugin has, with only the keys its specification gives.
- [ ] Every plugin whose operation, `$`, `@` or `#` is used is in `requires`.
- [ ] Lists where an expression goes are `seq.of`, not JSON arrays (arrays are fine in
      `state.initial`, `s` and a view).
- [ ] No open parameter is read in `when`, `actor` or a `domain`; every `validate` clause reads one.
- [ ] Every view binding names a projection the rules have, or a state field as `$name`.
- [ ] The link is one line, and every space in it is `%20`.

## The plugins Rulpane runs

A rule set can use only these. Write each one used into `requires` as shown.

| Plugin | In `requires` |
| --- | --- |
<!-- requires -->
