using OpenQA.Selenium;

namespace ResearchTrack.Ui.Tests.Utilities;

public static class ScreenshotHelper
{
    public static string? Capture(IWebDriver driver, string testId, string scenario)
    {
        if (driver is not ITakesScreenshot screenshotDriver)
        {
            return null;
        }

        try
        {
            var directory = Path.Combine(FindRepositoryRoot(), "TestResults", "SeleniumScreenshots");
            Directory.CreateDirectory(directory);
            var safeScenario = string.Concat(scenario.Select(character =>
                char.IsLetterOrDigit(character) ? character : '_'));
            var filename = $"{testId}_{safeScenario}_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}.png";
            var path = Path.Combine(directory, filename);
            screenshotDriver.GetScreenshot().SaveAsFile(path);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
