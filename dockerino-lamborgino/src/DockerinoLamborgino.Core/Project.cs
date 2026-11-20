namespace DockerinoLamborgino.Core;

/// <summary>A project explicitly supplied by the caller; this type does not discover projects.</summary>
public sealed record Project
{
    public Project(string name, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Name = name;
        Directory = directory;
    }

    public string Name { get; }
    public string Directory { get; }
}
