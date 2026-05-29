using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.test;

/// <summary>
/// A minimal <see cref="IResource"/> stub for unit testing providers
/// without requiring the full .NET Aspire hosting infrastructure.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class StubResource : IResource
{
    private readonly ResourceAnnotationCollection _annotations = new();

    public StubResource(string name)
    {
        Name = name;
    }

    public string Name { get; }

    ResourceAnnotationCollection IResource.Annotations => _annotations;

    /// <summary>
    /// Adds an annotation to this resource. Convenience method for test setup.
    /// </summary>
    public void AddAnnotation<T>(T annotation) where T : IResourceAnnotation
    {
        _annotations.Add(annotation);
    }
}
