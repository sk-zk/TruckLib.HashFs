using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace TruckLib.HashFs
{
    public abstract class HashFsWriterBase
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
        /// <para>The compression level used to compress files above the <see cref="CompressionThreshold"/>.</para>
        /// <para>Set this to <see cref="CompressionLevel.NoCompression"/> to disable compression.</para>
        /// </summary>
        public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.SmallestSize;

        /// <summary>
        /// The files which will be written to the archive. The key is the absolute archive path 
        /// of the file; the value is an <see cref="IFile"/> pointing to location of the file data.
        /// </summary>
        protected readonly Dictionary<string, IFile> files = [];

        /// <summary>
        /// Stores the directory tree from which the directory listing files will be generated.
        /// </summary>
        protected readonly Directory tree = new("");

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
        public void Add(Stream stream, string archivePath, bool keepOpen = false)
        {
            var fileEntry = Add<StreamFile>(archivePath);
            fileEntry.Stream = stream;
            fileEntry.KeepOpen = keepOpen;
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
        public abstract void Save(string path);

        /// <summary>
        /// Writes the HashFS archive containing the added files to the specified stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        public abstract void Save(Stream stream);

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

        internal static void WriteWatermark(BinaryWriter w)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var watermark = $"TruckLib.HashFs {version.Major}.{version.Minor}.{version.Build}";
            w.Write(Encoding.ASCII.GetBytes(watermark));
        }

        internal static void CompressZlib(Stream input, Stream output, CompressionLevel compressionLevelevel)
        {
            using var zlibStream = new ZLibStream(output, compressionLevelevel, true);
            input.CopyTo(zlibStream);
        }

        internal static string Combine(string path1, string path2)
        {
            if (path1.EndsWith('/'))
                return path1 + path2;
            else
                return path1 + "/" + path2;
        }

        public interface IFile
        {
            bool IsDirectory { get; set; }

            Stream Open();
        }

        public class DiskFile : IFile
        {
            public bool IsDirectory { get; set; }

            public string Path { get; set; }

            public Stream Open() => File.OpenRead(Path);
        }

        public class StreamFile : IFile
        {
            public bool IsDirectory { get; set; }

            public Stream Stream { get; set; }

            public bool KeepOpen { get; set; }

            public Stream Open() => Stream;
        }

        public class BufferFile : IFile
        {
            public bool IsDirectory { get; set; }

            public byte[] Buffer { get; set; }

            public Stream Open() => new MemoryStream(Buffer);
        }

        public class Directory
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
