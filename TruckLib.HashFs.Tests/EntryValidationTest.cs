using System.Text;

namespace TruckLib.HashFs.Tests
{
    public class EntryValidationTest
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExtractAcceptsValueCopies(int version)
        {
            using var reader = OpenReader(version, out var path);
            var entry = reader.GetEntry(path);
            var copy = Copy(entry);

            Assert.NotSame(entry, copy);
            Assert.Equal(reader.Extract(path)[0], reader.Extract(copy, path)[0]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExtractRejectsNull(int version)
        {
            using var reader = OpenReader(version, out var path);
            Assert.Throws<FileNotFoundException>(() => reader.Extract(null!, path));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExtractRejectsUnknownHash(int version)
        {
            using var reader = OpenReader(version, out var path);
            IEntry entry = version == 1 ? new EntryV1() : new EntryV2();
            Assert.False(reader.Entries.ContainsKey(entry.Hash));
            Assert.Throws<FileNotFoundException>(() => reader.Extract(entry, path));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExtractRejectsDifferentMetadataWithTheSameHash(int version)
        {
            using var reader = OpenReader(version, out var path);
            var entry = reader.GetEntry(path);
            var changed = Copy(entry);
            changed.IsDirectory = !changed.IsDirectory;

            Assert.Equal(entry.Hash, changed.Hash);
            Assert.Throws<FileNotFoundException>(() => reader.Extract(changed, path));
            Assert.NotEmpty(reader.Extract(entry, path)[0]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExtractObservesEntryRemovalAndRestoration(int version)
        {
            using var reader = OpenReader(version, out var path);
            var entry = reader.GetEntry(path);

            Assert.True(reader.Entries.Remove(entry.Hash));
            Assert.Throws<FileNotFoundException>(() => reader.Extract(entry, path));

            reader.Entries.Add(entry.Hash, entry);
            Assert.NotEmpty(reader.Extract(entry, path)[0]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void ExtractObservesEntryReplacement(int version)
        {
            using var reader = OpenReader(version, out var path);
            var entry = reader.GetEntry(path);
            var changed = Copy(entry);
            changed.IsDirectory = !changed.IsDirectory;
            reader.Entries[entry.Hash] = changed;

            Assert.Throws<FileNotFoundException>(() => reader.Extract(entry, path));
        }

        private static IEntry Copy(IEntry entry) => entry switch
        {
            EntryV1 v1 => v1,
            EntryV2 v2 => v2,
            _ => throw new ArgumentException("Unexpected fixture entry type.")
        };

        private static IHashFsReader OpenReader(int version, out string path)
        {
            if (version == 2)
            {
                path = "/uncompressed.txt";
                return HashFsReader.Open("Data/simple_v2.scs");
            }

            path = "/sample.txt";
            var writer = new HashFsV1Writer();
            using var source = new MemoryStream(Encoding.UTF8.GetBytes("Entry validation fixture"));
            writer.Add(source, path);
            var archive = new MemoryStream();
            writer.Save(archive);
            archive.Position = 0;
            return HashFsReader.Open(archive);
        }
    }
}
