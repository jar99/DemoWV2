# BuildInfo Analyzers

This package provides Roslyn analyzers to expose MSBuild properties at compile time via a `BuildInfo` class.

## Usage

1.  Install the `BuildInfoAnalyzers` NuGet package.
2.  Add properties to your `.csproj` file within a `CompilerVisibleProperty` item group.
3.  Define a partial `BuildInfo` class with properties decorated with `[CompilerVisibleProperty]`.
4.  Use the code fix to automatically generate the property implementations in the `BuildInfo` class.