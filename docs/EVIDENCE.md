# Native Evidence

製品側の主張は、可能な限り公開Labの実YMM4検証へ紐付けます。

Lab repository:  
https://github.com/ziro-lab/chat-native-work-lab-001

| Evidence | Proven | Not yet proven |
| --- | --- | --- |
| [PR #116 — VOICEVOX Pronounce mutation](https://github.com/ziro-lab/chat-native-work-lab-001/pull/116) | `VoiceItem.Pronounce`、VOICEVOX AudioQuery、AccentPhrase、PauseMora、Moraへ到達。pause vowel lengthを0へ変更可能。 | 実VoiceItem接続はPR #128で確認。 |
| [PR #117 — Jimaku effect key](https://github.com/ziro-lab/chat-native-work-lab-001/pull/117) | `JimakuVideoEffects`へ独自Effectを保持し、type + `IsEnabled`でopt-in判定可能。 | copy時の最終製品挙動。save/reloadはPR #131で確認。 |
| [PR #118 — VoiceItem observer](https://github.com/ziro-lab/chat-native-work-lab-001/pull/118) | Serif / Hatsuon / JimakuVideoEffects / Effect.IsEnabledの変更通知取得。 | 製品Controllerの寿命管理 |
| [PR #123 — Official control-tag bridge](https://github.com/ziro-lab/chat-native-work-lab-001/pull/123) | `<w0>`は字幕上で幅を持たず非表示。Serifには残る。public `ControlTagParser`からclean textとTimingTag位置取得。複数marker位置も確認。 | 実VOICEVOX voice providerを使ったSerif→Hatsuon全経路 |
| [PR #127 — Voice Review item identity](https://github.com/ziro-lab/chat-native-work-lab-001/pull/127) | VoiceItemにはpublic Guid/Id surfaceがなく、standalone serializationにもGuidなし。Timeline上ではlive object参照を保持。 | cross-session再解決アルゴリズムの最終実装 |
| [PR #125 — Public modified AudioQuery synthesis](https://github.com/ziro-lab/chat-native-work-lab-001/pull/125) | public `IVoiceSpeaker.CreateVoiceAsync(...)`で補正済みAudioQueryを`/audio_query`再呼出しなしに`/synthesis`へ渡し、WAV生成まで完走。 | 実VoiceItemへの接続はPR #128で検証。 |
| [PR #128 — Real VoiceItem regeneration lifecycle](https://github.com/ziro-lab/chat-native-work-lab-001/pull/128) | 実Timeline VoiceItemで通常生成し、public speakerからPronounceを取得・補正後、同じ`VoiceItem.FilePath`へ再合成。最終`/synthesis`前に`/audio_query`再解析なし。cache無効化後も補正済みPronounceをVoiceItemへ保持。 | cache/state境界はPR #135、Effect lifecycleはPR #134で確認。 |
| [PR #131 — Correction save/reload](https://github.com/ziro-lab/chat-native-work-lab-001/pull/131) | native SaveProject/OpenProjectで`<w0>`・Hatsuon・Assist Effect・Effect設定/有効状態を復元。別VoiceItem objectとしてreloadされる。一方`VoiceItem.Pronounce`は非永続。 | plugin-unavailable時の挙動。 |
| [PR #132 — Reload correction reapply](https://github.com/ziro-lab/chat-native-work-lab-001/pull/132) | 実project reload後にdurable marker + Assist Effect/settingsからCorrectionを再解決。fresh analysis pause `0.25`を`0.0`へ補正し、public synthesisでreloaded VoiceItemのWAV/Pronounceへ再適用。最終補正synthesis前に再`/audio_query`なし。 | 自動trigger/subscriptionの最終Controller実装。 |
| [PR #134 — Assist Effect disable/remove lifecycle](https://github.com/ziro-lab/chat-native-work-lab-001/pull/134) | enabledでpause `0.0` + corrected WAV、disable/removeでpause `0.25` + baseline WAV、re-enableで同一corrected WAVへ復帰。Effect/VoiceItem通知とWAV SHA256一致まで実ホスト確認。 | debounce/coalescing・batch schedulingの製品Controller実装。 |
| [PR #130 — Correction Undo/Redo](https://github.com/ziro-lab/chat-native-work-lab-001/pull/130) | Pronounce + 実WAV bytesを1つの`UndoRedoActionCommand` / `Record()`として保持し、public `UndoAsync/RedoAsync` とYMM4標準 `CommandType.Undo/Redo` の双方でbaseline/corrected stateを完全往復。 | manager取得はPR #133でpublic route確認。 |
| [PR #133 — Public Undo manager acquisition](https://github.com/ziro-lab/chat-native-work-lab-001/pull/133) | real `ITimelineToolViewModel.SetTimelineToolInfo(TimelineToolInfo)` へYMM4自身がnon-null `Timeline` / `UndoRedoManager`を渡す。managerの`AddCommand` / `Record` / `UndoAsync` / `RedoAsync`もpublic。 | timeline-tool/controller以外の取得形態は未評価。 |
| [PR #135 — VoiceItem audio/cache refresh surface](https://github.com/ziro-lab/chat-native-work-lab-001/pull/135) | baseline/corrected WAVが実`VoiceItem.FilePath`で別SHA256。public `VoiceCache : byte[]`をstale 5-byte sentinelから`ClearVoiceCache()`でnullへ破棄し、corrected WAVは保持。Pronounce/VoiceCache通知と`Timeline.CurrentFrame`通知も確認。 | GitHub-hosted Windowsでの物理スピーカー知覚確認は対象外。専用public preview/audio redraw APIは確認されず。 |

| [PR #136 — <w0> real VoiceItem voice path](https://github.com/ziro-lab/chat-native-work-lab-001/pull/136) | Serif-only `<w0>`はofficial parserではclean-text boundaryとして解決されるが、baselineと同じVOICEVOX HTTP/Pronounce生成構造になり自動AccentPhrase境界化されない。Hatsuonへliteral `<w0>`を入れるとそのままVOICEVOXへ渡るため不採用。 | A1 semantic resolverは別途必要。 |

| [PR #137 — Same-speaker reading prefix resolver](https://github.com/ziro-lab/chat-native-work-lab-001/pull/137) | public `ConvertKanjiToYomiAsync`はbuilt-in VOICEVOX speakerで`/audio_query?text=...`を使い、full/prefix readingを返す。normalized prefix readingをAudioQueryの累積`Mora.Text` phrase-endへ一意に対応付け、PauseMora存在まで確認。 | 実文ごとのprefix安定性は保証せず、exact per-item validationでfail closed。 |

| [PR #140 — Transient helper mora](https://github.com/ziro-lab/chat-native-work-lab-001/pull/140) | Real VoiceItemでpersisted Serif/Hatsuonを変更せず、transient readingだけへhelper kanaを挿入。`エウエウエ`のhelper `ウ.vowel_length=0`、`エセエ`のhelper `セ.consonant_length=0` + `vowel_length=0.12`維持を実`/synthesis` JSONまで確認。corrected WAVはそれぞれ5444/5644 bytes。 | durable helper anchor/schema・source編集後の再位置決めは未freeze。 |

Lab #140 helper-mora chain:

- run `35961814286`
- job `107511806806`
- source `3422be366646382317e47bdc2c367e3c525e1d3b`
- artifact `10792227381`
- artifact SHA256 `18621932e2b36b65d3417eb61abd464ed3fe6e91b5d83f80b8eb087220b29b2b`

## Product native evidence

| Evidence | Proven | Boundary |
| --- | --- | --- |
| [Product PR #2 — A1 Zero-pause MVP](https://github.com/ziro-lab/ymm4-voice-quality-assist/pull/2) | Real YMM4 Lite 4.56.1.0上で製品DLLそのものを別pluginとしてロードし、Toolを開かず自動runtime起動。baseline WAV（4844 bytes / SHA256 `58a2f64e...`）→ corrected WAV（5444 bytes / SHA256 `285df804...`）へ補正。Effect disableでbaseline、re-enableで同一corrected SHA256、marker removeでbaseline、marker restoreでcorrected、Hatsuon不一致でfail-closed baseline、互換Hatsuon復帰で同一corrected WAVへ再適用。通常CIは13/13 tests PASS。 | 物理スピーカーでの知覚確認は対象外。project save/reloadそのものの製品native smokeはLab #131/#132のhost evidenceに依存。 |

Native chain:

- run `35961051326`
- job `107509502673`
- source `601d3ad4736d9a21a0c75a00042f6169ea42e542`
- artifact `10792103203`
- artifact SHA256 `f19f2cdea5b68efce40fcd289a70e3c2dccd95e0a57959738acc9582d9dfc80d`

## Current evidence chain

現時点で、次の部品は実ホスト上でつながっています。

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
public IVoiceSpeaker.CreateVoiceAsync
      │
      ▼
/synthesis without /audio_query re-analysis
      │
      ▼
real VoiceItem.FilePath
      │
      ├─ ClearVoiceCache()
      └─ corrected Pronounce retained on VoiceItem
      │
      ▼
native save / reload
      │
      ├─ durable: <w0> + Hatsuon + Assist Effect/settings
      └─ transient: generated Pronounce
      │
      ▼
fresh analysis after reload
      │
      ▼
Correction re-resolution
      │
      ▼
public corrected synthesis back to reloaded VoiceItem
```

public合成接続点はPR #125、実VoiceItemへの生成→補正→再生成E2EはPR #128で閉じました。PR #131ではsave/reload境界を確認し、`Pronounce`自体は保存せず、`<w0>`・Hatsuon・Assist Effect設定をdurable sourceとして扱う方針を確定しました。さらにPR #132で、reload後にそのdurable sourceからCorrectionを再解決し、fresh Pronounce/WAVへ補正を再適用するE2Eも閉じました。

PR #130ではPronounce + WAVを1つのhost Undo単位に載せ、YMM4標準Undo/Redo commandからも往復できることを確認済みです。PR #133でcurrent managerもpublic `TimelineToolInfo.UndoRedoManager` から取得できることを確認しました。PR #135でWAV置換後の`VoiceCache`破棄・Pronounce再装着・通常host state通知まで閉じ、専用preview/audio redraw APIへの依存は不要と判断します。Effect disable/removeの状態遷移はPR #134で閉じました。

Product PR #2では、これらのhost evidenceを製品コードへ統合し、ModuleInitializerで自動runtimeを起動、internal `MainViewModel`上の**public** `ActiveTimelineViewModel` getterだけをbounded reflectionで取得した後、public `TimelineViewModel.Items -> TimelineItemViewModel.Item -> VoiceItem`経路で監視する構成をnative GREENにしました。private fieldやHarmonyは使用していません。

Voice Review側のv0 identityは、Guidではなくsession ref + fingerprint再解決を採用します。

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
