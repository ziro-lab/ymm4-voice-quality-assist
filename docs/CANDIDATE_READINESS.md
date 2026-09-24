# Candidate readiness

Branch: `work/candidate-readiness`, stacked on shared model PR #9.

The initial A0–A3 and B0–B3 GREEN results close specific host/data paths, not release readiness. This pass adds concrete preserve-work fixes rather than another general framework.

## Changes to verify

- Discard stale synthesis after text/effect/parameter changes, removal or Timeline replacement.
- Keep pending rescans during asynchronous processing.
- Track in-place parameter change revisions instead of relying only on parameter object identity.
- Do not resynthesize disabled/unconfigured items on every unrelated scan.
- Stage WAV replacement without truncating the current output on copy failure.
- Reject two selected review records resolving to one live item.
- Recheck live membership at import commit.
- Treat removed same-session targets as MISSING, not as a reason to rebind an identical item.
- Add an enabled Assist Effect for boundary-only imports when none is active.
- Revalidate B2 data against the actual export at the B3 planning boundary.
- Report rollback failure rather than claiming restoration.
- Show runtime/compatibility status in the Tool without storing or transmitting project text.
- Save the LLM prompt together with a matching source JSON for cross-session import.
- Bound correction JSON, reject unknown fields and protect the derived human CSV from formula-like text.

## Acceptance

CandidateSafetyTests covers guard invalidation, atomic file replacement failure, duplicate/missing import targets, boundary-only effect ownership, input limits, and CSV neutralization. A1 native smoke additionally holds a corrected synthesis response while Assist is disabled, requires the old response to be discarded, confirms baseline restoration, verifies disabled configuration changes cause no new plugin synthesis, and confirms recovery after re-enable.

The candidate workflow builds/tests, runs A1/A2/A3/B3 in clean host user directories using the packaged DLL, and only then emits the ZIP. Results must be read from the completed run; the presence of this document alone is not a PASS claim.

## Still open before a public release

Real VOICEVOX listening, GUI/installation hands-on, wider host/engine combinations, final distribution license choice, and any defects found during those checks. Source fingerprint v0 remains unchanged by this pass. The shared Correction Model has no storage migration.
