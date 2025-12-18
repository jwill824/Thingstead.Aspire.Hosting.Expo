namespace Aspire.Hosting;

/// <summary>
/// Maps to the "Expo" section in appsettings.
/// </summary>
public record ExpoOptions
{
    /// <summary>
    /// Resource name used by the AppHost for the Expo frontend.
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Port that Expo uses (e.g. 8082).
    /// </summary>
    public int Port { get; set; } = 8082;

    /// <summary>
    /// Target port mapped by the orchestrator (e.g. 8082).
    /// </summary>
    public int TargetPort { get; set; } = 8082;

    /// <summary>
    /// Callback to generate the Expo app URI (e.g. exp://...).
    /// </summary>
    public Func<string> UriCallback { get; set; } = () => string.Empty;

    /// <summary>
    /// Path to the Docker build context (usually the consumer's project folder). Required.
    /// </summary>
    public string BuildContext { get; set; } = string.Empty;
}