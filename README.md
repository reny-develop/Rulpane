# Rulpane

A web service that turns a [Rulealize](https://github.com/reny-develop/Rulealize) rule set carried in a URL into a screen you can operate. There is no server: it runs entirely on GitHub Pages static hosting.

```
https://<user>.github.io/Rulpane/#r=approval
https://<user>.github.io/Rulpane/#r=approval&s=z:q1YqLklMT1WyUipKLctMLVfSATISi_PzlKzySnNyagE
https://<user>.github.io/Rulpane/#r=approval&v=approval
https://<user>.github.io/Rulpane/#r=reversi&v=reversi
https://<user>.github.io/Rulpane/#r=approval&v=approval&t=paper
https://<user>.github.io/Rulpane/#r=approval&v=approval-stages
https://<user>.github.io/Rulpane/#r=Rulealize.RuleSet.SignedRequest@1.1.0
```

## The URL

Everything goes in the fragment (after `#`), which the browser never sends to a server.

| Key | Value |
| --- | --- |
| `r` | The rule set. A lowercase name is one bundled with the viewer (`wwwroot/rulesets/<name>.json`); `<id>@<version>` is one published to the [Rulealize Registry](https://github.com/reny-develop/Rulealize.Registry), at exactly that version; a value starting with `z:` is JSON, deflated and base64url-encoded |
| `v` | The view: where each value goes on the screen. A lowercase name is one bundled with the viewer (`wwwroot/views/<name>.json`); `z:` is the JSON, packed as for `r`. Omitted, the screen is generated from the rule set |
| `t` | The theme: how the screen under the bar looks. A lowercase name is one bundled with the viewer (`wwwroot/themes/<name>.json`); `z:` is the JSON, packed as for `r`. Omitted, the screen looks like Rulpane |
| `s` | The state: only the `data` of the state document, in the `z:` form. Omitted, the rule set starts from its initial state |

The URL is rewritten on every move, so the browser's Back button is an undo, and sharing the URL hands over the position as it stands.

## Rule sets from the Registry

`r=Rulealize.RuleSet.Request@1.1.0` names a rule set the Rulealize Registry indexes, with all three numbers of its version, so a link says which document it ran. The viewer never fetches anything outside its own origin, so every deploy copies them in first: [`tool/Mirror`](tool/Mirror/Program.cs) reads the Registry's catalogue, takes the documents out of each version's package on nuget.org, and writes them to `registry/` beside the viewer. A version the Registry withholds is not copied, and a link to it stops opening at the next deploy.

What any rule set holds through `uses` is read from the same copy, however the rule set itself arrived — bundled, from the Registry, or in the link. A part its own package ships is taken from the package; anything else is the lowest version the Registry has that satisfies every `uses` naming it, as `RuleSetRequirement.Choose` decides, so a link resolves to the same documents however many versions are published after it. A rule set in a link can therefore carry only what it adds — `uses`, `held` and `projections` — and leave what it holds to the Registry.

```sh
dotnet run --project tool/Mirror -- src/Rulpane.Viewer/wwwroot/registry   # for a local run; the folder is ignored by git
```

The start page lists the newest version of each, once it has compiled it; one Rulpane cannot run is listed with the reason.

## The screen

Without a view, the screen is generated from the rule set:

- its `projections` are shown if it has any; otherwise the state is shown as it is
- the results of `GetValidInputs` are shown as buttons, grouped by actor and then by input; no move is set apart from the others, because the rules do not rank them
- an input with `open` parameters is shown as a form, and a refusal the rules name against a parameter is shown under its field; a parameter whose schema is a `type.enum` is a list of the values it allows, rather than a box to type in
- an input that draws picks one outcome in the browser, weighted by the probabilities from `GetOutcomes`
- a terminal state shows its result

On a narrow screen the title, the last move and the moves stay pinned while the view scrolls beneath them; on a wide one the moves are a column that stays in view.

## Views

A view (`rulealize/view/v1`) lays the screen out. It holds bindings and appearance, and no conditions: a block that should show only sometimes binds `showWhen` to a value the rules project, so the screen never restates the rules.

```jsonc
{
  "$schema": "rulealize/view/v1",
  "ruleSet": "approval@1",          // optional: the id, and as much of the version as must match
  "title": "Expense claim",         // optional: replaces the rule set's id in the heading
  "layout": [
    { "c": "text", "text": "Submitted and waiting for a reviewer.", "showWhen": "summary.waiting" },
    { "c": "fields", "title": "Claim", "rows": [
      { "label": "Stage", "bind": "summary.stage", "tone": { "approved": "ok", "rejected": "danger" } },
      { "label": "Reason", "bind": "summary.reason" } ] },
    { "c": "actions", "title": "Review", "inputs": ["approve", "reject"] }
  ]
}
```

A binding is a projection then `.`-separated steps into it (`summary.stage`), or a state field written as the rules write one (`$total`); a number steps into a list. Its first name is checked against the rules when the link is opened, and a path that leads nowhere shows `—`.

| Block | Shows |
| --- | --- |
| `heading` | `text` as a heading |
| `text` | `text` as a paragraph |
| `fields` | A card of `rows`, each a `label` and a `bind`. With `tone`, the value is a chip coloured by it: `neutral`, `accent`, `ok`, `warn` or `danger` |
| `stats` | `items`, each a `label` and a `bind`, set large |
| `list` | The list at `bind`, its values side by side |
| `table` | The list at `bind`, one record per line. `columns` are keys, or `{ "key", "label" }`; without them, every key |
| `board` | The Grid board in the state field at `bind` (`$board`), square by square; its size and coordinates come from the field's `grid.board` schema. `play` (`"place.at"`, an input and its parameter) makes each square that input can be played on a button, when exactly one of its moves is played there. `pieces` maps a value to a piece, `dark`, `light`, `accent`, `ok`, `warn` or `danger`; a value it leaves out is shown as text |
| `actions` | The moves of `inputs`, a list of input names or `"*"` for every move a board on the screen does not already offer as a square. A block naming inputs none of which can be played now is not shown, and neither is `"*"` when the boards offer every move. With `"as": "select"`, the choices of an input with arguments are one list to pick from and a button to play the one picked, in place of a button each (`"buttons"`, the default) |

Every block, and every row of `fields`, takes `showWhen`, a binding: it is shown only while that reads `true`. The game's end and its result are shown above the layout whatever the view says.

### Names

The labels a view writes are its own, but the rules' names reach the screen too: on the move buttons, in a form, in a refusal, in the values themselves. `names` says what to call them, in the one language the view is written in. Each kind has its own map, so an input and a value that share a spelling never collide:

```jsonc
"names": {
  "inputs": { "approve": "Approve", "reject": "Send back" },   // move buttons and the last move
  "params": { "reason": "Why" },                                // argument and form field names
  "codes":  { "int.max": "That is more than a claim can be." }, // a refusal, in place of its code
  "values": { "review": "In review", "scope": "Out of scope" }   // values wherever they are shown
}
```

| Map | Checked when the link is opened |
| --- | --- |
| `inputs` | Each key is an input the rules have |
| `params` | Each key is a parameter of one of the rules' inputs, when the rules write every input themselves |
| `codes` | Not checked: codes come from plugins as well as from the rules |
| `values` | Not checked: values come from the state. A value is looked up by the text the screen would show, so a number can be named too |

A name the view leaves out is shown as the rules spell it. A `tone` and a board's `pieces` still key on the value the rules hold, not on its name. The screen without a view shows every name as it is.

### Templates

A template is a view written for no one rule set. In place of `ruleSet` it says what it `expects` — the projections it reads, each with a type, and the inputs it names — and binds nothing else: no state field, no projection it did not list. Any rules that have what it expects can use it.

```jsonc
{
  "$schema": "rulealize/view/v1",
  "expects": {
    "projections": { "board": "board", "turn": "string" },  // a name and a type each
    "inputs": { "place": ["at"] }                          // an input and the parameters the template uses
  },
  "layout": [
    { "c": "fields", "rows": [ { "label": "To move", "bind": "turn" } ] },
    { "c": "board", "bind": "board", "play": "place.at", "pieces": { "black": "dark", "white": "light" } },
    { "c": "actions", "title": "Other moves", "inputs": "*" }
  ]
}
```

| Type | A value of it is |
| --- | --- |
| `string`, `number`, `bool` | That scalar |
| `any` | Anything, null included |
| `list<T>` | A list of `T` |
| `{a, b: T}` | A record with these keys; a key with no type may hold anything |
| `board` | A Grid board: a state field the rules declare with `grid.board`, which no projection can be. Only the whole of a type can be one |
| `T?` | `T`, or null |

A template's bindings are checked against its types before any rules are seen, so a row cannot bind a key the record does not have, a `list` block a string, or a `showWhen` anything but a `bool`. When it is opened, every projection it expects must be one the rules have and every input one they offer, and at every position each value the rules give must be of its type; a value that is not stops the screen with the projection that failed. Its `names` are not checked against any rules.

Rules that keep what a template expects under other names take it through a view that applies it: `template` names a template bundled with Rulpane, and `bind` says where these rules keep each projection, as any binding of theirs — another projection, a path into one, or a state field. A projection it does not bind is read by its own name. Its `title` replaces the template's, and its `names`, checked against the rules as any view's are, go over the template's one by one.

```jsonc
{
  "$schema": "rulealize/view/v1",
  "ruleSet": "reversi@1",
  "template": "grid-game",
  "bind": { "projections": { "board": "$board", "turn": "$turn" } },
  "title": "Reversi",
  "names": { "values": { "black": "Black", "white": "White" } }
}
```

An input a template expects is matched by its name alone. The bundled templates are `stages` (a stage and the moves), `progress` (the items done and all of them, from a checklist or a sign-off) and `grid-game` (a board played by choosing a square).

## Themes

A theme (`rulealize/theme/v1`) sets the colours, corners, typeface and spacing of everything under the top bar. The bar keeps Rulpane's own look whatever the theme says, so nothing a link brings can pass for it.

```jsonc
{
  "$schema": "rulealize/theme/v1",
  "colors": {
    "light": { "accent": "#1f7a4d", "accent-soft": "#e4f4ea" },  // optional, each colour optional
    "dark":  { "accent": "#6fd6a0" }
  },
  "radius": "round",      // sharp, default or round
  "font": "rounded",      // inter, system, serif or rounded
  "density": "compact"    // comfortable or compact
}
```

| Key | Value |
| --- | --- |
| `colors.light`, `colors.dark` | Any of `bg`, `surface`, `sunken`, `text`, `muted`, `line`, `line-strong`, `accent`, `accent-hover`, `accent-text`, `accent-soft`, `mark`, `mark-text`, `ok`, `ok-soft`, `danger`, `danger-soft`, `warn`, `warn-soft`, as `#rrggbb`. A colour left out, or a mode left out, keeps Rulpane's own |
| `radius` | The corners: `sharp`, `default` or `round` |
| `font` | The typeface for text: `inter` (bundled), or the device's own `system`, `serif` or `rounded` face. Code and values stay in JetBrains Mono. No other font can load: the page's CSP lets none in |
| `density` | `comfortable`, or `compact` for less space between and inside cards |

A theme is checked when the link is opened. Every pair of colours the screen sets text in — `text` on `bg`, `surface`, `sunken` and `accent-soft`; `muted` on `bg`, `surface` and `sunken`; `accent-text` on `accent` and `accent-hover`; `accent` on `bg`, `surface` and `accent-soft`; `mark-text` on `mark`; and `ok`, `danger` and `warn` each on `surface` and on its `-soft` — must reach a contrast of 4.5:1 (WCAG AA), counting the colours the theme leaves to Rulpane. A theme that falls short is refused with the pair that did.

## Who made the screen

When the rules, the view or the theme came in the link rather than with the viewer, the top bar says so in place of the rule set's name: "Made by the link's author, not by Rulpane. Rulpane never asks for passwords or payment." When nothing came in the link but the rules are from the Registry, it says "Published by others, not by Rulpane." in place of the first sentence. The bar is the one part of the page a link cannot lay out, and it stays in view as the page scrolls. The sentence is a button that opens the detail: who made the screen, which rule sets from the Registry it runs, what a link can do, what to watch for, and the documents themselves, the ones the rules hold included.

## Plugins

A rule set in a URL can use only the plugins listed in [`CuratedRuntime`](src/Rulpane.Viewer/Engine/CuratedRuntime.cs). Plugins are never loaded from a URL.

## Development

```sh
dotnet run --project src/Rulpane.Viewer --launch-profile http   # http://localhost:5080
```

A push to `main` runs [`pages.yml`](.github/workflows/pages.yml), which publishes the viewer, mirrors the Registry's rule sets beside it, and deploys it to GitHub Pages. The repository's Settings → Pages must have its Source set to "GitHub Actions".
