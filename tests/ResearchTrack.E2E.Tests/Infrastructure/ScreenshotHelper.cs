using OpenQA.Selenium;

namespace ResearchTrack.E2E.Tests.Infrastructure;

internal static class ScreenshotHelper
{
    internal static void Capture(IWebDriver driver, string testName, Exception exception)
    {
        try
        {
            var safeName = string.Concat(testName.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            var directory = Path.Combine(FindRepositoryRoot(), "TestResults", "E2E", safeName);
            Directory.CreateDirectory(directory);

            if (driver is ITakesScreenshot screenshotDriver)
            {
                screenshotDriver.GetScreenshot().SaveAsFile(Path.Combine(directory, "failure.png"));
            }

            File.WriteAllText(
                Path.Combine(directory, "failure.txt"),
                $"Test: {testName}{Environment.NewLine}" +
                $"URL: {SafeUrl(driver)}{Environment.NewLine}" +
                $"UTC: {DateTimeOffset.UtcNow:O}{Environment.NewLine}{Environment.NewLine}" +
                exception);
        }
        catch
        {
            // Test evidence capture must never hide the original test failure.
        }
    }

    private static string SafeUrl(IWebDriver driver)
    {
        try
        {
            return driver.Url;
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ResearchTrack.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
