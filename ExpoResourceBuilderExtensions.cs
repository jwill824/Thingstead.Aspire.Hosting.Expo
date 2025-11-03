using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Thingstead.Aspire.Hosting.Expo.Utils;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding and configuring an <see cref="ExpoResource"/> in an
/// <see cref="IDistributedApplicationBuilder"/>. These are placed in the <c>Aspire.Hosting</c>
/// namespace for discoverability when referencing the Aspire hosting packages.
/// </summary>

public static class ExpoResourceBuilderExtensions
{
    private const string EXPO_DEV = "true";

    // Names of the embedded resources (relative to the assembly manifest)
    private const string EmbeddedDockerfileName = "Dockerfile";
    private const string EmbeddedEntrypointName = "docker-entrypoint.sh";

    // Lazy extraction to temporary directory so we only write once per process
    private static readonly Lazy<string> _extractedResourceDir = new(() => FileHelpers.ExtractEmbeddedResources(typeof(ExpoResourceBuilderExtensions).Assembly, [EmbeddedDockerfileName, EmbeddedEntrypointName]));

    /// <summary>
    /// 
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="name"></param>
    /// <param name="url">Public URL used to configure the EXPO public API and proxy URLs.</param>
    /// <param name="buildContext">Path to the build context (directory) for Docker builds (required). The consumer's folder will be used as the build context and the packaged Dockerfile will be passed as the override dockerfile path.</param>
    /// <param name="port"></param>
    /// <param name="targetPort"></param>
    /// <returns></returns>
    public static IResourceBuilder<ContainerResource> AddExpo(
        this IDistributedApplicationBuilder builder,
        string name,
        string url,
        string buildContext,
        int port = 8082,
        int targetPort = 8082)
    {
        ArgumentException.ThrowIfNullOrEmpty(buildContext);

        // Ensure embedded files are extracted and then point the builder at the appropriate build context
        var resourceDir = _extractedResourceDir.Value;
        // Use the provided build context and pass the packaged Dockerfile as the override dockerfile path.
        string dockerfilePathArg = Path.GetFullPath(Path.Combine(resourceDir, EmbeddedDockerfileName));
        string contextDir = buildContext;

        // If the consumer's buildContext does not include the entrypoint, create a temporary merged
        // context that contains the consumer files plus the embedded entrypoint so COPY will succeed.
        try
        {
            var entrypointPathInContext = Path.Combine(buildContext, EmbeddedEntrypointName);
            if (!File.Exists(entrypointPathInContext))
            {
                var mergedTemp = Path.Combine(Path.GetTempPath(), "aspire-expo-merged", Guid.NewGuid().ToString());
                Directory.CreateDirectory(mergedTemp);

                // Copy consumer buildContext into mergedTemp
                new DirectoryInfo(buildContext).CopyTo(mergedTemp);

                // Copy embedded entrypoint into mergedTemp
                var srcEntrypoint = Path.Combine(resourceDir, EmbeddedEntrypointName);
                var dstEntrypoint = Path.Combine(mergedTemp, EmbeddedEntrypointName);
                if (File.Exists(srcEntrypoint))
                {
                    File.Copy(srcEntrypoint, dstEntrypoint, overwrite: true);
                    // Ensure executable bit on Unix
                    new FileInfo(dstEntrypoint).TrySetExecutable();
                    contextDir = mergedTemp;
                }
            }
        }
        catch
        {
            // On failure, fall back to original buildContext so the orchestrator can report a clear error
            contextDir = buildContext;
        }

        var rb = builder
            .AddDockerfile(name, contextDir, dockerfilePathArg)
            .WithBuildArg("EXPO_PORT", port)
            .WithEnvironment(nameof(EXPO_DEV), EXPO_DEV)
            .WithEnvironment("EXPO_PUBLIC_API_URL", () => url)
            .WithEnvironment("EXPO_PACKAGER_PROXY_URL", () => url)
            .WithHttpEndpoint(port: port, targetPort: targetPort);

        return rb;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="publicUrlTask"></param>
    /// <param name="qrFilePath"></param>
    /// <param name="infoLogger"></param>
    /// <param name="errorLogger"></param>
    /// <returns></returns>
    public static IResourceBuilder<ContainerResource> WithQrCommand(
        this IResourceBuilder<ContainerResource> builder,
        Task<Uri?>? publicUrlTask = null,
        string? qrFilePath = null,
        Action<string>? infoLogger = null,
        Action<string>? errorLogger = null)
    {
        var commandOptions = new CommandOptions
        {
            UpdateState = OnUpdateResourceState,
            IconName = "QrCode",
            IconVariant = IconVariant.Filled
        };

        Action<string> info = infoLogger ?? (m => Console.WriteLine(m));
        Action<string> error = errorLogger ?? (m => Console.WriteLine(m));

        builder.WithCommand(
            name: "generate-and-open",
            displayName: "Generate QR and Open",
            executeCommand: async context =>
            {
                await OnRunGenerateQrCommandAsync(info, error, publicUrlTask, qrFilePath);
                return await OnRunOpenQrInBrowserCommandAsync(error, qrFilePath);
            },
            commandOptions: commandOptions);

        return builder;
    }

    private static async Task<ExecuteCommandResult> OnRunGenerateQrCommandAsync(
        Action<string> info,
        Action<string> error,
        Task<Uri?>? publicUrlTask = null,
        string? qrFilePath = null)
    {

        info("Waiting for ngrok to publish public URL...");

        try
        {
            Uri? url = null;

            if (publicUrlTask != null)
            {
                try { url = await publicUrlTask; } catch { url = null; }
            }

            if (url is null)
            {
                error("Ngrok did not publish a public URL within the timeout");
                return CommandResults.Success();
            }

            info("Ngrok public URL: " + url);

            try
            {
                if (qrFilePath == null)
                {
                    error("QR file path not provided.");
                    return CommandResults.Failure();
                }

                await QrUtil.GenerateAsync($"exp://{url.Host}", qrFilePath);
                info($"Generated QR for exp://{url.Host} at {qrFilePath}");
            }
            catch (Exception ex)
            {
                error($"Failed to generate QR: {ex.Message}");
                return CommandResults.Failure();
            }
        }
        catch (Exception ex)
        {
            error($"Error waiting for ngrok public URL: {ex.Message}");
            return CommandResults.Failure();
        }

        return CommandResults.Success();
    }

    private static Task<ExecuteCommandResult> OnRunOpenQrInBrowserCommandAsync(Action<string> error, string? qrFilePath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(qrFilePath))
            {
                error("QR file path not provided for open-qr-browser command.");
                return Task.FromResult(CommandResults.Failure());
            }

            var fileUri = new Uri(qrFilePath).AbsoluteUri;
            var psi = new ProcessStartInfo
            {
                FileName = fileUri,
                UseShellExecute = true
            };

            Process.Start(psi);
            return Task.FromResult(CommandResults.Success());
        }
        catch (Exception ex)
        {
            error($"Failed to open QR in browser: {ex.Message}");
            return Task.FromResult(CommandResults.Failure());
        }
    }

    private static ResourceCommandState OnUpdateResourceState(
        UpdateCommandStateContext context)
    {
        return context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
            ? ResourceCommandState.Enabled
            : ResourceCommandState.Disabled;
    }
}