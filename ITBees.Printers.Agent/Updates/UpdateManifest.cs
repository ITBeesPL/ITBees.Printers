namespace ITBees.Printers.Agent.Updates;

/// <summary>
/// latestversion.json - published by the build next to the agent's exe (see README, "Aktualizacje
/// agenta"). Only <see cref="Version"/> and one of <see cref="FileName"/> / <see cref="DownloadUrl"/>
/// are required; size and SHA-256 are checked when present.
/// </summary>
public sealed class UpdateManifest
{
    public string? Version { get; set; }

    /// <summary>Name of the exe, next to the manifest ("ITBeesFastPrintAgent.exe").</summary>
    public string? FileName { get; set; }

    /// <summary>Full or relative address of the exe - takes precedence over <see cref="FileName"/>.</summary>
    public string? DownloadUrl { get; set; }

    public long? Size { get; set; }

    public string? Sha256 { get; set; }

    public string? PublishedAt { get; set; }
}

/// <summary>A published version newer than the running one.</summary>
public sealed record AvailableUpdate(string Version, Uri DownloadUrl, long? Size, string? Sha256);

/// <summary>The new exe, downloaded and checked, waiting next to the running one to install itself.</summary>
public sealed record DownloadedUpdate(string File, string Version);

public readonly record struct DownloadProgress(long Received, long? Total);

/// <summary>An update that cannot go on; the message is shown to the user as it is.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message)
    {
    }
}
