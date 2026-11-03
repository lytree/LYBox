#!/usr/bin/env dotnet
#:property PublishAot=false

using System;
using System.IO;
using System.Text;

// ---------------------------------------------------------------------------
// AppHostPatcher —— 改写 .NET apphost 内嵌的应用 DLL 相对路径（file-based app）。
//
// 发布布局（dnSpy 风格）：
//   根目录只保留启动器可执行文件（LYBox.Launcher.Desktop / LYBox.Launcher.Console），
//   其余运行时文件集中到 bin/。
//
// apphost 默认只查找与自身同目录的 <name>.dll，因此需要把内嵌路径改写为
// bin/<name>.dll。apphost 在 SDK 生成时为应用路径预留了固定大小的空间
// （MaxPathBytes），只要新路径不越过预留区即可原地覆盖。
//
// 用法：
//   dotnet run build/AppHostPatcher.cs -- <apphost> -d <subdir>
//   dotnet run build/AppHostPatcher.cs -- <apphost> <origDllPath> <newDllPath>
//
// 示例：
//   dotnet run build/AppHostPatcher.cs -- LYBox.Launcher.Desktop.exe -d bin
// ---------------------------------------------------------------------------

if (args.Length is < 2 or > 3)
{
    PrintUsage();
    return 1;
}

var appHost = args[0];
string originalPath;
string newPath;

if (args.Length == 3 && args[1] == "-d")
{
    originalPath = GetAppDllName(appHost);
    newPath = args[2].TrimEnd('/', '\\') + GetPathSeparator(appHost) + originalPath;
}
else
{
    originalPath = args[1];
    newPath = args[2];
}

if (!File.Exists(appHost))
{
    Console.Error.WriteLine($"错误：找不到 apphost 文件 '{appHost}'");
    return 1;
}

if (originalPath.Length == 0)
{
    Console.Error.WriteLine("错误：原始 DLL 路径为空");
    return 1;
}

try
{
    if (!TryPatch(appHost, originalPath, newPath, out var error))
    {
        Console.Error.WriteLine($"错误：{error}");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误：{ex.Message}");
    return 1;
}

Console.WriteLine($"已改写：{appHost} -> {newPath}");
return 0;

// ---------------------------------------------------------------------------

static void PrintUsage()
{
    Console.Error.WriteLine("用法：");
    Console.Error.WriteLine("  apphostpatcher <apphost> -d <subdir>");
    Console.Error.WriteLine("  apphostpatcher <apphost> <origDllPath> <newDllPath>");
}

// Windows apphost 带 .exe 后缀，Unix apphost 无后缀。
// 这里按目标文件自身的扩展名决定分隔符，而不是按当前构建主机 OS——
// 允许在任意平台为另一平台产物打补丁。
static char GetPathSeparator(string appHost)
    => appHost.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? '\\' : '/';

static string GetAppDllName(string appHost)
{
    var fileName = Path.GetFileName(appHost);
    return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(fileName) + ".dll"
        : fileName + ".dll";
}

static bool TryPatch(string appHost, string originalPath, string newPath, out string error)
{
    error = string.Empty;

    var originalBytes = Encoding.UTF8.GetBytes(originalPath + "\0");
    var newBytes = Encoding.UTF8.GetBytes(newPath + "\0");

    // apphost 的应用路径预留区大小（与 SDK HostModel 一致）
    const int MaxPathBytes = 1024;
    if (originalBytes.Length > MaxPathBytes)
    {
        error = $"原始路径过长（> {MaxPathBytes} 字节）：{originalPath}";
        return false;
    }

    if (newBytes.Length > MaxPathBytes)
    {
        error = $"新路径过长（> {MaxPathBytes} 字节）：{newPath}";
        return false;
    }

    var bytes = File.ReadAllBytes(appHost);
    var offset = Find(bytes, originalBytes);
    if (offset < 0)
    {
        error = $"未找到内嵌路径 '{originalPath}'（不是 .NET apphost，或已改写）";
        return false;
    }

    // 新路径可能比旧路径更长：越界部分必须仍在 apphost 的零填充预留区内，
    // 否则会覆盖相邻数据，破坏可执行文件。
    var end = offset + Math.Max(originalBytes.Length, newBytes.Length);
    if (end > bytes.Length)
    {
        error = $"新路径超出文件末尾：{newPath}";
        return false;
    }

    for (var i = offset + Math.Min(originalBytes.Length, newBytes.Length); i < end; i++)
    {
        if (bytes[i] != 0)
        {
            error = $"内嵌路径预留空间不足（偏移 {i} 处不是零填充）：{newPath}";
            return false;
        }
    }

    Buffer.BlockCopy(newBytes, 0, bytes, offset, newBytes.Length);
    // 新路径比旧路径短时，清空残留的旧字符
    for (var i = offset + newBytes.Length; i < end; i++)
        bytes[i] = 0;

    File.WriteAllBytes(appHost, bytes);
    return true;
}

static int Find(byte[] haystack, byte[] needle)
{
    var first = needle[0];
    for (var i = 0; i <= haystack.Length - needle.Length; i++)
    {
        if (haystack[i] != first)
            continue;

        var matched = true;
        for (var j = 1; j < needle.Length; j++)
        {
            if (haystack[i + j] != needle[j])
            {
                matched = false;
                break;
            }
        }

        if (matched)
            return i;
    }

    return -1;
}
