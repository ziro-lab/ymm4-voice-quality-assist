using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public enum ReviewImportPrepareStatus
{
    Success,
    NoSelection,
    InvalidSelection,
    PreflightFailed,
}

public sealed record ReviewImportPrepareResult(
    ReviewImportPrepareStatus Status,
    ReviewImportJournal? Journal,
    string? FailedExportRef,
    string? Message)
{
    public bool IsSuccess =>
        Status == ReviewImportPrepareStatus.Success
        && Journal is not null;
}

public enum ReviewImportCommitStatus
{
    Success,
    AlreadyCommitted,
    SourceChanged,
    ApplyFailed,
}

public sealed record ReviewImportCommitResult(
    ReviewImportCommitStatus Status,
    string? FailedExportRef,
    string? Message)
{
    public bool IsSuccess =>
        Status == ReviewImportCommitStatus.Success;
}

public sealed class ReviewImportJournal
{
    readonly IReadOnlyList<PreparedVoiceChange> changes;
    bool committed;

    internal ReviewImportJournal(
        IReadOnlyList<PreparedVoiceChange> changes)
    {
        this.changes =
            changes
            ?? throw new ArgumentNullException(
                nameof(changes));
    }

    public IReadOnlyList<string> ExportRefs =>
        changes
            .Select(x => x.ExportRef)
            .ToArray();

    public ReviewImportCommitResult Commit()
    {
        if (committed)
        {
            return new ReviewImportCommitResult(
                ReviewImportCommitStatus.AlreadyCommitted,
                null,
                "Import journal has already been committed.");
        }

        foreach (var change in changes)
        {
            if (!change.MatchesBefore(
                out var reason))
            {
                return new ReviewImportCommitResult(
                    ReviewImportCommitStatus.SourceChanged,
                    change.ExportRef,
                    reason
                        ?? "VoiceItem changed after import preview.");
            }
        }

        var touched =
            new List<PreparedVoiceChange>();

        try
        {
            foreach (var change in changes)
            {
                touched.Add(change);
                change.ApplyAfterOrThrow();
            }
        }
        catch (Exception ex)
        {
            for (var index =
                    touched.Count - 1;
                 index >= 0;
                 index--)
            {
                try
                {
                    touched[index]
                        .ApplyBeforeOrThrow();
                }
                catch
                {
                    // Best-effort rollback. The original apply error remains
                    // the primary failure; native acceptance covers this path.
                }
            }

            return new ReviewImportCommitResult(
                ReviewImportCommitStatus.ApplyFailed,
                touched.LastOrDefault()?.ExportRef,
                ex.GetBaseException().Message);
        }

        committed = true;

        return new ReviewImportCommitResult(
            ReviewImportCommitStatus.Success,
            null,
            null);
    }

    public void UndoOrThrow()
    {
        if (!committed)
        {
            throw new InvalidOperationException(
                "Import journal has not been committed.");
        }

        for (var index =
                changes.Count - 1;
             index >= 0;
             index--)
        {
            changes[index]
                .ApplyBeforeOrThrow();
        }
    }

    public void RedoOrThrow()
    {
        if (!committed)
        {
            throw new InvalidOperationException(
                "Import journal has not been committed.");
        }

        foreach (var change in changes)
        {
            change.ApplyAfterOrThrow();
        }
    }
}

public static class ReviewImportApplier
{
    public static ReviewImportPrepareResult Prepare(
        ReviewImportPlan plan,
        IReadOnlyCollection<string> selectedExportRefs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(
            selectedExportRefs);

        if (!plan.IsSuccess)
        {
            return Failure(
                ReviewImportPrepareStatus.InvalidSelection,
                null,
                "Import plan is not valid.");
        }

        var selected =
            selectedExportRefs
                .ToHashSet(
                    StringComparer.Ordinal);

        if (selected.Count == 0)
        {
            return Failure(
                ReviewImportPrepareStatus.NoSelection,
                null,
                "No review corrections are selected.");
        }

        var byRef =
            plan.Items.ToDictionary(
                x => x.ExportRecord
                    .Target.ExportRef,
                StringComparer.Ordinal);

        foreach (var exportRef in selected)
        {
            if (!byRef.TryGetValue(
                exportRef,
                out var item))
            {
                return Failure(
                    ReviewImportPrepareStatus.InvalidSelection,
                    exportRef,
                    "Selected exportRef is not present in the import plan.");
            }

            if (!item.CanApply
                || item.Target is null
                || item.Preview is null)
            {
                return Failure(
                    ReviewImportPrepareStatus.InvalidSelection,
                    exportRef,
                    "Only exact resolved review items can be selected for apply.");
            }
        }

        var prepared =
            new List<PreparedVoiceChange>();

        foreach (var item
            in plan.Items.Where(x =>
                selected.Contains(
                    x.ExportRecord.Target.ExportRef)))
        {
            if (item.Proposal.Operations.Count == 1
                && item.Proposal.Operations[0]
                    is NoChangeCorrection)
            {
                continue;
            }

            if (!TryPrepareChange(
                item,
                out var change,
                out var error)
                || change is null)
            {
                return Failure(
                    ReviewImportPrepareStatus.PreflightFailed,
                    item.ExportRecord.Target.ExportRef,
                    error
                        ?? "Import preflight failed.");
            }

            if (change.HasChanges)
                prepared.Add(change);
        }

        if (prepared.Count == 0)
        {
            return Failure(
                ReviewImportPrepareStatus.NoSelection,
                null,
                "Selected review records contain no durable changes.");
        }

        return new ReviewImportPrepareResult(
            ReviewImportPrepareStatus.Success,
            new ReviewImportJournal(
                prepared),
            null,
            null);
    }

    static bool TryPrepareChange(
        ReviewImportItem item,
        out PreparedVoiceChange? change,
        out string? error)
    {
        change = null;
        error = null;

        var voice =
            item.Target
            ?? throw new InvalidOperationException(
                "Exact import item has no VoiceItem target.");

        if (!SourceFingerprint.TryCreate(
            voice,
            out var currentFingerprint,
            out var fingerprintError)
            || currentFingerprint is null)
        {
            error =
                fingerprintError
                ?? "Current source fingerprint could not be created.";
            return false;
        }

        if (!string.Equals(
            currentFingerprint.Fingerprint,
            item.ExportRecord.SourceFingerprint,
            StringComparison.Ordinal))
        {
            error =
                "Current VoiceItem source no longer matches the import preview.";
            return false;
        }

        var beforeSerif =
            voice.Serif;

        var beforeHatsuon =
            voice.Hatsuon;

        var afterSerif =
            beforeSerif;

        var boundaryOperations =
            item.Proposal.Operations
                .Where(x =>
                    x is AddBoundaryCorrection
                    or RemoveBoundaryCorrection)
                .ToArray();

        BoundaryMarkerParseResult controls;

        try
        {
            controls =
                BoundaryMarkerParser.Parse(
                    beforeSerif
                    ?? string.Empty);
        }
        catch (Exception ex)
        {
            error =
                "Current Serif control tags could not be parsed: "
                + ex.GetBaseException().Message;
            return false;
        }

        IReadOnlyList<int> afterBoundaries =
            controls.ZeroWaitPositions
                .OrderBy(x => x)
                .ToArray();

        if (boundaryOperations.Length > 0)
        {
            var edited =
                ReviewBoundaryEditor.Apply(
                    beforeSerif
                    ?? string.Empty,
                    boundaryOperations);

            if (!edited.IsSuccess)
            {
                error =
                    edited.Message
                    ?? "Boundary edit failed.";
                return false;
            }

            afterSerif =
                edited.Serif;

            afterBoundaries =
                edited.Boundaries;
        }

        var afterHatsuon =
            item.Proposal.Operations
                .OfType<SetReadingCorrection>()
                .SingleOrDefault()
                ?.Reading
            ?? beforeHatsuon;

        var assistEffects =
            ReviewAssistEffectCollection
                .Enumerate(voice)
                .ToArray();

        var enabledEffects =
            assistEffects
                .Where(x => x.IsEnabled)
                .ToArray();

        var helperOperations =
            item.Proposal.Operations
                .Select(
                    operation =>
                        operation switch
                        {
                            HelperVowelZeroCorrection helper =>
                                new ReviewImportHelperAddition(
                                    HelperMoraKind.ZeroVowel,
                                    helper.CleanTextPosition,
                                    helper.Helper),

                            HelperConsonantZeroCorrection helper =>
                                new ReviewImportHelperAddition(
                                    HelperMoraKind.ZeroConsonant,
                                    helper.CleanTextPosition,
                                    helper.Helper),

                            _ => null,
                        })
                .Where(x => x is not null)
                .Cast<ReviewImportHelperAddition>()
                .ToArray();

        var prosodyOperation =
            item.Proposal.Operations
                .OfType<SetProsodyGestureCorrection>()
                .SingleOrDefault();

        var transitions =
            new Dictionary<
                PronunciationAssistEffect,
                EffectTransition>(
                    ReferenceEqualityComparer.Instance);

        var decodedRules =
            new Dictionary<
                PronunciationAssistEffect,
                HelperRuleSet>(
                    ReferenceEqualityComparer.Instance);

        var allEnabledRules =
            new List<HelperMoraRule>();

        foreach (var effect in enabledEffects)
        {
            if (!HelperRuleCodec.TryDecode(
                effect.HelperRulesJson,
                out var decoded,
                out var decodeError))
            {
                error =
                    decodeError
                    ?? "Enabled Assist Effect contains invalid helper rules.";
                return false;
            }

            decodedRules.Add(
                effect,
                decoded);

            allEnabledRules.AddRange(
                decoded.Rules);
        }

        var existingHelperPositions =
            new HashSet<int>();

        if (allEnabledRules.Count > 0)
        {
            var resolved =
                HelperAnchorResolver.Resolve(
                    controls.CleanText,
                    allEnabledRules);

            if (!resolved.IsSuccess)
            {
                error =
                    resolved.Message
                    ?? "Existing helper anchors could not be resolved.";
                return false;
            }

            foreach (var duplicate
                in resolved.Anchors
                    .GroupBy(x => x.Position)
                    .Where(x => x.Count() > 1))
            {
                error =
                    $"Existing helper rules collide at clean-text boundary {duplicate.Key}.";
                return false;
            }

            existingHelperPositions.UnionWith(
                resolved.Anchors.Select(
                    x => x.Position));
        }

        foreach (var position
            in existingHelperPositions)
        {
            if (afterBoundaries.Contains(
                position))
            {
                error =
                    $"Existing helper and <w0> boundary collide at clean-text position {position}.";
                return false;
            }
        }

        foreach (var helper
            in helperOperations)
        {
            if (existingHelperPositions.Contains(
                helper.CleanTextPosition))
            {
                error =
                    $"A helper already resolves to clean-text boundary {helper.CleanTextPosition}.";
                return false;
            }

            if (afterBoundaries.Contains(
                helper.CleanTextPosition))
            {
                error =
                    $"Helper and <w0> boundary cannot share clean-text position {helper.CleanTextPosition}.";
                return false;
            }
        }

        PronunciationAssistEffect? primary =
            enabledEffects.FirstOrDefault();

        var requiresPrimary =
            helperOperations.Length > 0
            || (
                prosodyOperation is not null
                && prosodyOperation.Gesture
                    != ProsodyGesture.None
            );

        if (requiresPrimary
            && primary is null)
        {
            primary =
                new PronunciationAssistEffect
                {
                    IsEnabled = true,
                    HelperRulesJson =
                        string.Empty,
                    Prosody =
                        ProsodyGesture.None,
                };

            transitions.Add(
                primary,
                EffectTransition.ForNew(
                    primary));
        }

        if (helperOperations.Length > 0)
        {
            if (primary is null)
            {
                error =
                    "Helper correction has no Assist Effect target.";
                return false;
            }

            var primaryRules =
                decodedRules.TryGetValue(
                    primary,
                    out var existing)
                    ? existing.Rules.ToList()
                    : new List<HelperMoraRule>();

            foreach (var helper
                in helperOperations)
            {
                primaryRules.Add(
                    HelperRuleFactory.Create(
                        controls.CleanText,
                        helper.CleanTextPosition,
                        helper.Helper,
                        helper.Kind));
            }

            var transition =
                GetOrCreateTransition(
                    primary,
                    transitions,
                    presentBefore:
                        ReviewAssistEffectCollection.Contains(
                            voice,
                            primary));

            transition.After =
                transition.After with
                {
                    HelperRulesJson =
                        HelperRuleCodec.Encode(
                            new HelperRuleSet(
                                HelperRuleSet.CurrentVersion,
                                primaryRules)),
                };
        }

        if (prosodyOperation is not null)
        {
            foreach (var effect
                in enabledEffects)
            {
                var transition =
                    GetOrCreateTransition(
                        effect,
                        transitions,
                        presentBefore: true);

                transition.After =
                    transition.After with
                    {
                        Prosody =
                            ProsodyGesture.None,
                    };
            }

            if (prosodyOperation.Gesture
                    != ProsodyGesture.None)
            {
                if (primary is null)
                {
                    error =
                        "Prosody correction has no Assist Effect target.";
                    return false;
                }

                var transition =
                    GetOrCreateTransition(
                        primary,
                        transitions,
                        presentBefore:
                            ReviewAssistEffectCollection.Contains(
                                voice,
                                primary));

                transition.After =
                    transition.After with
                    {
                        Prosody =
                            prosodyOperation.Gesture,
                    };
            }
        }

        change =
            new PreparedVoiceChange(
                item.ExportRecord.Target.ExportRef,
                voice,
                currentFingerprint.Fingerprint,
                beforeSerif,
                afterSerif,
                beforeHatsuon,
                afterHatsuon,
                transitions.Values
                    .ToArray());

        return true;
    }

    static EffectTransition GetOrCreateTransition(
        PronunciationAssistEffect effect,
        Dictionary<
            PronunciationAssistEffect,
            EffectTransition> transitions,
        bool presentBefore)
    {
        if (transitions.TryGetValue(
            effect,
            out var existing))
        {
            return existing;
        }

        var before =
            new AssistEffectState(
                presentBefore,
                effect.IsEnabled,
                effect.HelperRulesJson,
                effect.Prosody);

        var transition =
            new EffectTransition(
                effect,
                before,
                before);

        transitions.Add(
            effect,
            transition);

        return transition;
    }

    static ReviewImportPrepareResult Failure(
        ReviewImportPrepareStatus status,
        string? exportRef,
        string message) =>
        new(
            status,
            null,
            exportRef,
            message);
}

internal sealed class PreparedVoiceChange
{
    readonly VoiceItem voice;
    readonly string expectedFingerprint;
    readonly string? beforeSerif;
    readonly string? afterSerif;
    readonly string? beforeHatsuon;
    readonly string? afterHatsuon;
    readonly IReadOnlyList<EffectTransition> effects;

    public PreparedVoiceChange(
        string exportRef,
        VoiceItem voice,
        string expectedFingerprint,
        string? beforeSerif,
        string? afterSerif,
        string? beforeHatsuon,
        string? afterHatsuon,
        IReadOnlyList<EffectTransition> effects)
    {
        ExportRef =
            exportRef
            ?? throw new ArgumentNullException(
                nameof(exportRef));

        this.voice =
            voice
            ?? throw new ArgumentNullException(
                nameof(voice));

        this.expectedFingerprint =
            expectedFingerprint
            ?? throw new ArgumentNullException(
                nameof(expectedFingerprint));

        this.beforeSerif = beforeSerif;
        this.afterSerif = afterSerif;
        this.beforeHatsuon = beforeHatsuon;
        this.afterHatsuon = afterHatsuon;
        this.effects = effects;
    }

    public string ExportRef { get; }

    public bool HasChanges =>
        !string.Equals(
            beforeSerif,
            afterSerif,
            StringComparison.Ordinal)
        || !string.Equals(
            beforeHatsuon,
            afterHatsuon,
            StringComparison.Ordinal)
        || effects.Any(x =>
            x.Before != x.After);

    public bool MatchesBefore(
        out string? reason)
    {
        if (!SourceFingerprint.TryCreate(
            voice,
            out var fingerprint,
            out var error)
            || fingerprint is null)
        {
            reason =
                error
                ?? "Current fingerprint is unavailable.";
            return false;
        }

        if (!string.Equals(
            fingerprint.Fingerprint,
            expectedFingerprint,
            StringComparison.Ordinal))
        {
            reason =
                "VoiceItem fingerprint changed after import preview.";
            return false;
        }

        if (!string.Equals(
            voice.Serif,
            beforeSerif,
            StringComparison.Ordinal)
            || !string.Equals(
                voice.Hatsuon,
                beforeHatsuon,
                StringComparison.Ordinal))
        {
            reason =
                "VoiceItem Serif/Hatsuon changed after import preview.";
            return false;
        }

        foreach (var effect
            in effects)
        {
            if (!effect.Matches(
                voice,
                effect.Before))
            {
                reason =
                    "Assist Effect state changed after import preview.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public void ApplyAfterOrThrow() =>
        ApplyOrThrow(
            afterSerif,
            afterHatsuon,
            after: true);

    public void ApplyBeforeOrThrow() =>
        ApplyOrThrow(
            beforeSerif,
            beforeHatsuon,
            after: false);

    void ApplyOrThrow(
        string? serif,
        string? hatsuon,
        bool after)
    {
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;

        foreach (var effect
            in effects)
        {
            effect.ApplyOrThrow(
                voice,
                after
                    ? effect.After
                    : effect.Before);
        }
    }
}

internal sealed record AssistEffectState(
    bool Present,
    bool IsEnabled,
    string HelperRulesJson,
    ProsodyGesture Prosody);

internal sealed class EffectTransition
{
    public EffectTransition(
        PronunciationAssistEffect effect,
        AssistEffectState before,
        AssistEffectState after)
    {
        Effect = effect;
        Before = before;
        After = after;
    }

    public PronunciationAssistEffect Effect { get; }
    public AssistEffectState Before { get; }
    public AssistEffectState After { get; set; }

    public static EffectTransition ForNew(
        PronunciationAssistEffect effect)
    {
        var absent =
            new AssistEffectState(
                false,
                effect.IsEnabled,
                effect.HelperRulesJson,
                effect.Prosody);

        var present =
            absent with
            {
                Present = true,
            };

        return new EffectTransition(
            effect,
            absent,
            present);
    }

    public bool Matches(
        VoiceItem voice,
        AssistEffectState state)
    {
        var present =
            ReviewAssistEffectCollection.Contains(
                voice,
                Effect);

        if (present != state.Present)
            return false;

        if (!state.Present)
            return true;

        return Effect.IsEnabled
                == state.IsEnabled
            && string.Equals(
                Effect.HelperRulesJson,
                state.HelperRulesJson,
                StringComparison.Ordinal)
            && Effect.Prosody
                == state.Prosody;
    }

    public void ApplyOrThrow(
        VoiceItem voice,
        AssistEffectState state)
    {
        // Configure before exposing a newly-created Effect to the VoiceItem
        // so the controller never observes a half-configured new source.
        Effect.IsEnabled =
            state.IsEnabled;

        Effect.HelperRulesJson =
            state.HelperRulesJson;

        Effect.Prosody =
            state.Prosody;

        var ok =
            state.Present
                ? ReviewAssistEffectCollection.TryAdd(
                    voice,
                    Effect,
                    out var error)
                : ReviewAssistEffectCollection.TryRemove(
                    voice,
                    Effect,
                    out error);

        if (!ok)
        {
            throw new InvalidOperationException(
                error
                ?? "Assist Effect membership update failed.");
        }
    }
}
