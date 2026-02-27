using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace openLuo.Infrastructure.Database;

internal static class SqliteVecExtensionLoader
{
    public static string ResolveExtensionPath(string baseDir, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return ResolveDefaultPath(baseDir);

        var candidate = ExpandHome(configuredPath);

        if (!Path.IsPathRooted(candidate))
            candidate = Path.GetFullPath(Path.Combine(baseDir, candidate));

        if (Directory.Exists(candidate))
            candidate = Path.Combine(candidate, GetLibraryFileName());

        if (!Path.HasExtension(candidate))
            candidate += Path.GetExtension(GetLibraryFileName());

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"sqlite-vec 扩展文件不存在: '{candidate}'。请检查 config.sqliteVec.extensionPath。"
            );
        }

        return candidate;
    }

    public static void Load(SqliteConnection connection, string extensionPath)
    {
        try
        {
            connection.EnableExtensions(true);
            connection.LoadExtension(extensionPath, "sqlite3_vec_init");
            connection.EnableExtensions(false);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT vec_version()";
            cmd.ExecuteScalar();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"加载 sqlite-vec 扩展失败: '{extensionPath}'。请确认扩展与当前平台兼容。", ex);
        }
    }

    private static string ResolveDefaultPath(string baseDir)
    {
        var libraryFileName = GetLibraryFileName();
        var rid = GetRuntimeIdentifier();
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "native", "sqlite-vec", rid, libraryFileName)),
            Path.GetFullPath(Path.Combine(baseDir, "..", "native", "sqlite-vec", rid, libraryFileName)),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "native", "sqlite-vec", rid, libraryFileName)),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "native", "sqlite-vec", rid, libraryFileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "native", "sqlite-vec", rid, libraryFileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "openLuo", "native", "sqlite-vec", rid, libraryFileName)),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException(
            $"未找到 sqlite-vec 扩展。已尝试路径: {string.Join(", ", candidates)}。" +
            "请在 config.sqliteVec.extensionPath 指定扩展文件路径。"
        );
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);

        return path;
    }

    private static string GetLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "vec0.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "vec0.dylib";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "vec0.so";

        throw new PlatformNotSupportedException(
            $"不支持的操作系统: {RuntimeInformation.OSDescription}");
    }

    private static string GetRuntimeIdentifier()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"sqlite-vec 暂不支持当前架构: {RuntimeInformation.ProcessArchitecture}")
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return $"linux-{arch}";

        throw new PlatformNotSupportedException(
            $"不支持的操作系统: {RuntimeInformation.OSDescription}");
    }
}
