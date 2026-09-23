# AGENTS.md

This repository is for the product-side design and implementation of YMM4 Voice Quality Assist.

## Core product boundary

The project has two cooperating tracks:

1. **Local Pronunciation Assist**
   - deterministic
   - lightweight
   - usable without an LLM

2. **Voice Review Bridge**
   - export/import workflow
   - LLM proposes structured corrections
   - product validates and applies them

Do not collapse these into an always-on LLM plugin.

## Engineering rules

### Prefer public YMM4 surfaces

Order of preference:

1. public YMM4 API
2. official control-tag/parser surface
3. normal Plugin/editor service APIs
4. proven stable host surface
5. reflection only with an explicit compatibility rationale
6. Harmony only when the public route has been demonstrated insufficient

Harmony is not a default implementation technique.

### Lab before product assumptions

If YMM4 host behavior is uncertain, validate it in:

- `ziro-lab/chat-native-work-lab-001`

before freezing the product design.

External repositories, docs, and reverse-engineering are **Reference**.  
A focused native Lab run is **Evidence**.

### Evidence labels

Use:

- **PROVEN** — reproducible real-host evidence exists
- **CANDIDATE** — plausible but not yet product-validated
- **BLOCKED** — missing required host surface or environment

Do not silently upgrade CANDIDATE claims to PROVEN.

### Preserve user work

Do not make Serif/Hatsuon or project state a plugin-private data store that the user cannot safely edit.

Prefer:

- declarative markers
- re-resolvable corrections
- source fingerprints
- explicit diff/review
- normal Undo/Redo where possible

Avoid fragile absolute mora indexes when a semantic/declarative locator can be used.

### Opt-in behavior

No assist Effect / disabled assist Effect should mean ordinary YMM4 behavior.

Do not globally change every VoiceItem just because the plugin is installed.

### LLM boundary

The initial LLM integration is a review/proposal system.

LLM output must be structured and validated before project mutation.

Do not execute arbitrary code or arbitrary project-edit instructions from an imported review file.

## Change discipline

Prefer small focused PRs.

A product PR that depends on a newly discovered YMM4 behavior should link the corresponding Lab evidence.

Avoid broad rewrites while host behavior is still being explored.

Keep docs synchronized when a previously CANDIDATE architecture becomes PROVEN or is rejected.
