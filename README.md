# YMM4 Voice Quality Assist

YukkuriMovieMaker4（YMM4）上で、VOICEVOXの**読み・発音・句境界・初期イントネーションの品質を改善する**ための実験・設計・実装リポジトリです。

このプロジェクトの目的は、音声を完全自動で仕上げることではありません。  
**「手直しが必要でも、最初から直しやすい状態にする」**ことを重視します。

> Status: design / validation / early prototype  
> 現時点では配布版プラグインはありません。

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
- [PR #130](https://github.com/ziro-lab/chat-native-work-lab-001/pull/130)  
  補正前後のPronounce + WAVを1つのUndo単位として保持し、public `UndoAsync/RedoAsync` とYMM4標準 `CommandType.Undo/Redo` の両方で正確に往復できることを確認。

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

save/reloadの永続化境界も確認済みで、**`Pronounce`そのものではなく`<w0>`とAssist Effect設定を永続化し、reload後にPronounce/WAVを再構築する**方針が確定しました。

Undo/Redoの履歴semanticsも実ホストで成立しました。残る取得境界として、製品コードがcurrent `UndoRedoManager` をreflectionなしで受け取るPlugin API経路を確定させる必要があります。

現在の大きな技術課題は、**reload直後の自動再適用・Undo managerのpublic取得経路・プレビューcache更新・Assist Effect無効化/削除までライフサイクルを閉じること**です。

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
