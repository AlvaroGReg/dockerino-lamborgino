using System.Text.Json;
using DockerinoLamborgino.Core;

namespace DockerinoLamborgino.App;

// The integration layer owns the mapping to the shared Core model and validation.
// ConfigurationJson is an editable, lossless representation supplied by that layer.
public sealed record ProjectConfigurationDraft(string? Id, string? Revision, string Name, string Directory, string ConfigurationJson, string RepositoriesJson = "[]");
public sealed record ProjectActionEntry(string Name, string Color, string Description)
{
    public string Label => $"{Name} — {Color} — {Description} (not executable until Phase 5)";
}
public sealed record ProjectConfigurationEntry(string Id, string Name, string Directory, IReadOnlyList<ProjectActionEntry> Actions);
public sealed record ProjectConfigurationList(IReadOnlyList<ProjectConfigurationEntry> Projects, IReadOnlyList<string> Errors);
public sealed record ProjectConfigurationSaveResult(bool Saved, IReadOnlyList<string> Errors, string? Id = null);

public interface IProjectConfigurationAdapter
{
    ProjectConfigurationList List();
    ProjectConfigurationDraft Load(string id);
    ProjectConfigurationSaveResult Save(ProjectConfigurationDraft draft);
}

internal sealed class CoreProjectConfigurationAdapter(string? storageDirectory) : IProjectConfigurationAdapter
{
    private readonly ProjectConfigurationService service = new(storageDirectory);

    public ProjectConfigurationList List()
    {
        var entries = new List<ProjectConfigurationEntry>();
        var errors = new List<string>();
        foreach (var result in service.List())
        {
            errors.AddRange(result.Issues.Select(issue => $"{result.Source}: {issue.Path}: {issue.Message}"));
            var project = result.Project;
            if (project is null || project.Id == Guid.Empty ||
                !Guid.TryParse(System.IO.Path.GetFileNameWithoutExtension(result.Source), out var fileId) || fileId != project.Id)
                continue;
            var actions = (project.Actions ?? []).Where(a => a is not null)
                .Select(a => new ProjectActionEntry(a.Name, a.Color, a.Description)).ToArray();
            entries.Add(new(project.Id.ToString(), project.Name, project.Directory, actions));
        }
        return new(entries, errors);
    }

    public ProjectConfigurationDraft Load(string id)
    {
        var result = service.Get(Guid.Parse(id));
        if (result.Project is null || result.Revision is null)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Issues.Select(i => $"{i.Path}: {i.Message}")));
        return new(id, result.Revision, result.Project.Name, result.Project.Directory,
            JsonSerializer.Serialize(result.Project.Actions, ProjectConfigurationService.JsonOptions),
            JsonSerializer.Serialize(result.Project.Repositories, ProjectConfigurationService.JsonOptions));
    }

    public ProjectConfigurationSaveResult Save(ProjectConfigurationDraft draft)
    {
        if (draft.Id is not null && draft.Revision is null)
            return new(false, ["Reload the project before saving; its revision is unavailable."]);
        var actions = JsonSerializer.Deserialize<List<LaunchAction>>(draft.ConfigurationJson, ProjectConfigurationService.JsonOptions);
        var repositories = JsonSerializer.Deserialize<List<string>>(draft.RepositoriesJson, ProjectConfigurationService.JsonOptions);
        var project = draft.Id is null
            ? ProjectConfigurationService.Create(draft.Name, draft.Directory)
            : new ProjectConfiguration { Id = Guid.Parse(draft.Id), Name = draft.Name, Directory = draft.Directory };
        project.Actions = actions!;
        project.Repositories = repositories!;
        var issues = service.Save(project, draft.Revision);
        return new(issues.Count == 0, issues.Select(i => $"{i.Path}: {i.Message}").ToArray(), project.Id.ToString());
    }
}
