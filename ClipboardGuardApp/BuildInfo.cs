using BuildInfoAnalyzers;

namespace ClipboardGuardApp;

/// <summary>
/// Example build info class using the CompilerVisibleProperty attribute.
/// </summary>
public static partial class BuildInfo
{
    [CompilerVisibleProperty] public static partial string Version { get; }

    [CompilerVisibleProperty] public static partial string BuildDate { get; }

    [CompilerVisibleProperty] public static partial string RepositoryUrl { get; }

    [CompilerVisibleProperty] public static partial string ReleaseType { get; }
}