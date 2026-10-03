using System.ComponentModel.DataAnnotations;
using ResearchTrack.MeetingService.Contracts;

namespace ResearchTrack.MeetingService.Tests.Features;

public sealed class MeetingContractValidationTests
{
    [Fact]
    public void Meeting_record_validation_metadata_is_attached_to_record_constructor_parameters()
    {
        var constructor = Assert.Single(typeof(MeetingRecordUpsertRequest).GetConstructors());
        var parameters = constructor.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.NotEmpty(parameters["MeetingDate"].GetCustomAttributes(typeof(RequiredAttribute), false));
        Assert.NotEmpty(parameters["DurationMinutes"].GetCustomAttributes(typeof(RangeAttribute), false));
        Assert.NotEmpty(parameters["DiscussionSummary"].GetCustomAttributes(typeof(RequiredAttribute), false));
    }

    [Fact]
    public void Meeting_channel_validation_metadata_is_attached_to_record_constructor_parameters()
    {
        var constructor = Assert.Single(typeof(MeetingChannelCreateRequest).GetConstructors());
        var parameters = constructor.GetParameters().ToDictionary(parameter => parameter.Name!);

        Assert.NotEmpty(parameters["Platform"].GetCustomAttributes(typeof(RequiredAttribute), false));
        Assert.NotEmpty(parameters["ChannelName"].GetCustomAttributes(typeof(RequiredAttribute), false));
        Assert.NotEmpty(parameters["LinkOrIdentifier"].GetCustomAttributes(typeof(RequiredAttribute), false));
    }
}
