# Pronunciation Assist vNext — Implementation Plan

Status: **PHASE 0–4 GREEN / PHASE 5 IMPLEMENTED, VALIDATION PENDING**

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

Status: **GREEN**

Accepted product code source: `38549eae374be543ca1c62cd5bff545cc8f7dd47` (PR #13).

Acceptance:

- unit: 165/165 PASS;
- A1/A2/A3 native regressions: GREEN;
- B3 import/new-write Audio storage: GREEN;
- legacy -> Audio migration Commit / one Undo / Redo: GREEN on the real host;
- B0/source fingerprint remains stable across equivalent legacy/audio storage.

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

Status: **GREEN**

Accepted product code source: `fb5eb5d44df28c980c2fbf904ec2cf429caf49d5` (PR #14).

Acceptance:

- unit: 173/173 PASS;
- A1/A2/A3 native regressions: GREEN;
- B3 native typed-UI lifecycle: GREEN;
- real Item Editor selects the canonical Audio Effect;
- helper editor and prosody editor are visible;
- raw `HelperRulesJson` is not exposed;
- helper add creates one normal YMM4 Undo record;
- Undo restores the exact previous helper JSON;
- Redo restores the edited helper state;
- helper editor resolves the owning VoiceItem through the non-persistent Store owner registry and rebuilds semantic anchors from current clean Serif.

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

Status: **GREEN**

Accepted product code source: `ae28abd6f45db26fa56774fb51c468da66f7e108` (PR #15).  
Accepted validation source: `0142d891d657dec34eb19ce79faf49e3cb4b6042`.

Implemented policy:

- each Pronunciation Assist setting persists an editing-only `BoundaryInputToken` (default `|`);
- token changes do not alter audio source identity and do not by themselves trigger synthesis;
- normalization is **explicit user action only**; no automatic keyboard interception;
- selected VoiceItems convert the configured token to canonical `<w0>`;
- an explicit clean-text-position insertion action is also available as fallback;
- `VoiceItem.Serif` changes use normal YMM4 property history, producing one native Undo record rather than a duplicate custom command;
- legacy subtitle Effect -> Audio Effect migration preserves the token exactly;
- empty/whitespace, overlong, control-character and `<` / `>` tokens are rejected;
- token occurrences inside YMM4 control-tag spans are protected;
- source changes after prepare fail closed;
- insertion into surrogate-pair interiors is rejected;
- normalization rejects first/last-position boundaries, adjacent tokens resolving to the same clean-text position, and collisions with an existing canonical `<w0>`.

Acceptance on pinned YMM4 Lite 4.56.1.0:

- unit/build: run `36203926580`, job `108296416941`
  - **204/204 PASS**
  - 0 build errors
- A1 native: run `36203926573`, job `108296308858`
  - artifact `10893770006`
  - SHA256 `7c12ed4ee0dc44f8ddda13cb13700857153c05c007100881dcaa409615fa7f32`
- A2 helper native: run `36203926625`, job `108296498507`
  - artifact `10893106907`
  - SHA256 `f060609f0d8d170c06ad30a7605f440e5c560210c40fd3a272ff81db56d91b25`
- A3 prosody native: run `36203926587`, job `108296499082`
  - artifact `10893705253`
  - SHA256 `cfacc68d90e19893a4079797d2f2ef945304c8d8f1c32242f55d80351af99c7e`
- B3 import / typed UI / Phase 4 native: run `36203926607`, job `108296495055`
  - artifact `10893700301`
  - SHA256 `b0af2c6413365af9da55f5ffe973e4d67b720bc5d00865a0b1d9298c6ae6e96d`
  - `PASS_B3_IMPORT_PRODUCT_NATIVE_SMOKE_E2E`
  - `PASS_B3_FORCED_BOUNDARY_RESTART_RELOAD_E2E`

Real-host Phase 4 proof includes:

- token text remains ordinary Serif until explicit normalization;
- `え|ええ -> え<w0>ええ`;
- exactly one normal YMM4 Undo record;
- Undo restores the original token text;
- Redo restores canonical `<w0>`;
- saved YMMP contains the canonical marker and configured token;
- after a clean YMM4 shutdown + restart, the probe opens the saved project through public `OpenProject(path)`;
- reloaded VoiceItem restores `<w0>`, `BoundaryInputToken`, and Effect enabled state exactly.

No Harmony and no global keyboard interception are used.

## Phase 5 — Review Bridge alignment

Status: **IMPLEMENTED / VALIDATION PENDING**

Implemented:

- B1 export descriptions now define `controls.boundaries[].source == "w0"` / CSV `position:w0` as canonical forced automatic-accent boundaries;
- B2 LLM prompt explicitly defines `addBoundary` as a forced VOICEVOX automatic-accent phrase boundary, not a request to zero an existing source punctuation pause;
- B3 preview labels use **「VOICEVOX自動アクセント用の強制区切り」**;
- README / Review Bridge / architecture / schema docs use the vNext meaning;
- wire/schema remain unchanged.

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
