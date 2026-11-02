using FeedbackIntel.Core.Privacy;

namespace FeedbackIntel.Integration.Tests.Privacy;

/// <summary>
/// The PII test set (FR-PRV-04, <see cref="PiiTestSet"/>): text with known personal information
/// must come back without it, and ordinary issue text must come back unchanged. Runs in CI on every
/// pull request, so a change that weakens redaction can't be merged.
/// </summary>
public class PiiRedactionTests
{
    public static TheoryData<string, string, string> PersonalInformation
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var sample in PiiTestSet.Samples)
            {
                data.Add(sample.Text, sample.Secret, sample.Placeholder);
            }

            return data;
        }
    }

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
