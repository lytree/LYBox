#!/usr/bin/env dotnet
#:sdk Cake.Sdk@6.2.0
#:package Spectre.Console@0.57.2
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.NuGet.Push;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Core;
using Cake.Core.Diagnostics;
using Spectre.Console;
// Disambiguate System.IO types from Cake.Core.IO.Path / Cake.Common helpers
using Path = System.IO.Path;
using File = System.IO.File;
using Directory = System.IO.Directory;
using Architecture = System.Runtime.InteropServices.Architecture;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS / CONTEXT
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var buildContext = new BuildContext(Context);

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(c =>
{
    var t = buildContext.Target;

    // Bin 同时清理 SDK 与宿主产物
    if (t.HasFlag(BuildTarget.Bin))
    {
        CleanDirectoryIfExists(c, buildContext.NuGetPackagesDir);
        CleanDirectoryIfExists(c, buildContext.LauncherPublishDir);
        CleanDirectoryIfExists(c, buildContext.LegacyPackageDir);
    }

    if (t.HasFlag(BuildTarget.All))
    {
        CleanDirectoryIfExists(c, buildContext.ArtifactsDir);
    }

    c.Log.Information("Clean completed. Target: {0}", t);

    static void CleanDirectoryIfExists(ICakeContext ctx, string dir)
    {
        // Defensive clean: a held-open file (Defender, indexer, a stale
        // Cake/dotnet child, or a hung publish) makes Directory.Delete throw
        // UnauthorizedAccessException; a transient sharing violation surfaces
        // as IOException. Retry briefly, then warn and continue so the rest
        // of the build can run and overwrite outputs downstream.
        if (!Directory.Exists(dir)) return;

        const int MaxAttempts = 4;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                ctx.CleanDirectory(dir);
                return;
            }
            catch (Exception ex) when (
                ex is IOException
                || ex is UnauthorizedAccessException
                || ex is DirectoryNotFoundException)
            {
                lastError = ex;
            }

            Thread.Sleep(150 * attempt);
        }

        ctx.Log.Warning(
            "Clean skipped after " + MaxAttempts + " attempts for '" + dir +
            "'. Files are still in use (likely Defender / indexer scanning " +
            "outputs, or a stale build/publish process). The build will " +
            "continue and downstream targets will overwrite outputs. Last " +
            "error: " + (lastError?.Message ?? "(unknown)"));
    }
});

Task("Build")
    .IsDependentOn("Clean")
    .Does(c =>
{
    var hostSettings = buildContext.CreateHostMSBuildSettings();

    // SDK 编译：Bin 与 NuGet 目标都需要（NuGet 目标下 PackNuGet 以 NoBuild=true 复用此构建结果）
    var needSdk = buildContext.Target.HasFlag(BuildTarget.Bin) || buildContext.Target.HasFlag(BuildTarget.NuGet);
    if (needSdk)
    {
        // SDK layer: Generators + CommandLine + Shared + Shared.Web
        c.DotNetBuild(buildContext.GeneratorsProject, new DotNetBuildSettings
        {
            Configuration = buildContext.BuildConfiguration,
            MSBuildSettings = hostSettings
        });

        c.DotNetBuild(buildContext.CommandLineProject, new DotNetBuildSettings
        {
            Configuration = buildContext.BuildConfiguration,
            MSBuildSettings = hostSettings
        });

        c.DotNetBuild(buildContext.SharedProject, new DotNetBuildSettings
        {
            Configuration = buildContext.BuildConfiguration,
            MSBuildSettings = hostSettings
        });

        c.DotNetBuild(buildContext.WebSharedProject, new DotNetBuildSettings
        {
            Configuration = buildContext.BuildConfiguration,
            MSBuildSettings = hostSettings
        });
    }

    if (buildContext.Target.HasFlag(BuildTarget.Bin))
    {
        // SDK NuGet 打包（NoBuild=true 复用上一步构建结果，输出到 artifacts/packages/sdk）
        c.EnsureDirectoryExists(buildContext.NuGetPackagesDir);
        c.DotNetPack(buildContext.GeneratorsProject, new DotNetPackSettings
        {
            Configuration = buildContext.BuildConfiguration,
            OutputDirectory = buildContext.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = hostSettings
        });
        c.DotNetPack(buildContext.CommandLineProject, new DotNetPackSettings
        {
            Configuration = buildContext.BuildConfiguration,
            OutputDirectory = buildContext.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = hostSettings
        });
        c.DotNetPack(buildContext.SharedProject, new DotNetPackSettings
        {
            Configuration = buildContext.BuildConfiguration,
            OutputDirectory = buildContext.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = hostSettings
        });
        c.DotNetPack(buildContext.WebSharedProject, new DotNetPackSettings
        {
            Configuration = buildContext.BuildConfiguration,
            OutputDirectory = buildContext.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = hostSettings
        });
        c.Log.Information("SDK NuGet packages created in: {0}", buildContext.NuGetPackagesDir);

        // 宿主层：Launcher
        c.DotNetBuild(buildContext.LauncherProject, new DotNetBuildSettings
        {
            Configuration = buildContext.BuildConfiguration,
            MSBuildSettings = hostSettings
        });
        c.DotNetBuild(buildContext.ConsoleProject, new DotNetBuildSettings
        {
            Configuration = buildContext.BuildConfiguration,
            MSBuildSettings = hostSettings
        });
    }

    c.Log.Information("Build completed. Target: {0}", buildContext.Target);
});

Task("PackNuGet")
    .IsDependentOn("Build")
    .WithCriteria(c => buildContext.Target.HasFlag(BuildTarget.NuGet))
    .Does(c => BuildTasks.PackNuGet(buildContext));

Task("PackBin")
    .IsDependentOn("Build")
    .WithCriteria(c => buildContext.Target.HasFlag(BuildTarget.Bin))
    .Does(c =>
{
    // 统一发布目录：GUI 启动器（WinExe，LYBox.Launcher.Desktop.exe）与控制台调试版（Exe，LYBox.Launcher.Console.exe）
    // 都发布到 HostPublishDir。两者共享大部分依赖，dotnet publish 会按文件归并，最终目录里同时包含两份 .exe。
    c.EnsureDirectoryExists(buildContext.HostPublishDir);

    // GUI 启动器
    var settings = new DotNetPublishSettings
    {
        Configuration = buildContext.BuildConfiguration,
        OutputDirectory = buildContext.HostPublishDir,
        NoRestore = true,
        NoBuild = true,
    };

    if (!string.IsNullOrEmpty(buildContext.RuntimeIdentifier))
    {
        settings.Runtime = buildContext.RuntimeIdentifier;
        // Build 未按 RID 编译，publish 需要重新构建 RID 产物
        settings.NoBuild = false;
        settings.NoRestore = false;
    }

    if (buildContext.SelfContained)
    {
        settings.SelfContained = true;
    }

    c.DotNetPublish(buildContext.LauncherProject, settings);

    // 控制台调试版（与 GUI 版共用同一套启动逻辑，输出 LYBox.Launcher.Console.exe）
    var consoleSettings = new DotNetPublishSettings
    {
        Configuration = buildContext.BuildConfiguration,
        OutputDirectory = buildContext.HostPublishDir,
        NoRestore = true,
        NoBuild = true,
    };

    if (!string.IsNullOrEmpty(buildContext.RuntimeIdentifier))
    {
        consoleSettings.Runtime = buildContext.RuntimeIdentifier;
        consoleSettings.NoBuild = false;
        consoleSettings.NoRestore = false;
    }

    if (buildContext.SelfContained)
    {
        consoleSettings.SelfContained = true;
    }

    c.DotNetPublish(buildContext.ConsoleProject, consoleSettings);

    // 清理构建中间产物（.pdb / .xml），让最终发布目录只保留运行时必需文件。
    // .deps.json / .runtimeconfig.json 保留：apphost 启动必需，剥离将导致双击 .exe 无法启动。
    BuildTasks.StripLauncherIntermediates(buildContext);

    c.Log.Information("Host launchers published to: {0}", buildContext.HostPublishDir);
});

Task("LocalInstall")
    .IsDependentOn("PackNuGet")
    .Does(c => BuildTasks.LocalInstall(buildContext));

Task("PublishNuGet")
    .IsDependentOn("PackNuGet")
    .WithCriteria(c => buildContext.Target.HasFlag(BuildTarget.NuGetPublish))
    .Does(c => BuildTasks.PublishNuGet(buildContext));

Task("Default")
    .IsDependentOn("PackBin")
    .IsDependentOn("PackNuGet")
    .IsDependentOn("PublishNuGet");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);

//////////////////////////////////////////////////////////////////////
// SUPPORTING TYPES
//////////////////////////////////////////////////////////////////////

[Flags]
public enum BuildTarget
{
    None = 0,
    // Bin：构建宿主 launcher + console（并同时产出 SDK NuGet 包）
    Bin = 1,
    // NuGet：仅打包 SDK NuGet 包（Generators + Shared + CommandLine + Shared.Web）
    NuGet = 2,
    // NuGetPublish：推送 SDK NuGet 包到 NuGet 源
    NuGetPublish = 8,
    All = Bin | NuGet
}

/// <summary>
/// 包装 ICakeContext，集中管理构建参数、目录解析、版本覆盖与交互式提示。
/// </summary>
public class BuildContext
{
    private ICakeContext Cake { get; }

    public BuildTarget Target { get; }
    public string BuildConfiguration { get; }

    // 两层独立版本覆盖：宿主 / SDK 各自管理，留空时由各层 csproj 真相源决定
    //
    // 版本解析优先级（高 → 低）：
    //   1. --host-version   （宿主 launcher 显式覆盖，CI workflow_dispatch 使用）
    //   2. --sdk-version    （SDK Generators + Shared 显式覆盖）
    //   3. --package-version（兼容旧用法，作为未单独指定层的全局兜底）
    //   4. 各层 csproj 真相源（HostVersion / <Version> Fallback）
    public string? HostVersionOverride { get; }
    public string? SdkVersionOverride { get; }

    // 兼容回退：显式传 --package-version 时作为未指定层的全局兜底（紧急发版用）
    public string? PackageVersion { get; }

    public string NuGetSource { get; }
    public string NuGetApiKey { get; }
    public string RuntimeIdentifier { get; }
    public bool SelfContained { get; }
    public bool NoBuild { get; }

    public string RootDir { get; }
    public string ArtifactsDir { get; }
    public string NuGetPackagesDir { get; }
    public string LauncherPublishDir { get; }
    public string LegacyPackageDir { get; }

    // 宿主启动器统一发布目录（GUI 版与控制台调试版共用同一套目录）。
    // --runtime-identifier 指定时为 LauncherPublishDir/{rid}，否则为 LauncherPublishDir。
    // 两个可执行文件与其共享依赖位于同一目录，zip/分发无需额外合并步骤。
    public string HostPublishDir { get; }

    public string GeneratorsProject { get; }
    public string CommandLineProject { get; }
    public string SharedProject { get; }
    public string WebSharedProject { get; }
    public string LauncherProject { get; }
    public string ConsoleProject { get; }

    // 宿主版本覆盖（优先级：--host-version > --package-version > csproj 真相源 HostVersion）
    public DotNetMSBuildSettings CreateHostMSBuildSettings()
    {
        var settings = BaseSettings();
        var effective = EffectiveHostVersion;
        if (!string.IsNullOrEmpty(effective))
            settings.SetVersion(effective);
        return settings;
    }

    // 计算实际生效的宿主版本：--host-version > --package-version
    // 返回 null 时由 csproj 内 HostVersion Fallback 生效
    public string? EffectiveHostVersion
    {
        get
        {
            if (!string.IsNullOrEmpty(HostVersionOverride))
                return HostVersionOverride;
            if (!string.IsNullOrEmpty(PackageVersion))
                return PackageVersion;
            return null;
        }
    }

    // 计算实际生效的 SDK 版本：--sdk-version > --package-version
    // 返回 null 时由 SDK csproj 内 <Version> Fallback 生效
    public string? EffectiveSdkVersion
    {
        get
        {
            if (!string.IsNullOrEmpty(SdkVersionOverride))
                return SdkVersionOverride;
            if (!string.IsNullOrEmpty(PackageVersion))
                return PackageVersion;
            return null;
        }
    }

    // SDK（Generators + Shared）独立版本，与宿主版本解耦
    public DotNetMSBuildSettings CreateSdkMSBuildSettings()
    {
        var settings = BaseSettings();
        var effective = EffectiveSdkVersion;
        if (!string.IsNullOrEmpty(effective))
            settings.SetVersion(effective);
        return settings;
    }

    private DotNetMSBuildSettings BaseSettings()
    {
        return new DotNetMSBuildSettings()
            .SetConfiguration(BuildConfiguration)
            .WithProperty("ContinuousIntegrationBuild", "true");
    }

    public BuildContext(ICakeContext context)
    {
        Cake = context;

        var requestedBuildTarget = context.Argument("build", "");
        Target = SelectBuildTarget(
            ParseBuildTarget(requestedBuildTarget),
            !string.IsNullOrWhiteSpace(requestedBuildTarget));
        var requestedBuildConfiguration = context.Argument("configuration", "");
        BuildConfiguration = SelectBuildConfiguration(
            requestedBuildConfiguration,
            !string.IsNullOrWhiteSpace(requestedBuildConfiguration));

        HostVersionOverride = context.Argument("host-version", "");
        SdkVersionOverride = context.Argument("sdk-version", "");
        // 默认空：不覆盖，让各层 csproj 真相源各自生效；传值则作为未指定层的全局兜底（兼容旧用法）
        PackageVersion = context.Argument("package-version", "");
        NuGetSource = context.Argument("nuget-source", "https://api.nuget.org/v3/index.json");
        NuGetApiKey = context.Argument("nuget-api-key", "");
        RuntimeIdentifier = SelectRuntimeIdentifier(Target, NormalizeRuntimeIdentifier(context.Argument("runtime-identifier", "")));
        SelfContained = SelectSelfContained(Target, context.Argument("self-contained", false), context.HasArgument("self-contained"));
        NoBuild = context.Argument("no-build", false);

        RootDir = ResolveRepositoryRoot();
        ArtifactsDir = Path.Combine(RootDir, "artifacts");
        NuGetPackagesDir = Path.Combine(ArtifactsDir, "packages", "sdk");
        LauncherPublishDir = Path.Combine(ArtifactsDir, "publish", "launcher");
        LegacyPackageDir = Path.Combine(ArtifactsDir, "package");

        // 宿主启动器统一输出目录：RID 模式下挂子目录，否则直接在 LauncherPublishDir 下。
        // 两个启动器的产物合并到此目录，便于后续打包/分发一步完成。
        HostPublishDir = string.IsNullOrEmpty(RuntimeIdentifier)
            ? LauncherPublishDir
            : Path.Combine(LauncherPublishDir, RuntimeIdentifier);

        GeneratorsProject = Path.Combine(RootDir, "src", "Plugin", "LYBox.Plugin.Generators", "LYBox.Plugin.Generators.csproj");
        CommandLineProject = Path.Combine(RootDir, "src", "Plugin", "LYBox.Plugin.CommandLine", "LYBox.Plugin.CommandLine.csproj");
        SharedProject = Path.Combine(RootDir, "src", "Plugin", "LYBox.Plugin.Shared", "LYBox.Plugin.Shared.csproj");
        WebSharedProject = Path.Combine(RootDir, "src", "Plugin", "LYBox.Plugin.Shared.Web", "LYBox.Plugin.Shared.Web.csproj");
        LauncherProject = Path.Combine(RootDir, "src", "App", "LYBox.Launcher.Desktop", "LYBox.Launcher.Desktop.csproj");
        ConsoleProject = Path.Combine(RootDir, "src", "App", "LYBox.Launcher.Console", "LYBox.Launcher.Console.csproj");
    }

    public ICakeLog Log => Cake.Log;
    public void EnsureDirectoryExists(string path) => Cake.EnsureDirectoryExists(path);
    public void CleanDirectory(string path) => Cake.CleanDirectory(path);
    public void DotNetBuild(string project, DotNetBuildSettings settings) => Cake.DotNetBuild(project, settings);
    public void DotNetPack(string project, DotNetPackSettings settings) => Cake.DotNetPack(project, settings);
    public void DotNetPublish(string project, DotNetPublishSettings settings) => Cake.DotNetPublish(project, settings);
    public IEnumerable<Cake.Core.IO.FilePath> GetFiles(string pattern) => Cake.GetFiles(pattern);
    public int StartProcess(string fileName, Cake.Core.IO.ProcessSettings settings) => Cake.StartProcess(fileName, settings);

    private static string ResolveRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var buildDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("无法解析 build.cs 所在目录");
        return Path.GetFullPath(Path.Combine(buildDirectory, ".."));
    }

    private static BuildTarget ParseBuildTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BuildTarget.All;

        var result = BuildTarget.None;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result |= part.ToLowerInvariant() switch
            {
                "all" => BuildTarget.All,
                "bin" => BuildTarget.Bin,
                "nuget" => BuildTarget.NuGet,
                "publish-nuget" or "nuget-publish" or "push-nuget" => BuildTarget.NuGetPublish,
                _ => throw new ArgumentException($"Unknown build target: '{part}'. Valid values: all, bin, nuget, publish-nuget")
            };
        }
        return result == BuildTarget.None ? BuildTarget.All : result;
    }

    // ---- 交互式提示（未传参且终端可交互时触发）----

    private static bool CanPrompt => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    private static BuildTarget SelectBuildTarget(BuildTarget requestedTarget, bool isConfigured)
    {
        if (isConfigured || !CanPrompt)
            return requestedTarget;

        AnsiConsole.Write(new Rule("[yellow]未指定 --build：请选择构建目标[/]")
            .RuleStyle("grey"));

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择构建方式")
                .AddChoices("构建全部", "选择构建目标"));

        if (mode == "构建全部")
            return BuildTarget.All;

        var selectedTargets = AnsiConsole.Prompt(
            new MultiSelectionPrompt<BuildTarget>()
                .Title("选择构建目标")
                .InstructionsText("[grey]使用 [blue]↑[/]/[blue]↓[/] 移动，按 [blue]Space[/] 勾选，按 [blue]Enter[/] 确认。[/]")
                .UseConverter(GetBuildTargetDisplayName)
                .AddChoices(
                    BuildTarget.Bin,
                    BuildTarget.NuGet,
                    BuildTarget.NuGetPublish));

        var combined = selectedTargets.Aggregate(BuildTarget.None, (acc, item) => acc | item);
        return combined.HasFlag(BuildTarget.NuGetPublish) ? combined | BuildTarget.NuGet : combined;
    }

    private static string GetBuildTargetDisplayName(BuildTarget target)
    {
        return target switch
        {
            BuildTarget.Bin => "宿主启动器 (bin)",
            BuildTarget.NuGet => "SDK NuGet 包 (nuget)",
            BuildTarget.NuGetPublish => "打包并发布 NuGet (publish-nuget)",
            _ => target.ToString(),
        };
    }

    private static string SelectBuildConfiguration(string requestedConfiguration, bool isConfigured)
    {
        if (isConfigured || !CanPrompt)
            return string.IsNullOrWhiteSpace(requestedConfiguration) ? "Release" : requestedConfiguration;

        AnsiConsole.Write(new Rule("[yellow]未指定 --configuration：请选择构建配置[/]")
            .RuleStyle("grey"));

        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("选择构建配置")
                .AddChoices("Release", "Debug"));
    }

    private static bool SelectSelfContained(BuildTarget target, bool requestedSelfContained, bool isConfigured)
    {
        if (!target.HasFlag(BuildTarget.Bin) || isConfigured || !CanPrompt)
            return requestedSelfContained;

        AnsiConsole.Write(new Rule("[yellow]未指定 --self-contained：请选择发布模式[/]")
            .RuleStyle("grey"));

        return AnsiConsole.Prompt(
            new SelectionPrompt<bool>()
                .Title("生成自包含发布包？")
                .UseConverter(value => value ? "是：包含 .NET 运行时" : "否：需要目标机器已安装 .NET 运行时")
                .AddChoices(true, false));
    }

    private static string SelectRuntimeIdentifier(BuildTarget target, string requestedRuntimeIdentifier)
    {
        if (!target.HasFlag(BuildTarget.Bin) || !string.IsNullOrWhiteSpace(requestedRuntimeIdentifier) || !CanPrompt)
            return requestedRuntimeIdentifier;

        AnsiConsole.Write(new Rule("[yellow]未指定 --runtime-identifier：请选择目标运行时[/]")
            .RuleStyle("grey"));

        var choices = CreateRuntimeIdentifierChoices();
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<RuntimeIdentifierChoice>()
                .Title("选择运行时标识符 (RID)")
                .UseConverter(choice => Markup.Escape(choice.DisplayName))
                .AddChoices(choices));

        return choice.PromptForValue
            ? NormalizeRuntimeIdentifier(PromptForRequiredString("输入自定义运行时标识符 (RID)："))
            : choice.Value;
    }

    private static IReadOnlyList<RuntimeIdentifierChoice> CreateRuntimeIdentifierChoices()
    {
        var choices = new List<RuntimeIdentifierChoice>();
        var current = GetCurrentRuntimeIdentifier();
        if (!string.IsNullOrEmpty(current))
            choices.Add(new RuntimeIdentifierChoice($"当前系统 ({current})", current));

        foreach (var rid in new[] { "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" })
        {
            if (!string.Equals(rid, current, StringComparison.OrdinalIgnoreCase))
                choices.Add(new RuntimeIdentifierChoice(rid, rid));
        }

        choices.Add(new RuntimeIdentifierChoice("不指定 RID（使用框架默认发布）", ""));
        choices.Add(new RuntimeIdentifierChoice("输入自定义 RID", "", true));
        return choices;
    }

    private static string PromptForRequiredString(string prompt)
    {
        while (true)
        {
            var value = AnsiConsole.Ask<string>(prompt);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

            AnsiConsole.MarkupLine("[red]请输入一个值。[/]");
        }
    }

    private static string GetCurrentRuntimeIdentifier()
    {
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "";
        return string.IsNullOrEmpty(platform) ? "" : $"{platform}-{GetDefaultRidArchitecture()}";
    }

    private static string NormalizeRuntimeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var arch = GetDefaultRidArchitecture();
        return value.Trim().ToLowerInvariant() switch
        {
            "win" or "windows" => $"win-{arch}",
            "linux" => $"linux-{arch}",
            "mac" or "macos" or "osx" => $"osx-{arch}",
            _ => value.Trim()
        };
    }

    private static string GetDefaultRidArchitecture()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new ArgumentException(
                $"Unsupported process architecture '{RuntimeInformation.ProcessArchitecture}'. Specify a concrete runtime identifier, for example win-x64 or linux-arm64.")
        };
    }

}

//////////////////////////////////////////////////////////////////////
// BUILD TASKS
//////////////////////////////////////////////////////////////////////

public static class BuildTasks
{
    public static void PackNuGet(BuildContext context)
    {
        context.EnsureDirectoryExists(context.NuGetPackagesDir);

        var sdkSettings = context.CreateSdkMSBuildSettings();

        context.DotNetPack(context.GeneratorsProject, new DotNetPackSettings
        {
            Configuration = context.BuildConfiguration,
            OutputDirectory = context.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = sdkSettings
        });

        context.DotNetPack(context.CommandLineProject, new DotNetPackSettings
        {
            Configuration = context.BuildConfiguration,
            OutputDirectory = context.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = sdkSettings
        });

        context.DotNetPack(context.SharedProject, new DotNetPackSettings
        {
            Configuration = context.BuildConfiguration,
            OutputDirectory = context.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = sdkSettings
        });

        context.DotNetPack(context.WebSharedProject, new DotNetPackSettings
        {
            Configuration = context.BuildConfiguration,
            OutputDirectory = context.NuGetPackagesDir,
            NoRestore = true,
            NoBuild = true,
            MSBuildSettings = sdkSettings
        });

        context.Log.Information("SDK NuGet packages created in: {0}", context.NuGetPackagesDir);
        foreach (var pkg in context.GetFiles(Path.Combine(context.NuGetPackagesDir, "*.nupkg")))
        {
            context.Log.Information("  NuGet: {0}", pkg.GetFilename());
        }
    }

    public static void LocalInstall(BuildContext context)
    {
        var localFeedName = "AvaloniaPluginLocal";
        var localFeedPath = context.NuGetPackagesDir;

        context.StartProcess("dotnet", new Cake.Core.IO.ProcessSettings
        {
            Arguments = $"nuget add source \"{localFeedPath}\" -n {localFeedName}"
        });

        context.Log.Information("Local NuGet feed '{0}' configured at: {1}", localFeedName, localFeedPath);
        context.Log.Information("To consume these packages, add the following to your nuget.config:");
        context.Log.Information("  <add key=\"{0}\" value=\"{1}\" />", localFeedName, localFeedPath);
    }

    public static void PublishNuGet(BuildContext context)
    {
        if (string.IsNullOrEmpty(context.NuGetApiKey))
        {
            context.Log.Error("NuGet API key is required. Use --nuget-api-key=<KEY>");
            return;
        }

        var packages = context.GetFiles(Path.Combine(context.NuGetPackagesDir, "*.nupkg"));
        foreach (var pkg in packages)
        {
            context.Log.Information("Pushing {0}...", pkg.GetFilename());
            context.StartProcess("dotnet", new Cake.Core.IO.ProcessSettings
            {
                Arguments = new Cake.Core.IO.ProcessArgumentBuilder()
                    .Append("nuget")
                    .Append("push")
                    .AppendQuoted(pkg.FullPath)
                    .Append("--source")
                    .AppendQuoted(context.NuGetSource)
                    .Append("--api-key")
                    .AppendQuoted(context.NuGetApiKey)
                    .Append("--skip-duplicate")
            });
        }

        context.Log.Information("NuGet packages pushed to: {0}", context.NuGetSource);
    }

    /// <summary>
    /// 清理宿主启动器统一发布目录中的构建中间产物。
    /// 当前移除 <c>*.pdb</c> 与 <c>*.xml</c>——它们是构建产物中的调试符号与 XML 文档，
    /// 运行时不需要，去掉后发布目录只剩可直接分发的运行时必需文件。
    /// <para>
    /// 故意保留 <c>*.deps.json</c> 与 <c>*.runtimeconfig.json</c>——
    /// 它们是 apphost 启动所必需的运行时配置。剥离后双击 .exe 将无法启动，
    /// 强行最小化的产物应通过 <c>PublishSingleFile</c> 单文件发布获得。
    /// </para>
    /// </summary>
    public static void StripLauncherIntermediates(BuildContext context)
    {
        var dir = context.HostPublishDir;
        if (!Directory.Exists(dir))
        {
            context.Log.Warning("Host publish dir not found: {0}", dir);
            return;
        }

        string[] patterns = { "*.pdb", "*.xml" };
        int removed = 0;
        foreach (var pattern in patterns)
        {
            foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    context.Log.Warning("Failed to delete {0}: {1}", file, ex.Message);
                }
            }
        }

        context.Log.Information("Stripped {0} build intermediate file(s) (.pdb / .xml) from {1}",
            removed, dir);
    }
}

public sealed record RuntimeIdentifierChoice(string DisplayName, string Value, bool PromptForValue = false);
