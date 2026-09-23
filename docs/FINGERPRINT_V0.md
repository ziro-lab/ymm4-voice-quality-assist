# Source Fingerprint v0

Status: **CANDIDATE / ready for unit validation**

Voice Review Bridgeでcross-session Importのstale判定・再解決に使うfingerprint案です。

## Goal

レビュー対象の意味内容が変わったときだけfingerprintを変え、単なるタイムライン移動では変えません。

## Canonical input

v0では次の4要素だけを対象にします。

```json
{
  "characterName": "...",
  "serif": "...",
  "hatsuon": "...",
  "assistSource": {
    "enabled": true
  }
}
```

### Included

- `characterName`
- stored `Serif` exactly as authored
- current `Hatsuon`
- source-level assist state that changes pronunciation interpretation

### Excluded

- frame
- layer
- length
- selection state
- generated VoiceCache
- preview state
- exportIndex / exportRef
- derived cleanText
- derived boundary positions
- LLM proposal/result

Derived values are excluded because they can always be recomputed from the stored source.

## Normalization

Before hashing:

1. UTF-8 encoding
2. fixed property order
3. JSON without insignificant whitespace
4. null is preserved as JSON `null`
5. strings are **not** trimmed
6. Unicode normalization is **not** silently applied in v0

文字列を勝手にtrim/NFC変換しない理由は、YMM4上の実入力との差を隠さないためです。

## Hash

```text
sha256:<lowercase-hex>
```

## Re-resolution policy

cross-session Importでは:

1. exact fingerprint matchesを集める
2. 1件なら `EXACT_FINGERPRINT_MATCH`
3. 0件なら locator/contextを使って候補探索するが、自動適用はしない
4. 複数件なら `AMBIGUOUS`

locator/contextから一意候補が見つかっても、fingerprint不一致なら `STALE` として確認を要求します。

## Why not include frame/layer

レビュー中にVoiceItemを移動しただけでproposalが無効になるのを避けるためです。

## Next validation

- canonical JSON test vectors
- 日本語/改行/制御タグ
- emoji / surrogate pair
- null vs empty string
- same content at different frame/layer produces same hash
- Serif/Hatsuon変更でhashが変わる
- assist source state変更でhashが変わる
