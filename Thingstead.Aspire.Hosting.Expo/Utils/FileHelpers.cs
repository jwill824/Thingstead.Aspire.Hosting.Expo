using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Thingstead.Aspire.Hosting.Expo.Utils;

internal static class FileHelpers
{
    public static string ExtractEmbeddedResources(Assembly asm, string[] resourceNames)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "aspire-expo-resources", asm.GetName().Name ?? "expo");
            Directory.CreateDirectory(tempDir);

            foreach (var resourceName in resourceNames)
            {
                var manifestNames = asm.GetManifestResourceNames();
                string? match = manifestNames.FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    var candidate = Path.Combine(AppContext.BaseDirectory ?? string.Empty, resourceName);
                    if (File.Exists(candidate))
                    {
                        File.Copy(candidate, Path.Combine(tempDir, resourceName), overwrite: true);
                    }
                    continue;
                }

                using var stream = asm.GetManifestResourceStream(match)!;
                if (stream is null)
                    continue;

                var outPath = Path.Combine(tempDir, resourceName);
                using var fs = File.Create(outPath);
                stream.CopyTo(fs);

                // Try to set executable bit on Unix-like systems
                TrySetExecutable(outPath);
            }

            return tempDir;
        }
        catch
        {
            return ".";
        }
    }

    public static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var nextTargetSubDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, nextTargetSubDir);
        }
    }

    /// <summary>
    /// Attempts to set the executable bit on Unix-like platforms for the given file path.
    /// No-op on Windows. Swallows exceptions to avoid breaking file-copy flows.
    /// </summary>
    public static void TrySetExecutable(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            var psi = new ProcessStartInfo("chmod", $"+x {path}") { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
        }
        catch
        {
            // ignore
        }
    }
}

/// <summary>
/// Extension methods for DirectoryInfo and FileInfo to provide fluent helpers.
/// </summary>
public static class FileSystemExtensions
{
    /// <summary>
    /// Copies the directory to the destination path (recursively).
    /// </summary>
    public static void CopyTo(this DirectoryInfo src, string destinationDir)
    {
        FileHelpers.CopyDirectory(src.FullName, destinationDir);
    }

    /// <summary>
    /// Attempts to set the executable bit on Unix-like platforms for the file.
    /// </summary>
    public static void TrySetExecutable(this FileInfo file)
    {
        FileHelpers.TrySetExecutable(file.FullName);
    }
}
