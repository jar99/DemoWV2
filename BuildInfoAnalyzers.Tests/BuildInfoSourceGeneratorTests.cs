﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BuildInfoAnalyzers.Tests;

[TestFixture]
public class BuildInfoSourceGeneratorTests
{
    // This is the source code for the attribute, matching the one in the generator.
    private const string AttributeSource = @"
using System;
namespace BuildInfoAnalyzers
{
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    internal sealed class CompilerVisiblePropertyAttribute : Attribute
    {
        public CompilerVisiblePropertyAttribute() { }
    }
}
";

    [Test]
    public void GenerateSource_WithValidProperties_GeneratesCorrectCode()
    {
        // Arrange
        var userSource = @"
using BuildInfoAnalyzers;
namespace MyTestApp {
    public static partial class BuildInfo {
        [CompilerVisibleProperty] public static partial string Version { get; }
        [CompilerVisibleProperty] public static partial string ProductName { get; }
        [CompilerVisibleProperty] public static partial int Year { get; }
        [CompilerVisibleProperty] public static partial bool IsBeta { get; }
    }
}";
        var buildProperties = new Dictionary<string, string>
        {
            { "build_property.Version", "1.2.3-alpha" },
            { "build_property.ProductName", "Super Awesome App" },
            { "build_property.Year", "2025" },
            { "build_property.IsBeta", "true" }
        };
        var options = new TestAnalyzerConfigOptions(buildProperties);
        var compilation = CreateCompilation(userSource);
        var buildInfoClassSymbol = compilation.GetTypeByMetadataName("MyTestApp.BuildInfo");
        Assert.That(buildInfoClassSymbol, Is.Not.Null);

        // Act
        string generatedSource = BuildInfoSourceGenerator.GenerateSource(buildInfoClassSymbol, options);

        // Assert
        var expectedSource = @"
// <auto-generated/>
namespace MyTestApp
{
    public static partial class BuildInfo
    {
        public static partial string Version => ""1.2.3-alpha"";
        public static partial string ProductName => ""Super Awesome App"";
        public static partial int Year => 2025;
        public static partial bool IsBeta => true;
    }
}";
        Assert.That(
            TestUtils.NormalizeWhitespace(generatedSource),
            Is.EqualTo(TestUtils.NormalizeWhitespace(expectedSource)));
    }

    [Test]
    public void GenerateSource_WithMissingProperties_GeneratesOnlyAvailableProperties()
    {
        // Arrange
        var userSource = @"
using BuildInfoAnalyzers;
namespace MyTestApp {
    public static partial class BuildInfo {
        [CompilerVisibleProperty] public static partial string Version { get; }
        [CompilerVisibleProperty] public static partial string ProductName { get; }
    }
}";
        // Only provide one of the two properties.
        var buildProperties = new Dictionary<string, string>
        {
            { "build_property.Version", "1.0.0" }
        };
        var options = new TestAnalyzerConfigOptions(buildProperties);
        var compilation = CreateCompilation(userSource);
        var buildInfoClassSymbol = compilation.GetTypeByMetadataName("MyTestApp.BuildInfo");
        Assert.That(buildInfoClassSymbol, Is.Not.Null);

        // Act
        string generatedSource = BuildInfoSourceGenerator.GenerateSource(buildInfoClassSymbol, options);

        // Assert
        var expectedSource = @"
// <auto-generated/>
namespace MyTestApp
{
    public static partial class BuildInfo
    {
        public static partial string Version => ""1.0.0"";
    }
}";
        Assert.That(
            TestUtils.NormalizeWhitespace(generatedSource),
            Is.EqualTo(TestUtils.NormalizeWhitespace(expectedSource)));
    }

    [Test]
    public void GenerateSource_WithNoMatchingBuildProperties_GeneratesEmptyClassBody()
    {
        // Arrange
        var userSource = @"
using BuildInfoAnalyzers;
namespace MyTestApp {
    public static partial class BuildInfo {
        [CompilerVisibleProperty] public static partial string Version { get; }
    }
}";
        // Provide no matching build properties.
        var buildProperties = new Dictionary<string, string>();
        var options = new TestAnalyzerConfigOptions(buildProperties);
        var compilation = CreateCompilation(userSource);
        var buildInfoClassSymbol = compilation.GetTypeByMetadataName("MyTestApp.BuildInfo");
        Assert.That(buildInfoClassSymbol, Is.Not.Null);

        // Act
        string generatedSource = BuildInfoSourceGenerator.GenerateSource(buildInfoClassSymbol, options);

        // Assert
        var expectedSource = @"
// <auto-generated/>
namespace MyTestApp
{
    public static partial class BuildInfo
    {
    }
}";
        Assert.That(
            TestUtils.NormalizeWhitespace(generatedSource),
            Is.EqualTo(TestUtils.NormalizeWhitespace(expectedSource)));
    }

    [Test]
    public void GenerateSource_WithNoAttributedProperties_GeneratesEmptyClassBody()
    {
        // Arrange
        var userSource = @"
namespace MyTestApp {
    // No attributes on properties
    public static partial class BuildInfo {
        public static partial string Version { get; }
    }
}";
        var buildProperties = new Dictionary<string, string> { { "build_property.Version", "1.0.0" } };
        var options = new TestAnalyzerConfigOptions(buildProperties);
        var compilation = CreateCompilation(userSource);
        var buildInfoClassSymbol = compilation.GetTypeByMetadataName("MyTestApp.BuildInfo");
        Assert.That(buildInfoClassSymbol, Is.Not.Null);

        // Act
        string generatedSource = BuildInfoSourceGenerator.GenerateSource(buildInfoClassSymbol, options);

        // Assert
        var expectedSource = @"
// <auto-generated/>
namespace MyTestApp
{
    public static partial class BuildInfo
    {
    }
}";
        Assert.That(
            TestUtils.NormalizeWhitespace(generatedSource),
            Is.EqualTo(TestUtils.NormalizeWhitespace(expectedSource)));
    }

    [Test]
    public void GenerateSource_WithValueContainingQuotes_GeneratesCorrectlyEscapedString()
    {
        // Arrange
        var userSource = @"
using BuildInfoAnalyzers;
namespace MyTestApp {
    public static partial class BuildInfo {
        [CompilerVisibleProperty] public static partial string CompanyName { get; }
    }
}";
        var buildProperties = new Dictionary<string, string>
        {
            { "build_property.CompanyName", @"The ""Awesome"" Company" }
        };
        var options = new TestAnalyzerConfigOptions(buildProperties);
        var compilation = CreateCompilation(userSource);
        var buildInfoClassSymbol = compilation.GetTypeByMetadataName("MyTestApp.BuildInfo");
        Assert.That(buildInfoClassSymbol, Is.Not.Null);

        // Act
        string generatedSource = BuildInfoSourceGenerator.GenerateSource(buildInfoClassSymbol, options);

        // Assert
        var expectedSource = @"
// <auto-generated/>
namespace MyTestApp
{
    public static partial class BuildInfo
    {
        public static partial string CompanyName => ""The \""Awesome\"" Company"";
    }
}";
        Assert.That(
            TestUtils.NormalizeWhitespace(generatedSource),
            Is.EqualTo(TestUtils.NormalizeWhitespace(expectedSource)));
    }

    /// <summary>
    /// Creates a C# compilation from source code, ensuring the attribute is included.
    /// </summary>
    private static Compilation CreateCompilation(string source)
    {
        // The test compilation MUST include the attribute source text.
        return CSharpCompilation.Create("TestCompilation",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(AttributeSource) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

/// <summary>
/// A helper class to provide a test implementation of AnalyzerConfigOptions.
/// </summary>
internal class TestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly ImmutableDictionary<string, string> _options;

    public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options)
    {
        _options = options.ToImmutableDictionary();
    }

    public override bool TryGetValue(string key, out string value)
    {
        return _options.TryGetValue(key, out value);
    }
}

/// <summary>
/// A helper class with utility methods for testing.
/// </summary>
internal static class TestUtils
{
    /// <summary>
    /// Normalizes whitespace in a string to make comparisons easier.
    /// </summary>
    public static string NormalizeWhitespace(string text)
    {
        return string.Join(" ",
            text.Split(new[] { ' ', '\r', '\n', '\t' }, System.StringSplitOptions.RemoveEmptyEntries));
    }
}