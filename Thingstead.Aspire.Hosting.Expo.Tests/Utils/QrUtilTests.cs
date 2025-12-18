using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Aspire.Hosting;
using Xunit;

namespace Thingstead.Aspire.Hosting.Expo.Tests.Utils;

public class QrUtilTests
{
    [Fact]
    public async Task GenerateAsync_CreatesPngFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "aspire-qr-" + Guid.NewGuid() + ".png");
        try
        {
            var asm = typeof(ExpoOptions).Assembly;
            var qrType = asm.GetType("Thingstead.Aspire.Hosting.Expo.Utils.QrUtil", throwOnError: true, ignoreCase: false) ?? throw new InvalidOperationException("QrUtil type not found");
            var method = qrType.GetMethod("GenerateAsync", BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("GenerateAsync not found");

            var invokeResult = method.Invoke(null, ["https://example.com", tempFile]);
            if (invokeResult is not Task<string> taskObj)
                throw new InvalidOperationException("GenerateAsync did not return Task<string>");
            var resultPath = await taskObj;

            Assert.Equal(tempFile, resultPath);
            Assert.True(File.Exists(tempFile));
            Assert.True(new FileInfo(tempFile).Length > 0);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }
}
