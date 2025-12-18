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

    // Lazy extraction to temporary directory so we only write once per process
    private static readonly Lazy<string> _extractedResourceDir = new(() => FileHelpers.ExtractEmbeddedResources(typeof(ExpoResourceBuilderExtensions).Assembly, [EmbeddedDockerfileName, EmbeddedEntrypointName]));

    /// <summary>
    /// Adds and configures an Expo container resource to the distributed application
    /// builder using the provided <see cref="ExpoOptions"/>. The method will ensure
    /// embedded runtime assets (Dockerfile, entrypoint and instrumentation) are
    /// available to the build context and will register an HTTP endpoint and
    /// environment variables required for running the Expo packager in development.
    /// </summary>
    /// <param name="builder">The distributed application builder to attach the resource to.</param>
    /// <param name="options">Options that control the build context, ports and public
    /// callback URI for the Expo packager.</param>
    /// <returns>An <see cref="IResourceBuilder{ContainerResource}"/> that can be used to
    /// fluently configure the created Expo resource further.</returns>
    /// <exception cref="ArgumentException">Thrown when <see cref="ExpoOptions.BuildContext"/>
    /// is null or empty.</exception>
    public static IResourceBuilder<ContainerResource> AddExpo(
        this IDistributedApplicationBuilder builder,
        ExpoOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(options.BuildContext, nameof(options.BuildContext));

        // Ensure embedded files are extracted and then point the builder at the appropriate build context
        var resourceDir = _extractedResourceDir.Value;

        // Use the provided build context and pass the packaged Dockerfile as the override dockerfile path.
        string dockerfilePathArg = Path.GetFullPath(Path.Combine(resourceDir, EmbeddedDockerfileName));
        string contextDir = options.BuildContext;

        // If the consumer's buildContext does not include the entrypoint, create a temporary merged
        // context that contains the consumer files plus the embedded entrypoint so COPY will succeed.
        try
        {
            var entrypointPathInContext = Path.Combine(options.BuildContext, EmbeddedEntrypointName);
            if (!File.Exists(entrypointPathInContext))
            {
                var mergedTemp = Path.Combine(Path.GetTempPath(), "aspire-expo-merged", Guid.NewGuid().ToString());
                Directory.CreateDirectory(mergedTemp);

                // Copy consumer buildContext into mergedTemp
                new DirectoryInfo(options.BuildContext).CopyTo(mergedTemp);

                // Copy embedded entrypoint into mergedTemp
                var srcEntrypoint = Path.Combine(resourceDir, EmbeddedEntrypointName);
                var dstEntrypoint = Path.Combine(mergedTemp, EmbeddedEntrypointName);
                if (File.Exists(srcEntrypoint))
                {
                    File.Copy(srcEntrypoint, dstEntrypoint, overwrite: true);
                    new FileInfo(dstEntrypoint).TrySetExecutable();
                    contextDir = mergedTemp;
                }
            }
        }
        catch
        {
            // On failure, fall back to original buildContext so the orchestrator can report a clear error
            contextDir = options.BuildContext;
        }

        try
        {
            var rb = builder
                .AddDockerfile(options.ResourceName, contextDir, dockerfilePathArg)
                .WithBuildArg("EXPO_PORT", options.Port)
                .WithEnvironment(nameof(EXPO_DEV), EXPO_DEV)
                .WithEnvironment("EXPO_PUBLIC_API_URL", options.UriCallback)
                .WithEnvironment("EXPO_PACKAGER_PROXY_URL", options.UriCallback)
                .WithHttpEndpoint(options.Port, options.TargetPort);

            return rb;
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Registers a command on the resource that will generate a QR code for the Expo app's
    /// public URL and optionally open it in the default browser. The command is named
    /// <c>generate-and-open</c> and is intended to be displayed in tooling that exposes
    /// resource actions.
    /// </summary>
    /// <param name="builder">The resource builder to attach the command to.</param>
    /// <param name="publicUrlTask">An optional task that will yield the published public URL (for example from an ngrok resource). The command waits for this task when executing.</param>
    /// <param name="qrFilePath">Optional file path where the generated QR image will be written. If null the command will fail with a helpful message.</param>
    /// <returns>The same <see cref="IResourceBuilder{ContainerResource}"/> instance to allow fluent chaining.</returns>
    public static IResourceBuilder<ContainerResource> WithQrCommand(
        this IResourceBuilder<ContainerResource> builder,
        Task<Uri?>? publicUrlTask = null,
        string? qrFilePath = null)
    {
        var commandOptions = new CommandOptions
        {
            UpdateState = OnUpdateResourceState,
            IconName = "QrCode",
            IconVariant = IconVariant.Filled
        };

        builder.WithCommand(
            name: "generate-and-open",
            displayName: "Generate QR and Open",
            executeCommand: async context =>
            {
                await OnRunGenerateQrCommandAsync(publicUrlTask, qrFilePath);
                return await OnRunOpenQrInBrowserCommandAsync(qrFilePath);
            },
            commandOptions: commandOptions);

        return builder;
    }

    private static async Task<ExecuteCommandResult> OnRunGenerateQrCommandAsync(
        Task<Uri?>? publicUrlTask = null,
        string? qrFilePath = null)
    {
        try
        {
            Uri? url = null;

            if (publicUrlTask != null)
            {
                try { url = await publicUrlTask; } catch { url = null; }
            }

            if (url is null)
            {
                return CommandResults.Success();
            }

            try
            {
                if (qrFilePath == null)
                {
                    return CommandResults.Failure();
                }

                await QrUtil.GenerateAsync($"exp://{url.Host}", qrFilePath);
            }
            catch (Exception)
            {
                return CommandResults.Failure();
            }
        }
        catch (Exception)
        {
            return CommandResults.Failure();
        }

        return CommandResults.Success();
    }

    private static Task<ExecuteCommandResult> OnRunOpenQrInBrowserCommandAsync(string? qrFilePath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(qrFilePath))
            {
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
        catch (Exception)
        {
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