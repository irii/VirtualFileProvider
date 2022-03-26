using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Xunit;

namespace Brics.FileProviders.Tests;

public class VirtualFileProviderTests {
    [Fact]
    public void Should_Add_Files_And_Notify_Listeners() {
        var provider = new VirtualFileProvider<TestVirtualFile>();
        
        var watch = provider.Watch("/newfile.txt");
        
        provider.Add("/newfile.txt", new TestVirtualFile("NEWFILE1"));
        Assert.True(watch.HasChanged);
    }
    
    [Fact]
    public void Should_Remove_And_Notify_Listeners() {
        var provider = new VirtualFileProvider<TestVirtualFile> {
            { "/removefile.txt", new TestVirtualFile("REMOVE") }
        };
        
        var watch = provider.Watch("/removefile.txt");
        provider.Remove("/removefile.txt");
        
        Assert.True(watch.HasChanged);
    }
    
    [Fact]
    public void Should_Remove_Many_And_Notify_Listeners() {
        var provider = new VirtualFileProvider<TestVirtualFile> {
            { "/remove/file1.txt", new TestVirtualFile("REMOVE1") },
            { "/remove/file2.txt", new TestVirtualFile("REMOVE2") }
        };
        
        var watch = provider.Watch("/remove/file*");
        
        provider.RemoveMany(new []{"/remove/file1.txt", "/remove/file2.txt"});

        Assert.True(watch.HasChanged);
    }

    [Fact]
    public void Should_Get_Top_Dict_List() {
        var provider = new VirtualFileProvider<TestVirtualFile> {
            { "/filetop1.txt", new TestVirtualFile("D1") },
            { "/sub/filesub1.txt", new TestVirtualFile("D2") },
            { "/sub/nested/filesubsub2.txt", new TestVirtualFile("D3") }
        };

        var files = provider.GetDirectoryContents("/");
        Assert.True(files.Exists);

        var filesList = files.ToList();
        Assert.Equal(2, filesList.Count);
        Assert.All(filesList, x => Assert.True(x.Exists));
        
        Assert.Equal("filetop1.txt", filesList.Single(x => !x.IsDirectory).Name);
        Assert.Equal("sub", filesList.Single(x => x.IsDirectory).Name);
    }

    [Fact]
    public void Should_Get_Nested_Dict_List() {
        var provider = new VirtualFileProvider<TestVirtualFile> {
            { "/filetop1.txt", new TestVirtualFile("D1") },
            { "/sub/filesub1.txt", new TestVirtualFile("D2") },
            { "/sub/filesub2.txt", new TestVirtualFile("D2") },
            { "/sub/nested/filesubsub2.txt", new TestVirtualFile("D3") }
        };

        var files = provider.GetDirectoryContents("/sub");
        Assert.True(files.Exists);

        var filesList = files.ToList();
        Assert.Equal(3, filesList.Count);
        Assert.Single(filesList.Where(x => x.IsDirectory));
        Assert.All(filesList.Where(x => !x.IsDirectory), x => {
            Assert.Contains(x.Name, new[] {"filesub1.txt", "filesub2.txt"});
        });
    }

    [Fact]
    public void Should_Get_File() {
        var provider = new VirtualFileProvider<TestVirtualFile> {
            { "/filetop1.txt", new TestVirtualFile("D1") },
            { "/sub/filesub1.txt", new TestVirtualFile("D2") },
            { "/sub/filesub2.txt", new TestVirtualFile("D2") },
            { "/sub/nested/filesubsub2.txt", new TestVirtualFile("D3") }
        };

        var file = provider.GetFileInfo("/sub/filesub2.txt");
        Assert.True(file.Exists);
        Assert.Equal("filesub2.txt", file.Name);
    }

    [Fact]
    public void Should_Get_All_Files() {
        var provider = new VirtualFileProvider<TestVirtualFile> {
            { "/filetop1.txt", new TestVirtualFile("D1") },
            { "/sub/filesub1.txt", new TestVirtualFile("D2") },
            { "/sub/filesub2.txt", new TestVirtualFile("D2") },
            { "/sub/nested/filesubsub2.txt", new TestVirtualFile("D3") }
        };

        var files = GetAllFiles(provider, "/");
        
        Assert.Equal(4, files.Count);
    }
    
    /// <summary>
    /// Returns all files from the file provider, beginning with <paramref name="start"/>
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="start"></param>
    /// <returns></returns>
    internal static IReadOnlyList<string> GetAllFiles(IFileProvider provider, string start)
    {
        var files = new List<string>();
        var dirs = new Queue<string>();

        var infos = provider.GetDirectoryContents(start);

        foreach (var info in infos)
        {
            if (info.IsDirectory)
            {
                dirs.Enqueue(info.Name);
            }
            else if (info.Exists)
            {
                files.Add(info.Name);
            }
        }

        while (dirs.Count > 0)
        {
            var path = dirs.Dequeue();

            infos = provider.GetDirectoryContents(path);

            foreach (var info in infos)
            {
                if (info.IsDirectory)
                {
                    dirs.Enqueue(Path.Combine(path, info.Name));
                }
                else if (info.Exists)
                {
                    files.Add(Path.Combine(path, info.Name));
                }
            }

        }


        return files;
    }

    
    private class TestVirtualFile : IVirtualFile {
        private readonly byte[] _content;
        
        public TestVirtualFile(string content) {
            _content = Encoding.UTF8.GetBytes(content);
        }
        
        public TestVirtualFile(byte[] content) {
            _content = content;
        }
        
        public long Length { get; } = 1;
        public DateTimeOffset LastModified { get; } = DateTimeOffset.MinValue + TimeSpan.FromDays(20);
        public Stream CreateReadStream() {
            return new MemoryStream(_content);
        }

        public int GetHashCode(IVirtualFile obj)
        {
            throw new NotImplementedException();
        }

        protected bool Equals(TestVirtualFile other)
        {
            return _content.Equals(other._content) && Length == other.Length && LastModified.Equals(other.LastModified);
        }

        public bool Equals(IVirtualFile? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TestVirtualFile) obj);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((TestVirtualFile) obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_content, Length, LastModified);
        }
    }
}