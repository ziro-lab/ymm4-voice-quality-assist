# Candidate compatibility and evidence boundary

## Supported candidate target

Version 0.1.0-candidate.1 is built and tested against YMM4 4.56.1.0 Lite on Windows with .NET 10. The pinned YMM4 ZIP SHA256 is `49c0ed689f545737b7ce939971bfc625962e00791c57883dc8e6f058aa336c5a`.

The local synthesis adapter targets YMM4's built-in `YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker`. Other providers are reported as unsupported and are not fed into the VOICEVOX mutation pipeline.

There is no numeric-version kill switch. A newer host is not disabled solely because its version number changed. Actual missing/throwing host members stop the affected runtime path and report a diagnostic where the plugin can still load. If the host removes a referenced assembly/type so that the DLL itself cannot load, the host's plugin-loader error is the available diagnostic; this plugin cannot display an in-process message before it has loaded.

## Reflection inventory

Product startup resolves the internal MainViewModel by type name and reads only its public `ActiveTimelineViewModel` getter. Public TimelineViewModel/TimelineItemViewModel item access is then strongly typed.

The Assist Effect collection adapter reflects public `JimakuVideoEffects` and public Add/Remove members to support its collection shape. This is not a private-field fallback. The optional Tool receives Timeline and UndoRedoManager through public TimelineToolInfo.

Private-field reflection used by isolated native probes is not included in the shipped DLL as a product acquisition path. No Harmony dependency is used.

## Preserve-work rules

Synthesis works on detached pronunciation data and temporary output. A lease observes source/parameter/effect changes, also checking target membership and output identity before commit. Superseded results are discarded, including when the controller's Timeline was replaced. A pending rescan is retained rather than dropped while a job awaits the engine.

The final WAV is staged in the destination directory before replace/move. An unsuccessful staging copy or stale lease must leave the current file untruncated. This is file-replacement atomicity, not a claim of a filesystem-wide transaction across every host notification.

Disabled/unconfigured items do not trigger repeated plugin regeneration. On a first failed correction, ordinary/manual audio is left untouched. When this controller previously applied a correction, disabling/removing it restores a baseline when the provider is available. A provider failure cannot promise a newly generated baseline; the error is reported.

Import requires exact target resolution, current-source checks, one live target per selected record, and complete preflight. Same-session removal never silently rebinds to an identical clone. A failed rollback is explicitly reported, not described as a successful restore.

## Evidence levels

- Build/unit tests: pure logic plus the pinned YMM4 DLL surface.
- Native tests: real YMM4 processes with deterministic fake VOICEVOX responses. These verify HTTP payloads, state, files, cache, persistence and Undo/Redo.
- Candidate workflow: repeats the native suites on the exact packaged candidate DLL. Its source SHA and DLL digest are recorded in the manifest.
- Hands-on required: real engine/voice acoustic quality, latency with real projects, GUI dialogs/list usability, clean installation on the user's machine.

Passing fake-engine tests does not establish that pronunciation is better or speech sounds natural. No improvement percentage is claimed. No external LLM was called by these tests.

## Distribution boundary

This is a hands-on candidate ZIP, not a published release or an asserted public-distribution license decision. It contains only the product DLL, instructions and provenance. Host libraries, engine binaries, dictionaries, fonts, native probes and user data are excluded. All implementation PRs remain separate from main unless explicitly integrated.
