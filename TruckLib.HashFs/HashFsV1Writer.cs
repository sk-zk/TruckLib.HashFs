using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs
{
    /// <summary>
    /// Creates a new HashFS v1 archive.
    /// </summary>
    public class HashFsV1Writer
    {
        /// <summary>
        /// Sets the salt which will be prepended to file paths before hashing them.
        /// If this value is 0, no salt will be used.
        /// </summary>
        public ushort Salt { get; set; } = 0;

        /// <summary>
        /// A file will be compressed if it is larger than this threshold in bytes.
        /// </summary>
        public int CompressionThreshold { get; set; } = 64;

        /// <summary>
        /// Whether the writer should compute the CRC32 checksums of entries.
        /// ATS/ETS2 don't appear to actually require this.
        /// </summary>
        public bool ComputeChecksums { get; set; } = false;

        /// <summary>
        /// <para>The compression level used to compress files above the <see cref="CompressionThreshold"/>.</para>
        /// <para>Set this to <see cref="CompressionLevel.NoCompression"/> to disable compression.</para>
        /// </summary>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

        /// <summary>
        /// The files which will be written to the archive. The key is the absolute archive path 
        /// of the file; the value is an <see cref="IFile"/> pointing to location of the file data.
        /// </summary>
        private readonly Dictionary<string, IFile> files = [];

        /// <summary>
        /// Stores the directory tree from which the directory listing files will be generated.
        /// </summary>
        private readonly Directory tree = new("");

        /// <summary>
        /// <para>Adds a file on the local file system to the archive.</para>
        /// <para>The given file will be not be opened until <see cref="Save(Stream)">Write</see> is called,
        /// so it must not be deleted before this point.</para>
        /// </summary>
        /// <param name="filePath">The path of the file to add.</param>
        /// <param name="archivePath">The path this file will have in the archive.</param>
        /// <exception cref="FileNotFoundException">Thrown if <paramref name="filePath"/> does not exist.</exception>
        public void Add(string filePath, string archivePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"\"{filePath}\" is not a file or does not exist.");
            }

            var fileEntry = Add<DiskFile>(archivePath);
            fileEntry.Path = filePath;
        }

        /// <summary>
        /// <para>Adds a file to the archive.</para>
        /// <para>This stream must remain open until <see cref="Save(Stream)">Write</see> has been called.</para>
        /// </summary>
        /// <param name="stream">The stream containing the file.</param>
        /// <param name="archivePath">The path this file will have in the archive.</param>
        public void Add(Stream stream, string archivePath)
        {
            var fileEntry = Add<StreamFile>(archivePath);
            fileEntry.Stream = stream;
        }

        /// <summary>
        /// <para>Adds a file to the archive.</para>
        /// </summary>
        /// <param name="buffer">The byte array containing the file.</param>
        /// <param name="archivePath">The path this file will have in the archive.</param>
        public void Add(byte[] buffer, string archivePath)
        {
            var fileEntry = Add<BufferFile>(archivePath);
            fileEntry.Buffer = buffer;
        }

        private T Add<T>(string archivePath) where T : IFile, new()
        {
            if (string.IsNullOrEmpty(archivePath))
            {
                throw new ArgumentNullException($"{nameof(archivePath)}");
            }

            var pathParts = archivePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length == 0)
            {
                throw new ArgumentException("The archive path must not be \"/\".", nameof(archivePath));
            }

            var archiveFileName = pathParts[^1];

            var parent = CreateDirectories(pathParts);
            parent.Files.Add(archiveFileName);

            var fileEntry = new T();
            if (!files.TryAdd(archivePath, fileEntry))
            {
                files[archivePath] = fileEntry;
            }
            return fileEntry;
        }

        /// <summary>
        /// Writes the HashFS archive containing the added files to a file.
        /// </summary>
        /// <param name="path">The path of the file to create.</param>
        public void Save(string path)
        {
            using var fs = File.Create(path);
            Save(fs);
        }

        /// <summary>
        /// Writes the HashFS archive containing the added files to the specified stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        public void Save(Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, true);

            // 1) Generate directory listing files
            var dirLists = GenerateDirectoryListings(stream, tree, "/");

            // 2) Write the header
            var numEntries = (uint)(files.Count + dirLists.Count);
            WriteHeader(w, numEntries);

            // 3) Write the files and generate the entry table
            var entryMetadata = WriteFiles(stream, files.Concat(dirLists));

            // 4) Write the entry table
            var entryTableOffset = (ulong)stream.Position;
            // In v1, unlike in v2, the entry table must be sorted by hash.
            foreach (var entry in entryMetadata.OrderBy(x => x.Hash))
            {
                entry.Serialize(w);
            }

            // 5) Add a version number at the end for future debugging purposes
            WriteWatermark(w);

            // 6) Jump back to the header and fill in the start offset
            // of the entry table
            stream.Position = 0x10;
            w.Write(entryTableOffset);
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

        private void WriteHeader(BinaryWriter w, uint numEntries)
        {
            var header = new HeaderV1()
            {
                Salt = Salt,
                HashMethod = "CITY",
                NumEntries = numEntries,
                StartOffset = 0, // we'll fill this in later
            };
            header.Serialize(w);
        }

        private static void WriteWatermark(BinaryWriter w)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var watermark = $"TruckLib.HashFs {version.Major}.{version.Minor}.{version.Build}";
            w.Write(Encoding.ASCII.GetBytes(watermark));
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

        private static string Combine(string path1, string path2)
        {
            if (path1.EndsWith('/'))
                return path1 + path2;
            else
                return path1 + "/" + path2;
        }

        private EntryV1 WriteFile(Stream outStream, IFile file, string path)
        {
            var startPos = outStream.Position;

            var fileStream = file.Open();

            var compress = CompressionLevel != CompressionLevel.NoCompression 
                && fileStream.Length > CompressionThreshold;
            if (compress)
            {
                using var zlibStream = new ZLibStream(outStream, CompressionLevel, true);
                fileStream.CopyTo(zlibStream);
            }
            else
            {
                fileStream.CopyTo(outStream);
            }

            var endPos = outStream.Position;
            var uncompressedSize = (uint)fileStream.Length;
            var compressedSize = (uint)(endPos - startPos);

            var crc = ComputeChecksums ? ComputeChecksum(fileStream) : 0u;

            fileStream.Dispose();

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

        private Directory CreateDirectories(string[] pathParts)
        {
            var dirs = pathParts[0..^1];
            var parent = tree;
            foreach (var part in dirs)
            {
                if (!parent.Directories.TryGetValue(part, out var current))
                {
                    current = new Directory(part);
                    parent.Directories.Add(part, current);
                }
                parent = current;
            }
            return parent;
        }

        internal interface IFile
        {
            bool IsDirectory { get; set; }

            Stream Open();
        }

        internal class DiskFile : IFile
        {
            public bool IsDirectory { get; set; }

            public string Path { get; set; }

            public Stream Open() => File.OpenRead(Path);
        }

        internal class StreamFile : IFile
        {
            public bool IsDirectory { get; set; }

            public Stream Stream { get; set; }

            public Stream Open() => Stream;

        }

        internal class BufferFile : IFile
        {
            public bool IsDirectory { get; set; }

            public byte[] Buffer { get; set; }

            public Stream Open() => new MemoryStream(Buffer);
        }

        internal class Directory
        {
            public string Name { get; init; }

            public HashSet<string> Files { get; init; } = [];

            public Dictionary<string, Directory> Directories { get; init; } = [];

            public Directory(string name)
            {
                Name = name;
            }
        }
    }
}
