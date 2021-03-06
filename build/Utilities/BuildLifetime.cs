using System;
using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.Build;
using Cake.Frosting;
using Cake.Common.Tools.GitVersion;
using System.Text;
using Cake.Core;
using Cake.Core.Diagnostics;

public class BuildLifetime : FrostingLifetime<BuildContext>
{
    public override void Setup(BuildContext context)
    {
        context.Target = context.Argument<string>("target", "Default");
        context.Configuration = context.Argument<string>("configuration", "Release");

        // MyGet
        context.MyGetSource = GetEnvironmentValueOrArgument(context, "FROSTING_MYGET_SOURCE", "mygetsource");
        context.MyGetApiKey = GetEnvironmentValueOrArgument(context, "FROSTING_MYGET_API_KEY", "mygetapikey");

        // Build system information.
        var buildSystem = context.BuildSystem();
        context.AppVeyor = buildSystem.AppVeyor.IsRunningOnAppVeyor;
        context.IsLocalBuild = buildSystem.IsLocalBuild;
        context.IsPullRequest = buildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
        context.IsOriginalRepo = StringComparer.OrdinalIgnoreCase.Equals("cake-build/frosting", buildSystem.AppVeyor.Environment.Repository.Name);
        context.IsTagged = IsBuildTagged(buildSystem);
        context.IsMasterBranch = StringComparer.OrdinalIgnoreCase.Equals("master", buildSystem.AppVeyor.Environment.Repository.Branch);

        // Force publish?
        context.ForcePublish = context.Argument<bool>("forcepublish", false);

        // Setup projects.
        context.Projects = new Project[] 
        {
            new Project { Name = "Cake.Frosting", Path = "./src/Cake.Frosting/project.json", Publish = true },
            new Project { Name = "Cake.Frosting.Testing" ,Path = "./src/Cake.Frosting.Testing/project.json" },
            new Project { Name = "Cake.Frosting.Tests" ,Path = "./src/Cake.Frosting.Tests/project.json" },
            new Project { Name = "Cake.Frosting.Sandbox", Path = "./src/Cake.Frosting.Sandbox/project.json" },
            new Project { Name = "Cake.Frosting.Cli", Path = "./src/Cake.Frosting.Cli/project.json", Publish = true }
        };

        // Calculate semantic version.
        var info = GetVersion(context);
        context.Version = context.Argument<string>("version", info.Version);
        context.Suffix = context.Argument<string>("suffix", info.Suffix);

        context.Information("Version: {0}", context.Version);
        context.Information("Version suffix: {0}", context.Suffix);
        context.Information("Configuration: {0}", context.Configuration);
        context.Information("Target: {0}", context.Target);
        context.Information("AppVeyor: {0}", context.AppVeyor);
    }

    private static BuildVersion GetVersion(BuildContext context)
    {
        string version = null;
        string semVersion = null;

        if (context.IsRunningOnWindows())
        {
            context.Information("Calculating semantic version...");
            if (!context.IsLocalBuild)
            {
                context.GitVersion(new GitVersionSettings
                {
                    OutputType = GitVersionOutput.BuildServer
                });
            }
            
            GitVersion assertedVersions = context.GitVersion(new GitVersionSettings
            {
                OutputType = GitVersionOutput.Json
            });
            version = assertedVersions.MajorMinorPatch;
            semVersion = assertedVersions.LegacySemVerPadded;
        }

        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(semVersion))
        {
            context.Information("Fetching verson from first project.json...");
            foreach(var project in context.Projects) 
            {
                var content = System.IO.File.ReadAllText(project.Path.FullPath, Encoding.UTF8);
                var node = Newtonsoft.Json.Linq.JObject.Parse(content);
                if(node["version"] != null) 
                {
                    version = node["version"].ToString().Replace("-*", "");
                }
            }
            semVersion = version;
        }

        if(string.IsNullOrWhiteSpace(version))
        {
            throw new CakeException("Could not calculate version of build.");
        }

        return new BuildVersion(version, semVersion.Substring(version.Length).TrimStart('-'));
    }

    private static bool IsBuildTagged(BuildSystem buildSystem)
    {
        return buildSystem.AppVeyor.Environment.Repository.Tag.IsTag
            && !string.IsNullOrWhiteSpace(buildSystem.AppVeyor.Environment.Repository.Tag.Name);
    }

    private static string GetEnvironmentValueOrArgument(BuildContext context, string environmentVariable, string argumentName)
    {
        var arg = context.EnvironmentVariable(environmentVariable);
        if(string.IsNullOrWhiteSpace(arg))
        {
            arg = context.Argument<string>(argumentName, null);
        }
        return arg;
    }
}