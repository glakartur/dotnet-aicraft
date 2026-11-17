using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace DotnetAICraft.Tests.Daemon.FileSetSync;

/// <summary>
/// A temporary directory with project files on disk and an in-memory
/// <see cref="AdhocWorkspace"/> solution that points at them. No MSBuild.
/// </summary>
internal sealed class FileSetTestSolution : IDisposable
{
    public const string SdkProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    public const string LegacyProject = """
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <ItemGroup>
            <Compile Include="Foo.cs" />
          </ItemGroup>
        </Project>
        """;

    private readonly AdhocWorkspace _workspace;

    public FileSetTestSolution()
    {
        Root = Path.Combine(Path.GetTempPath(), "aicraft-fileset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        var assemblies = MefHostServices.DefaultAssemblies
            .Concat([typeof(CSharpCompilation).Assembly, typeof(CSharpFormattingOptions).Assembly])
            .Distinct();
        _workspace = new AdhocWorkspace(MefHostServices.Create(assemblies));
        Solution = _workspace.CurrentSolution;
    }

    public string Root { get; }

    public Solution Solution { get; set; }

    public string PathOf(string relativePath)
        => Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public string WriteFile(string relativePath, string content)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Adds a project whose file is written to disk. Returns its id.</summary>
    public ProjectId AddProject(string relativeProjectPath, string projectXml = SdkProject, string? name = null)
    {
        var projectPath = WriteFile(relativeProjectPath, projectXml);
        return AddProjectInstance(projectPath, name ?? Path.GetFileNameWithoutExtension(projectPath));
    }

    /// <summary>Adds another project instance for an existing project file (multi-targeting).</summary>
    public ProjectId AddProjectInstance(string projectPath, string name)
    {
        // Always C#: the test host has no VB/F# services. The resolver decides
        // ownership by the project file extension, so a ".vbproj" path still
        // models a non-C# owner.
        var id = ProjectId.CreateNewId(name);
        var info = ProjectInfo.Create(
            id,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: projectPath,
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
        Solution = Solution.AddProject(info);
        return id;
    }

    /// <summary>Writes a source file and adds it as a document to the given projects.</summary>
    public string AddDocument(string relativePath, string content, params ProjectId[] projects)
    {
        var path = WriteFile(relativePath, content);
        foreach (var projectId in projects)
        {
            Solution = Solution.AddDocument(
                DocumentId.CreateNewId(projectId),
                Path.GetFileName(path),
                SourceText.From(content),
                filePath: path);
        }

        return path;
    }

    public IReadOnlyList<Document> DocumentsAt(Solution solution, string path)
        => solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => string.Equals(d.FilePath, path, StringComparison.Ordinal))
            .ToList();

    public void Dispose()
    {
        _workspace.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
