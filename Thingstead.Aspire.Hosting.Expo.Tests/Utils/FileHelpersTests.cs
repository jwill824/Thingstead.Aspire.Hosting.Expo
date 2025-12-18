using System;
using System.IO;
using System.Reflection;
using Aspire.Hosting;
using Thingstead.Aspire.Hosting.Expo.Utils;
using Xunit;

namespace Thingstead.Aspire.Hosting.Expo.Tests.Utils;

public class FileHelpersTests
{
    [Fact]
    public void FileSystemExtensions_CopyTo_CopiesDirectoryRecursively()
    {
        var src = Path.Combine(Path.GetTempPath(), "aspire-src-" + Guid.NewGuid());
        var dst = Path.Combine(Path.GetTempPath(), "aspire-dst-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "root.txt"), "root");
            Directory.CreateDirectory(Path.Combine(src, "nested"));
            File.WriteAllText(Path.Combine(src, "nested", "child.txt"), "child");

            var di = new DirectoryInfo(src);
            di.CopyTo(dst);

            Assert.True(Directory.Exists(dst));
            Assert.True(File.Exists(Path.Combine(dst, "root.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "nested", "child.txt")));
        }
        finally
        {
            try { Directory.Delete(src, true); } catch { }
            try { Directory.Delete(dst, true); } catch { }
        }
    }

    [Fact]
    public void FileHelpers_CopyDirectory_Reflection_Works()
    {
        var src = Path.Combine(Path.GetTempPath(), "aspire-src2-" + Guid.NewGuid());
        var dst = Path.Combine(Path.GetTempPath(), "aspire-dst2-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "a.txt"), "a");
            Directory.CreateDirectory(Path.Combine(src, "sub"));
            File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "b");

            var asm = typeof(ExpoOptions).Assembly;
            var fhType = asm.GetType("Thingstead.Aspire.Hosting.Expo.Utils.FileHelpers", throwOnError: true, ignoreCase: false);
            if (fhType == null)
                throw new InvalidOperationException("FileHelpers type not found");
            var method = fhType.GetMethod("CopyDirectory", BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("CopyDirectory not found");
            method.Invoke(null, [src, dst]);

            Assert.True(Directory.Exists(dst));
            Assert.True(File.Exists(Path.Combine(dst, "a.txt")));
            Assert.True(File.Exists(Path.Combine(dst, "sub", "b.txt")));
        }
        finally
        {
            try { Directory.Delete(src, true); } catch { }
            try { Directory.Delete(dst, true); } catch { }
        }
    }
}
