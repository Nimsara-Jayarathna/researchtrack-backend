namespace ResearchTrack.SubmissionService.Domain;

public static class SubmissionConstants
{
    public const int RequirementTitleMaxLength = 200;
    public const int RequirementDescriptionMaxLength = 4000;
    public const int AllowedFileTypesMaxLength = 512;
    public const int FileNameMaxLength = 255;
    public const int FileExtensionMaxLength = 16;
    public const int ContentTypeMaxLength = 255;
    public const int ObjectKeyMaxLength = 1024;
    public const int UserDisplayNameMaxLength = 200;
    public const int SubmissionNoteMaxLength = 2000;
    public const int ReviewFeedbackMaxLength = 4000;
    public const int UploadFailureReasonMaxLength = 1000;
    public const int ResponsibilityModeMaxLength = 32;
    public const int SubmitterRoleSnapshotMaxLength = 32;

    public static class ResponsibilityMode
    {
        public const string ProjectLeader = "PROJECT_LEADER";
        public const string AssignedStudent = "ASSIGNED_STUDENT";

        public static bool IsKnown(string value) =>
            value is ProjectLeader or AssignedStudent;
    }

    public static class SubmitterRole
    {
        public const string ProjectLeader = "PROJECT_LEADER";
        public const string AssignedStudent = "ASSIGNED_STUDENT";
    }

    public static class RequirementStatus
    {
        public const string Open = "OPEN";
        public const string Closed = "CLOSED";
        public const string Archived = "ARCHIVED";
    }

    public static class SubmissionStatus
    {
        public const string PendingReview = "PENDING_REVIEW";
        public const string ChangesRequested = "CHANGES_REQUESTED";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
    }

    public static class ReviewDecision
    {
        public const string Approved = "APPROVED";
        public const string ChangesRequested = "CHANGES_REQUESTED";
        public const string Rejected = "REJECTED";

        public static bool IsKnown(string value) =>
            value is Approved or ChangesRequested or Rejected;

        public static bool RequiresFeedback(string value) =>
            value is ChangesRequested or Rejected;
    }

    public static class UploadSessionStatus
    {
        public const string Pending = "PENDING";
        public const string Completed = "COMPLETED";
        public const string Expired = "EXPIRED";
        public const string Failed = "FAILED";
    }
}
