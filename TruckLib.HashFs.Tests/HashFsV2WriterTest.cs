using System;
using System.Collections.Generic;
using System.Text;
using TruckLib.HashFs.HashFsV2;

namespace TruckLib.HashFs.Tests
{
    public class HashFsV2WriterTest
    {
        [Fact]
        public void AddFromFsAndSave()
        {
            var writer = new HashFsV2Writer() 
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

            Assert.Equal(2, reader.Version);
            Assert.Equal(42, reader.Salt);
            Assert.Equal(14, reader.Entries.Count);

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

            var tobjEntry = (EntryV2)reader.GetEntry("/model/simple_cube/cubetx.tobj");
            Assert.True(tobjEntry.IsCompressed);
            Assert.NotNull(tobjEntry.TobjMetadata);
            Assert.Equal(256u, tobjEntry.TobjMetadata.Value.TextureWidth);
            Assert.Equal(256u, tobjEntry.TobjMetadata.Value.TextureHeight);
            Assert.Equal(HashFs.Dds.DxgiFormat.BC1_UNORM_SRGB, tobjEntry.TobjMetadata.Value.Format);
            Assert.Equal(9u, tobjEntry.TobjMetadata.Value.MipmapCount);
            Assert.False(tobjEntry.TobjMetadata.Value.IsCube);

            var extracted = reader.ReadAllText("/def/world/model.tests.sii");
            var original = File.ReadAllText("Data/SampleMod/def/world/model.tests.sii");
            Assert.Equal(original, extracted);
        }

        [Fact]
        public void ThrowIfArchivePathIsEmptyString()
        {
            var writer = new HashFsV2Writer();
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() => writer.Add(ms, ""));
            Assert.Throws<ArgumentNullException>(() => writer.Add(ms, null));
        }

        [Fact]
        public void ThrowIfArchivePathIsRoot()
        {
            var writer = new HashFsV2Writer();
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentException>(() => writer.Add(ms, "/"));
        }
    }
}
