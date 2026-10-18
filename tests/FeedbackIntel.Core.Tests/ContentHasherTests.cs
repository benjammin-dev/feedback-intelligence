using FeedbackIntel.Core.Text;

using Xunit;

namespace FeedbackIntel.Core.Tests;

public class ContentHasherTests
{
    [Fact]
    public void SameText_ProducesSameHash()
    {
        Assert.Equal(
            ContentHasher.Compute("Crash on start", "Steps to reproduce"),
            ContentHasher.Compute("Crash on start", "Steps to reproduce"));
    }

    [Fact]
    public void DifferentBody_ProducesDifferentHash()
    {
        Assert.NotEqual(
            ContentHasher.Compute("Crash on start", "Version 1.2"),
            ContentHasher.Compute("Crash on start", "Version 1.3"));
    }

    [Fact]
    public void WindowsAndUnixLineEndings_ProduceSameHash()
    {
        Assert.Equal(
            ContentHasher.Compute("Title", "line one\r\nline two"),
            ContentHasher.Compute("Title", "line one\nline two"));
    }

    [Fact]
    public void TrailingWhitespace_IsIgnored()
    {
        Assert.Equal(
            ContentHasher.Compute("Title  ", "line one   \nline two\n\n"),
            ContentHasher.Compute("Title", "line one\nline two"));
    }

    [Fact]
    public void NullBody_EqualsEmptyBody()
    {
        Assert.Equal(
            ContentHasher.Compute("Title", null),
            ContentHasher.Compute("Title", ""));
    }

    [Fact]
    public void TitleBodyBoundary_DoesNotCollide()
    {
        Assert.NotEqual(
            ContentHasher.Compute("a b", "c"),
            ContentHasher.Compute("a", "b c"));
    }

    [Fact]
    public void Hash_Is32Bytes()
    {
        Assert.Equal(32, ContentHasher.Compute("Title", "Body").Length);
    }

    [Fact]
    public void NullTitle_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ContentHasher.Compute(null!, "Body"));
    }
}
