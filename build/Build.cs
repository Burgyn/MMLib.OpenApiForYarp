using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "ci",
    GitHubActionsImage.UbuntuLatest,
    OnPushBranchesIgnore = new[] { "main" },
    OnPullRequestBranches = new[] { "main" },
    InvokedTargets = new[] { nameof(Test) },
    FetchDepth = 0)]
[GitHubActions(
    "publish",
    GitHubActionsImage.UbuntuLatest,
    OnPushBranches = new[] { "main" },
    InvokedTargets = new[] { nameof(Publish) },
    ImportSecrets = new[] { nameof(NuGetApiKey) },
    FetchDepth = 0)]
class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution(GenerateProjects = false)]
    readonly Solution Solution;

    [Parameter("NuGet feed source URL")]
    readonly string NuGetSource = "https://api.nuget.org/v3/index.json";

    [Parameter("NuGet API key for publishing")]
    [Secret]
    readonly string NuGetApiKey;

    [Parameter("The package id used for the version-already-published check")]
    readonly string PackageId = "MMLib.OpenApiForYarp";

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    string PackageVersion =>
        XDocument.Load(RootDirectory / "Directory.Build.props")
            .Descendants("Version")
            .FirstOrDefault()?.Value
        ?? throw new Exception("No <Version> found in Directory.Build.props.");

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            TestsDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() => DotNetRestore(s => s.SetProjectFile(Solution)));

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() => DotNetBuild(s => s
            .SetProjectFile(Solution)
            .SetConfiguration(Configuration)
            .EnableNoRestore()));

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() => DotNetTest(s => s
            .SetProjectFile(Solution)
            .SetConfiguration(Configuration)
            .EnableNoRestore()
            .EnableNoBuild()));

    Target Pack => _ => _
        .DependsOn(Test)
        .Produces(ArtifactsDirectory / "*.nupkg")
        .Executes(() => DotNetPack(s => s
            .SetProject(Solution)
            .SetConfiguration(Configuration)
            .SetOutputDirectory(ArtifactsDirectory)
            .EnableNoRestore()
            .EnableNoBuild()));

    Target Publish => _ => _
        .DependsOn(Pack)
        .Requires(() => NuGetApiKey)
        .OnlyWhenDynamic(() => !VersionExistsOnNuGet(PackageId, PackageVersion).GetAwaiter().GetResult())
        .Executes(() =>
        {
            Serilog.Log.Information("Publishing {PackageId} {Version}", PackageId, PackageVersion);
            ArtifactsDirectory.GlobFiles("*.nupkg").ForEach(package =>
                DotNetNuGetPush(s => s
                    .SetTargetPath(package)
                    .SetSource(NuGetSource)
                    .SetApiKey(NuGetApiKey)
                    .EnableSkipDuplicate()));
        });

    Target MutationTest => _ => _
        .Description("Run Stryker.NET mutation testing on the core library")
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNet("tool restore", RootDirectory);
            DotNet("stryker", RootDirectory);
        });

    // Returns true when the given version is already published on the NuGet flat-container index,
    // so the publish step becomes a no-op until the version in Directory.Build.props is bumped.
    static async System.Threading.Tasks.Task<bool> VersionExistsOnNuGet(string id, string version)
    {
        using var http = new HttpClient();
        var url = $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json";
        var response = await http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("versions").EnumerateArray()
            .Any(v => string.Equals(v.GetString(), version, StringComparison.OrdinalIgnoreCase));
    }
}
