namespace ClipboardGuardApp;

public static partial class BuildInfo
{
    [BuildInfoAnalyzers.CompilerVisibleProperty]
    public static partial string RepositoryUrl { get; }

    [BuildInfoAnalyzers.CompilerVisibleProperty]
    public static partial string Version { get; }

    [BuildInfoAnalyzers.CompilerVisibleProperty]
    public static partial string ReleaseType { get; }


    public static void test()
    {
        var t = test;
        Console.WriteLine();
    }
}