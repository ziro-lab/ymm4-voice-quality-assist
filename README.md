# YMM4 Voice Quality Assist

YukkuriMovieMaker4（YMM4）上で、VOICEVOXの**読み・発音・句境界・初期イントネーションの品質を改善する**ための実験・設計・実装リポジトリです。

このプロジェクトの目的は、音声を完全自動で仕上げることではありません。  
**「手直しが必要でも、最初から直しやすい状態にする」**ことを重視します。

> Status: **vNext Phase 1–4 GREEN / Phase 5 Review Bridge alignment next**  
> 現時点では配布版プラグインはありません。

## vNext 現在地

候補版フィードバックを受け、発音補助の句境界仕様をvNextへ更新しています。

- 強制区切りは「既存pauseを0にする」意味ではなく、`<w0>`位置へ解析時だけ日本語読点 `、` を注入し、VOICEVOXに通常の自動アクセント再解析をさせたうえで、**注入した読点のPauseMoraだけを0**にします。
- 元からある読点pauseは変更しません。
- Phase 1製品実装はPR #12 / source `ac9d530f00a59efc67ee396b04d4d29d962a3bf1` で unit 157/157 + A1/A2/A3/B3 native GREENです。
- Lab PR #142で `VoiceItem.AudioEffects` のpublic列挙・追加/削除・設定UI・通知・pass-through・Undo/Redo・save/reloadがGREENになりました。
- Phase 2はGREENです。`PronunciationAssistSettingsStore` を導入し、**新規設定はAudio Effect、旧候補版の字幕Effectはdual-read + 明示migration**へ移行しました。実YMM4でmigrationのCommit / Undo / Redoまで確認済みです。
- Harmony/private collection traversalはこの移行に不要です。
- Phase 3もGREENです。Audio Effect内にtyped helper editorと抑揚selectorを実装し、raw `HelperRulesJson` をユーザー操作から隠しました。実YMM4でhelper追加→Undo→Redoまで確認済みです。
- Phase 4もGREENです。Audio Effectで編集用tokenを設定し、Toolの明示操作でcanonical `<w0>`へ変換できます。自動キー横取りは行わず、YMM4標準Undo/Redoで1履歴、save→正常終了→再起動→public `OpenProject(path)` 後のmarker/token復元まで実YMM4で確認済みです。
- 次はPhase 5として、Review Bridgeの `addBoundary` 説明・prompt・previewを新しい「VOICEVOX自動アクセント用の強制区切り」 semanticsへ揃えます。

正本:
- `docs/VNEXT_PRONUNCIATION_ASSIST_REQUIREMENTS.md`
- `docs/VNEXT_IMPLEMENTATION_PLAN.md`

## 二本柱

| Track | 役割 | 主な処理 |
| --- | --- | --- |
| **A. Local Pronunciation Assist** | YMM4内で軽量・決定的に補正する | 句境界、pause=0、補助モーラ、再適用、将来の軽量prosody gesture |
| **B. Voice Review Bridge** | Voice一覧をExport/Importし、LLMに文脈レビューさせる | 読み、固有名詞、句境界、補助文字候補、レビュー差分 |

両者は別製品ではなく、同じ**Correction Model**を共有する想定です。

- 人間がYMM4上で指定した修正
- Local Assistが機械的に適用する修正
- LLM Reviewが提案する修正

を同じ表現へ寄せ、適用経路だけを分離します。

## 設計原則

- **Opt-in**: 発音補助キーを持つVoiceItemだけを対象にする。
- **非破壊**: Serifや通常のYMM4編集をプラグイン専用形式へ乗っ取らない。
- **公式機能優先**: YMM4のpublic API・公式文字制御タグを優先する。
- **Harmonyは最後の手段**: public経路で成立する限り使わない。
- **LLM非依存**: Local AssistはLLMなしで単独利用できる。
- **LLMは提案側**: 初期構想ではLLMが直接プロジェクトを破壊的変更しない。
- **実ホスト検証優先**: 不明なYMM4挙動は公開Labで検証してから製品へ入れる。

## 現在までに確認できていること

公開Lab: [ziro-lab/chat-native-work-lab-001](https://github.com/ziro-lab/chat-native-work-lab-001)

- [PR #116](https://github.com/ziro-lab/chat-native-work-lab-001/pull/116)  
  VOICEVOXのPronounce / AudioQuery / PauseMoraへ到達し、pause durationを0へ変更可能。
- [PR #117](https://github.com/ziro-lab/chat-native-work-lab-001/pull/117)  
  VoiceItemの`JimakuVideoEffects`へ独自Effectを保持し、個別opt-inキーとして判定可能。
- [PR #118](https://github.com/ziro-lab/chat-native-work-lab-001/pull/118)  
  Serif / Hatsuon / JimakuVideoEffects / Effect.IsEnabledの変更通知を通常のVoiceItem監視で取得可能。
- [PR #123](https://github.com/ziro-lab/chat-native-work-lab-001/pull/123)  
  YMM4公式文字制御タグ`<w0>`を、字幕に表示されない境界マーカーとして利用可能。公式`ControlTagParser`からclean textとclean-text上の境界位置も取得可能。
- [PR #125](https://github.com/ziro-lab/chat-native-work-lab-001/pull/125)  
  修正済みVOICEVOX AudioQueryをpublic `IVoiceSpeaker.CreateVoiceAsync(...)`へ渡し、`/audio_query`再解析なしで`/synthesis`・WAV生成まで完走することを実通信で確認。
- [PR #128](https://github.com/ziro-lab/chat-native-work-lab-001/pull/128)  
  実Timeline上のVoiceItemで通常生成→Pronounce取得→pause補正→同じ`VoiceItem.FilePath`へのpublic再合成→cache無効化→Pronounce保持までE2E確認。
- [PR #131](https://github.com/ziro-lab/chat-native-work-lab-001/pull/131)  
  実プロジェクトsave/reloadで`<w0>`・Hatsuon・Assist Effect・Effect設定/有効状態が復元されることを確認。一方、`VoiceItem.Pronounce`は保存されないため、reload後は宣言的な補正情報から再構築する。
- [PR #132](https://github.com/ziro-lab/chat-native-work-lab-001/pull/132)  
  reload後の実VoiceItemで、永続化された`<w0>` + Assist Effect設定を再解決し、fresh Pronounceのpause `0.25 -> 0.0`補正→public再合成→cache無効化→Pronounce再装着までE2E確認。
- [PR #134](https://github.com/ziro-lab/chat-native-work-lab-001/pull/134)  
  Assist Effectのenable → disable → re-enable → removeで、補正済み/baselineのPronounceと実WAVが同じSHA256へ正確に往復することを確認。
- [PR #130](https://github.com/ziro-lab/chat-native-work-lab-001/pull/130)  
  補正前後のPronounce + WAVを1つのUndo単位として保持し、public `UndoAsync/RedoAsync` とYMM4標準 `CommandType.Undo/Redo` の両方で正確に往復できることを確認。
- [PR #133](https://github.com/ziro-lab/chat-native-work-lab-001/pull/133)  
  実Timeline ToolへYMM4自身が渡すpublic `TimelineToolInfo.UndoRedoManager` を確認。製品側でMainViewModel/private field reflectionを使わずcurrent Undo managerを取得可能。
- [PR #135](https://github.com/ziro-lab/chat-native-work-lab-001/pull/135)  
  実VoiceItemのbaseline/corrected WAV差し替え、public `VoiceCache` + `ClearVoiceCache()`、Pronounce再装着、`Timeline.CurrentFrame`通知まで4.56.1.0で確認。専用preview/audio強制refresh APIは確認されず、通常host state経路を採用。
- [PR #136](https://github.com/ziro-lab/chat-native-work-lab-001/pull/136)  
  Serifの`<w0>`はControlTagParser上の境界としては取れるが、Serif-only markerはVOICEVOX生成構造を自動分割しないことを確認。Hatsuonへliteral tagを入れる経路も不採用。A1には明示的なsemantic boundary resolverが必要。
- [PR #137](https://github.com/ziro-lab/chat-native-work-lab-001/pull/137)  
  public `IVoiceSpeaker.ConvertKanjiToYomiAsync`でsame-speaker full/prefix readingを取得し、prefix readingをAudioQueryの累積`Mora.Text` phrase-endへ一意に対応付けられるmechanismを確認。exact match + PauseMora存在時だけ適用するfail-closed resolverを採用可能。
- [PR #140](https://github.com/ziro-lab/chat-native-work-lab-001/pull/140)  
  helper kanaをSerif/Hatsuonへ保存せずtransient readingだけへ挿入し、helper vowel=0 / helper consonant=0（母音維持）を実VoiceItem・実`/synthesis`まで確認。

詳細は [docs/EVIDENCE.md](docs/EVIDENCE.md)。

## 現在の最重要課題

データ経路そのものはかなり確認できています。

```text
Serif
  ↓
YMM4 ControlTagParser
  ├─ clean text
  └─ boundary positions
  ↓
VOICEVOX AudioQuery
  ↓
deterministic correction
  ├─ pause = 0
  ├─ helper vowel/consonant = 0
  └─ optional relative prosody
  ↓
synthesis
```

補正済みPronounceをpublic `IVoiceSpeaker.CreateVoiceAsync(...)`から再解析なしでVOICEVOX `/synthesis`へ渡し、さらに**実VoiceItemが所有する音声ファイルへ補正済み音声を再合成して戻すE2E経路まで実ホスト検証済み**です。

save/reloadの永続化境界に加えて、**reload後に`<w0>` + Assist Effect設定からCorrectionを再解決し、fresh Pronounce/WAVへ補正を再適用するE2E経路**も確認済みです。生成済み`Pronounce`はdurable stateではなく、再構築可能なruntime stateとして扱います。

Undo/Redoの履歴semanticsに加えて、current `UndoRedoManager` をpublic `TimelineToolInfo.UndoRedoManager` から受け取る製品向け経路も実ホストで成立しました。

A0の主要host routeは閉じました。補正WAV差し替え後はpublic `ClearVoiceCache()`でstale cacheを破棄し、regenerated Pronounceを戻して通常のhost state通知へ流します。CIでは物理スピーカーの知覚確認までは主張しませんが、製品統合を止める専用refresh API依存はありません。

A1 Zero-pause boundary MVPは製品コードへ実装済みです。YMM4 4.56.1.0上でToolを開かず自動runtimeが起動し、`<w0>`境界のsame-speaker resolver、pause=0補正、cache更新、disable/re-enable、marker remove/restore、Hatsuon mismatch時のfail-closed baseline復帰までnative GREENになりました。

Product native chain:

- run `35961051326`
- job `107509502673`
- source `601d3ad4736d9a21a0c75a00042f6169ea42e542`
- artifact `10792103203`
- SHA256 `f19f2cdea5b68efce40fcd289a70e3c2dccd95e0a57959738acc9582d9dfc80d`

A2 Helper moraも製品実装まで完了しました。version付きhelper ruleをAssist Effectへ保存し、clean Serif上のposition + left/right contextからsource編集後も一意な場合だけanchorを再解決します。helperはSerif/Hatsuonへ永続挿入せず、same-speaker reading上のtransient augmented readingとして合成します。

YMM4 4.56.1.0 product-nativeで、zeroVowel / zeroConsonant、source位置ずれ後の再解決、曖昧anchor時のbaseline復帰、A1 `<w0>`との同居、実project save/reload後のhelper rule復元と自動再適用までGREENです。

A2 final native chain:

- run `35971940325`
- job `107543448102`
- source `95d10914d768e5326abb03d1f23752c3ed0d1c79`
- artifact `10796611315`
- SHA256 `5cfbdd9678bc222129a5fde555c5614b979fa9f76d31a9ea0e427ceb5e414d0a`
- build/unit `35971940326`: 22/22 PASS
- A1 native regression `35971940306`: GREEN

A3 Optional prosody assistも製品MVPまで完了しました。Lab #141でbaseline-relative Mora.Pitch mutationを実証し、製品側では `None / LightRise / LightFall / Hold` を同じdetached Pronounceへ重ねます。Effect disable / Noneでbaseline復帰し、real project save/reload後のProsody設定復元と自動再適用までnative GREENです。

A3 final native chain:

- run `35979300166`
- job `107567421818`
- source `dcc43d6ccfcc83f2602f1404ded3eb1f40f64e9f`
- artifact `10799671345`
- SHA256 `5747b60beb3642e4e312034abf7fae0ce9326efa5c4b4919e7c54414b48c21e8`

Track Aの当初MVP範囲（A0〜A3）は閉じました。

Track BもB0 identity/fingerprint/validationに続いて、B1 Exportまで実装済みです。現在TimelineのVoiceItemをcanonical `ymm4.voice-review.v0` JSONへ書き出し、same-session live target mapを保持できます。ToolからJSON正本と人間向けCSVを保存できます。

B1 final verification:

- source `3c58905043f125f38ef5b0e00bd173b99dda9215`
- build/unit `35985287715`: 79/79 PASS / 0 warnings / 0 errors
- A1 native `35985287788`: GREEN
- A2 native `35985287645`: GREEN
- A3 native `35985287677`: GREEN

B2 LLM review workflowも実装済みです。ToolからB1 packageを埋め込んだLLMレビュー用prompt fileを出力でき、LLMは `ymm4.voice-corrections.v0` の構造化JSONだけを返します。全Voice exactly-one、変更なしは明示的 `noChange`、session/ref/fingerprint/coverageとB0 domain rulesを検証します。B2ではYMM4を変更しません。

B2 final verification:

- source `9fde8e810fedbbd58fc5078be78b201e89d299f9`
- build/unit `35986180201`: 91/91 PASS / 0 warnings / 0 errors
- A1 native `35986180608`: GREEN
- A2 native `35986180270`: GREEN
- A3 native `35986180221`: GREEN

B3 Import / Reviewも製品MVPまで完了しました。LLM correction JSONを現在Timelineへsame-session/cross-session再解決し、EXACT / STALE / MISSING / AMBIGUOUSを表示、before/afterを確認してEXACTだけ選択適用できます。選択batchはwhole-preflight後にdurable sourceへatomic applyし、YMM4標準Undo/Redoへ1 recordとして登録します。

B3 final native chain:

- source `b1ecbcd4a0eef09daecbc8fb3aa94463ee829d9d`
- build/unit `35993465474`: 121/121 PASS / 0 warnings / 0 errors
- B3 native `35993465363`: GREEN
- job `107612773315`
- artifact `10805292609`
- SHA256 `f548bbf7271f1562712fb8ced06fe25579ea9d1f24bd43eec6b0d39747348ff2`
- A1 native `35993465612`: GREEN
- A2 native `35993465546`: GREEN
- A3 native `35993465367`: GREEN

nativeではSTALE proposal拒否、EXACTのみapply、`<w0>` + helper + HoldのA1/A2/A3複合再生成、Undo baseline復帰、Redoで同一corrected WAV SHA256への復帰まで確認済みです。

これでTrack A A0〜A3とTrack B B0〜B3の当初MVP範囲は一周しました。次はShared層（Correction Model / status model / compatibility / packaging / release）を整理する段階です。実機スピーカー知覚確認とファイルダイアログ/Import一覧の見た目確認はnon-blocking hands-on acceptanceとして残します。

## Documents

- [Vision / Scope](docs/VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
- [Native Evidence](docs/EVIDENCE.md)
- [LLM Voice Review Bridge](docs/REVIEW_BRIDGE.md)
- [Review Bridge Schema v0](docs/SCHEMA_V0.md)
- [Source Fingerprint v0](docs/FINGERPRINT_V0.md)
- [Contributor / Agent Rules](AGENTS.md)

## Repository policy

このRepoは製品側の設計・実装を扱います。  
YMM4本体の挙動が不明な検証は、原則として公開Labへ切り出し、結果だけをEvidenceとしてこちらへ持ち込みます。
