using System.Collections;
using System.Collections.Concurrent;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Primitives;

namespace Brics.FileProviders;

/// <summary>
/// Implements <c>Microsoft.Extensions.FileProviders.IFileProvider</c> for using virtual files.
/// </summary>
public class VirtualFileProvider<TVirtualFile> : IFileProvider, IEnumerable<KeyValuePair<string, IVirtualFileInfo<TVirtualFile>>> where TVirtualFile : IVirtualFile {
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _listeners = new();
    private readonly ConcurrentDictionary<string, IVirtualFileInfo<TVirtualFile>> _files;

    private readonly bool _caseSensitive;
    private readonly IEqualityComparer<TVirtualFile> _virtualFileComparer;

    private readonly StringComparer _sorter;

    public VirtualFileProvider(bool caseSensitive = true, IEqualityComparer<TVirtualFile>? virtualFileComparer = default) {
        _caseSensitive = caseSensitive;
        _virtualFileComparer = virtualFileComparer ?? EqualityComparer<TVirtualFile>.Default;
        _sorter = caseSensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _files = new(_sorter);
    }

    public IChangeToken Watch(string filter) {
        var cts = _listeners.GetOrAdd(filter, x => new());
        return new CancellationChangeToken(cts.Token);
    }

    public bool TryGetData(string path, out TVirtualFile? virtualFile) {
        if(!_files.TryGetValue(path, out var vf)) {
            virtualFile = default;
            return false;
        }

        virtualFile = vf.Source;
        return true;
    }

    public void Clear(bool skipNotify = false) {
        if (skipNotify) {
            var keys = _files.Keys.ToArray();
            if (keys.Length <= 0) return;
            _files.Clear();
            NotifyListeners(keys);
        } else {
            _files.Clear();
        }
    }

    public bool Add(string path, TVirtualFile virtualFile, bool skipNotify = false) {
        var file = CreateFileInfo(path, virtualFile);
        
        var resultFile = _files.AddOrUpdate(path, file, (x, y) => {
            return _virtualFileComparer.Equals(y.Source, virtualFile) ? y : file;
        });

        var updated = file == resultFile;

        if (!skipNotify && updated) {
            NotifyListeners(new[] {path});
        }

        return updated;
    }

    public bool Remove(string path, bool skipNotify = false) {
        var removed = _files.TryRemove(path, out _);
        var notify = removed && !skipNotify;
        
        if (notify) {
            NotifyListeners(new[] {path});
        }

        return removed;
    }

    public bool RemoveMany(IEnumerable<string> paths, bool skipNotify = false) {
        var removedFiles = new HashSet<string>(_caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths) {
            if (_files.TryRemove(path, out _)) {
                removedFiles.Add(path);
            }
        }

        if (!skipNotify && removedFiles.Count > 0) {
            NotifyListeners(removedFiles);
        }

        return removedFiles.Count > 0;
    }

    protected void NotifyListeners(ICollection<string> paths) {
        var keys = _listeners.Keys.ToArray();

        foreach (var key in keys) {
            var matcher = new Matcher(_caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(key);

            if (matcher.Match("/", paths).HasMatches) {
                if (_listeners.TryRemove(key, out var value)) {
                    value.Cancel();
                }
            }
        }
    }

    public IDirectoryContents GetDirectoryContents(string subpath) {
        subpath = subpath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        if (string.IsNullOrWhiteSpace(subpath)) {
            subpath = Path.DirectorySeparatorChar.ToString();
        }

        if (subpath[0] != Path.DirectorySeparatorChar && subpath[0] != Path.AltDirectorySeparatorChar) {
            subpath = Path.AltDirectorySeparatorChar + subpath;
        }
        
        if (subpath[subpath.Length - 1] != Path.DirectorySeparatorChar && subpath[subpath.Length - 1] != Path.AltDirectorySeparatorChar) {
            subpath += Path.AltDirectorySeparatorChar;
        }

        var subPathParts = subpath.Split(new[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}, StringSplitOptions.RemoveEmptyEntries);
        
        var files = new List<IFileInfo>();
        var dirs = new HashSet<string>(_caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _files.OrderBy(x => x.Key, _sorter)) {
            if (!entry.Key.StartsWith(subpath, _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var partialName = entry.Key.Substring(subpath.Length - 1);
            var partialNameParts = partialName.Split(new[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar}, StringSplitOptions.RemoveEmptyEntries);

            var folders = new Span<string>(partialNameParts, 0, partialNameParts.Length - 1);

            if (folders.Length == 0) {
                files.Add(entry.Value);
                continue;
            }

            var subFolder = string.Join(Path.DirectorySeparatorChar.ToString(), subPathParts.Union(new[] {folders[0]}));

            if (dirs.Contains(subFolder)) {
                continue;
            }

            dirs.Add(subFolder);
            files.Add(new VirtualDirectoryFileInfo(folders[0], true));
        }

        return new VirtualDirectoryContents(files);
    }

    public IFileInfo GetFileInfo(string subpath) {
        subpath = subpath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        if (subpath[0] != Path.DirectorySeparatorChar && subpath[0] != Path.AltDirectorySeparatorChar) {
            subpath = Path.AltDirectorySeparatorChar + subpath;
        }
        
        if (_files.TryGetValue(subpath, out var fileInfo)) {
            return fileInfo;
        }

        return new VirtualFileInfoNotFound(subpath);
    }

    protected virtual IVirtualFileInfo<TVirtualFile> CreateFileInfo(string path, TVirtualFile virtualFile) {
        return new DefaultVirtualFileInfo<TVirtualFile>(path, virtualFile);
    }
    
    public IEnumerator<KeyValuePair<string, IVirtualFileInfo<TVirtualFile>>> GetEnumerator() {
        return _files.OrderBy(x => x.Key, _sorter).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }

    private class VirtualFileInfoNotFound : IFileInfo {
        public VirtualFileInfoNotFound(string path) {
            Name = path;
            LastModified = DateTimeOffset.MinValue;
            IsDirectory = true;
        }

        public Stream CreateReadStream() {
            throw new NotSupportedException();
        }

        public bool Exists { get; } = false;
        public long Length { get; } = 0;
        public string? PhysicalPath { get; } = default;
        public string Name { get; }
        public DateTimeOffset LastModified { get; }
        public bool IsDirectory { get; }
    }

    private class VirtualDirectoryFileInfo : IFileInfo {
        public VirtualDirectoryFileInfo(string name, bool exists) {
            Name = name;
            Exists = exists;
        }

        public bool Exists { get; }

        public bool IsDirectory { get; } = true;

        public DateTimeOffset LastModified { get; } = default;

        public long Length { get; } = -1;

        public string Name { get; }

        public string? PhysicalPath { get; } = default;

        public Stream CreateReadStream() {
            throw new NotSupportedException();
        }
    }

    private class VirtualDirectoryContents : IDirectoryContents {
        private readonly List<IFileInfo> _matchingFiles;

        public VirtualDirectoryContents(List<IFileInfo> matchingFiles) {
            _matchingFiles = matchingFiles;
            Exists = matchingFiles.Count > 0;
        }

        public bool Exists { get; }

        IEnumerator<IFileInfo> IEnumerable<IFileInfo>.GetEnumerator() {
            return _matchingFiles.AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return _matchingFiles.AsEnumerable().GetEnumerator();
        }
    }
}