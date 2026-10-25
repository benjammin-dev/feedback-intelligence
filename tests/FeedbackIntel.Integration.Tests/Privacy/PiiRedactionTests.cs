namespace FeedbackIntel.Integration.Tests.Privacy;

/// <summary>
/// The PII test set (FR-PRV-04): text with known personal information must come back
/// without it, and ordinary issue text must come back unchanged. Runs in CI on every
/// pull request, so a change that weakens redaction can't be merged.
/// </summary>
public class PiiRedactionTests
{
    // Built at runtime so this file never contains anything that looks like a real token.
    private static readonly string ClassicToken = "ghp_" + new string('A', 36);
    private static readonly string FineGrainedToken = "github_pat_" + new string('B', 22) + "_" + new string('c', 59);

    public static TheoryData<string, string, string> PersonalInformation => new()
    {
        { "Please email logs to jane.doe@example.com when you can", "jane.doe@example.com", "<EMAIL_ADDRESS>" },
        { "The proxy at 192.168.1.20 refused the connection", "192.168.1.20", "<IP_ADDRESS>" },
        { "Listening on fe80::1ff:fe23:4567:890a failed", "fe80::1ff:fe23:4567:890a", "<IP_ADDRESS>" },
        { $"I set GH_TOKEN={ClassicToken} and it still fails", ClassicToken, "<GITHUB_TOKEN>" },
        { $"Token {FineGrainedToken} was rejected", FineGrainedToken, "<GITHUB_TOKEN>" },
        { @"Log written to C:\Users\jsmith\AppData\Local\Temp\WinGet\defaultState.log", "jsmith", "<PATH_USERNAME>" },
        { @"""path"": ""C:\\Users\\jsmith\\Downloads\\setup.exe""", "jsmith", "<PATH_USERNAME>" },
        { "Config is in /home/jsmith/.config/app/settings.json", "jsmith", "<PATH_USERNAME>" },
        { "Crash report saved to /Users/jsmith/Library/Logs/report.txt", "jsmith", "<PATH_USERNAME>" },
    };

    [Theory]
    [MemberData(nameof(PersonalInformation))]
    public async Task RedactAsync_PersonalInformation_IsReplaced(string text, string secret, string placeholder)
    {
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        var result = await redactor.RedactAsync(text, CancellationToken.None);

        Assert.DoesNotContain(secret, result.Text);
        Assert.Contains(placeholder, result.Text);
        Assert.True(result.RedactionCount >= 1);
    }

    [Fact]
    public async Task RedactAsync_WindowsPath_KeepsRestOfPath()
    {
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        var result = await redactor.RedactAsync(@"See C:\Users\jsmith\AppData\Local\log.txt", CancellationToken.None);

        Assert.Equal(@"See C:\Users\<PATH_USERNAME>\AppData\Local\log.txt", result.Text);
    }

    [Fact]
    public async Task RedactAsync_EmailInsideText_IsNotAlsoTreatedAsLink()
    {
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        var result = await redactor.RedactAsync("Contact john.smith@contoso.com for access", CancellationToken.None);

        Assert.Equal("Contact <EMAIL_ADDRESS> for access", result.Text);
        Assert.Equal(1, result.RedactionCount);
    }

    [Fact]
    public async Task RedactAsync_SeveralItems_CountsEach()
    {
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        var result = await redactor.RedactAsync(
            @"From a@example.com and b@example.com on 10.0.0.5, see C:\Users\jsmith\log.txt", CancellationToken.None);

        Assert.Equal(4, result.RedactionCount);
    }

    [Theory]
    [InlineData("winget upgrade --id Microsoft.PowerToys fails with 0x8a15000f on version 1.21.2771.0")]
    [InlineData("See https://github.com/microsoft/winget-cli/issues/4521 for the same crash")]
    [InlineData("PowerToys Run stops responding after the 0.95 update on Windows 11 24H2")]
    [InlineData(@"Installed to C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller")]
    public async Task RedactAsync_OrdinaryIssueText_IsUnchanged(string text)
    {
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        var result = await redactor.RedactAsync(text, CancellationToken.None);

        Assert.Equal(text, result.Text);
        Assert.Equal(0, result.RedactionCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RedactAsync_EmptyText_ReturnsItUnchanged(string text)
    {
        var redactor = await PresidioTestServices.CreateRedactorAsync();

        var result = await redactor.RedactAsync(text, CancellationToken.None);

        Assert.Equal(text, result.Text);
        Assert.Equal(0, result.RedactionCount);
    }
}
