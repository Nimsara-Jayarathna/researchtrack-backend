using ResearchTrack.SubmissionService.Domain;

namespace ResearchTrack.SubmissionService.MutationTests;

public sealed class SubmissionWorkflowRulesTests
{
    [Theory]
    [InlineData("APPROVED", true)]
    [InlineData("CHANGES_REQUESTED", true)]
    [InlineData("REJECTED", true)]
    [InlineData("PENDING_REVIEW", false)]
    public void ReviewDecision_recognizes_only_formal_decisions(string decision, bool expected)
    {
        Assert.Equal(expected, SubmissionConstants.ReviewDecision.IsKnown(decision));
    }

    [Theory]
    [InlineData("CHANGES_REQUESTED", true)]
    [InlineData("REJECTED", true)]
    [InlineData("APPROVED", false)]
    public void ReviewDecision_feedback_requirement_matches_business_rule(string decision, bool expected)
    {
        Assert.Equal(expected, SubmissionConstants.ReviewDecision.RequiresFeedback(decision));
    }
}
