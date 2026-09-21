using AutoBackup.Core.Formatting;

namespace AutoBackup.Core.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(1024L * 1024 * 1024 * 2, "2 GB")]
    public void Format_ProducesHumanReadableSize(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }
}
