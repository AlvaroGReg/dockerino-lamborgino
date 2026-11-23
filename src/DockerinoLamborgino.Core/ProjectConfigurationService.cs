using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace DockerinoLamborgino.Core;

public sealed class ProjectConfigurationService
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string StorageDirectory { get; }

    public ProjectConfigurationService(string? storageDirectory = null)
    {
        StorageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DockerinoLamborgino", "projects");
    }

    public static ProjectConfiguration Create(string name, string directory) =>
        new() { Id = Guid.NewGuid(), Name = name, Directory = directory };

    public IReadOnlyList<ProjectLoadResult> List()
    {
        try
        {
            return System.IO.Directory.EnumerateFiles(StorageDirectory, "*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(LoadStoredFile).ToArray();
        }
        catch (DirectoryNotFoundException) { return []; }
        catch (Exception ex) when (IsStorageError(ex))
        {
            return [new(StorageDirectory, null, [new("$", ex.Message)])];
        }
    }

    public ProjectLoadResult Get(Guid id) => id == Guid.Empty
        ? new(StorageDirectory, null, [new("id", "A non-empty project ID is required.")])
        : LoadStoredFile(FilePath(id));

    private ProjectLoadResult LoadStoredFile(string path)
    {
        var result = LoadFile(path);
        if (result.Project is not null &&
            (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "D", out var fileId) ||
             fileId != result.Project.Id))
            return result with { Issues = result.Issues.Append(new ConfigurationIssue("id", "Project ID does not match its storage filename.")).ToArray() };
        return result;
    }

    public ProjectLoadResult LoadFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var revision = Convert.ToHexString(SHA256.HashData(bytes));
            var project = JsonSerializer.Deserialize<ProjectConfiguration>(bytes, JsonOptions);
            if (project is null) return new(path, null, [new("$", "Expected a project object.")]);
            var issues = Validate(project).ToList();
            return new(path, project, issues, revision);
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is JsonException)
        {
            return new(path, null, [new("$", ex.Message)]);
        }
    }

    // A null revision means create only; updates must supply the revision returned by Get/List.
    public IReadOnlyList<ConfigurationIssue> Save(ProjectConfiguration? project, string? expectedRevision = null)
    {
        var issues = Validate(project);
        if (issues.Count != 0 || project is null) return issues;
        try
        {
            System.IO.Directory.CreateDirectory(StorageDirectory);
            string path = FilePath(project.Id);
            string temporary = Path.Combine(StorageDirectory, $".{Guid.NewGuid():N}.tmp");
            string lockName = "DockerinoLamborgino-" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
            using var mutex = new Mutex(false, lockName);
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) return [new("$", "Timed out waiting for another writer.")];
                if (File.Exists(path))
                {
                    var currentRevision = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                    if (expectedRevision is null || !string.Equals(expectedRevision, currentRevision, StringComparison.Ordinal))
                        return [new("$", "Project changed or already exists; reload it before saving.")];
                }
                else if (expectedRevision is not null)
                    return [new("$", "Project no longer exists; reload before saving.")];
                File.WriteAllText(temporary, JsonSerializer.Serialize(project, JsonOptions));
                File.Move(temporary, path, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
        catch (Exception ex) when (IsStorageError(ex))
        {
            return [new("$", ex.Message)];
        }
        return issues;
    }

    public IReadOnlyList<ConfigurationIssue> Validate(ProjectConfiguration? project)
    {
        if (project is null) return [new("$", "A project is required.")];
        var issues = new List<ConfigurationIssue>();
        var ids = new HashSet<Guid>();
        void Id(Guid id, string path)
        {
            if (id == Guid.Empty) issues.Add(new(path, "A non-empty ID is required."));
            else if (!ids.Add(id)) issues.Add(new(path, "IDs must be unique within a project."));
        }
        void Required(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value)) issues.Add(new(path, "A value is required."));
        }
        void DirectoryPath(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value)) issues.Add(new(path, "A directory is required."));
            else if (!Path.IsPathFullyQualified(value) || !System.IO.Directory.Exists(value))
                issues.Add(new(path, "An existing absolute directory is required."));
        }
        Id(project.Id, "id");
        Required(project.Name, "name");
        DirectoryPath(project.Directory, "directory");
        if (project.Repositories is null) issues.Add(new("repositories", "An array is required."));
        else
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < project.Repositories.Count; r++)
            {
                var value = project.Repositories[r];
                string path = $"repositories[{r}]";
                DirectoryPath(value, path);
                if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value))
                {
                    try
                    {
                        if (!paths.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(value))))
                            issues.Add(new(path, "Repository paths must be unique within a project."));
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        issues.Add(new(path, "A valid absolute directory is required."));
                    }
                }
            }
        }
        if (project.Actions is null) { issues.Add(new("actions", "An array is required.")); return issues; }
        for (int a = 0; a < project.Actions.Count; a++)
        {
            string ap = $"actions[{a}]";
            var action = project.Actions[a];
            if (action is null) { issues.Add(new(ap, "An action is required.")); continue; }
            Id(action.Id, $"{ap}.id");
            Required(action.Name, $"{ap}.name");
            if (action.Description is null) issues.Add(new($"{ap}.description", "Description cannot be null."));
            if (string.IsNullOrWhiteSpace(action.Color) || !Regex.IsMatch(action.Color, @"\A#[0-9a-fA-F]{6}\z"))
                issues.Add(new($"{ap}.color", "Use a color in #RRGGBB format."));
            if (action.Processes is null || action.Processes.Count == 0)
            { issues.Add(new($"{ap}.processes", "At least one process is required.")); continue; }
            for (int p = 0; p < action.Processes.Count; p++)
            {
                string pp = $"{ap}.processes[{p}]";
                var process = action.Processes[p];
                if (process is null) { issues.Add(new(pp, "A process is required.")); continue; }
                Id(process.Id, $"{pp}.id");
                Required(process.Name, $"{pp}.name");
                DirectoryPath(process.WorkingDirectory, $"{pp}.workingDirectory");
                if (process.Steps is null || process.Steps.Count == 0)
                { issues.Add(new($"{pp}.steps", "At least one step is required.")); continue; }
                var previous = new HashSet<Guid>();
                for (int s = 0; s < process.Steps.Count; s++)
                {
                    string sp = $"{pp}.steps[{s}]";
                    var step = process.Steps[s];
                    if (step is null) { issues.Add(new(sp, "A step is required.")); continue; }
                    Id(step.Id, $"{sp}.id");
                    Required(step.Command, $"{sp}.command");
                    if (step.DependsOnSuccessOf is Guid dependency && !previous.Contains(dependency))
                        issues.Add(new($"{sp}.dependsOnSuccessOf", "Dependency must reference an earlier step in this process."));
                    previous.Add(step.Id);
                }
            }
        }
        return issues;
    }

    private string FilePath(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A non-empty project ID is required.", nameof(id));
        return Path.Combine(StorageDirectory, $"{id:D}.json");
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
