using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public enum ReviewImportBuildStatus
{
    Success,
    InvalidCorrectionPackage,
}

public enum ReviewImportResolutionStatus
{
    ExactSessionMatch,
    ExactFingerprintMatch,
    Stale,
    Missing,
    Ambiguous,
}

public sealed record ReviewImportHelperAddition(
    HelperMoraKind Kind,
    int CleanTextPosition,
    string Helper);

public sealed record ReviewImportPreview(
    string? BeforeHatsuon,
    string? AfterHatsuon,
    IReadOnlyList<int> BeforeBoundaries,
    IReadOnlyList<int> AfterBoundaries,
    IReadOnlyList<SourceFingerprintAssistProfile> BeforeAssistProfiles,
    IReadOnlyList<ReviewImportHelperAddition> HelperAdditions,
    ProsodyGesture? ProposedProsody,
    bool IsNoChange);

public sealed record ReviewImportItem(
    ReviewVoiceExportRecord ExportRecord,
    ReviewCorrectionProposal Proposal,
    ReviewImportResolutionStatus ResolutionStatus,
    VoiceItem? Target,
    ReviewImportPreview? Preview,
    bool SelectedByDefault,
    string? Message)
{
    public bool CanApply =>
        ResolutionStatus
            is ReviewImportResolutionStatus.ExactSessionMatch
            or ReviewImportResolutionStatus.ExactFingerprintMatch;
}

public sealed record ReviewImportPlan(
    ReviewImportBuildStatus Status,
    IReadOnlyList<ReviewImportItem> Items,
    string? Message)
{
    public bool IsSuccess =>
        Status == ReviewImportBuildStatus.Success;
}

public static class ReviewImportPlanner
{
    public static ReviewImportPlan Build(
        ReviewExportPackage exportPackage,
        ReviewCorrectionDecodeResult corrections,
        IReadOnlyList<VoiceItem> currentVoices,
        ReviewExportSession? liveSession = null)
    {
        ArgumentNullException.ThrowIfNull(
            exportPackage);
        ArgumentNullException.ThrowIfNull(
            corrections);
        ArgumentNullException.ThrowIfNull(
            currentVoices);

        if (!corrections.IsSuccess
            || corrections.WirePackage is null)
        {
            return new ReviewImportPlan(
                ReviewImportBuildStatus.InvalidCorrectionPackage,
                [],
                "Correction package must pass B2 wire and export validation before import planning.");
        }

        var byRef =
            exportPackage.Voices.ToDictionary(
                x => x.Target.ExportRef,
                StringComparer.Ordinal);

        var proposals =
            corrections.Proposals.ToDictionary(
                x => x.ExportRef,
                StringComparer.Ordinal);

        var current =
            BuildCurrentCandidates(
                currentVoices);

        var currentSet =
            currentVoices.ToHashSet(
                ReferenceEqualityComparer.Instance);

        var canUseLiveSession =
            liveSession is not null
            && string.Equals(
                liveSession.Package.ExportSessionId,
                exportPackage.ExportSessionId,
                StringComparison.Ordinal);

        var items =
            new List<ReviewImportItem>(
                exportPackage.Voices.Count);

        foreach (var record
            in exportPackage.Voices)
        {
            if (!proposals.TryGetValue(
                record.Target.ExportRef,
                out var proposal))
            {
                return new ReviewImportPlan(
                    ReviewImportBuildStatus.InvalidCorrectionPackage,
                    [],
                    "Validated correction package is missing exportRef "
                    + record.Target.ExportRef
                    + ".");
            }

            if (canUseLiveSession
                && liveSession!.LiveTargets.TryGetValue(
                    record.Target.ExportRef,
                    out var live)
                && currentSet.Contains(live))
            {
                items.Add(
                    BuildSameSessionItem(
                        record,
                        proposal,
                        live));
                continue;
            }

            items.Add(
                BuildCrossSessionItem(
                    record,
                    proposal,
                    current));
        }

        return new ReviewImportPlan(
            ReviewImportBuildStatus.Success,
            items,
            null);
    }

    static ReviewImportItem BuildSameSessionItem(
        ReviewVoiceExportRecord record,
        ReviewCorrectionProposal proposal,
        VoiceItem target)
    {
        if (!SourceFingerprint.TryCreate(
            target,
            out var currentFingerprint,
            out var error)
            || currentFingerprint is null)
        {
            return new ReviewImportItem(
                record,
                proposal,
                ReviewImportResolutionStatus.Stale,
                target,
                null,
                false,
                error
                    ?? "Current VoiceItem source fingerprint could not be created.");
        }

        if (!string.Equals(
            currentFingerprint.Fingerprint,
            record.SourceFingerprint,
            StringComparison.Ordinal))
        {
            return new ReviewImportItem(
                record,
                proposal,
                ReviewImportResolutionStatus.Stale,
                target,
                null,
                false,
                "Same-session VoiceItem source changed after export.");
        }

        return BuildResolvedItem(
            record,
            proposal,
            ReviewImportResolutionStatus.ExactSessionMatch,
            target,
            null);
    }

    static ReviewImportItem BuildCrossSessionItem(
        ReviewVoiceExportRecord record,
        ReviewCorrectionProposal proposal,
        IReadOnlyList<CurrentCandidate> current)
    {
        var locator =
            new ReviewTargetLocator(
                record.Target.Frame,
                record.Target.Layer,
                record.CharacterName,
                record.Context.PreviousSerif,
                record.Context.NextSerif);

        var resolution =
            ReviewTargetResolver.Resolve(
                record.SourceFingerprint,
                locator,
                current
                    .Select(x => x.Candidate)
                    .ToArray());

        var status =
            resolution.Status switch
            {
                ReviewTargetResolutionStatus.ExactFingerprintMatch =>
                    ReviewImportResolutionStatus.ExactFingerprintMatch,
                ReviewTargetResolutionStatus.Stale =>
                    ReviewImportResolutionStatus.Stale,
                ReviewTargetResolutionStatus.Missing =>
                    ReviewImportResolutionStatus.Missing,
                ReviewTargetResolutionStatus.Ambiguous =>
                    ReviewImportResolutionStatus.Ambiguous,
                _ =>
                    throw new ArgumentOutOfRangeException(),
            };

        if (resolution.Item is null)
        {
            return new ReviewImportItem(
                record,
                proposal,
                status,
                null,
                null,
                false,
                resolution.Message);
        }

        if (status
            is ReviewImportResolutionStatus.ExactFingerprintMatch)
        {
            return BuildResolvedItem(
                record,
                proposal,
                status,
                resolution.Item,
                resolution.Message);
        }

        return new ReviewImportItem(
            record,
            proposal,
            status,
            resolution.Item,
            null,
            false,
            resolution.Message);
    }

    static ReviewImportItem BuildResolvedItem(
        ReviewVoiceExportRecord record,
        ReviewCorrectionProposal proposal,
        ReviewImportResolutionStatus status,
        VoiceItem target,
        string? message)
    {
        var preview =
            BuildPreview(
                target,
                proposal);

        var noChange =
            proposal.Operations.Count == 1
            && proposal.Operations[0]
                is NoChangeCorrection;

        return new ReviewImportItem(
            record,
            proposal,
            status,
            target,
            preview,
            !noChange,
            message);
    }

    static ReviewImportPreview BuildPreview(
        VoiceItem target,
        ReviewCorrectionProposal proposal)
    {
        var controls =
            BoundaryMarkerParser.Parse(
                target.Serif
                ?? string.Empty);

        var boundaries =
            controls.ZeroWaitPositions
                .ToHashSet();

        foreach (var remove
            in proposal.Operations
                .OfType<RemoveBoundaryCorrection>())
        {
            boundaries.Remove(
                remove.CleanTextPosition);
        }

        foreach (var add
            in proposal.Operations
                .OfType<AddBoundaryCorrection>())
        {
            boundaries.Add(
                add.CleanTextPosition);
        }

        var reading =
            proposal.Operations
                .OfType<SetReadingCorrection>()
                .SingleOrDefault()
                ?.Reading
            ?? target.Hatsuon;

        var helperAdditions =
            proposal.Operations
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

        var proposedProsody =
            proposal.Operations
                .OfType<SetProsodyGestureCorrection>()
                .SingleOrDefault()
                ?.Gesture;

        var profiles =
            SourceFingerprint.TryCreateInput(
                target,
                out var input,
                out _)
                && input is not null
                ? input.AssistProfiles
                : [];

        var noChange =
            proposal.Operations.Count == 1
            && proposal.Operations[0]
                is NoChangeCorrection;

        return new ReviewImportPreview(
            target.Hatsuon,
            reading,
            controls.ZeroWaitPositions
                .OrderBy(x => x)
                .ToArray(),
            boundaries
                .OrderBy(x => x)
                .ToArray(),
            profiles.ToArray(),
            helperAdditions,
            proposedProsody,
            noChange);
    }

    static IReadOnlyList<CurrentCandidate>
        BuildCurrentCandidates(
            IReadOnlyList<VoiceItem> voices)
    {
        var ordered =
            voices
                .Select(
                    (voice, index) =>
                        new
                        {
                            Voice = voice
                                ?? throw new ArgumentException(
                                    "Current Voice collection contains null.",
                                    nameof(voices)),
                            SourceIndex = index,
                        })
                .OrderBy(x => x.Voice.Frame)
                .ThenBy(x => x.Voice.Layer)
                .ThenBy(x => x.SourceIndex)
                .Select(x => x.Voice)
                .ToArray();

        var result =
            new List<CurrentCandidate>(
                ordered.Length);

        for (var index = 0;
             index < ordered.Length;
             index++)
        {
            var voice =
                ordered[index];

            var previous =
                index > 0
                    ? ordered[index - 1].Serif
                    : null;

            var next =
                index + 1 < ordered.Length
                    ? ordered[index + 1].Serif
                    : null;

            var fingerprint =
                SourceFingerprint.TryCreate(
                    voice,
                    out var created,
                    out _)
                && created is not null
                    ? created.Fingerprint
                    : "invalid-current-source:"
                        + index.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);

            result.Add(
                new CurrentCandidate(
                    new ReviewTargetCandidate<VoiceItem>(
                        voice,
                        fingerprint,
                        voice.Frame,
                        voice.Layer,
                        voice.CharacterName,
                        previous,
                        next)));
        }

        return result;
    }

    sealed record CurrentCandidate(
        ReviewTargetCandidate<VoiceItem> Candidate);
}
