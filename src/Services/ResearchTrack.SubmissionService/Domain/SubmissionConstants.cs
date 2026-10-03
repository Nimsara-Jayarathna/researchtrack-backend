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
    public const int UploadFailureReasonMaxLength = 1000;

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

    public static class UploadSessionStatus
    {
        public const string Pending = "PENDING";
        public const string Completed = "COMPLETED";
        public const string Expired = "EXPIRED";
        public const string Failed = "FAILED";
    }
}
