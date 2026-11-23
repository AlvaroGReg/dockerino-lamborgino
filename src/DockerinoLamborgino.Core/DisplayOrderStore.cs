using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DockerinoLamborgino.Core;

/// <summary>Persists dashboard order independently of project configuration revisions.</summary>
public sealed class DisplayOrderStore(string directory)
{
    private string PathFor(Guid? projectId) => Path.Combine(directory,
        projectId is null ? ".projects.order" : $".actions-{projectId:D}.order");

    public IReadOnlyList<Guid> Read(Guid? projectId = null)
    {
        var path = PathFor(projectId);
        if (!File.Exists(path)) return [];
        return JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("The saved display order is invalid.");
    }

    public void Save(IReadOnlyList<Guid> visibleOrder, Guid? projectId = null)
    {
        ArgumentNullException.ThrowIfNull(visibleOrder);
        if (visibleOrder.Any(id => id == Guid.Empty) || visibleOrder.Distinct().Count() != visibleOrder.Count)
            throw new ArgumentException("Display order must contain distinct non-empty IDs.", nameof(visibleOrder));
        Directory.CreateDirectory(directory);
        var path = PathFor(projectId);
        var name = "DockerinoLamborgino-Order-" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        using var mutex = new Mutex(false, name);
        bool acquired = false;
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Timed out waiting to save display order.");
            // Retain IDs not currently visible, such as temporarily invalid projects.
            var existing = Read(projectId);
            var visible = visibleOrder.ToHashSet();
            var ordered = new Queue<Guid>(visibleOrder);
            var result = new List<Guid>();
            var seen = new HashSet<Guid>();
            foreach (var id in existing)
            {
                if (id == Guid.Empty || !seen.Add(id)) continue;
                if (visible.Contains(id))
                {
                    if (ordered.Count > 0) result.Add(ordered.Dequeue());
                }
                else result.Add(id);
            }
            while (ordered.Count > 0) result.Add(ordered.Dequeue());
            File.WriteAllText(temporary, JsonSerializer.Serialize(result));
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
    }

    public static IReadOnlyList<T> Apply<T>(IEnumerable<T> items, IEnumerable<Guid> order, Func<T, Guid> id)
    {
        var positions = new Dictionary<Guid, int>();
        foreach (var item in order)
            if (!positions.ContainsKey(item)) positions.Add(item, positions.Count);
        return items.OrderBy(item => positions.GetValueOrDefault(id(item), int.MaxValue)).ToArray();
    }

    public static bool Move<T>(IList<T> items, T item, int insertionIndex)
    {
        var oldIndex = items.IndexOf(item);
        if (oldIndex < 0 || insertionIndex < 0 || insertionIndex > items.Count) return false;
        if (insertionIndex > oldIndex) insertionIndex--;
        if (oldIndex == insertionIndex) return false;
        items.RemoveAt(oldIndex);
        items.Insert(insertionIndex, item);
        return true;
    }
}
