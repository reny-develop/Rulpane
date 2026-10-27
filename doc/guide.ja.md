## 書き方

ペインセットは、リンクの `#p=` の後ろに 1 行で書いても開けます。

::: card
```text
https://reny-develop.github.io/Rulpane/#p={"$schema":"rulpane/paneset/v1","r":{…},"v":{…}}
```

リンクを手で書いてチャットで送るなら、空白を `%20` にしておくと途中で切れません（「JSON からリンクを作る」で作ったリンクには空白が入りません）。開けないときは理由が出るので、上の「[JSON からリンクを作る](#make)」に貼って直してください。
:::

- [Rulealize.Registry](https://reny-develop.github.io/Rulealize.Registry/)

  ルールに使える演算を、名前で探せます。キーの書き方は、プラグインのページからたどる仕様書にあります。

  `r`

- [Rulpane.Gallery](https://reny-develop.github.io/Rulpane.Gallery/)

  ビュー・テーマ・ペインセットを探せます。ルールセットの名前から、そのまま合うビューも引けます。

  `v` `t` `p`

書式の詳しい説明は、ルールセットが Rulealize の[クイックガイド](https://github.com/reny-develop/Rulealize/blob/main/doc/ruleset-quickguide.md)に、[ビュー](https://github.com/reny-develop/Rulpane#views)・[テーマ](https://github.com/reny-develop/Rulpane#themes)・[使えるプラグイン](https://github.com/reny-develop/Rulpane#plugins)が Rulpane の README にあります。

リンクには秘密を持てません。リンクを持つ人は誰でも中身を見られ、どの操作もできます。
