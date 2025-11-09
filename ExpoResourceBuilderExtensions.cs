using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Thingstead.Aspire.Hosting.Expo.Utils;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods to add and configure an Expo container resource and related
/// commands for Aspire distributed applications. The extensions support using a Dockerfile
/// packaged with this library while using a consumer-supplied build context for application
/// source files.
/// </summary>
public static class ExpoResourceBuilderExtensions
{
    private const string EXPO_DEV = "true";

    // Names of the embedded resources (relative to the assembly manifest)
    private const string EmbeddedDockerfileName = "Dockerfile";
    private const string EmbeddedEntrypointName = "docker-entrypoint.sh";
    private const string EmbeddedBootstrapName = "otel-bootstrap.js";

    // Lazy extraction to temporary directory so we only write once per process
    private static readonly Lazy<string> _extractedResourceDir = new(() => FileHelpers.ExtractEmbeddedResources(typeof(ExpoResourceBuilderExtensions).Assembly, [EmbeddedDockerfileName, EmbeddedEntrypointName, EmbeddedBootstrapName]));

    /// <summary>
    /// Adds an Expo container resource to the distributed application. The container image is built
    /// from the Dockerfile packaged with this library, while the specified <paramref name="buildContext"/>
    /// is used as the Docker build context so the Dockerfile can COPY consumer source files. The
    /// <paramref name="urlFactory"/> is used to lazily provide the public URL value for the EXPO
    /// environment variables.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Logical name for the Expo resource.</param>
    /// <param name="urlFactory">Factory that produces the public URL used to configure the EXPO public API and proxy URLs. Evaluated lazily.</param>
    /// <param name="buildContext">Path to the Docker build context (usually the consumer's project folder). Required.</param>
    /// <param name="port">Port exposed by the container (default 8082).</param>
    /// <param name="targetPort">Target port mapped by the orchestrator (default 8082).</param>
    /// <param name="logger"></param>
    /// <returns>An <see cref="IResourceBuilder{ContainerResource}"/> for further configuration.</returns>
    public static IResourceBuilder<ContainerResource> AddExpo(
        this IDistributedApplicationBuilder builder,
        string name,
        Func<string> urlFactory,
        string buildContext,
        int port = 8082,
        int targetPort = 8082,
        Action<string>? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(buildContext);
        Action<string> log = logger ?? Console.WriteLine;

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
            log($"[expo] checking for entrypoint at '{entrypointPathInContext}'");
            if (!File.Exists(entrypointPathInContext))
            {
                log($"[expo] merging embedded entrypoint into build context for resource '{name}'");
                var mergedTemp = Path.Combine(Path.GetTempPath(), "aspire-expo-merged", Guid.NewGuid().ToString());
                Directory.CreateDirectory(mergedTemp);
                log($"[expo] created temporary merged build context at '{mergedTemp}'");

                // Copy consumer buildContext into mergedTemp
                new DirectoryInfo(buildContext).CopyTo(mergedTemp);
                log($"[expo] copied consumer build context from '{buildContext}' to '{mergedTemp}'");

                // Copy embedded entrypoint into mergedTemp
                var srcEntrypoint = Path.Combine(resourceDir, EmbeddedEntrypointName);
                var dstEntrypoint = Path.Combine(mergedTemp, EmbeddedEntrypointName);
                if (File.Exists(srcEntrypoint))
                {
                    File.Copy(srcEntrypoint, dstEntrypoint, overwrite: true);
                    // Ensure executable bit on Unix
                    new FileInfo(dstEntrypoint).TrySetExecutable();
                    contextDir = mergedTemp;
                    log($"[expo] copied embedded entrypoint to '{dstEntrypoint}'");
                }

                var srcBootstrap = Path.Combine(resourceDir, EmbeddedBootstrapName);
                var dstBootstrap = Path.Combine(mergedTemp, EmbeddedBootstrapName);
                if (File.Exists(srcBootstrap))
                {
                    File.Copy(srcBootstrap, dstBootstrap, overwrite: true);
                    log($"[expo] copied embedded OpenTelemetry bootstrap to '{dstBootstrap}'");
                }
            }
        }
        catch
        {
            // On failure, fall back to original buildContext so the orchestrator can report a clear error
            contextDir = buildContext;
            log($"[expo] failed to merge entrypoint into build context; using original build context '{buildContext}'");
        }

        try
        {
            log($"[expo] adding Expo resource '{name}' with build context '{contextDir}' and dockerfile '{dockerfilePathArg}'");
            var rb = builder
                .AddDockerfile(name, contextDir, dockerfilePathArg)
                .WithBuildArg("EXPO_PORT", port)
                .WithEnvironment(nameof(EXPO_DEV), EXPO_DEV)
                .WithEnvironment("EXPO_PUBLIC_API_URL", urlFactory)
                .WithEnvironment("EXPO_PACKAGER_PROXY_URL", urlFactory)
                .WithHttpEndpoint(port, targetPort);

            return rb;
        }
        catch (Exception ex)
        {
            log($"[expo] error adding Expo resource '{name}': {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Convenience overload that accepts a string URL (evaluated immediately). Prefer the Func overload
    /// when the URL is produced asynchronously or later (for example, from an ngrok resource).
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Logical name for the Expo resource.</param>
    /// <param name="url">The public URL used to configure the EXPO public API and proxy URLs. This value is captured at the time of the call.</param>
    /// <param name="buildContext">Path to the Docker build context (usually the consumer's project folder). Required.</param>
    /// <param name="port">Port exposed by the container (default 8082).</param>
    /// <param name="targetPort">Target port mapped by the orchestrator (default 8082).</param>
    /// <param name="logger"></param>
    /// <returns>An <see cref="IResourceBuilder{ContainerResource}"/> for further configuration.</returns>
    public static IResourceBuilder<ContainerResource> AddExpo(
        this IDistributedApplicationBuilder builder,
        string name,
        string url,
        string buildContext,
        int port = 8082,
        int targetPort = 8082,
        Action<string>? logger = null)
        => AddExpo(builder, name, () => url, buildContext, port, targetPort, logger);

    /// <summary>
    /// Registers a command on the resource that will generate a QR code for the Expo app's
    /// public URL and optionally open it in the default browser. The command is named
    /// <c>generate-and-open</c> and is intended to be displayed in tooling that exposes
    /// resource actions.
    /// </summary>
    /// <param name="builder">The resource builder to attach the command to.</param>
    /// <param name="publicUrlTask">An optional task that will yield the published public URL (for example from an ngrok resource). The command waits for this task when executing.</param>
    /// <param name="qrFilePath">Optional file path where the generated QR image will be written. If null the command will fail with a helpful message.</param>
    /// <param name="infoLogger">Optional logger for informational messages produced by the command. Defaults to <see cref="Console.WriteLine(string)"/>.</param>
    /// <param name="errorLogger">Optional logger for error messages produced by the command. Defaults to <see cref="Console.WriteLine(string)"/>.</param>
    /// <returns>The same <see cref="IResourceBuilder{ContainerResource}"/> instance to allow fluent chaining.</returns>
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