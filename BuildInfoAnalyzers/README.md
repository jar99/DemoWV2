# BuildInfoAnalyzers

BuildInfoAnalyzers provides Roslyn source generators for:
- Exposing build and version information to your code and analyzers (with full support for Semantic Versioning 2.0.0)
- Automatic method wrapping for logging, diagnostics, or cross-cutting concerns

## Features

- **CompilerVisiblePropertyAttribute**: Mark static properties in your build info class to expose them to analyzers and source generators. Supports dynamic build metadata (version, build date, commit, etc.) set by MSBuild or CI.
- **WrapperAttribute**: Automatically generate method wrappers for logging, timing, or error handling using your own wrapper class.

## Quick Start

1. **Install**
   - Add the `BuildInfoAnalyzers` NuGet package to your project.

2. **Expose Build Info**
   - Define your build info class as follows:

     ```csharp
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
     ```

   - These properties are automatically populated at build time from MSBuild/CI properties.

3. **Wrap Methods**
   - Add a partial method and decorate it with `[Wrapper(typeof(MyWrapper))]`.
   - Implement your wrapper class with static `OnEnter`, `OnExit`, and `OnError` methods.
   - Example:

     ```csharp
     [Wrapper(typeof(MyWrapper))]
     public partial void MyMethod(int value);
     ```

## Versioning & Build Metadata
- Follows [Semantic Versioning 2.0.0](https://semver.org/)
- Build date, commit, branch, and release type are set automatically by MSBuild or CI

## Requirements
- C# 9.0 or later
- .NET 5.0 or later

## License

MIT License. See [LICENSE](../LICENSE).