using DockerinoLamborgino.Core;
using Xunit;

namespace DockerinoLamborgino.Core.Tests.Configuration;

public class DisplayOrderStoreTests
{
    [Fact]
    public void MovesBeforeAndAfterAndIgnoresNoOp()
    {
        var items = new List<string> { "a", "b", "c" };
        Assert.True(DisplayOrderStore.Move(items, "a", 3));
        Assert.Equal(new[] { "b", "c", "a" }, items);
        Assert.True(DisplayOrderStore.Move(items, "a", 0));
        Assert.Equal(new[] { "a", "b", "c" }, items);
        Assert.False(DisplayOrderStore.Move(items, "a", 1));
        Assert.False(DisplayOrderStore.Move(items, "missing", 0));
    }

    [Fact]
    public void PersistsSeparateOrdersAndRetainsTemporarilyMissingItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dockerino-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DisplayOrderStore(directory);
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var c = Guid.NewGuid();
            var project = Guid.NewGuid();
            store.Save([a, b, c]);
            store.Save([c, a]);
            Assert.Equal(new[] { c, b, a }, store.Read());
            Assert.Equal(new[] { c, b, a }, DisplayOrderStore.Apply(new[] { a, b, c }, store.Read(), x => x));
            store.Save([b, a], project);
            Assert.Equal(new[] { b, a }, new DisplayOrderStore(directory).Read(project));
            Assert.Equal(new[] { c, b, a }, store.Read());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void AppendsNewItemsInTheirOriginalOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Assert.Equal(new[] { b, a, c },
            DisplayOrderStore.Apply(new[] { a, b, c }, new[] { b, b }, x => x));
    }
}
