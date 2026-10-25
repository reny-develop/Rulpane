# Rulpane

A web service that turns a [Rulealize](https://github.com/reny-develop/Rulealize) rule set carried in a URL into a screen you can operate. There is no server: it runs entirely on GitHub Pages static hosting.

```
https://<user>.github.io/Rulpane/#r=approval
https://<user>.github.io/Rulpane/#r=approval&s=z:q1YqLklMT1WyUipKLctMLVfSATISi_PzlKzySnNyagE
https://<user>.github.io/Rulpane/#r=approval&v=approval
https://<user>.github.io/Rulpane/#r=reversi&v=reversi
https://<user>.github.io/Rulpane/#r=approval&v=approval&t=paper
```

## The URL

Everything goes in the fragment (after `#`), which the browser never sends to a server.

| Key | Value |
| --- | --- |
| `r` | The rule set. A lowercase name is one bundled with the viewer (`wwwroot/rulesets/<name>.json`); a value starting with `z:` is JSON, deflated and base64url-encoded |
| `v` | The view: where each value goes on the screen. A lowercase name is one bundled with the viewer (`wwwroot/views/<name>.json`); `z:` is the JSON, packed as for `r`. Omitted, the screen is generated from the rule set |
| `t` | The theme: how the screen under the bar looks. A lowercase name is one bundled with the viewer (`wwwroot/themes/<name>.json`); `z:` is the JSON, packed as for `r`. Omitted, the screen looks like Rulpane |
| `s` | The state: only the `data` of the state document, in the `z:` form. Omitted, the rule set starts from its initial state |

The URL is rewritten on every move, so the browser's Back button is an undo, and sharing the URL hands over the position as it stands.

## The screen

Without a view, the screen is generated from the rule set:

- its `projections` are shown if it has any; otherwise the state is shown as it is
- the results of `GetValidInputs` are shown as buttons, grouped by actor and then by input; no move is set apart from the others, because the rules do not rank them
- an input with `open` parameters is shown as a form, and a refusal the rules name against a parameter is shown under its field
- an input that draws picks one outcome in the browser, weighted by the probabilities from `GetOutcomes`
- a terminal state shows its result

On a narrow screen the title, the last move and the moves stay pinned while the view scrolls beneath them; on a wide one the moves are a column that stays in view.

## Views

A view (`rulealize/view/v1`, still a draft) lays the screen out. It holds bindings and appearance, and no conditions: a block that should show only sometimes binds `showWhen` to a value the rules project, so the screen never restates the rules.

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
| `table` | The list at `bind`, one record per line. `columns` are keys, or `{ "key", "label" }`; without them, every key |
| `board` | The Grid board in the state field at `bind` (`$board`), square by square; its size and coordinates come from the field's `grid.board` schema. `play` (`"place.at"`, an input and its parameter) makes each square that input can be played on a button, when exactly one of its moves is played there. `pieces` maps a value to a piece, `dark`, `light`, `accent`, `ok`, `warn` or `danger`; a value it leaves out is shown as text |
| `actions` | The moves of `inputs`, a list of input names or `"*"` for all. A block naming inputs none of which can be played now is not shown |

Every block, and every row of `fields`, takes `showWhen`, a binding: it is shown only while that reads `true`. The game's end and its result are shown above the layout whatever the view says.

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

When the rules, the view or the theme came in the link rather than with the viewer, the top bar says so in place of the rule set's name: "Made by the link's author, not by Rulpane. Rulpane never asks for passwords or payment." The bar is the one part of the page a link cannot lay out, and it stays in view as the page scrolls. The sentence is a button that opens the detail: who made the screen, what a link can do, what to watch for, and the documents themselves.

## Plugins

A rule set in a URL can use only the plugins listed in [`CuratedRuntime`](src/Rulpane.Viewer/Engine/CuratedRuntime.cs). Plugins are never loaded from a URL.

## Development

```sh
dotnet run --project src/Rulpane.Viewer --launch-profile http   # http://localhost:5080
```

A push to `main` runs [`pages.yml`](.github/workflows/pages.yml), which publishes the viewer and deploys it to GitHub Pages. The repository's Settings → Pages must have its Source set to "GitHub Actions".
