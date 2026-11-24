using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public class SessionOutputHistoryTests
{
    [Fact]
    public void ReaderFailureIsNotReportedAsSuccessfulCompletion()
    {
        var history = new SessionOutputHistory();
        history.Append("partial");
        history.Fail(new IOException("read failed"));
        var error = Assert.Throws<InvalidOperationException>(() => history.Read(0));
        Assert.IsType<IOException>(error.InnerException);
    }

    [Fact]
    public void ReadsSuffixesInUtf16WithoutTruncation()
    {
        var history = new SessionOutputHistory();
        var prefix = new string('x', 2_000_000);
        history.Append(prefix);
        history.Append("😀end");
        Assert.Equal(prefix + "😀end", history.Read(0).Text);
        var suffix = history.Read(prefix.Length);
        Assert.Equal("😀end", suffix.Text);
        Assert.Equal(prefix.Length + 5, suffix.NextOffset);
        Assert.Equal("", history.Read(suffix.NextOffset).Text);
        Assert.False(suffix.Completed);
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Read(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Read(suffix.NextOffset + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Read(long.MaxValue));
        history.Complete();
        Assert.True(history.Read(suffix.NextOffset).Completed);
    }

    [Fact]
    public async Task ConcurrentFollowersObserveAllOutputBeforeCompletion()
    {
        var history = new SessionOutputHistory();
        var followers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            var output = new System.Text.StringBuilder();
            long offset = 0;
            while (true)
            {
                var part = history.Read(offset);
                output.Append(part.Text);
                offset = part.NextOffset;
                if (part.Completed) return output.ToString();
                await Task.Yield();
            }
        })).ToArray();
        for (var i = 0; i < 1000; i++) history.Append("abc");
        history.Complete();
        foreach (var follower in followers)
            Assert.Equal(string.Concat(Enumerable.Repeat("abc", 1000)), await follower);
    }
}
