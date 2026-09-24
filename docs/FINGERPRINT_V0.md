# Source Fingerprint v0

Status: **FROZEN / UNIT-COVERED**

Voice Review Bridgeでcross-session Importのstale判定・再解決に使うfingerprintです。

## Goal

レビュー対象の意味内容・発音補助sourceが変わったときfingerprintを変え、単なるタイムライン移動やEffect/list順序変更では変えません。

## Canonical input

v0のcanonical JSONは次の固定形です。

```json
{
  "characterName": "...",
  "serif": "...",
  "hatsuon": "...",
  "assistSource": {
    "enabled": true,
    "profiles": [
      {
        "prosody": "lightRise",
        "helperRules": [
          {
            "kind": "zeroVowel",
            "helper": "ウ",
            "position": 2,
            "left": "東京",
            "right": "大学"
          }
        ]
      }
    ]
  }
}
```

### Included

- `characterName`
- stored `Serif` exactly as authored
- current `Hatsuon`
- enabled Voice Quality Assist Effect profiles
  - canonical helper rules
  - current A3 prosody gesture
- Assist enabled/disabled semantic state

`<w0>` is already part of stored Serif, so it is not duplicated as derived boundary data.

### Excluded

- frame
- layer
- length
- selection state
- generated VoiceCache
- generated Pronounce / pitch values
- preview state
- exportIndex / exportRef
- derived cleanText
- derived boundary positions
- disabled Assist Effect configuration
- LLM proposal/result

Derived values are excluded because they can be recomputed. Disabled Assist configuration is excluded because it does not change the current pronunciation source; enabling it changes `assistSource` and therefore changes the fingerprint.

## Canonicalization

1. UTF-8 JSON
2. fixed property order
3. no insignificant whitespace
4. null is preserved as JSON `null`
5. strings are **not** trimmed
6. Unicode normalization is **not** silently applied
7. JSON writer keeps ordinary Japanese unescaped; supplementary Unicode such as emoji follows `Utf8JsonWriter` canonical surrogate escaping (for example `\\uD83D\\uDE00`)
8. enabled Assist profiles are sorted by their canonical representation
9. helper rules inside each profile are sorted by:
   - position
   - left
   - right
   - kind
   - helper
10. duplicate profiles/rules are preserved; only ordering is normalized

Malformed enabled `HelperRulesJson` fails fingerprint construction. The exporter must not silently hash a degraded source representation.

## Hash

```text
sha256:<lowercase-hex>
```

### Frozen test vector

Input:

- characterName: `小夜`
- Serif: `東京<w0>大学\n😀`
- Hatsuon: `トウキョウダイガク`
- prosody: `lightRise`
- helper: `zeroVowel ウ` at position 2, left `東京`, right `大学`

Canonical JSON:

```json
{"characterName":"小夜","serif":"東京<w0>大学\n\uD83D\uDE00","hatsuon":"トウキョウダイガク","assistSource":{"enabled":true,"profiles":[{"prosody":"lightRise","helperRules":[{"kind":"zeroVowel","helper":"ウ","position":2,"left":"東京","right":"大学"}]}]}}
```

Fingerprint:

```text
sha256:7fa3f4e31c268876868cf51eb70ab2933326684e3eef5847cce6d70cbddb8274
```

## Re-resolution policy

cross-session Import:

1. exact fingerprint matchesを集める
2. exact matchが1件 → `EXACT_FINGERPRINT_MATCH` / auto-apply candidate
3. exact matchが複数 → `AMBIGUOUS`
4. exact matchが0件 → locator/contextで候補探索
5. locatorが1件 → `STALE`（候補は示せるがauto-apply禁止）
6. locatorが0件 → `MISSING`
7. locatorが複数 → `AMBIGUOUS`

**複数のexact fingerprintをframe/layer等で自動的に1件へ絞りません。** 同一台詞の重複配置で誤適用するより、明示確認を要求します。

## Locator fields

v0 resolverは必要に応じて次をexact filterとして使えます。

- frame
- layer
- characterName
- previous Serif
- next Serif

locatorはidentityそのものではなく、STALE/MISSING/AMBIGUOUSを説明・候補化するための補助情報です。

## Unit coverage

- frozen canonical JSON + SHA-256 vector
- Japanese / control tag / newline / emoji
- null vs empty
- whitespace preservation
- composed vs decomposed Unicode
- assist profile / helper rule ordering independence
- character / Serif / Hatsuon changes
- prosody / helper changes
- one exact match
- duplicate exact match
- unique stale locator
- missing locator
- ambiguous locator
