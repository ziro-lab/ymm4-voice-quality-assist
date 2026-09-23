# Architecture

## 1. High-level architecture

```text
                         ┌─────────────────────────────┐
                         │ Voice Review Bridge         │
                         │ Export → LLM → Import       │
                         └──────────────┬──────────────┘
                                        │ correction proposals
                                        ▼
VoiceItem ───────────────────────► Correction Model
  │                                     │
  ├─ Serif                              │
  ├─ Hatsuon                            │
  ├─ Pronounce / AudioQuery             │
  └─ JimakuVideoEffects                 │
       └─ Voice Quality Assist Key       │
                                        ▼
                              Local Correction Engine
                                        │
                                        ▼
                                  VOICEVOX synthesis
```

## 2. Per-item opt-in

VoiceItemの字幕Effect列 `JimakuVideoEffects` に、Voice Quality Assist用Effectを保持する構想です。

このEffectの責務:

- このVoiceItemが補助対象かを示す
- VoiceItem固有の設定を保持する
- 設定UIを提供する

このEffect自身がVOICEVOX処理を行うわけではありません。

Runtime側の別ControllerがVoiceItemを監視し、Effectの存在と`IsEnabled`をキーに処理します。

## 3. Official control-tag transport

句境界マーカーは、独自の不可視文字ではなくYMM4公式文字制御タグを優先します。

現在の候補:

```text
<w0>
```

Labで確認済みの公式Parse結果:

```text
A<w0>B<w0>C
  ↓
clean text: ABC
boundary positions: [1, 2]
```

境界として採用するTimingTag候補条件:

```text
Type == Wait
Value == 0
Operator == Set
```

非ゼロwait（例: `<w100>`）は発音境界として扱いません。

## 4. Local correction pipeline

```text
Serif
  ↓
ControlTagParser.Parse
  ├─ clean text
  └─ timing tags
  ↓
VOICEVOX analysis
  ↓
AudioQuery / AccentPhrases
  ↓
Correction Model
  ├─ zero-pause boundary
  ├─ helper vowel = 0
  ├─ helper consonant = 0
  └─ optional prosody gesture
  ↓
synthesis
```

### Important boundary

補正はできる限り**宣言的なデータ**として保持し、固定インデックスへ依存しない設計を優先します。

悪い例:

```text
「3番目と7番目のmoraを0にする」
```

よりも、

```text
「このcontrol/helper markerに対応するmoraを0にする」
```

のように、再解析後も再解決できる形を優先します。

## 5. Correction Model

Correction ModelはLocal AssistとLLM Review Bridgeの共通境界です。

まだschemaはfreezeしていませんが、概念上は次を扱います。

```text
CorrectionSet
├─ Target
│   ├─ project/item reference
│   └─ source fingerprint
├─ ReadingCorrection
├─ BoundaryCorrections[]
├─ HelperMoraCorrections[]
├─ ProsodyGesture?
└─ ReviewMetadata?
```

Correction Modelは「誰が判断したか」と「どう適用するか」を分離します。

- Human
- Local deterministic rule
- LLM proposal

のどれでも、同じApply層へ渡せることを目標にします。

## 5.1 Proven VOICEVOX synthesis route

YMM4 4.56.1.0の実ホスト検証で、補正済み`VOICEVOXVoicePronounce`をpublic `IVoiceSpeaker.CreateVoiceAsync(text, pronounce, parameter, filePath)`へ渡した場合、AudioQueryが再解析されずそのままVOICEVOX `/synthesis`へ送られることを確認済みです。

```text
modified VOICEVOXVoicePronounce
        ↓
public IVoiceSpeaker.CreateVoiceAsync
        ↓
YMM4 configured VOICEVOXEngine
        ↓
POST /synthesis
        ↓
WAV
```

この経路では既存Pronounceが渡されている場合、検証条件下で`/audio_query`は再呼び出しされません。

製品側はinternal `VOICEVOXEngine.CreateVoiceFileAsync`を直接呼ぶ設計にしません。

## 5.2 Proven real VoiceItem apply route

Lab PR #128で、上記public speaker経路を実Timeline上の`VoiceItem`へ接続できることを確認済みです。

```text
real VoiceItem
   │
   ├─ Character.Voice.Speaker
   ├─ VoiceParameter
   └─ FilePath
        │
        ▼
public IVoiceSpeaker.CreateVoiceAsync
        │
        ├─ pronounce == null
        │    └─ baseline Pronounce / AudioQueryを取得
        │
        └─ pronounce == corrected Pronounce
             └─ 再解析なしでVoiceItem.FilePathへ再合成
        │
        ▼
VoiceItem.ClearVoiceCache()
        │
        ▼
VoiceItem.Pronounce = regenerated Pronounce
```

重要なのは、`VoiceItem.CreateVoiceFileAsync()`単体が編集用`Pronounce`を保持してくれることを前提にしない点です。Local Assist側Controllerがpublic `IVoiceSpeaker.CreateVoiceAsync(...)`の戻り値を保持し、Correctionを適用したPronounceを明示的にVoiceItemへ戻します。

PR #128の検証では、target PauseMoraを`0.25 -> 0.0`へ変更した後、同じ実VoiceItemの音声ファイルへ再合成でき、最終`/synthesis`前に新しい`/audio_query`は発生しませんでした。

Undo/Redo・interactive preview cache・Effect lifecycleは、このapply routeとは別のlifecycle境界として検証します。Undo/RedoはPR #130、Effect disable/removeはPR #134で閉じています。

## 5.3 Proven persistence boundary

Lab PR #131で、実YMM4プロジェクトのsave/reload境界を確認済みです。

永続化される補正source:

```text
VoiceItem.Serif
  └─ official <w0> marker

VoiceItem.Hatsuon

VoiceItem.JimakuVideoEffects
  └─ Assist Effect
       ├─ IsEnabled
       └─ plugin settings
```

一方、YMM4 4.56.1.0の検証条件では`VoiceItem.Pronounce`はreload後に`null`となり、補正済みAudioQuery自体はproject persistenceではありません。

したがって製品では、生成物を永続状態として扱わず、宣言的な補正意図をsource of truthにします。

```text
save
  ↓
Serif / marker + Assist Effect settings
  ↓
reload
  ↓
Correctionを再解決
  ↓
fresh Pronounce / AudioQuery
  ↓
補正
  ↓
public synthesis
  ↓
VoiceItem.FilePath
```

これによりVOICEVOX側の再解析結果やYMM4の一時Pronounce objectへ永続性を依存しません。

### 5.3.1 Proven reload reapply route

Lab PR #132で、PR #131のdurable persistence boundaryから実際にCorrectionを再構築してVoiceItemへ戻す経路を確認済みです。

```text
reloaded VoiceItem
  │
  ├─ Serif / <w0>
  ├─ Hatsuon
  └─ Assist Effect/settings
        │
        ▼
controller reapply condition
        │
        ▼
fresh IVoiceSpeaker.CreateVoiceAsync(..., pronounce:null, ...)
        │
        └─ fresh Pronounce / AudioQuery
                 pause = 0.25
        │
        ▼
Correction re-resolution
        │
        └─ target pause = 0.0
        │
        ▼
IVoiceSpeaker.CreateVoiceAsync(..., corrected Pronounce, ..., VoiceItem.FilePath)
        │
        ▼
ClearVoiceCache()
        │
        ▼
VoiceItem.Pronounce = regenerated Pronounce
```

最終補正synthesisではfresh analysis後のPronounceが再解析されず、target pause `0.0`を保持したままWAVへ反映されました。

このLabではfake VOICEVOX provider自体のproject persistenceは検証対象から分離しています。実製品でのvoice provider永続化はYMM4/各provider側の責務であり、Voice Quality Assistが永続化するsource of truthはCorrection marker / Effect設定です。

残るController課題は「reloadやitem rebindingをどのpublic lifecycle signalで検知してこのreapplyを起動するか」であり、Correctionの再解決・合成可能性そのものは閉じています。

## 5.4 Proven Undo / Redo apply unit

Lab PR #130で、1 VoiceItemの補正適用をYMM4 host historyへ1つのUndo単位として載せられることを確認済みです。

補正前後で保持するsnapshot:

```text
CorrectionApplySnapshot
├─ Pronounce
└─ VoiceItem.FilePath の WAV bytes
```

apply時:

```text
corrected snapshotを適用
  ↓
UndoRedoManager.AddCommand(
  Undo -> baseline snapshot
  Redo -> corrected snapshot
)
  ↓
UndoRedoManager.Record()
```

snapshot復元後は`VoiceItem.ClearVoiceCache()`を呼び、Pronounceと実音声ファイルを同じ履歴境界で揃えます。

実ホストではpublic `UndoAsync()/RedoAsync()`だけでなく、YMM4の標準`CommandSettings.Default[CommandType.Undo/Redo]`からも同じ履歴が実行され、pause値とWAV SHA256がbaseline/corrected間で完全に往復しました。

PR #133でcurrent `UndoRedoManager` の製品向け取得経路も閉じました。real `ITimelineToolViewModel.SetTimelineToolInfo(TimelineToolInfo)` へYMM4自身が渡す `TimelineToolInfo.UndoRedoManager` はnon-nullで、`AddCommand` / `Record` / `UndoAsync` / `RedoAsync` までpublicです。

したがって製品のtimeline-tool/controller境界では、MainViewModel/private field reflectionを使わず、host-supplied `TimelineToolInfo.UndoRedoManager` を保持してPR #130のUndo/Redo apply unitへ渡します。

## 5.5 Proven Assist Effect lifecycle

Lab PR #134で、Assist Effectの有効状態・存在状態をCorrectionのactive/inactive境界として使い、実VoiceItemのPronounceとWAVを両方整合させられることを確認済みです。

```text
Effect present + enabled
        ↓
fresh analysis
        ↓
Correction apply
        ↓
pause = 0.0 / corrected WAV

Effect disabled or removed
        ↓
fresh baseline analysis/synthesis
        ↓
pause = 0.25 / baseline WAV

Effect re-enabled
        ↓
Correction reapply
        ↓
same corrected WAV
```

検証ではcorrected WAVとbaseline WAVを異なる内容にし、SHA256でenabled → corrected、disabled → baseline、re-enabled → same corrected、removed → same baselineの完全な往復を確認しました。`Effect.IsEnabled`変更通知と`VoiceItem.JimakuVideoEffects`変更通知も同じ実ホスト経路で観測されています。

製品Controllerはこの状態遷移をdebounce/coalesceして実行すればよく、Effect無効化・削除時に補正済み音声を残し続ける設計にはしません。

## 6. Voice Review Bridge

LLMとの初期連携はYMM4内部へLLMを常駐させず、Export/Import方式を優先します。

```text
YMM4 project
  ↓ Export
Voice Review Package
  ↓
LLM / Codex / manual review
  ↓
Correction Proposal
  ↓ Import
YMM4 diff / validation / user selection
  ↓
Apply
```

交換形式のcanonical representationは構造化JSONを第一候補にします。

CSV/XLSXは、人間向け表示・編集用の派生形式として追加可能にします。

理由:

- 前後文脈を保持しやすい
- nested pronunciation dataを持てる
- correction operationsを明示できる
- versioningしやすい

詳細は [REVIEW_BRIDGE.md](REVIEW_BRIDGE.md)。

## 7. Host integration policy

優先順位:

1. YMM4 public API
2. YMM4公式control-tag/parser surface
3. 通常のPlugin API / editor service
4. 実ホスト検証で確認した安定surface
5. reflection（必要性と境界を明記）
6. Harmony（最後の手段）

Lab用reflectionと製品コード用reflectionは同一視しません。  
Labでは探索のためにreflectionを許容し、製品側へ入れる場合は別途理由と互換性戦略を要求します。
