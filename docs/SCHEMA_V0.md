# Review Bridge schema v0

Status: **v0 workflow FROZEN through B3 import/apply**

この文書はVoice Review Bridgeの最初の交換形式を具体化します。

## 1. Design goals

- 1 VoiceItem = 1 review record
- LLMはYMM4プロジェクト全体を書き換えない
- Proposalは構造化operationだけを返す
- Import時に元データ変更を検知できる
- タイムライン移動だけで別Item扱いしない
- 対象が一意に解決できない場合は自動適用しない
- unknown operationは黙って無視しない

## 2. Identity model

Labで確認した結果、YMM4 4.56.1.0の`VoiceItem`にはPlugin APIから読める安定Guid surfaceがありません。
そのためv0では`itemGuid`を主キーにしません。

### Same-session identity

ExportしたYMM4セッションが生きている間は、Plugin内部で

```text
exportSessionId + exportRef -> actual VoiceItem object
```

の対応を保持します。

package側:

```json
{
  "exportSessionId": "session-...",
  "target": {
    "exportRef": "voice-000012",
    "exportIndex": 12,
    "frame": 420,
    "layer": 3
  }
}
```

### Cross-session re-resolution — FROZEN

YMM4を閉じた後にImportする場合は、`exportRef`だけでは足りません。

1. exact `sourceFingerprint` matchが1件 → `EXACT_FINGERPRINT_MATCH` / auto-apply candidate
2. exact matchが複数 → `AMBIGUOUS`
3. exact matchが0件 → frame/layer/character/previous/next contextをlocatorとして探索
4. locatorが1件 → `STALE`（候補提示のみ、自動適用禁止）
5. locatorが0件 → `MISSING`
6. locatorが複数 → `AMBIGUOUS`

複数のexact fingerprintをlocatorで自動的に1件へ絞りません。重複台詞の誤適用を避けるためです。

## 3. Source fingerprint

`sourceFingerprint` はstale proposal検知とcross-session再解決の中核です。

v0 frozen input:

```text
characterName
stored Serif
current Hatsuon
enabled Assist profiles
  ├─ canonical helper rules
  └─ prosody gesture
```

をfixed-order UTF-8 canonical JSONへ変換し、SHA-256を計算します。Assist Effect / helper ruleの単なる並び順は正規化します。文字列はtrimやUnicode normalizationを行いません。詳細とfrozen hash vectorは [FINGERPRINT_V0.md](FINGERPRINT_V0.md)。

原則含めない:

- frame
- layer
- selection state
- UI-only state
- generated cache path

理由は、VoiceItemを移動しただけでレビュー内容までstaleにしたくないためです。

## 4. Export package — B1 FROZEN

schema: `ymm4.voice-review.v0`

top-level:

- `schema`
- `exportSessionId`
- `exportedAt`（UTC）
- `voices`

Voice record:

- `target`
  - `exportRef`
  - `exportIndex`
  - `frame`
  - `layer`
- `sourceFingerprint`
- `characterName`
- `speaker`
  - `api`
  - `id`
- `serif`
- `hatsuon`
- `context`
  - `previousSerif`
  - `nextSerif`
- `controls`
  - `cleanText`
  - `boundaries[] { position, source }`
- `assist`
  - `enabled`
  - canonical enabled Assist profiles（helper rules + prosody）
- `pronunciation`
  - `hasGeneratedPronounce`
  - optional `generatedMoraReading`
  - optional `accentPhraseCount`

並び順は `Frame -> Layer -> original input order`。Export後の各recordには0-based indexから `voice-000000` 形式のexportRefを振る。

same-sessionではpackageとは別にPlugin内部で `exportRef -> live VoiceItem` mapを保持する。

### Context

LLMが文脈読みを判断できるよう、export順に前後VoiceのSerifを持たせる。

### Derived official control information

Local parserで取れる情報はLLMに再解析させない。

```json
"controls": {
  "cleanText": "このあとゲートを抜けます",
  "boundaries": [
    { "position": 4, "source": "w0" }
  ]
}
```

`controls.boundaries` はSerifへ既に保存されている **VOICEVOX自動アクセント用の強制区切り** を表す。`source: "w0"` はcanonical durable marker `<w0>` 由来であることを示す。元からある日本語読点や通常pauseの一覧ではない。

### Failure policy

enabled Assist Effectのhelper JSONが壊れているなど、B0 fingerprint materialを正確に作れないVoiceが1件でもあればwhole exportをfail closedする。劣化したpackageを黙って出力しない。


## 5. Correction proposal — B2 FROZEN

schema: `ymm4.voice-corrections.v0`

top-level:

- `schema`
- `exportSessionId`
- `corrections[]`

各correction:

- `exportRef`
- `sourceFingerprint`
- `operations[]`

B2 v0は**exportされた全Voiceをexactly once返す**。変更不要のVoiceも省略せず、operationを1つだけ `noChange` にする。これにより未レビューと変更なしを区別する。

operation:

- `setReading { reading }`
- `addBoundary { position }`
- `removeBoundary { position }`
- `helperVowelZero { position, helper }`
- `helperConsonantZero { position, helper }`
- `setProsodyGesture { gesture }`
- `noChange`

prosody gesture:

- `none`
- `lightRise`
- `lightFall`
- `hold`

### Review semantics

- 読み・固有名詞・専門語は `setReading` でHatsuon候補を返す。Serifは書き換えない。
- `addBoundary { position }` は `controls.cleanText` のinterior UTF-16 boundaryへ **VOICEVOX自動アクセント用の強制区切り** を追加する要求。既存source punctuation pauseを単に0にする意味ではない。
- `removeBoundary { position }` は同位置のcanonical `<w0>` 強制区切りを削除する要求。
- add/removeのwire名と `position` payloadはv0のまま維持する。
- Track A適用時はcanonical `<w0>` から解析時だけ `、` を注入してVOICEVOXへ自動アクセント再解析させ、Plugin注入PauseMoraだけを0にする。元の読点pauseは変更しない。
- boundary positionは `controls.cleanText` のUTF-16 boundary。add/removeはinteriorのみ。
- helper positionは0〜cleanText lengthのboundary。
- helperは発音上妥当な場合だけ提案する。
- 不確実なら推測修正より `noChange` を優先する。
- unknown operationは拒否する。
- free-form code/proseはcanonical responseに含めない。

### B2 wire validation

LLM responseは元B1 Exportに対して次を検証する。

- schema
- exportSessionId exact
- exportRef存在
- duplicate exportRef拒否
- sourceFingerprint exact
- exported Voiceのcomplete coverage
- operation type/payload
- B0 domain validation

B2はここで停止し、現在のYMM4 Timelineは変更しない。current source再解決・diff・選択・applyはB3。

## 6. Import resolution states — FROZEN

- `EXACT_SESSION_MATCH`
- `EXACT_FINGERPRINT_MATCH`
- `STALE`
- `MISSING`
- `AMBIGUOUS`

auto-apply可能なのはexact session / exact fingerprintの一意解決だけです。`STALE / MISSING / AMBIGUOUS` は自動適用しません。

## 7. Import validation — B0 core FROZEN

typed validation coreでImport前に次を拒否します。

1. schema mismatch
2. exportSessionId missing / mismatch
3. exportRef missing
4. sourceFingerprint format mismatch
5. resolved source fingerprint mismatch
6. operation 0件
7. unknown operation
8. `noChange` と他operationの混在
9. 複数 `setReading`
10. empty reading
11. 複数 `setProsodyGesture`
12. unsupported prosody
13. add/remove boundaryの非interior位置
14. duplicate boundary operation
15. 同一位置へのadd/remove conflict
16. helper position範囲外
17. normalize後empty helper
18. 同一clean-text boundaryへの複数helper

強制区切りはvNext A1仕様に合わせてclean textの**interior**のみ。helperはA2仕様に合わせて0〜cleanTextLengthの境界を許可します。

before/after diff生成、JSON wire parsing、実YMM4 mutation、Undo単位化はB1/B3側です。

## 8. Import planning / apply — B3 FROZEN

B3はB2でvalidatedなcorrection packageだけを受け取る。

### Resolution

same-session:

1. `exportSessionId + exportRef` からlive VoiceItemを取得
2. current fingerprintがexport時fingerprintと一致 → `EXACT_SESSION_MATCH`
3. sourceが変わった → `STALE`

cross-session:

- B0 resolverを使用
- exactly one fingerprint match → `EXACT_FINGERPRINT_MATCH`
- unique locator only → `STALE`
- none → `MISSING`
- multiple → `AMBIGUOUS`

apply可能なのはEXACT 2種のみ。

### Preview

mutation前に各itemへ以下を生成する。

- Hatsuon before / after
- `<w0>` boundary before / after
- helper additions
- proposed prosody
- noChange state
- resolution state / message

### Selection

- EXACTだけ選択可能
- noChangeは初期非選択
- select-allはapply可能itemだけ
- STALE / MISSING / AMBIGUOUSは選択不可

### Preflight

選択itemを1件ずつcommitするのではなく、batch全体を先に検証する。

- apply直前fingerprint一致
- boundary rewrite safety
- helper anchor再解決
- helper / boundary collision
- enabled Assist helper JSON validity
- selected itemがEXACTであること

1件でも失敗すればbatch全体を変更しない。

### Boundary rewrite safety

B3 v0は、official control tagを除いたplain textが

```text
Serif.Replace("<w0>", "")
```

と完全一致する場合だけSerifをrebuildする。

つまり自動rewrite可能なのは通常text + literal `<w0>` だけ。`<w100>` 等ほかのofficial control tagが混在する場合はfail closedする。

UTF-16 surrogate pairの途中へboundaryを置かない。

### Durable apply

journalが変更するsource of truth:

- Serif
- Hatsuon
- Voice Quality Assist Effect membership
- Effect.IsEnabled
- HelperRulesJson
- Prosody

generated Pronounce / WAVはjournal snapshotへ保存しない。Track A runtimeのderived stateとして再生成する。

### Undo / Redo

選択batchはYMM4 public

```text
UndoRedoActionCommand
UndoRedoManager.AddCommand(...)
UndoRedoManager.Record()
```

へ1 recordとして登録する。

Undo / Redoはdurable source snapshotを往復し、その後Track A runtimeがPronounce / WAVを再生成する。

Product-native run `35993465363` で Recorded=1 / Undoed=1 / Redoed=1、baseline/corrected audio roundtripまで確認済み。

## 9. JSON / human-readable split — B1 FROZEN

canonicalはJSON。

B1ではhuman review用の派生viewとしてCSVを実装済み。固定列順・全field quote・quote doubling・CRLFで出力し、Tool保存時はUTF-8 BOM付きで書き出す。

CSVはnested helper/prosodyを平坦化した閲覧用であり、Importのcanonical sourceにはしない。XLSXは必要なら将来追加するがB1完了条件ではない。