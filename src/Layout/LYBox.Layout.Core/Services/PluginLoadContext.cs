using System.Reflection;
using System.Runtime.Loader;

namespace LYBox.Layout.Core.Services;

internal class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// Default must-share patterns used when shared-assemblies.txt is not present.
    /// Kept in sync with LYBox.Plugin.Shared.props PluginExcludeAssembly items.
    /// </summary>
    private static readonly string[] DefaultSharedPatterns =
    [
        "System.*",
        "System.Private.Uri",
        "System.Reactive",
        "Microsoft.Bcl.AsyncInterfaces",
        "Avalonia",
        "Avalonia.*",
        "SkiaSharp",
        "SkiaSharp.*",
        "HarfBuzzSharp",
        "HarfBuzzSharp.*",
        
        "LYBox.Plugin.Shared",
        "LYBox.Plugin.Shared.Web",
        "LYBox.Plugin.CommandLine",
        "System.CommandLine",
        "System.CommandLine.*",
        "Spectre.Console",
        "Spectre.Console.*",
        "CommunityToolkit.*",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        "Microsoft.Extensions.Logging.Abstractions",
        "Irihi.*",
        "Ursa",
        "Semi.Avalonia",
    ];

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;
    private readonly Dictionary<string, string> _assemblyPathCache;
    private readonly List<string> _sharedPrefixes;
    private readonly HashSet<string> _sharedExactNames;

    public PluginLoadContext(string pluginPath, IEnumerable<string>? additionalSharedAssemblies = null)
        : base(isCollectible: true)
    {
        _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? pluginPath;
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _assemblyPathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _sharedPrefixes = new List<string>();
        _sharedExactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadSharedAssembliesList();

        // Merge additional shared assemblies declared in the plugin manifest
        if (additionalSharedAssemblies is not null)
        {
            foreach (var pattern in additionalSharedAssemblies)
            {
                AddSharedPattern(pattern);
            }
        }

        BuildAssemblyCache();
    }

    /// <summary>
    /// Reads shared-assemblies.txt from the plugin directory. Each line is either:
    /// - An exact assembly name (e.g., "LYBox.Plugin.Shared")
    /// - A prefix pattern ending with '*' (e.g., "System.*")
    /// Lines starting with '#' or empty lines are ignored.
    /// </summary>
    private void LoadSharedAssembliesList()
    {
        // 内置契约集合始终生效：shared-assemblies.txt 只能在其之上追加，不能退订。
        // 历史教训：插件目录里遗留的旧 shared-assemblies.txt 不含后加入的共享程序集
        // （例如 LYBox.Plugin.Shared.Web），插件于是加载了自己那份陈旧 SDK 副本，
        // 既让宿主侧修复对新插件不生效，又因类型标识分裂使 IWebPlugin /
        // IGeneratedPluginWebModule 跨 ALC 转换失败。
        foreach (var pattern in DefaultSharedPatterns)
        {
            AddSharedPattern(pattern);
        }

        var listPath = Path.Combine(_pluginDirectory, "shared-assemblies.txt");
        if (!File.Exists(listPath))
            return;

        foreach (var rawLine in File.ReadAllLines(listPath))
        {
            var pattern = rawLine.Trim();
            if (string.IsNullOrEmpty(pattern) || pattern.StartsWith('#'))
                continue;
            AddSharedPattern(pattern);
        }
    }

    private void AddSharedPattern(string pattern)
    {
        if (pattern.EndsWith('*'))
        {
            // Strip the trailing '*' to get the prefix
            _sharedPrefixes.Add(pattern[..^1]);
        }
        else
        {
            _sharedExactNames.Add(pattern);
        }
    }

    /// <summary>
    /// Returns true if the assembly should be forwarded to the host's default ALC
    /// (i.e., it's in the must-share set).
    /// </summary>
    private bool IsShared(string name)
    {
        if (_sharedExactNames.Contains(name))
            return true;

        foreach (var prefix in _sharedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;

        if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
        {
            return LoadSatelliteAssembly(assemblyName);
        }

        // Shared assemblies: forward to host's default ALC so all plugins and the host
        // share the same type identity for contract/framework assemblies.
        if (IsShared(name))
        {
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
                // 修复：原代码 fallthrough 到插件本地探测，会导致同一程序集
                // 同时存在于 Default ALC 与 Plugin ALC（如未来宿主补齐依赖后），
                // 类型标识不一致引发 InvalidCastException / DependencyContext 冲突。
                // 共享程序集必须由宿主提供；缺失即宿主配置错误，直接抛出。
                throw;
            }
        }

        // Plugin-local: prefer the plugin's own DLL so each plugin can use its own versions.
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        assemblyPath = ProbePluginDirectory(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Last resort: let the default ALC try (e.g., BCL assemblies not in the shared list)
        return null;
    }

    private Assembly? LoadSatelliteAssembly(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        var culture = assemblyName.CultureName;
        if (!string.IsNullOrEmpty(culture))
        {
            var dllName = $"{assemblyName.Name}.dll";
            var satellitePath = Path.Combine(_pluginDirectory, culture, dllName);
            if (File.Exists(satellitePath))
            {
                return LoadFromAssemblyPath(satellitePath);
            }
        }

        assemblyPath = ProbePluginDirectory(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
    }

    private string? ProbePluginDirectory(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name)) return null;

        if (_assemblyPathCache.TryGetValue(name, out var cachedPath))
        {
            if (File.Exists(cachedPath))
                return cachedPath;
            _assemblyPathCache.Remove(name);
        }

        return null;
    }

    private void BuildAssemblyCache()
    {
        if (!Directory.Exists(_pluginDirectory)) return;

        foreach (var dllPath in Directory.GetFiles(_pluginDirectory, "*.dll", SearchOption.AllDirectories))
        {
            try
            {
                var foundName = AssemblyName.GetAssemblyName(dllPath);
                if (foundName.Name is not null && !_assemblyPathCache.ContainsKey(foundName.Name))
                {
                    _assemblyPathCache[foundName.Name] = dllPath;
                }
            }
            catch
            {
                // Skip files that are not valid .NET assemblies
            }
        }
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        libraryPath = ProbeNativeLibrary(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }

    private string? ProbeNativeLibrary(string unmanagedDllName)
    {
        var rid = RuntimeIdentifier.Current;
        var nativeSubDir = Path.Combine("runtimes", rid, "native");

        var candidates = new List<string>();

        var ridDir = Path.Combine(_pluginDirectory, nativeSubDir);
        if (Directory.Exists(ridDir))
        {
            candidates.AddRange(Directory.GetFiles(ridDir));
        }

        var rootDir = _pluginDirectory;
        if (Directory.Exists(rootDir))
        {
            foreach (var f in Directory.GetFiles(rootDir))
            {
                var fn = Path.GetFileName(f);
                if (IsNativeLibraryMatch(fn, unmanagedDllName))
                    candidates.Add(f);
            }
        }

        foreach (var candidate in candidates)
        {
            var fileName = Path.GetFileName(candidate);
            if (IsNativeLibraryMatch(fileName, unmanagedDllName))
            {
                return candidate;
            }
        }

        if (Directory.Exists(Path.Combine(_pluginDirectory, "runtimes")))
        {
            foreach (var file in Directory.GetFiles(
                Path.Combine(_pluginDirectory, "runtimes"), "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                if (IsNativeLibraryMatch(fileName, unmanagedDllName))
                {
                    return file;
                }
            }
        }

        return null;
    }

    private static bool IsNativeLibraryMatch(string fileName, string unmanagedDllName)
    {
        var baseName = unmanagedDllName;

        if (string.Equals(fileName, baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(fileName, baseName + ".dll", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(fileName, "lib" + baseName + ".so", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(fileName, "lib" + baseName + ".dylib", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(fileName[..^4], baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static class RuntimeIdentifier
    {
        public static string Current
        {
            get
            {
                var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
                {
                    System.Runtime.InteropServices.Architecture.X64 => "x64",
                    System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
                    System.Runtime.InteropServices.Architecture.X86 => "x86",
                    System.Runtime.InteropServices.Architecture.Arm => "arm",
                    _ => "x64"
                };

                if (OperatingSystem.IsWindows()) return $"win-{arch}";
                if (OperatingSystem.IsLinux()) return $"linux-{arch}";
                if (OperatingSystem.IsMacOS()) return $"osx-{arch}";
                return $"win-{arch}";
            }
        }
    }
}
