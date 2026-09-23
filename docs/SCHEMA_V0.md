# Review Bridge schema v0

Status: **DRAFT / CANDIDATE**

この文書はVoice Review Bridgeの最初の交換形式を具体化します。
YMM4側の安定Item identityはLab検証中のため、`itemGuid`の最終freezeはまだ行いません。

## 1. Design goals

- 1 VoiceItem = 1 review record
- LLMはYMM4プロジェクト全体を書き換えない
- Proposalは構造化operationだけを返す
- Import時に元データ変更を検知できる
- VoiceItemがタイムライン上で移動しても、同一Itemなら再照合できる
- unknown operationは黙って無視しない

## 2. Target identity

draft:

```json
"target": {
  "itemGuid": "00000000-0000-0000-0000-000000000000",
  "exportIndex": 12,
  "frame": 420,
  "layer": 3
}
```

- `itemGuid`: 主識別子候補
- `exportIndex`: そのExport内の安定した表示順
- `frame/layer`: 人間向けlocator。identityそのものには使わない

### Why frame/layer is not identity

レビュー中にアイテムを移動しただけで別Item扱いにすると使いにくいためです。

## 3. Source fingerprint

`sourceFingerprint` はstale proposal検知に使います。

v0 candidate input:

```text
characterName
serif
hatsuon
assist-relevant source controls
```

をcanonical JSONへ正規化し、SHA-256を計算する案です。

含めない候補:

- frame
- layer
- selection state
- UI-only state
- generated cache path

Import時:

```text
itemGuid一致
  ↓
現在のsourceFingerprint一致？
  ├ yes → proposal適用候補
  └ no  → STALE。自動適用しない
```

## 4. Export package

schema: `ymm4.voice-review.v0`

Voice record required fields:

- target
- sourceFingerprint
- characterName
- serif
- hatsuon
- context

### Context

LLMが文脈読みを判断できるよう、最低限前後Voiceの文章を持たせます。前後が無い場合はnull。

### Derived official control information

Local parserで取れる情報はLLMに再解析させません。

```json
"controls": {
  "cleanText": "このあとゲートを抜けます",
  "boundaries": [
    { "position": 4, "source": "w0" }
  ]
}
```

## 5. Correction proposal

schema: `ymm4.voice-corrections.v0`

v0 operation候補:

- `setReading`
- `addBoundary`
- `removeBoundary`
- `helperVowelZero`
- `helperConsonantZero`
- `setProsodyGesture`
- `noChange`

### addBoundary

位置は**control-tag除去後のclean text上のUTF-16 index**をv0候補とします。
Unicode境界問題があるため、日本語・絵文字・サロゲートペアを実装前にLab/Unit testで確認します。

### helper mora

helper表現自体が未freezeなのでoperationは予約候補です。

## 6. Review metadata

LLM出力は理由を持てますが、自由文章は適用ロジックに使いません。
`confidence` は表示用で、自動採用判定へ直結させません。

## 7. Import validation

最低限:

1. schema/version一致
2. target存在
3. itemGuid一致
4. sourceFingerprint一致
5. operation type既知
6. operation payload妥当
7. cleanTextPosition範囲内
8. before/after diff生成

一つでも危険条件なら、そのCorrectionは適用しません。

## 8. JSON / XLSX split

canonicalはJSON。XLSX / CSVはhuman review用の派生ビューにします。
XLSXだけではnested operationや将来versioningを保持しづらいため、canonicalにはしません。