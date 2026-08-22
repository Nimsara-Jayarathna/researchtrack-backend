using System.Net.Http.Json;
using ResearchTrack.AuthService.Configuration;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;

namespace ResearchTrack.AuthService.Infrastructure.Email;

public sealed class BrevoEmailProvider : IEmailProvider
{
    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;

    public BrevoEmailProvider(HttpClient httpClient, EmailOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task SendAsync(string to, string subject, string htmlContent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("accept", "application/json");
        request.Content = JsonContent.Create(new
        {
            sender = new { name = _options.SenderName, email = _options.SenderEmail },
            to = new[] { new { email = to } },
            subject,
            htmlContent
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.ServiceUnavailable,
                "Email service is currently unavailable. Please try again later.");
        }
    }
}
