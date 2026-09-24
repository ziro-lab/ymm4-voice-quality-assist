# Pronunciation Assist vNext — Implementation Plan

Status: **PHASE 0–1 GREEN / PHASE 2 IN IMPLEMENTATION**

Base candidate:
- branch: `work/candidate-readiness`
- verified source: `f15e884624bc828dbe26891a71a46957eab09c5e`

Requirements:
- `docs/VNEXT_PRONUNCIATION_ASSIST_REQUIREMENTS.md`

## Phase 0 — Lab gates

Status: **GREEN**

Accepted Lab evidence is frozen in `ziro-lab/chat-native-work-lab-001` PR #142.

- L0.1 forced-boundary transient comma:
  - source `592115e48f67a0f52fb829fc70ff4e26d411a4e5`
  - run `36013239960`
  - all 14 required assertions PASS
- L0.2 Audio Effect host surface:
  - source `ea0ecae8925c40adcd7bb28c4cac7559468da4ef`
  - run `36021832446`
  - all 14 required assertions PASS

Do these before changing durable Effect storage.

### L0.1 Forced-boundary transient comma

Goal:
prove the exact intended VOICEVOX behavior on a real VoiceItem.

Fixture must compare:

1. baseline text without marker;
2. same text with one forced boundary;
3. original real comma + one forced boundary;
4. two forced boundaries.

Evidence required:

- transient analysis input contains plugin-injected `、`;
- VOICEVOX returns an additional phrase boundary at each injected location;
- original comma PauseMora remains non-zero;
- injected comma PauseMora can be uniquely identified and set to zero;
- final synthesis succeeds;
- durable Serif keeps `<w0>`;
- durable Hatsuon contains neither `<w0>` nor the injected comma;
- save/reload re-applies the same behavior.

Use the user's acceptance example around `実<w0>験` as one fixture.

### L0.2 Audio Effect host surface

Goal:
decide Audio Effect migration without speculation.

Prove on pinned YMM4 4.56.1.0 Lite:

- custom `AudioEffectBase` registration;
- VoiceItem Audio Effects UI appearance;
- custom property editor appearance;
- public collection access;
- enable/disable notification;
- save/reload;
- pass-through audio identity;
- add/remove + Undo/Redo compatibility.

Decision:

- GREEN -> Phase 2 migrates canonical storage to audio Effect.
- BLOCKED -> keep subtitle Effect for this release and proceed with all other phases.

Do not delay forced-boundary semantics on this UI-placement decision.

## Phase 1 — Forced boundary core

Status: **GREEN**

Accepted product source: `ac9d530f00a59efc67ee396b04d4d29d962a3bf1` (PR #12).

- unit: 157/157 PASS
- A1 vNext native: GREEN
- A2 helper native: GREEN
- A3 prosody native: GREEN
- B3 import native: GREEN

Replace the old A1 resolver semantics.

Suggested core units:

- `ForcedBoundaryAnalysisPlan`
  - clean Serif
  - canonical marker positions
  - transient analysis text/reading
  - injected punctuation identities

- `ForcedBoundaryReadingPlanner`
  - maps clean-text positions to same-speaker reading positions
  - inserts transient comma tokens
  - supports coexistence with helper-mora transient insertion

- `InjectedPauseResolver`
  - maps only plugin-injected commas to generated PauseMoras
  - proves one-to-one identity
  - never returns original punctuation pauses

- `ForcedBoundaryMutator`
  - sets resolved injected PauseMora vowel length to zero

Pipeline order:

1. parse canonical Serif markers;
2. resolve transient helper and forced-boundary insertion plan;
3. obtain fresh VOICEVOX analysis using transient punctuation;
4. identify injected PauseMoras;
5. apply helper zero-length mutation;
6. apply injected PauseMora=0;
7. apply optional prosody;
8. final synthesis;
9. commit only under current lease.

Do not persist generated transient reading.

### Phase 1 acceptance

Unit:

- one marker;
- multiple markers;
- original comma before/after injected comma;
- duplicate/ambiguous mapping;
- surrogate boundary;
- source change invalidation;
- helper + boundary same item;
- helper/boundary collision policy;
- prosody coexistence.

Native:

- user acceptance example produces new phrase split;
- original comma pause preserved;
- injected comma pause=0;
- disable/re-enable exact baseline/corrected roundtrip;
- save/reload exact reapply;
- Undo/Redo.

## Phase 2 — Effect storage adapter

Status: **IN IMPLEMENTATION / L0.2 GREEN**

Current implementation branch: `feature/vnext-audio-effect-storage` / PR #13.

The accepted storage policy is:

- canonical/new-write: `VoiceItem.AudioEffects`;
- compatibility read: legacy `JimakuVideoEffects`;
- runtime/core collection identity is isolated behind `PronunciationAssistSettingsStore`;
- legacy migration is explicit, user-triggered and Undo/Redo-able;
- mixed legacy+audio state is not auto-merged; migration fails closed for manual review.

Only after L0.2 decision.

Introduce a storage abstraction so runtime/core do not care which host collection owns the Effect.

Example responsibility:

`PronunciationAssistSettingsStore`

- Enumerate enabled settings;
- Add;
- Remove;
- Snapshot;
- Restore;
- detect legacy/new representation.

If Audio Effect GREEN:

- add new audio Effect type;
- dual-read legacy video Effect;
- new-write audio Effect;
- migrate candidate state safely.

If BLOCKED:

- store remains legacy video Effect;
- do not fork runtime logic.

The runtime should stop directly naming `JimakuVideoEffects` outside the adapter.

## Phase 3 — Detail settings UI

Expose typed settings, not raw serialization.

### 3.1 Forced boundary

- configurable input token;
- insert/normalize action if needed;
- boundary count;
- short help text.

### 3.2 Helper rules

Replace raw JSON editing with rows:

- helper;
- zero vowel / zero consonant;
- anchor/position;
- remove rule.

Keep `HelperRulesJson` as internal persistence until a later schema migration is justified.

### 3.3 Prosody

Simple enum selector:

- None
- LightRise
- LightFall
- Hold

### 3.4 Status

Show at least the last apply state in the Tool.
Inline Effect status is optional for this round.

## Phase 4 — Input-token normalization

Preferred:

- user chooses token;
- explicit or normal public edit action converts token -> `<w0>`;
- one YMM4 Undo record;
- no Harmony;
- no global text interception.

Safety tests:

- token already exists in ordinary text before feature enable;
- token inside YMM4 control tag;
- multiple tokens;
- empty token;
- multi-character token;
- Undo/Redo;
- save/reload.

If automatic normalization cannot be done safely:
ship an explicit Insert Boundary action first.

## Phase 5 — Review Bridge alignment

Update:

- B1 export descriptions;
- B2 LLM prompt wording;
- B3 preview labels;
- README / architecture / schema docs.

`addBoundary` retains its wire name and clean-text-position payload.

New UI wording should describe:

`VOICEVOX自動アクセント用の強制区切り`

rather than:

`pause=0境界`.

Regression:

- B0 fingerprint vectors reviewed;
- B1 deterministic export;
- B2 strict validation;
- B3 exact/stale/missing/ambiguous resolution;
- one-record Undo/Redo.

## Phase 6 — Candidate acceptance

Do not publish a release yet.

Required before the next candidate:

- unit all green;
- A1 vNext native green;
- A2 native green;
- A3 native green;
- B3 native green;
- candidate package built from exact native-tested DLL;
- copied YMMP hands-on;
- real VOICEVOX listening with at least:
  - one forced boundary;
  - original comma + forced boundary;
  - helper rule;
  - forced boundary + helper;
  - forced boundary + prosody;
- Effect detail UI hands-on;
- Audio Effect location hands-on if migrated.

## Implementation order summary

```text
Requirements freeze
      ↓
L0.1 transient comma proof ─────────┐
L0.2 Audio Effect proof ──────┐     │
                              │     │
Forced-boundary core ◄──────────────┘
      ↓
Effect storage adapter ◄──────┘
      ↓
Detail settings UI
      ↓
Input-token UX
      ↓
Review Bridge wording/semantics
      ↓
Native + real-engine hands-on
      ↓
next candidate
```

## Things explicitly not to refactor now

- B0 identity/fingerprint architecture;
- B3 atomic journal model;
- runtime lease/stale-result protections;
- helper anchor schema unless forced-boundary integration proves a concrete defect;
- prosody math;
- candidate packaging pipeline.

Reuse the working safety spine. Change the meaning and UX around forced boundaries without reopening unrelated subsystems.
