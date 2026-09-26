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
  └─ AudioEffects                       │
       └─ Pronunciation Assist           │
                                        ▼
                              Local Correction Engine
                                        │
                                        ▼
                                  VOICEVOX synthesis
```

## 2. Per-item opt-in

canonicalなPronunciation Assist設定は `VoiceItem.AudioEffects` に保持します。旧candidateで使った `JimakuVideoEffects` の `PronunciationAssistEffect` は互換read対象として残し、明示migrationでAudio Effectへ移行します。新規writeはAudio Effectのみです。

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

### Forced-boundary mapping safety

Lab PR #136で、Serif-only `<w0>`はYMM4/VOICEVOXの生成経路を自動的にAccentPhrase境界へ変換しないことを確認しました。vNextでは `<w0>` を「既存pauseを0にする印」ではなく、**VOICEVOX自動アクセント用の強制区切りintent** として扱います。

Serifのclean-text位置をそのままVOICEVOX mora indexとして扱いません。同じactive voice providerのfull/prefix readingからreading上の挿入位置を一意に求め、その位置へ解析時だけ日本語読点 `、` を注入します。VOICEVOXにはそのtransient readingを通常どおり解析させ、両側のaccentを自動生成させます。

返されたAudioQueryでは、Pluginが注入した読点に対応するPauseMoraだけを一意に同定し、`VowelLength = 0` にします。SerifにもHatsuonにもtransient読点は保存せず、元からある読点PauseMoraは変更しません。対応不能・不一致・複数候補はfail closedです。

```text
durable Serif + <w0>
        ↓
ControlTagParser → clean-text boundary
        ↓
same-speaker full/prefix reading
        ↓
unique reading insertion point
        ↓
transient readingへ「、」を注入
        ↓
fresh VOICEVOX automatic accent analysis
        ↓
Plugin注入commaのPauseMoraを一意に同定
        ↓
そのPauseMoraだけ VowelLength = 0
        ↓
public synthesis
```

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
  ├─ forced automatic-accent boundary
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

Review Bridge v0 schemaはfreeze済みで、概念上は次を扱います。

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

VoiceItem.AudioEffects
  └─ Pronunciation Assist
       ├─ IsEnabled
       ├─ BoundaryInputToken
       ├─ HelperRulesJson
       └─ Prosody

legacy compatibility read:
VoiceItem.JimakuVideoEffects
  └─ PronunciationAssistEffect
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

A1製品MVPでは、assembly load時にModuleInitializerからruntimeを起動し、active timelineの差し替えとitem rebindingを監視してreapplyを開始します。project save/reloadの補正source永続性と再合成可能性はLab #131/#132で閉じ、製品native smokeではsource変更・Effect lifecycle・Hatsuon不一致からの再解決を確認済みです。

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

PR #133でcurrent `UndoRedoManager` の取得経路自体は閉じています。real `ITimelineToolViewModel.SetTimelineToolInfo(TimelineToolInfo)` へYMM4自身が渡す `TimelineToolInfo.UndoRedoManager` はnon-nullで、`AddCommand` / `Record` / `UndoAsync` / `RedoAsync` までpublicです。

ただしA1製品MVPでは、補正済みPronounce/WAVを**独立したユーザー編集履歴としてRecordしません**。A1のsource of truthはSerif / `<w0>` / Hatsuon / Assist Effectであり、生成済み音声はderived runtime stateです。source側がUndo/Redo・編集・reloadで変化した場合、Controllerがderived stateを再生成します。

PR #130/#133のUndo apply-unitは、将来の明示的なCorrection Importや「補正適用を1操作として履歴へ載せる」機能で利用可能なPROVEN optionとして保持します。

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

製品Controllerは250msのcoalescing scanとper-item通知でこの状態遷移を実行し、Effect無効化・削除時に補正済み音声を残し続けません。Product PR #2のnative smokeでdisable/re-enable、marker remove/restore、Hatsuon mismatch/recoveryまで確認済みです。

## 5.6 Proven audio/cache refresh boundary

Lab PR #135で、補正後に実VoiceItemが参照する音声とcache stateをpublic surfaceだけで整合させられることを確認済みです。

```text
corrected public synthesis
        ↓
VoiceItem.FilePath を corrected WAV へ置換
        ↓
VoiceItem.ClearVoiceCache()
        └─ public VoiceCache : byte[] -> null
        ↓
VoiceItem.Pronounce = regenerated Pronounce
        ↓
normal VoiceItem / Timeline notifications
```

検証ではbaseline WAVとcorrected WAVを別内容にし、SHA256が異なることを確認した上で、5-byteのstale `VoiceCache` sentinelをpublic setterで投入しました。`ClearVoiceCache()`後はcacheがnullとなり、corrected WAVのSHA256は変化しませんでした。

VoiceItemでは`VoiceCache` / `Pronounce`のPropertyChanged、Timelineではpublic `CurrentFrame`変更時の`PropertyChanged("CurrentFrame")`を確認しています。

Plugin-facing surfaceのrefresh/redraw名走査で得られたのは `Timeline.RefreshTimelineLengthAndMaxLayer()` で、これはpreview/audio redraw用APIではありません。製品側は専用の非公開redraw hookを追加せず、上記のsupported state pathを使います。

GitHub-hosted CIは物理スピーカーからの知覚音声を証明しないため、補正直後の実機プレビュー聴取はhands-on acceptanceとして扱えますが、host integration routeのブロッカーにはしません。

## 5.7 Product runtime activation — A1 native proven

A1製品MVPは、Voice Quality Assist Toolを開くことを動作条件にしません。

```text
plugin assembly load
   ↓
CLR ModuleInitializer
   ↓
Application.Current.Dispatcherへhandoff
   ↓
YMM4 MainViewModelをwindow DataContextから発見
   ↓
public ActiveTimelineViewModel getter
   ↓
public TimelineViewModel.Items
   ↓
public TimelineItemViewModel.Item
   ↓
VoiceItem observer / reconcile
```

YMM4 4.56.1.0では`MainViewModel`型そのものがinternalですが、`ActiveTimelineViewModel` getterはpublicです。そのため製品コードのreflection境界は次の1点だけに限定します。

```text
type full name == YukkuriMovieMaker.ViewModels.MainViewModel
GetProperty("ActiveTimelineViewModel", BindingFlags.Public)
```

private fieldは読みません。特に`TimelineViewModel.timeline` private fieldやMainModel private fieldへは降りません。getter取得後はpublic `TimelineViewModel` / `TimelineItemViewModel.Item` / `VoiceItem`だけを使います。Harmonyも使用しません。

startupは`IPlugin.Initialize()`へ依存しません。YMM4 4.56.1.0の`IPlugin`は主にplugin metadata surfaceであり、A1ではCLR標準`ModuleInitializer`を意図的に使用します。assembly loadがWPF Application生成より先の場合は、loader thread上にDispatcherを作らず、短い`System.Threading.Timer`で`Application.Current`を待ってUI Dispatcherへ移します。

native run `35961051326` は旧A1 zero-pause経路のhost lifecycle証拠です。現行vNext runtimeは同じ自動Controller/lease安全性を再利用しつつ、forced-boundary transient-comma解析へ意味を更新しています。

互換性fail-safe:

- MainViewModel type名が変わる
- public ActiveTimelineViewModel getterが消える
- active timelineを取得できない

のいずれかではControllerをattachせず、project/audioを変更しません。

## 5.8 Helper mora transient synthesis — mechanism proven

Lab PR #140で、helper kanaをVoiceItemのSerif/Hatsuonへ永続挿入せず、**VOICEVOX解析時のtransient readingだけへ挿入**できることを確認しました。

```text
durable helper rule
      ↓
current sourceへanchor再解決
      ↓
baseline reading上の挿入境界
      ↓
transient augmented reading
      ↓
public VOICEVOX analysis
      ↓
inserted helper Moraを一意に特定
      ↓
target duration component = 0
      ↓
public synthesis
      ↓
real VoiceItem.FilePath
```

検証では、persisted `Serif=えええ` / `Hatsuon=エエエ` を変更せずtransient `エウエウエ`を解析し、helper `ウ`の`vowel_length=0`を実`/synthesis`へ送信しました。またpersisted `Serif=ええ` / `Hatsuon=エエ`のままtransient `エセエ`を解析し、helper `セ`の`consonant_length=0` while `vowel_length=0.12` preserved を実`/synthesis`まで確認しました。

したがってA2のhelper指定はSerif/Hatsuon rewritingではなく、Assist Effect側のdurable設定として保持します。生成済みaugmented reading / Pronounce / WAVはA1と同じくderived runtime stateです。

### Helper rule persistence model — A2 product-native proven

A2 MVPはPR #131でsave/reload実証済みの**Effect string setting**を再利用し、`PronunciationAssistEffect.HelperRulesJson`へversion付きJSONをcanonical durable representationとして保存します。Product PR #3のreal project save/reloadでもexact JSON復元と自動再適用まで確認済みです。

概念形:

```json
{
  "version": 1,
  "rules": [
    {
      "kind": "zeroVowel",
      "helper": "ウ",
      "anchor": {
        "position": 1,
        "left": "…",
        "right": "…"
      }
    }
  ]
}
```

`position`はfast path、`left/right`はsource編集後の再位置決め用です。現在clean Serifで保存positionの文脈がまだexactならそのまま使い、ずれた場合のみ全境界からcontext一致を探索します。候補0件または複数件ならfail closedし、baselineへ戻します。

anchorをreadingへ写像する際はA1と同じsame-speaker `ConvertKanjiToYomiAsync(prefix)`を利用します。prefix readingがcurrent Hatsuonの正規化prefixに一意一致するUTF-16境界だけを採用し、Serif文字indexをmora indexとして直接扱いません。

transient augmented readingをVOICEVOX解析した後は、各helperの「挿入前までの正規化prefix」と「helperを含む正規化prefix」を累積`Mora.Text`境界へ照合します。A2 MVPはその差が**exactly 1 mora**の時だけmutationします。

- `zeroVowel`: target moraの`VowelLength = 0`
- `zeroConsonant`: positive consonantを持つtarget moraの`ConsonantLength = 0`。VowelLengthは保持
- helperと`<w0>`が同一reading boundaryへ重なる場合はfail closed
- A1/A2が別boundaryなら同じdetached Pronounce上でhelper mutationとPlugin注入commaのPauseMora=0を適用してから1回のfinal synthesisへ送る

Product PR #3のnative smokeでは、source先頭追加後のanchor relocation、曖昧context時のbaseline復帰、zeroVowel x2、zeroConsonant + vowel保持、A1 `<w0>`との同居、native project save/reload後のhelper rule復元と自動再適用までGREENです。

## 5.9 Baseline-relative prosody — A3 product-native proven

Lab PR #141で、real VoiceItemのfresh built-in VOICEVOX Pronounceに対してpublic `Mora.Pitch`を相対変更し、そのままpublic `IVoiceSpeaker.CreateVoiceAsync`へ渡せることを確認しました。製品A3は固定absolute pitchを使わず、fresh baselineへ小さいgesture curveを重ねます。

MVP:

- `None`
- `LightRise`: voiced target列へ `-0.08 .. +0.08` の線形offset
- `LightFall`: voiced target列へ `+0.08 .. -0.08` の線形offset
- `Hold`: baseline meanへ偏差を50%だけ縮める

処理順:

```text
fresh / transient VOICEVOX Pronounce
        ↓
A2 helper mutation
        ↓
A1 forced-boundary injected-pause mutation
        ↓
A3 baseline-relative pitch mutation
        ↓
1回のfinal public synthesis
        ↓
real VoiceItem.FilePath
```

A2のzero-vowel helperがpitch curveのmora countを歪めないよう、`Pitch > 0` かつ `VowelLength > 0` のmoraだけをprosody targetにします。複数enabled Assist Effectが異なるprosody gestureを指定する場合はfail closedします。

`PronunciationAssistEffect.Prosody` をdurable source of truthとし、生成済みPitch/Pronounce/WAVはderived runtime stateです。Product PR #4のnative smokeではLightRise→LightFall→None→Hold、Effect disable/re-enable、real project save/reload後のexact `Prosody=Hold`復元とautomatic reapplyまでGREENです。

A3 final evidence: run `35979300166`, artifact `10799671345`.

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


## 6.1 Voice Review import/apply — B3 product-native proven

B3はB2 correction packageをcurrent Timelineへ直接適用する前に、resolutionとpreviewを分離します。

```text
B1 Export + B2 Correction
      ↓
wire/source validation
      ↓
current Timeline resolution
      ├─ EXACT_SESSION
      ├─ EXACT_FINGERPRINT
      ├─ STALE
      ├─ MISSING
      └─ AMBIGUOUS
      ↓
preview + user selection
      ↓
whole-batch preflight
      ↓
atomic durable source journal
      ↓
UndoRedoActionCommand / Record
      ↓
Track A re-generation
```

same-sessionは`exportRef -> live VoiceItem` mapを優先します。frame/layer移動だけではidentityを失いませんが、sourceFingerprintが変わればSTALEです。cross-sessionはB0 fingerprint resolverへfallbackします。

B3 journalはgenerated Pronounce/WAVを保持しません。Undo/Redo対象はSerif / Hatsuon / Assist Effect membership / HelperRulesJson / Prosodyなどdurable sourceのみです。derived audioはTrack A runtimeが再生成します。

boundary rewriteはv0安全境界として通常text + literal `<w0>` のSerifだけを対象にし、他official control tagが混在する場合はfail closedします。

Product-native run `35993465363` では、EXACT apply後にpause=0 + helper vowel=0 + Hold pitchが同じfinal synthesisへ入り、Undoでbaseline WAV、Redoで同一corrected WAVへ戻ることを確認しました。

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
