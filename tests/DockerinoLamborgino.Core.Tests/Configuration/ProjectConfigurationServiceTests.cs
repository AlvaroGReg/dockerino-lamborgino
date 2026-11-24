using System.Text.Json;
using DockerinoLamborgino.Core;
using DockerinoLamborgino.Cli;
using Xunit;

namespace DockerinoLamborgino.Core.Tests;

public sealed class ProjectConfigurationServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dockerino tests " + Guid.NewGuid());
    private readonly ProjectConfigurationService service;

    public ProjectConfigurationServiceTests()
    {
        Directory.CreateDirectory(root);
        service = new(Path.Combine(root, "store"));
    }

    [Fact]
    public void RegisterEditAndRejectStaleRevision()
    {
        Assert.Empty(service.List());
        var project = ProjectConfigurationService.Create("Example", root);
        Assert.Empty(service.Save(project));
        var first = service.Get(project.Id);
        var second = service.Get(project.Id);
        first.Project!.Name = "Changed";
        Assert.Empty(service.Save(first.Project, first.Revision));
        second.Project!.Name = "Stale";
        Assert.NotEmpty(service.Save(second.Project, second.Revision));
        Assert.NotEmpty(service.Save(second.Project));
        Assert.Equal("Changed", service.Get(project.Id).Project!.Name);
    }

    [Fact]
    public void ValidationChecksDependenciesAndDoesNotRunCommands()
    {
        string marker = Path.Combine(root, "marker");
        var project = ProjectConfigurationService.Create("Example", root);
        var first = new LaunchStep { Id = Guid.NewGuid(), Command = $"echo unsafe > \"{marker}\"" };
        var second = new LaunchStep { Id = Guid.NewGuid(), Command = "next", DependsOnSuccessOf = first.Id };
        project.Actions.Add(new LaunchAction { Id = Guid.NewGuid(), Name = "Run", Color = "#AABBCC",
            Processes = [new LaunchProcess { Id = Guid.NewGuid(), Name = "shell", WorkingDirectory = root, Steps = [first, second] }] });
        Assert.Empty(service.Save(project));
        Assert.False(File.Exists(marker));
        second.DependsOnSuccessOf = Guid.NewGuid();
        Assert.Contains(service.Validate(project), x => x.Path.EndsWith("dependsOnSuccessOf"));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void ExistingNonAdjacentDependencySurvivesRoundTrip()
    {
        var project = ProjectConfigurationService.Create("Example", root);
        var first = new LaunchStep { Id = Guid.NewGuid(), Command = "first" };
        var middle = new LaunchStep { Id = Guid.NewGuid(), Command = "middle" };
        var last = new LaunchStep { Id = Guid.NewGuid(), Command = "last", DependsOnSuccessOf = first.Id };
        project.Actions.Add(new LaunchAction { Id = Guid.NewGuid(), Name = "Run", Color = "#AABBCC",
            Processes = [new LaunchProcess { Id = Guid.NewGuid(), Name = "shell", WorkingDirectory = root,
                Steps = [first, middle, last] }] });
        Assert.Empty(service.Save(project));
        var loaded = service.Get(project.Id);
        Assert.Empty(loaded.Issues);
        Assert.Equal(first.Id, loaded.Project!.Actions[0].Processes[0].Steps[2].DependsOnSuccessOf);
    }

    [Fact]
    public void QuickLaunchSettingRoundTripsThroughConfigurationAndCliJson()
    {
        var project = ProjectConfigurationService.Create("Example", root);
        project.Actions.Add(new LaunchAction
        {
            Id = Guid.NewGuid(),
            Name = "Start",
            Color = "#AABBCC",
            IsQuickLaunch = true,
            Processes = [new LaunchProcess
            {
                Id = Guid.NewGuid(), Name = "server", WorkingDirectory = root,
                Steps = [new LaunchStep { Id = Guid.NewGuid(), Command = "start" }]
            }]
        });

        Assert.Empty(service.Save(project));
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(["get", project.Id.ToString()], output, new StringWriter(), service.StorageDirectory));

        var result = JsonSerializer.Deserialize<ProjectLoadResult>(output.ToString(), ProjectConfigurationService.JsonOptions)!;
        Assert.True(result.Project!.Actions[0].IsQuickLaunch);
        Assert.Equal([result.Project.Actions[0]], result.Project.QuickLaunchActions);
        Assert.Contains("\"isQuickLaunch\": true", output.ToString());
    }

    [Fact]
    public void LegacyActionsDefaultToNotQuickLaunch()
    {
        var project = ProjectConfigurationService.Create("Example", root);
        project.Actions.Add(new LaunchAction
        {
            Id = Guid.NewGuid(), Name = "Start", Color = "#AABBCC",
            Processes = [new LaunchProcess
            {
                Id = Guid.NewGuid(), Name = "server", WorkingDirectory = root,
                Steps = [new LaunchStep { Id = Guid.NewGuid(), Command = "start" }]
            }]
        });

        Assert.Empty(service.Save(project));
        var loaded = service.Get(project.Id).Project!;

        Assert.False(loaded.Actions[0].IsQuickLaunch);
        Assert.Empty(loaded.QuickLaunchActions);
    }

    [Fact]
    public void DamagedProjectDoesNotHideOthers()
    {
        var project = ProjectConfigurationService.Create("Valid", root);
        Assert.Empty(service.Save(project));
        File.WriteAllText(Path.Combine(service.StorageDirectory, "broken.json"), "{");
        var list = service.List();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, x => x.Project?.Id == project.Id && x.Issues.Count == 0);
        Assert.Contains(list, x => x.Project is null && x.Issues.Count > 0);
    }

    [Fact]
    public async Task SimultaneousUpdatesAllowOnlyOneWriter()
    {
        var project = ProjectConfigurationService.Create("Original", root);
        Assert.Empty(service.Save(project));
        var first = service.Get(project.Id);
        var second = service.Get(project.Id);
        first.Project!.Name = "First";
        second.Project!.Name = "Second";
        var results = await Task.WhenAll(
            Task.Run(() => service.Save(first.Project, first.Revision)),
            Task.Run(() => new ProjectConfigurationService(service.StorageDirectory).Save(second.Project, second.Revision)));
        Assert.Single(results, x => x.Count == 0);
        Assert.Single(results, x => x.Count != 0);
    }

    [Fact]
    public void SimultaneousCreatesAllowOnlyOneWriter()
    {
        var project = ProjectConfigurationService.Create("original", root);
        var results = ParallelEnumerable.Range(0, 8)
            .Select(_ => new ProjectConfigurationService(service.StorageDirectory).Save(project)).ToArray();
        Assert.Single(results, x => x.Count == 0);
        Assert.Equal(7, results.Count(x => x.Count != 0));
        Assert.Empty(service.Get(project.Id).Issues);
    }

    [Fact]
    public void CliRegistersAndReadsUsingSharedStore()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, Program.Run(["register", "Name", root], output, error, service.StorageDirectory));
        var result = JsonSerializer.Deserialize<ProjectLoadResult>(output.ToString(), ProjectConfigurationService.JsonOptions)!;
        Assert.Equal("Name", service.Get(result.Project!.Id).Project!.Name);
        Assert.Equal(0, Program.Run(["get", result.Project.Id.ToString()], new StringWriter(), error, service.StorageDirectory));
    }

    [Fact]
    public void ExplicitRepositoriesRoundTripThroughCliAndLegacyFilesRemainCompatible()
    {
        var project = ProjectConfigurationService.Create("Example", root);
        var external = Path.Combine(root, "external repository");
        Directory.CreateDirectory(external);
        Assert.Empty(project.Repositories);
        Assert.Empty(service.Save(project));
        var loaded = service.Get(project.Id);
        loaded.Project!.Repositories.Add(external);
        var file = Path.Combine(root, "edited.json");
        File.WriteAllText(file, JsonSerializer.Serialize(loaded.Project, ProjectConfigurationService.JsonOptions));
        Assert.Equal(0, Program.Run(["save", file, loaded.Revision!], new StringWriter(), new StringWriter(), service.StorageDirectory));
        Assert.Equal([external], service.Get(project.Id).Project!.Repositories);
        var output = new StringWriter();
        Assert.Equal(0, Program.Run(["get", project.Id.ToString()], output, new StringWriter(), service.StorageDirectory));
        Assert.Equal([external], JsonSerializer.Deserialize<ProjectLoadResult>(output.ToString(), ProjectConfigurationService.JsonOptions)!.Project!.Repositories);

        File.WriteAllText(file, JsonSerializer.Serialize(new { id = Guid.NewGuid(), name = "legacy", directory = root, actions = Array.Empty<object>() }));
        Assert.Empty(service.LoadFile(file).Project!.Repositories);
        Assert.Empty(service.LoadFile(file).Issues);
    }

    [Fact]
    public void RepositoryPathsAreExplicitExistingAbsoluteAndUnique()
    {
        var project = ProjectConfigurationService.Create("Example", root);
        project.Repositories = [root, root + Path.DirectorySeparatorChar, "relative", Path.Combine(root, "missing"), null!];
        var issues = service.Validate(project);
        Assert.Contains(issues, x => x.Path == "repositories[1]");
        Assert.Contains(issues, x => x.Path == "repositories[2]");
        Assert.Contains(issues, x => x.Path == "repositories[3]");
        Assert.Contains(issues, x => x.Path == "repositories[4]");
        Assert.NotEmpty(service.Save(project));
        project.Repositories = null!;
        Assert.Contains(service.Validate(project), x => x.Path == "repositories");
    }

    [Fact]
    public void UnavailableResourcesAreDiagnosedButOnlySelectedActionBlocksLaunch()
    {
        var repository = Path.Combine(root, "repository");
        var otherDirectory = Path.Combine(root, "other action");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(otherDirectory);
        var project = ProjectConfigurationService.Create("Example", root);
        project.Repositories.Add(repository);
        LaunchAction Action(string name, string directory) => new()
        {
            Id = Guid.NewGuid(), Name = name, Color = "#123456",
            Processes = [new LaunchProcess { Id = Guid.NewGuid(), Name = name, WorkingDirectory = directory,
                Steps = [new LaunchStep { Id = Guid.NewGuid(), Command = "test" }] }]
        };
        var good = Action("good", root);
        var affected = Action("affected", otherDirectory);
        project.Actions.AddRange([good, affected]);
        Assert.Empty(service.Save(project));
        Directory.Delete(repository);
        Directory.Delete(otherDirectory);

        var loaded = Assert.Single(service.List());
        Assert.Equal(project.Id, loaded.Project!.Id);
        Assert.Equal(2, loaded.Issues.Count);
        Assert.All(loaded.Issues, issue => Assert.True(issue.IsResourceAvailability));
        Assert.Empty(service.ValidateLaunch(loaded, good.Id));
        var failure = Assert.Single(service.ValidateLaunch(loaded, affected.Id));
        Assert.Contains(otherDirectory, failure.Message);
        Assert.Contains("actions[1]", failure.Path);
        Assert.NotEmpty(service.Save(loaded.Project, loaded.Revision));
        Assert.False(new GitRepositoryService().GetStatus(repository).Success);

        Directory.CreateDirectory(otherDirectory);
        Assert.Empty(service.ValidateLaunch(loaded, affected.Id));
        Directory.Delete(otherDirectory);
        Assert.NotEmpty(service.ValidateLaunch(service.Get(project.Id), affected.Id));
        Assert.NotEmpty(service.ValidateLaunch(loaded, Guid.NewGuid()));
    }

    [Fact]
    public void LaunchRejectsStructuralAndStorageErrorsEvenWhenResourcesAreMissing()
    {
        var project = ProjectConfigurationService.Create("Example", root);
        Assert.Empty(service.Save(project));
        var path = Path.Combine(service.StorageDirectory, $"{project.Id:D}.json");
        project.Name = "";
        project.Repositories.Add(Path.Combine(root, "missing"));
        File.WriteAllText(path, JsonSerializer.Serialize(project, ProjectConfigurationService.JsonOptions));
        var loaded = service.Get(project.Id);
        Assert.Contains(loaded.Issues, issue => !issue.IsResourceAvailability && issue.Path == "name");
        Assert.Contains(service.ValidateLaunch(loaded, Guid.NewGuid()), issue => issue.Path == "name");
        var wrongFile = Path.Combine(service.StorageDirectory, $"{Guid.NewGuid():D}.json");
        File.Copy(path, wrongFile);
        var mismatch = service.List().Single(item => item.Source == wrongFile);
        Assert.Contains(service.ValidateLaunch(mismatch, Guid.NewGuid()), issue => issue.Path == "id");
    }

    [Fact]
    public void MissingActionsAndNullDescriptionAreReported()
    {
        var missing = Path.Combine(root, "missing.json");
        File.WriteAllText(missing, JsonSerializer.Serialize(new { id = Guid.NewGuid(), name = "x", directory = root }));
        Assert.NotEmpty(service.LoadFile(missing).Issues);

        var project = ProjectConfigurationService.Create("x", root);
        project.Actions.Add(new LaunchAction { Id = Guid.NewGuid(), Name = "a", Color = "#123456",
            Description = null!, Processes = [new LaunchProcess { Id = Guid.NewGuid(), Name = "p",
                WorkingDirectory = root, Steps = [new LaunchStep { Id = Guid.NewGuid(), Command = "x" }] }] });
        Assert.Contains(service.Validate(project), issue => issue.Path.EndsWith(".description"));
    }

    [Theory]
    [InlineData("#AABBCC\n")]
    [InlineData("#AABBCC\r\n")]
    public void ColorRejectsTrailingNewlines(string color)
    {
        var project = ProjectConfigurationService.Create("x", root);
        project.Actions.Add(new LaunchAction { Id = Guid.NewGuid(), Name = "a", Color = color });
        Assert.Contains(service.Validate(project), issue => issue.Path.EndsWith(".color"));
    }

    [Fact]
    public void InvalidStorageAndInputPathsReturnErrors()
    {
        var storageFile = Path.Combine(root, "not-a-directory");
        File.WriteAllText(storageFile, "x");
        var broken = new ProjectConfigurationService(storageFile);
        Assert.NotEmpty(broken.List());
        Assert.NotEmpty(broken.Save(ProjectConfigurationService.Create("x", root)));
        Assert.NotEmpty(service.LoadFile("\0").Issues);
        Assert.NotEmpty(service.Get(Guid.Empty).Issues);
    }

    [Fact]
    public void ExternalJsonFilenameDoesNotHaveToMatchProjectId()
    {
        var project = ProjectConfigurationService.Create("x", root);
        var file = Path.Combine(root, $"{Guid.NewGuid()}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(project, ProjectConfigurationService.JsonOptions));
        Assert.Empty(service.LoadFile(file).Issues);
    }

    [Fact]
    public void CliGetInvalidProjectReturnsFailure()
    {
        var projectDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(projectDirectory);
        var project = ProjectConfigurationService.Create("x", projectDirectory);
        Assert.Empty(service.Save(project));
        Directory.Delete(projectDirectory);
        var output = new StringWriter();
        Assert.Equal(1, Program.Run(["get", project.Id.ToString()], output, new StringWriter(), service.StorageDirectory));
        var result = JsonSerializer.Deserialize<ProjectLoadResult>(output.ToString(), ProjectConfigurationService.JsonOptions)!;
        Assert.Equal(project.Id, result.Project!.Id);
        Assert.NotEmpty(result.Revision!);
        Assert.NotEmpty(result.Issues);
        result.Project.Directory = root;
        Assert.Empty(service.Save(result.Project, result.Revision));
    }

    [Fact]
    public void NullAndMissingJsonValuesAreIsolatedAndReported()
    {
        Assert.NotEmpty(service.Save(null));
        Assert.NotEmpty(service.Validate(null));
        var project = ProjectConfigurationService.Create("valid", root);
        Assert.Empty(service.Save(project));
        var badPath = Path.Combine(service.StorageDirectory, $"{Guid.NewGuid():D}.json");
        File.WriteAllText(badPath, $$"""{"id":"{{Guid.NewGuid()}}","name":null,"directory":null,"actions":[null]}""");
        var broken = service.LoadFile(badPath);
        Assert.Contains(broken.Issues, x => x.Path == "name");
        Assert.Contains(broken.Issues, x => x.Path == "directory");
        Assert.Contains(broken.Issues, x => x.Path == "actions[0]");
        Assert.Contains(service.List(), x => x.Project?.Id == project.Id && x.Issues.Count == 0);
        File.WriteAllText(badPath, "null");
        Assert.Null(service.LoadFile(badPath).Project);
        Assert.NotEmpty(service.LoadFile(badPath).Issues);
    }

    public void Dispose() => Directory.Delete(root, true);
}
