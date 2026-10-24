# Rulpane

A web service that turns a [Rulealize](https://github.com/reny-develop/Rulealize) rule set carried in a URL into a screen you can operate. There is no server: it runs entirely on GitHub Pages static hosting.

```
https://<user>.github.io/Rulpane/#r=approval
https://<user>.github.io/Rulpane/#r=approval&s=z:q1YqLklMT1WyUipKLctMLVfSATISi_PzlKzySnNyagE
```

## The URL

Everything goes in the fragment (after `#`), which the browser never sends to a server.

| Key | Value |
| --- | --- |
| `r` | The rule set. A lowercase name is one bundled with the viewer (`wwwroot/rulesets/<name>.json`); a value starting with `z:` is JSON, deflated and base64url-encoded |
| `s` | The state: only the `data` of the state document, in the `z:` form. Omitted, the rule set starts from its initial state |

The URL is rewritten on every move, so the browser's Back button is an undo, and sharing the URL hands over the position as it stands.

## The screen

There is no view DSL yet. The screen is generated from the rule set:

- its `projections` are shown if it has any; otherwise the state is shown as it is
- the results of `GetValidInputs` are shown as buttons, grouped by input
- an input with `open` parameters is shown as a form
- an input that draws picks one outcome in the browser, weighted by the probabilities from `GetOutcomes`
- a terminal state shows its result

## Plugins

A rule set in a URL can use only the plugins listed in [`CuratedRuntime`](src/Rulpane.Viewer/Engine/CuratedRuntime.cs). Plugins are never loaded from a URL.

## Development

```sh
dotnet run --project src/Rulpane.Viewer --launch-profile http   # http://localhost:5080
```

A push to `main` runs [`pages.yml`](.github/workflows/pages.yml), which publishes the viewer and deploys it to GitHub Pages. The repository's Settings → Pages must have its Source set to "GitHub Actions".
