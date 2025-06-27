# BuildInfoAnalyzers: Usage Guide

BuildInfoAnalyzers provides Roslyn source generators for embedding build and version metadata in your application, and for automatic method wrapping.

---

## 1. CompilerVisibleProperty Attribute

Define your build info class as follows:

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
- You can add more `[CompilerVisibleProperty]` properties as needed.

---

## 2. Wrapper Generator (`WrapperAttribute`)

- Use `[Wrapper(typeof(MyWrapper))]` on partial methods to generate wrappers for logging, diagnostics, etc.
- See the main README for details and examples.

---

## Versioning & Build Metadata
- Follows [Semantic Versioning 2.0.0](https://semver.org/)
- Build date, commit, branch, and release type are set automatically by MSBuild or CI

---

## Best Practices
- Use the GitHub Actions workflow for reproducible builds
- Always check the About dialog or logs for build info in your app
- For more, see the main README and release notes
