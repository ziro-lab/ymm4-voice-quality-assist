using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewCorrectionValidatorTests
{
    const string Fingerprint =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void ValidMixedProposal_Passes()
    {
        var proposal = Proposal(
            [
                new SetReadingCorrection(
                    "トウキョウダイガク"),
                new AddBoundaryCorrection(2),
                new HelperVowelZeroCorrection(
                    0,
                    "ウ"),
                new HelperConsonantZeroCorrection(
                    4,
                    "セ"),
                new SetProsodyGestureCorrection(
                    ProsodyGesture.LightRise),
            ]);

        var result =
            ReviewCorrectionValidator.Validate(
                proposal,
                Context(cleanTextLength: 6));

        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors.Select(
                    x => $"{x.Code}: {x.Message}")));
    }

    [Fact]
    public void NoChangeAlone_Passes()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [new NoChangeCorrection()]),
                Context());

        Assert.True(
            result.IsValid);
    }

    [Fact]
    public void WrongSchemaAndSessionAndFingerprint_AreRejected()
    {
        var proposal =
            new ReviewCorrectionProposal(
                "ymm4.voice-corrections.v99",
                "wrong-session",
                "voice-1",
                "sha256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                [new NoChangeCorrection()]);

        var result =
            ReviewCorrectionValidator.Validate(
                proposal,
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .InvalidSchema,
            ReviewCorrectionValidationCode
                .SessionMismatch,
            ReviewCorrectionValidationCode
                .InvalidFingerprintFormat);
    }

    [Fact]
    public void FingerprintMismatch_IsRejected()
    {
        var proposal =
            Proposal(
                [new NoChangeCorrection()])
            with
            {
                SourceFingerprint =
                    "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            };

        var result =
            ReviewCorrectionValidator.Validate(
                proposal,
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .SourceFingerprintMismatch);
    }

    [Fact]
    public void MissingExportRefAndOperations_AreRejected()
    {
        var proposal =
            Proposal([])
            with
            {
                ExportRef = string.Empty,
            };

        var result =
            ReviewCorrectionValidator.Validate(
                proposal,
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .MissingExportRef,
            ReviewCorrectionValidationCode
                .MissingOperations);
    }

    [Fact]
    public void NoChangeCannotMixWithOtherOperations()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new NoChangeCorrection(),
                        new SetProsodyGestureCorrection(
                            ProsodyGesture.Hold),
                    ]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .NoChangeMixedWithOperations);
    }

    [Fact]
    public void MultipleReadingAndProsodyOperations_AreRejected()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new SetReadingCorrection("A"),
                        new SetReadingCorrection("B"),
                        new SetProsodyGestureCorrection(
                            ProsodyGesture.LightRise),
                        new SetProsodyGestureCorrection(
                            ProsodyGesture.LightFall),
                    ]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .MultipleReadingOverrides,
            ReviewCorrectionValidationCode
                .MultipleProsodyGestures);
    }

    [Fact]
    public void EmptyReading_IsRejected()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new SetReadingCorrection(
                            "   "),
                    ]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .EmptyReading);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(7)]
    public void BoundaryMustBeInterior(
        int position)
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new AddBoundaryCorrection(
                            position),
                    ]),
                Context(cleanTextLength: 6));

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .BoundaryPositionOutOfRange);
    }

    [Fact]
    public void DuplicateAndConflictingBoundaryOperations_AreRejected()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new AddBoundaryCorrection(2),
                        new AddBoundaryCorrection(2),
                        new RemoveBoundaryCorrection(2),
                    ]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .DuplicateBoundaryOperation,
            ReviewCorrectionValidationCode
                .ConflictingBoundaryOperation);
    }

    [Fact]
    public void HelperMayTargetStartOrEndBoundary()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new HelperVowelZeroCorrection(
                            0,
                            "ウ"),
                        new HelperConsonantZeroCorrection(
                            6,
                            "セ"),
                    ]),
                Context(cleanTextLength: 6));

        Assert.True(
            result.IsValid);
    }

    [Fact]
    public void HelperOutOfRangeEmptyOrDuplicate_IsRejected()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new HelperVowelZeroCorrection(
                            -1,
                            "ウ"),
                        new HelperConsonantZeroCorrection(
                            2,
                            " "),
                        new HelperVowelZeroCorrection(
                            2,
                            "オ"),
                    ]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .HelperPositionOutOfRange,
            ReviewCorrectionValidationCode
                .EmptyHelper,
            ReviewCorrectionValidationCode
                .DuplicateHelperPosition);
    }

    [Fact]
    public void UnknownOperation_IsRejected()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [new UnknownCorrection()]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .UnknownOperation);
    }

    [Fact]
    public void UndefinedProsodyEnum_IsRejected()
    {
        var result =
            ReviewCorrectionValidator.Validate(
                Proposal(
                    [
                        new SetProsodyGestureCorrection(
                            (ProsodyGesture)999),
                    ]),
                Context());

        AssertCodes(
            result,
            ReviewCorrectionValidationCode
                .UnsupportedProsodyGesture);
    }

    static ReviewCorrectionProposal Proposal(
        IReadOnlyList<
            ReviewCorrectionOperation>
            operations) =>
        new(
            ReviewCorrectionValidator.Schema,
            "session-1",
            "voice-1",
            Fingerprint,
            operations);

    static ReviewCorrectionValidationContext Context(
        int cleanTextLength = 6) =>
        new(
            "session-1",
            Fingerprint,
            cleanTextLength);

    static void AssertCodes(
        ReviewCorrectionValidationResult result,
        params ReviewCorrectionValidationCode[]
            expected)
    {
        Assert.False(
            result.IsValid);

        var actual =
            result.Errors
                .Select(x => x.Code)
                .ToHashSet();

        foreach (var code in expected)
        {
            Assert.Contains(
                code,
                actual);
        }
    }

    sealed record UnknownCorrection
        : ReviewCorrectionOperation;
}
