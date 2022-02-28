namespace Brics.FileProviders;

/// <summary>
/// Implements a virtual file source for the virtual file provider.
/// </summary>
public interface IVirtualFile : IEquatable<IVirtualFile> {
    long Length { get; }
    DateTimeOffset LastModified { get; }
    Stream CreateReadStream();
}