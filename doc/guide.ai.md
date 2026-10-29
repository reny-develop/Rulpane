# For an AI asked to make a Rulpane link

People see a short guide on the page; this part is for an AI reading the page as text.

Rulpane (<https://reny-develop.github.io/Rulpane/>) opens a working screen from one link. The link
carries a **paneset**: one JSON document holding

- `"$schema": "rulpane/paneset/v1"` — required;
- `r`, the rules: a [Rulealize](https://github.com/reny-develop/Rulealize) rule set — required;
- `v`, the view: what the screen shows and where, with `"$schema": "rulealize/view/v1"` — optional; without it the screen is made from the rules;
- `t`, the theme, with `"$schema": "rulealize/theme/v1"` — optional;
- `s`, the state's `data` to start from — optional; the rules' own `state.initial` without it;
- `b`, which of the rules' names the view reads under other names — optional.

Each of `r`, `v` and `t` is either the document written in place or a name: `<id>@<version>` for a
rule set the Registry publishes, `<owner>/<name>@<version>` for what the Gallery publishes. A name
is the thing as its publisher wrote it, in their words. `"r": "Rulealize.RuleSet.Request@1.1.0"`
with no `v` opens a screen made from those rules, with their own names on the buttons — not one
made for the request. Name a rule set only when it already does what was asked, and then write
the view for it; otherwise write the rules in place.

## What to do

1. Work out what can be done in the system, by whom, and when it is finished.
2. Read before you write. **Write no operation, type or key from memory**: a name that is not in
   what you read is refused, and the link does not open. Open these, in this order:
   1. How a rule set is laid out, in Rulealize's quick guide:
      <https://raw.githubusercontent.com/reny-develop/Rulealize/main/doc/ruleset-quickguide.md>.
      It names every top-level key; `inputs`, the things that may be done, is required.
   2. What the Rulpane Gallery publishes: <https://reny-develop.github.io/Rulpane.Gallery/llms.txt>.
      Take the paneset closest to the request and change it, rather than starting from nothing.
   3. Every operation and the keys it takes, in the Rulealize Registry:
      <https://reny-develop.github.io/Rulealize.Registry/llms.txt>. Look up each operation you
      write there. Use only the plugins listed at the end of this page.
   4. The view's blocks and keys, in Rulpane's README:
      <https://raw.githubusercontent.com/reny-develop/Rulpane/main/README.md>, under "Views".
3. When nothing in the Gallery is close, start from the whole paneset below.
4. Write `v`, always, in the language the person asked in: a `title`, a `label` on every value
   shown, and `names` for every input and parameter. Without it the screen shows the rules' own
   keys, such as `setMonthly` and `futureValue`, and a person cannot tell what they are. Round
   every number a person reads with `math.round` in the projection: `math.div` and `math.pow`
   give a number such as 12331010.055470388734304367250.
5. Give the person the link: `https://reny-develop.github.io/Rulpane/#p=` followed by the paneset
   as JSON on one line, percent-encoded whole as `encodeURIComponent` would: `{` as `%7B`, `"` as
   `%22`, a space as `%20`, and so on, so the link starts `#p=%7B%22%24schema%22`. A chat cuts a
   link at the first `{`, `"` or space it holds, and what is left opens nothing.
6. Give the JSON itself too, in a code block, and say: if the link does not open, paste the JSON
   at <https://reny-develop.github.io/Rulpane/#make>, press "Make link" and tell me the reason it
   shows. The reason names the key that is wrong.
7. When the request needs it, say plainly what a link cannot do: it keeps no secrets, anybody
   holding it can take any step, and two people working from one link make two separate histories.

## A whole paneset

Cups of water drunk today, against a goal that can be changed. It opens as it is.

```json paneset
{
  "$schema": "rulpane/paneset/v1",
  "r": {
    "$schema": "rulealize/ruleset/v1",
    "id": "water",
    "version": "1.0.0",
    "requires": [
      { "plugin": "Rulealize.Plugin.TypeSchema", "version": "^1.3" },
      { "plugin": "Rulealize.Plugin.State", "version": "^1.0" },
      { "plugin": "Rulealize.Plugin.Arithmetic", "version": "^1.2" },
      { "plugin": "Rulealize.Plugin.Comparison", "version": "^1.0" },
      { "plugin": "Rulealize.Plugin.Binding", "version": "^1.0" }
    ],
    "state": {
      "schema": {
        "cups": { "op": "type.int", "min": 0, "max": 30 },
        "goal": { "op": "type.int", "min": 1, "max": 30 }
      },
      "initial": { "cups": 0, "goal": 8 }
    },
    "inputs": {
      "drink": {
        "when": { "op": "cmp.lt", "left": "$cups", "right": 30 },
        "effects": [{ "op": "state.set", "path": "cups", "value": { "op": "math.add", "of": ["$cups", 1] } }]
      },
      "undo": {
        "when": { "op": "cmp.gt", "left": "$cups", "right": 0 },
        "effects": [{ "op": "state.set", "path": "cups", "value": { "op": "math.sub", "left": "$cups", "right": 1 } }]
      },
      "setGoal": {
        "params": { "goal": { "open": { "op": "type.int", "min": 1, "max": 30 } } },
        "effects": [{ "op": "state.set", "path": "goal", "value": "@goal" }]
      }
    },
    "projections": {
      "cups": "$cups",
      "left": { "op": "math.max", "of": [0, { "op": "math.sub", "left": "$goal", "right": "$cups" }] }
    }
  },
  "v": {
    "$schema": "rulealize/view/v1",
    "title": "Water today",
    "layout": [
      { "c": "stats", "items": [{ "label": "Cups", "bind": "cups" }, { "label": "To the goal", "bind": "left" }] },
      { "c": "actions", "inputs": ["drink", "undo"] },
      { "c": "actions", "title": "Goal", "inputs": ["setGoal"] }
    ],
    "names": {
      "inputs": { "drink": "Drank a cup", "undo": "Take one back", "setGoal": "Set" },
      "params": { "goal": "Cups a day" }
    }
  }
}
```

## Before you answer

- [ ] The paneset, and the view and the theme when written, each have their `$schema`.
- [ ] The rule set has `id`, `version`, `state` and `inputs` — or `r` names one the Registry
      publishes that does what was asked, and a `v` is written for it.
- [ ] Every operation and type was found in the Registry's list, with only the keys it gives there.
- [ ] Every plugin whose operation, `$`, `@` or `#` is used is in `requires`.
- [ ] Lists where an expression goes are `seq.of`, not JSON arrays (arrays are fine in
      `state.initial`, `s` and a view).
- [ ] No open parameter is read in `when`, `actor` or a `domain`; every `validate` clause reads one.
- [ ] `v` is written, in the person's language, and every number shown is rounded.
- [ ] Every view binding names a projection the rules have, or a state field as `$name`.
- [ ] The link is one line, and after `#p=` holds no `{`, `"` or space: it starts `#p=%7B%22`.

## The plugins Rulpane runs

A rule set can use only these. Write each one used into `requires` as shown.

| Plugin | In `requires` |
| --- | --- |
<!-- requires -->
