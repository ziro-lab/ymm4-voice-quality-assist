# Roadmap

このRoadmapは「機能数」ではなく、**製品として壊れにくい順序**を優先します。

## Track A — Local Pronunciation Assist

### A0. Host route closure — REAL VOICEITEM ROUTE PROVEN / LIFECYCLE IN PROGRESS

目的: private/internal APIへ製品が直接依存せず、補正済みPronounceを通常のYMM4合成経路へ流す。

済:

- [x] Pronounce / AudioQueryのpublic mutation確認
- [x] PauseMora duration = 0確認
- [x] VoiceItemごとのEffect key確認
- [x] VoiceItem変更監視確認
- [x] 公式`<w0>` marker transport確認
- [x] 公式ControlTagParserからclean text / boundary position取得
- [x] modified AudioQueryが再解析されず`/synthesis`へ届くことを確認

残:

- [x] public `IVoiceSpeaker.CreateVoiceAsync` synthesis routeを実通信で確認
- [x] 実VoiceItemで生成→補正→再生成を通す
- [x] Undo/Redo apply-unit semantics（Pronounce + WAVを1 Record、標準Undo/Redo commandで往復）
- [ ] current UndoRedoManagerのproduct-grade public取得経路
- [x] save/reload persistence境界（`<w0>` / Hatsuon / Assist Effect / 設定は復元、`Pronounce`は非永続）
- [ ] reload後のCorrection再解決→Pronounce/WAV再生成
- [ ] preview/audio cache更新の実機挙動
- [ ] Effect disable/remove時の挙動

### A1. Zero-pause boundary MVP

- [ ] Assist Effect
- [ ] VoiceItem observer/controller
- [ ] `<w0>` boundary extraction
- [ ] boundary → target AccentPhrase mapping
- [ ] target PauseMora duration = 0
- [ ] regeneration reapply
- [ ] no-op when Effect is absent/disabled

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
