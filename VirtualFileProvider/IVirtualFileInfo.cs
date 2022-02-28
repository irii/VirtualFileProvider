using Microsoft.Extensions.FileProviders;

namespace Brics.FileProviders; 

/// <summary>
/// 
/// </summary>
/// <typeparam name="TVirtualFile"></typeparam>
public interface IVirtualFileInfo<out TVirtualFile> : IFileInfo where TVirtualFile : IVirtualFile {
    /// <summary>
    /// Virtual file source implementation.
    /// </summary>
    TVirtualFile Source { get; }
}