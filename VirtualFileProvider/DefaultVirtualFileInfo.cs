namespace Brics.FileProviders;

public class DefaultVirtualFileInfo<TVirtualFile> : IVirtualFileInfo<TVirtualFile> where TVirtualFile : IVirtualFile {
    public TVirtualFile Source { get; }

    public DefaultVirtualFileInfo(string path, TVirtualFile virtualFile) {
        Name = Path.GetFileName(path);
        Source = virtualFile;
    }

    public bool Exists { get; } = true;
    public bool IsDirectory { get; } = false;

    public DateTimeOffset LastModified => Source.LastModified;

    public long Length => Source.Length;

    public string Name { get; }

    public string? PhysicalPath { get; } = default;

    public Stream CreateReadStream() => Source.CreateReadStream();
}