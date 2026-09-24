# Roadmap

このRoadmapは「機能数」ではなく、**製品として壊れにくい順序**を優先します。

## Track A — Local Pronunciation Assist

### A0. Host route closure — CLOSED

目的: private/internal APIへ製品が直接依存せず、補正済みPronounceを通常のYMM4合成経路へ流す。

済:

- [x] Pronounce / AudioQueryのpublic mutation確認
- [x] PauseMora duration = 0確認
- [x] VoiceItemごとのEffect key確認
- [x] VoiceItem変更監視確認
- [x] 公式`<w0>` marker transport確認
- [x] 公式ControlTagParserからclean text / boundary position取得
- [x] modified AudioQueryが再解析されず`/synthesis`へ届くことを確認
- [x] public `IVoiceSpeaker.CreateVoiceAsync` synthesis routeを実通信で確認
- [x] 実VoiceItemで生成→補正→再生成を通す
- [x] Undo/Redo apply-unit semantics（Pronounce + WAVを1 Record、標準Undo/Redo commandで往復）
- [x] save/reload persistence境界（`<w0>` / Hatsuon / Assist Effect / 設定は復元、`Pronounce`は非永続）
- [x] reload後のCorrection再解決→fresh Pronounce/WAV再生成

済（lifecycle / refresh）:

- [x] current UndoRedoManagerのproduct-grade public取得経路（`TimelineToolInfo.UndoRedoManager`）
- [x] audio/cache更新の実機挙動（WAV置換 + `ClearVoiceCache()` + Pronounce再装着 + host state通知）
- [x] Effect disable/remove時のbaseline復元 / re-enable再適用

非ブロッキングのhands-on acceptance:

- [ ] 実機スピーカーで補正直後のプレビュー音声を知覚確認（CIでは物理音声出力を証明しない）

### A1. Zero-pause boundary MVP — CLOSED / PRODUCT NATIVE GREEN

- [x] Assist Effect
- [x] VoiceItem observer/controller（Toolを開かず自動runtime起動）
- [x] `<w0>` boundary extraction
- [x] boundary → target AccentPhrase mapping mechanism（same-speaker reading prefix → unique cumulative Mora.Text phrase-end）
- [x] same-speaker reading resolver route（public `ConvertKanjiToYomiAsync`、exact match時のみ適用）
- [x] resolver product implementation + fail-closed unit coverage（13/13 PASS）
- [x] target PauseMora duration = 0
- [x] regeneration reapply（disable/re-enable、marker remove/restore、Hatsuon変更）
- [x] no-op / baseline restoration when Effect is absent or disabled

Product native evidence:

- run `35961051326`
- job `107509502673`
- source `601d3ad4736d9a21a0c75a00042f6169ea42e542`
- artifact `10792103203`
- artifact SHA256 `f19f2cdea5b68efce40fcd289a70e3c2dccd95e0a57959738acc9582d9dfc80d`

### A2. Helper mora — CLOSED / PRODUCT NATIVE GREEN

- [x] helperをSerif/Hatsuonへ永続挿入しない方針（Assist Effect設定 → transient augmented reading）
- [x] helper vowel duration = 0（Lab #140 + Product PR #3）
- [x] helper consonant duration = 0 + helper vowel維持（Lab #140 + Product PR #3）
- [x] subtitle/通常Hatsuonを変更せず実VoiceItemへ合成可能
- [x] version付きdurable helper rule schema / codec（Assist Effect string setting）
- [x] helper anchorの安全な再解決（position fast path + left/right context、0/複数候補はfail closed）
- [x] same-speaker Serif prefix → current Hatsuon boundaryのexact mapping
- [x] transient augmented reading → helper moraを累積`Mora.Text`で一意再特定
- [x] A1 `<w0>` + A2 helperの同一Pronounce上での併用
- [x] regeneration / real project save-reload後のproduct自動reapply
- [x] A2 product-native smoke

Product native evidence:

- run `35971940325`
- job `107543448102`
- source `95d10914d768e5326abb03d1f23752c3ed0d1c79`
- artifact `10796611315`
- artifact SHA256 `5cfbdd9678bc222129a5fde555c5614b979fa9f76d31a9ea0e427ceb5e414d0a`
- build/unit run `35971940326`: 22/22 PASS / 0 warnings / 0 errors
- A1 native regression run `35971940306`: GREEN

### A3. Optional prosody assist — CLOSED / PRODUCT NATIVE GREEN

MVPは万能な演技補正ではなく、fresh VOICEVOX baseline pitchへ小さい相対カーブを重ねる。

- [x] Lab #141でpublic Mora.Pitchのrelative mutation → public synthesis → real VoiceItemを確認
- [x] `None`
- [x] `LightRise`
- [x] `LightFall`
- [x] `Hold`
- [x] pitch=0 / zero-vowel helper moraをgesture対象から除外
- [x] A1 zero-pause / A2 helperの後に同じdetached Pronounceへprosodyを重ねる
- [x] Effect disable / `None` でbaselineへ復帰
- [x] real project save/reloadでProsody設定を復元
- [x] reload後のautomatic product reapply
- [x] A1/A2 native regression GREEN

Product native evidence:

- run `35979300166`
- job `107567421818`
- source `dcc43d6ccfcc83f2602f1404ded3eb1f40f64e9f`
- artifact `10799671345`
- artifact SHA256 `5747b60beb3642e4e312034abf7fae0ce9326efa5c4b4919e7c54414b48c21e8`
- normal build/unit run `35979300189`: GREEN
- A1 native regression `35979300297`: GREEN
- A2 native regression `35979300153`: GREEN

`smooth` / `sigh-like` / `surprise-like` はMVP完了条件に含めず、将来拡張として別途検証する。

---

## Track B — Voice Review Bridge

### B0. Interchange design — CLOSED / UNIT GREEN

- [x] VoiceItemにpublic安定Guidが無いことをLab確認
- [x] v0 identity方針: session ref + fingerprint + locator
- [x] export schema v0 Draft
- [x] correction proposal schema v0 Draft
- [x] version field Draft
- [x] source fingerprint canonicalizationをfreeze（UTF-8 canonical JSON + frozen SHA-256 vector）
- [x] cross-session再解決アルゴリズムをUnit test（exact 1件のみauto-apply、duplicate exactはAMBIGUOUS、locator-onlyはSTALE）
- [x] correction proposal validation rules実装（schema/session/fingerprint/operation payload/position/conflict）

B0 final verification:

- source `c308d18474ed2afd4360578494708ace8f9805ec`
- normal build/unit run `35982062464`: 66/66 PASS
- A1 native regression `35982062549`: GREEN
- A2 native regression `35982062596`: GREEN
- A3 native regression `35982062548`: GREEN

### B1. Export — CLOSED / UNIT + REGRESSION GREEN

- [x] canonical `ymm4.voice-review.v0` JSON package
- [x] deterministic ordering（Frame → Layer → original input order）
- [x] 0-based `exportIndex` + `voice-000000` exportRef
- [x] B0 `sourceFingerprint`をexact再利用
- [x] same-session `exportRef -> live VoiceItem` map
- [x] character / speaker API・ID
- [x] Serif / Hatsuon
- [x] previous / next Voice Serif context
- [x] official `<w0>` → cleanText / boundary情報
- [x] current Assist helper / prosody settings
- [x] optional generated pronunciation summary
- [x] JSON serialize / deserialize
- [x] 人間向けCSV派生view（stable columns / RFC-style quote escaping / CRLF）
- [x] ToolからJSON / CSV保存導線
- [x] malformed enabled Assist settingsはwhole-export fail closed

B1 final verification:

- source `3c58905043f125f38ef5b0e00bd173b99dda9215`
- normal build/unit run `35985287715`: 79/79 PASS / 0 warnings / 0 errors
- A1 native regression `35985287788`: GREEN
- A2 native regression `35985287645`: GREEN
- A3 native regression `35985287677`: GREEN

Non-blocking hands-on:

- [ ] 実YMM4でSaveFileDialogからJSON/CSVを保存して開く
- [ ] CSVをLibreOffice/Excelで見た目確認

XLSXは必須ではなく、CSVでhuman-readable viewを満たす。必要なら後続で追加する。

### B2. LLM review workflow — CLOSED / UNIT + REGRESSION GREEN

- [x] 読み候補 → `setReading`
- [x] 固有名詞 / 技術語の読み確認 → `setReading`
- [x] 句境界候補 → `addBoundary` / `removeBoundary`
- [x] helper mora候補 → `helperVowelZero` / `helperConsonantZero`
- [x] optional prosody direction → `none / lightRise / lightFall / hold`
- [x] structured correction only
- [x] no direct project mutation in B2
- [x] deterministic LLM review prompt生成
- [x] `ymm4.voice-corrections.v0` wire codec
- [x] wire operation → B0 typed correction conversion
- [x] exportSessionId / exportRef / sourceFingerprint照合
- [x] 全Voice exactly-one coverage要求（変更なしは明示的 `noChange`）
- [x] B0 domain validationをwire decode後にも適用
- [x] ToolからLLMレビュー用prompt fileを書き出し

B2 final verification:

- source `9fde8e810fedbbd58fc5078be78b201e89d299f9`
- normal build/unit run `35986180201`: 91/91 PASS / 0 warnings / 0 errors
- A1 native regression `35986180608`: GREEN
- A2 native regression `35986180270`: GREEN
- A3 native regression `35986180221`: GREEN

B2はproposal生成・検証まで。現在Timelineへの再解決、before/after diff、選択、apply、UndoはB3へ分離する。

### B3. Import / Review

- [ ] schema validation
- [ ] stale source detection
- [ ] before/after diff
- [ ] select all / per-item apply
- [ ] rejected proposalを適用しない
- [ ] Undo-compatible apply unit

---

## Shared

- [ ] Correction Model v0
- [ ] error/status model
- [ ] compatibility policy
- [ ] packaging
- [ ] README usage screenshots
- [ ] release checklist

## Later / not now

- always-on cloud LLM
- full automatic acting
- multiple TTS engines
- large semantic rewriting
- automatic subtitle rewriting
- heavy acoustic model inside plugin
