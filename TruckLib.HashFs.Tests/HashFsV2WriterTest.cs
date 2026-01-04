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
        public void AddCubemapAndSave()
        {
            var writer = new HashFsV2Writer();

            writer.Add(@"Data/HashFsV2Writer/iconomaker_cube.dds", 
                "/material/environment/iconomaker_cube/iconomaker_cube.dds");
            writer.Add(@"Data/HashFsV2Writer/iconomaker_cube.tobj", 
                "/material/environment/iconomaker_cube/iconomaker_cube.tobj");

            using var outStream = new MemoryStream();
            writer.Save(outStream);
            outStream.Position = 0;

            using var reader = HashFsReader.Open(outStream);

            var tobjEntry = (EntryV2)reader.GetEntry("/material/environment/iconomaker_cube/iconomaker_cube.tobj");
            Assert.NotNull(tobjEntry.TobjMetadata);
            Assert.True(tobjEntry.TobjMetadata.Value.IsCube);
            Assert.Equal(6u, tobjEntry.TobjMetadata!.Value.FaceCount);
            Assert.Equal(9u, tobjEntry.TobjMetadata!.Value.MipmapCount);
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

        [Fact]
        public void ThrowIfTobjReferencedDdsIsNonDx10()
        {
            var writer = new HashFsV2Writer();

            writer.Add(@"Data/HashFsV2Writer/dxt1.dds", "/dxt1.dds");
            writer.Add(@"Data/HashFsV2Writer/dxt1.tobj", "/dxt1.tobj");

            using var outStream = new MemoryStream();
            Assert.Throws<TexturePackingException>(() => writer.Save(outStream));
        }

        [Fact]
        public void ThrowIfTobjReferencedDdsMissing()
        {
            var writer = new HashFsV2Writer();

            writer.Add(@"Data/HashFsV2Writer/iconomaker_cube.tobj", "/bla.tobj");

            using var outStream = new MemoryStream();
            Assert.Throws<TexturePackingException>(() => writer.Save(outStream));
        }

        [Fact]
        public void ThrowIfTobjReferencedFileIsNotDds()
        {
            var writer = new HashFsV2Writer();

            writer.Add(@"Data/HashFsV2Writer/not_dds.tobj", "/not_dds.tobj");
            writer.Add([7, 2, 7], "/asdf.727");

            using var outStream = new MemoryStream();
            Assert.Throws<TexturePackingException>(() => writer.Save(outStream));
        }

        [Fact]
        public void ThrowIfTobjReferencedDdsIsInvalid()
        {
            var writer = new HashFsV2Writer();

            writer.Add(@"Data/HashFsV2Writer/iconomaker_cube.tobj",
                "/material/environment/iconomaker_cube/iconomaker_cube.tobj");
            writer.Add([1,2,3,4,5,6,7,8],
                "/material/environment/iconomaker_cube/iconomaker_cube.dds");

            using var outStream = new MemoryStream();
            Assert.Throws<TexturePackingException>(() => writer.Save(outStream));
        }

        [Fact]
        public void ThrowIfPathPartExceeds255()
        {
            var writer = new HashFsV2Writer();

            var acceptable = "/bla/" + new string('a', 255) + "/asd";
            var tooLong = "/bla/" + new string('a', 256) + "/asd";
            writer.Add([1, 2, 3, 4], acceptable); // should not throw
            Assert.Throws<ArgumentException>(() => writer.Add([1,2,3,4], tooLong));
        }
    }
}
