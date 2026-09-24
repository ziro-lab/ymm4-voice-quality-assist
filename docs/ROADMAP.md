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

### A2. Helper mora

- [ ] hidden/helper authoring representationを決める
- [ ] helper vowel duration = 0
- [ ] helper consonant duration = 0
- [ ] regeneration reapply
- [ ] subtitle表示との整合
- [ ] helper位置の安全な再解決

### A3. Optional prosody assist

最初から万能化しない。

候補:

- none
- smooth
- light rise
- light fall
- hold
- sigh-like
- surprise-like

VOICEVOXのbaseline pitchへ相対的に適用し、固定絶対値は避ける。

---

## Track B — Voice Review Bridge

### B0. Interchange design

- [x] VoiceItemにpublic安定Guidが無いことをLab確認
- [x] v0 identity方針: session ref + fingerprint + locator
- [x] export schema v0 Draft
- [x] correction proposal schema v0 Draft
- [x] version field Draft
- [ ] source fingerprint canonicalizationをfreeze
- [ ] cross-session再解決アルゴリズムをLab/Unit test
- [ ] validation rules実装

### B1. Export

候補出力:

- [x] JSON schema / example Draft
- [ ] 人間向けCSV/XLSX view

含めたい情報:

- VoiceItem reference
- character / speaker
- Serif
- Hatsuon
- previous / next voice context
- current control tags
- current pronunciation summary
- current assist settings

### B2. LLM review workflow

- [ ] 読み候補
- [ ] 固有名詞
- [ ] 句境界候補
- [ ] helper mora候補
- [ ] optional prosody direction
- [ ] structured correction only
- [ ] no direct project mutation in first version

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
