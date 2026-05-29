using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;

namespace Aspire.SLA.test;

[ExcludeFromCodeCoverage]
internal sealed class StubResource : IResource
{
    private readonly ResourceAnnotationCollection _annotations = new();

    public StubResource(string name) => Name = name;

    public string Name { get; }

    ResourceAnnotationCollection IResource.Annotations => _annotations;

    internal void AddAnnotation<T>(T annotation) where T : IResourceAnnotation =>
        _annotations.Add(annotation);
}
