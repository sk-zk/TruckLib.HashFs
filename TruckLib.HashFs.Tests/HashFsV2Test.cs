using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Hashing;

namespace TruckLib.HashFs.Tests
{
    public class HashFsV2Test : IDisposable
    {
        IHashFsReader reader;

        public HashFsV2Test() 
        {
            reader = HashFsReader.Open("Data/simple_v2.scs");
        }

        [Fact]
        public void GetEntryCount()
        {
            Assert.Equal(4, reader.Entries.Count);
        }

        [Fact]
        public void GetVersion()
        {
            Assert.Equal(2, reader.Version);
        }

        [Fact]
        public void EntryExists()
        {
            Assert.Equal(EntryType.Directory, reader.EntryExists("/"));
            Assert.Equal(EntryType.File, reader.EntryExists("/uncompressed.txt"));
            Assert.Equal(EntryType.Directory, reader.EntryExists("/somedir"));
            Assert.Equal(EntryType.File, reader.EntryExists("/somedir/long.txt"));
            Assert.Equal(EntryType.NotFound, reader.EntryExists("/does_not_exist"));
        }

        [Fact]
        public void TryGetEntry()
        {
            Assert.Equal(EntryType.Directory, reader.TryGetEntry("/", out var entry));
            Assert.True(entry.IsDirectory);

            Assert.Equal(EntryType.File, reader.TryGetEntry("/somedir/long.txt", out var entry2));
            Assert.Equal(3228U, entry2.Size);

            Assert.Equal(EntryType.NotFound, reader.TryGetEntry("/727", out var entry3));
            Assert.Null(entry3);
        }

        [Fact]
        public void GetDirectoryListing()
        {
            var listing = reader.GetDirectoryListing("/");
            Assert.Equal(["/uncompressed.txt"], listing.Files);
            Assert.Equal(["/somedir"], listing.Subdirectories);
        }

        [Fact]
        public void GetDirectoryListingThrowsIfNotFound()
        {
            Assert.Throws<FileNotFoundException>(() =>
            {
                var data = reader.GetDirectoryListing("/does_not_exist");
            });
        }

        [Fact]
        public void GetDirectoryListingThrowsIfFile()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                var data = reader.GetDirectoryListing("/uncompressed.txt");
            });
        }

        [Fact]
        public void ExtractUncompressed()
        {
            var data = reader.Extract("/uncompressed.txt");
            var expected = Encoding.ASCII.GetBytes("my hovercraft is full of eels.");
            Assert.Equal(expected, data[0]);
        }

        [Fact]
        public void ExtractCompressed()
        {
            var data = reader.Extract("/somedir/long.txt");
            var crc = new Crc32();
            crc.Append(data[0]);
            Assert.Equal(0xC83A92CA, crc.GetCurrentHashAsUInt32());
        }

        [Fact]
        public void ExtractThrowsIfNotFound()
        {
            Assert.Throws<FileNotFoundException>(() =>
            {
                var data = reader.Extract("/does_not_exist");
            });

            Assert.Throws<FileNotFoundException>(() =>
            {
                var data = reader.Extract(new EntryV2(), "/does_not_exist");
            });
        }

        [Fact]
        public void GetEntry()
        {
            var entry = reader.GetEntry("/somedir/long.txt");
            Assert.Equal(3228U, entry.Size);
            Assert.Equal(1343U, entry.CompressedSize);
            Assert.Equal(4096U, entry.Offset);
            Assert.True(entry.IsCompressed);
            Assert.False(entry.IsDirectory);
        }

        [Fact]
        public void HashPath()
        {
            var expected = 8645157520230346068UL;

            var actual = reader.HashPath("/käsefondue.txt");
            Assert.Equal(expected, actual);

            actual = reader.HashPath("käsefondue.txt");
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void UnsupportedHashThrows()
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                using var reader = HashFsReader.Open("Data/unsupported_hash_v2.scs");
            });
        }

        [Fact]
        public void DirectoryExists()
        {
            Assert.True(reader.DirectoryExists("/somedir"));
            Assert.True(reader.DirectoryExists("somedir"));
            Assert.True(reader.DirectoryExists("/"));
            Assert.False(reader.DirectoryExists("/nosuchdir"));
            Assert.False(reader.DirectoryExists("nosuchdir"));
        }

        public void Dispose()
        {
            reader.Dispose();
        }
    }
}
