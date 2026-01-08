using System;
using System.Collections.Generic;
using System.Text;

namespace TruckLib.HashFs.Tests
{
    public class HashFsV1WriterTest
    {
        [Fact]
        public void AddFromFsAndSave()
        {
            var writer = new HashFsV1Writer() 
            { 
                Salt = 42,
            };

            var fsRoot = @"Data/SampleMod";

            foreach (var file in Directory.EnumerateFiles(fsRoot, "*", SearchOption.AllDirectories))
            {
                var archivePath = Path.GetRelativePath(fsRoot, file);
                archivePath = archivePath.Replace('\\', '/');
                archivePath = '/' + archivePath;
                writer.Add(file, archivePath);
            }

            using var outStream = new MemoryStream();
            writer.Save(outStream);
            outStream.Position = 0;

            using var reader = HashFsReader.Open(outStream);

            Assert.Equal(1, reader.Version);
            Assert.Equal(42, reader.Salt);
            Assert.Equal(15, reader.Entries.Count);

            Assert.True(reader.DirectoryExists("/"));
            Assert.True(reader.FileExists("/manifest.sii"));
            Assert.True(reader.FileExists("/def/world/model.tests.sii"));

            var dirEntry = reader.GetEntry("/");
            Assert.False(dirEntry.IsCompressed);
            Assert.True(dirEntry.IsDirectory);
            Assert.Equal(0xDAC6B40444905D0UL, dirEntry.Hash);

            var fileEntry = reader.GetEntry("/def/world/model.tests.sii");
            Assert.True(fileEntry.IsCompressed);
            Assert.False(fileEntry.IsDirectory);
            Assert.Equal(0x3C6369BC6EFDD668UL, fileEntry.Hash);

            var extracted = reader.ReadAllText("/def/world/model.tests.sii");
            var original = File.ReadAllText("Data/SampleMod/def/world/model.tests.sii");
            Assert.Equal(original, extracted);
        }

        [Fact]
        public void ThrowIfArchivePathIsEmptyString()
        {
            var writer = new HashFsV1Writer();
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() => writer.Add(ms, ""));
            Assert.Throws<ArgumentNullException>(() => writer.Add(ms, null));
        }

        [Fact]
        public void ThrowIfArchivePathIsRoot()
        {
            var writer = new HashFsV1Writer();
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentException>(() => writer.Add(ms, "/"));
        }

        [Fact]
        public void KeepOpenTrueRespected()
        {
            var writer = new HashFsV1Writer();

            var fs = File.OpenRead(@"Data/SampleMod/manifest.sii");
            writer.Add(fs, "/manifest.sii", true);

            using var ms = new MemoryStream();
            writer.Save(ms);

            Assert.True(fs.CanRead);
        }

        [Fact]
        public void KeepOpenFalseRespected()
        {
            var writer = new HashFsV1Writer();

            var fs = File.OpenRead(@"Data/SampleMod/manifest.sii");
            writer.Add(fs, "/manifest.sii", false);

            using var ms = new MemoryStream();
            writer.Save(ms);

            Assert.False(fs.CanRead);
        }
    }
}
