using QRCoder;

namespace Thingstead.Aspire.Hosting.Expo.Utils
{
    internal static class QrUtil
    {
        public static Task<string> GenerateAsync(string url, string outPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(outPath, nameof(outPath));

            using var generator = new QRCodeGenerator();
            using var qrData = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(qrData).GetGraphic(20);
            if (png == null || png.Length == 0)
            {
                throw new InvalidOperationException("QR generator returned empty PNG bytes");
            }

            File.WriteAllBytes(outPath, png);

            return Task.FromResult(outPath);
        }
    }
}
