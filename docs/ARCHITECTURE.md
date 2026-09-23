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
