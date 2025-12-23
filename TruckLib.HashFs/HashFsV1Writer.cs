using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs
{
    /// <summary>
    /// Creates a new HashFS v1 archive.
    /// </summary>
    public class HashFsV1Writer : HashFsWriterBase
    {
        /// <summary>
        /// Whether the writer should compute the CRC32 checksums of entries.
        /// ATS/ETS2 don't appear to actually require this.
        /// </summary>
        public bool ComputeChecksums { get; set; } = false;

        /// <inheritdoc/>
        public override void Save(string path)
        {
            using var fs = File.Create(path);
            Save(fs);
        }

        /// <inheritdoc/>
        public override void Save(Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, true);

            // Generate directory listing files
            var dirLists = GenerateDirectoryListings(stream, tree, "/");

            // Write the files and generate the entry table
            var entryMetadata = WriteFiles(stream, files.Concat(dirLists));

            // Write the entry table
            var entryTableOffset = (ulong)stream.Position;
            foreach (var entry in entryMetadata.OrderBy(x => x.Hash))
            {
                entry.Serialize(w);
            }

            // Add a version number at the end for future debugging purposes
            WriteWatermark(w);

            // Write the header
            stream.Position = 0;
            var header = new HeaderV1()
            {
                Salt = Salt,
                HashMethod = "CITY",
                NumEntries = (uint)(files.Count + dirLists.Count),
                StartOffset = (uint)entryTableOffset,
            };
            header.Serialize(w);
        }

        private List<EntryV1> WriteFiles(Stream stream, IEnumerable<KeyValuePair<string, IFile>> files)
        {
            const int fileStartOffset = 4096;
            stream.Position = fileStartOffset;

            List<EntryV1> entryMetadata = [];
            foreach (var (path, file) in files)
            {
                var meta = WriteFile(stream, file, path);
                entryMetadata.Add(meta);
            }

            return entryMetadata;
        }

        private static Dictionary<string, IFile> GenerateDirectoryListings(Stream stream, Directory dir, 
            string path, Dictionary<string, IFile> dirListFiles = null)
        {
            dirListFiles ??= [];

            var sb = new StringBuilder();

            foreach (var (name, _) in dir.Directories)
            {
                sb.AppendLine(HashFsV1Reader.DirMarker + name);
            }
            foreach (var name in dir.Files)
            {
                sb.AppendLine(name);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            dirListFiles.Add(path, new BufferFile() 
            { 
                Buffer = bytes, 
                IsDirectory = true,
            });

            foreach (var (name, subdir) in dir.Directories)
            {
                var subpath = Combine(path, name);
                GenerateDirectoryListings(stream, subdir, subpath, dirListFiles);
            }

            return dirListFiles;
        }

        private EntryV1 WriteFile(Stream outStream, IFile file, string path)
        {
            var startPos = outStream.Position;

            var fileStream = file.Open();

            var compress = CompressionLevel != CompressionLevel.NoCompression
                && fileStream.Length > CompressionThreshold;
            if (compress)
            {
                CompressZlib(fileStream, outStream, CompressionLevel);
            }
            else
            {
                fileStream.CopyTo(outStream);
            }

            var endPos = outStream.Position;
            var uncompressedSize = (uint)fileStream.Length;
            var compressedSize = (uint)(endPos - startPos);

            var crc = ComputeChecksums ? ComputeChecksum(fileStream) : 0u;

            if (!(file is StreamFile sf && !sf.KeepOpen))
            {
                fileStream.Dispose();
            }

            var meta = new EntryV1()
            {
                Hash = Util.HashPath(path, Salt),
                Offset = (ulong)startPos,
                Crc = crc,
                Size = uncompressedSize,
                CompressedSize = compressedSize,
                IsDirectory = file.IsDirectory,
                IsCompressed = compress,
            };
            return meta;
        }

        /// <summary>
        /// Computes the CRC32 checksum of all bytes contained in the specified stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>The CRC32 checksum.</returns>
        private static uint ComputeChecksum(Stream stream)
        {
            stream.Position = 0;
            var crc32Generator = new Crc32();
            crc32Generator.Append(stream);
            var crc32 = crc32Generator.GetCurrentHashAsUInt32();
            return crc32;
        }
    }
}
