using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs.Tests
{
    public class AssetLoaderTest
    {
        private IHashFsReader archiveA;
        private IHashFsReader archiveB;

        public AssetLoaderTest() 
        {
            archiveA = HashFsReader.Open("Data/AssetLoaderTest/archive_a.scs");
            archiveB = HashFsReader.Open("Data/AssetLoaderTest/archive_b.scs");
        }

        [Fact]
        public void FileExists()
        {
            var loader = new AssetLoader([archiveA, archiveB]);

            Assert.True(loader.FileExists("hello.txt"));
            Assert.True(loader.FileExists("/hello.txt"));
            Assert.True(loader.FileExists("/only_in_a/only_in_a.txt"));
            Assert.True(loader.FileExists("/only_in_b/only_in_b.txt"));
            Assert.False(loader.FileExists("/nope.txt"));
        }

        [Fact]
        public void DirectoryExists()
        {
            var loader = new AssetLoader([archiveA, archiveB]);

            Assert.True(loader.DirectoryExists("/"));
            Assert.True(loader.DirectoryExists("/only_in_a"));
            Assert.True(loader.DirectoryExists("/only_in_b"));
            Assert.True(loader.DirectoryExists("only_in_b"));
            Assert.True(loader.DirectoryExists("only_in_b/"));
            Assert.True(loader.DirectoryExists("/only_in_b/"));
            Assert.False(loader.DirectoryExists("/nope"));
            Assert.False(loader.DirectoryExists("/hello.txt"));
            Assert.False(loader.DirectoryExists("hello.txt"));
        }

        [Fact]
        public void GetFiles()
        {
            var loader = new AssetLoader([archiveA, archiveB]);

            var actual = loader.GetFiles("/");
            Assert.Equal(["/hello.txt"], actual);

            actual = loader.GetFiles("/only_in_b");
            Assert.Equal(["/only_in_b/only_in_b.txt"], actual);
        }

        [Fact]
        public void Open()
        {
            var loader = new AssetLoader([archiveA, archiveB]);

            using (var stream = loader.Open("/hello.txt"))
            using (var reader = new StreamReader(stream))
            {
                var actual = reader.ReadToEnd();
                Assert.Equal("hello from archive a!", actual);
            }

            loader = new AssetLoader([archiveB, archiveA]);

            using (var stream = loader.Open("/hello.txt"))
            using (var reader = new StreamReader(stream))
            {
                var actual = reader.ReadToEnd();
                Assert.Equal("hello from archive b!", actual);
            }
        }

        [Fact]
        public void ReadAllText()
        {
            var loader = new AssetLoader([archiveA, archiveB]);
            var actual = loader.ReadAllText("/hello.txt");
            Assert.Equal("hello from archive a!", actual);

            loader = new AssetLoader([archiveB, archiveA]);
            actual = loader.ReadAllText("/hello.txt");
            Assert.Equal("hello from archive b!", actual);
        }
    }
}
