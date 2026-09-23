# Native Evidence

製品側の主張は、可能な限り公開Labの実YMM4検証へ紐付けます。

Lab repository:  
https://github.com/ziro-lab/chat-native-work-lab-001

| Evidence | Proven | Not yet proven |
| --- | --- | --- |
| [PR #116 — VOICEVOX Pronounce mutation](https://github.com/ziro-lab/chat-native-work-lab-001/pull/116) | `VoiceItem.Pronounce`、VOICEVOX AudioQuery、AccentPhrase、PauseMora、Moraへ到達。pause vowel lengthを0へ変更可能。 | 実VoiceItemの完全な生成→補正→再生成 |
| [PR #117 — Jimaku effect key](https://github.com/ziro-lab/chat-native-work-lab-001/pull/117) | `JimakuVideoEffects`へ独自Effectを保持し、type + `IsEnabled`でopt-in判定可能。 | save/reload・copy時の最終製品挙動 |
| [PR #118 — VoiceItem observer](https://github.com/ziro-lab/chat-native-work-lab-001/pull/118) | Serif / Hatsuon / JimakuVideoEffects / Effect.IsEnabledの変更通知取得。 | 製品Controllerの寿命管理 |
| [PR #123 — Official control-tag bridge](https://github.com/ziro-lab/chat-native-work-lab-001/pull/123) | `<w0>`は字幕上で幅を持たず非表示。Serifには残る。public `ControlTagParser`からclean textとTimingTag位置取得。複数marker位置も確認。 | 実VOICEVOX voice providerを使ったSerif→Hatsuon全経路 |
| [PR #127 — Voice Review item identity](https://github.com/ziro-lab/chat-native-work-lab-001/pull/127) | VoiceItemにはpublic Guid/Id surfaceがなく、standalone serializationにもGuidなし。Timeline上ではlive object参照を保持。 | cross-session再解決アルゴリズムの最終実装 |
| [PR #125 — Modified AudioQuery synthesis](https://github.com/ziro-lab/chat-native-work-lab-001/pull/125) | 補正済みAudioQueryの`pause_mora.vowel_length=0`が`/audio_query`再呼出しなしで`/synthesis`へ到達。 | 製品が使うsupported/public host route |

## Current evidence chain

現時点で、次の部品は個別に成立しています。

```text
VoiceItem opt-in
      │
      ▼
VoiceItem change observation
      │
      ▼
official <w0> marker
      │
      ▼
ControlTagParser
      ├─ clean text
      └─ boundary position
      │
      ▼
VOICEVOX AudioQuery mutation
      │
      ▼
modified AudioQuery
      │
      ▼
/synthesis without /audio_query re-analysis
```

残る主な接続点は、**通常の製品プラグインがこの合成経路をどう呼ぶか**です。Voice Review側のv0 identityは、Guidではなくsession ref + fingerprint再解決を採用します。

## Evidence labels

このRepoでは記述を次の3段階で扱います。

### PROVEN

固定YMM4版で実ホスト検証され、再現可能なLab証拠がある。

### CANDIDATE

API/既存コード/設計上は成立しそうだが、製品条件の実ホスト検証が未完了。

### BLOCKED

必要なホストsurfaceやテスト環境が不足し、現在の証拠では前へ進めない。

「既存プラグインがやっている」だけではPROVENにはしません。  
既存コードはReferenceとして利用し、必要な挙動はLabで再検証します。
