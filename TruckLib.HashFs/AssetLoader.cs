using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs
{
    /// <summary>
    /// Wraps multiple <see cref="IFileSystem"/>s for prioritized access similar to 
    /// how mod files are loaded in ATS/ETS2.
    /// </summary>
    public class AssetLoader : IFileSystem
    {   
        /// <inheritdoc/>
        public char DirectorySeparator => '/';

        private readonly IFileSystem[] fileSystems;

        /// <summary>
        /// Instantiates a new AssetLoader.
        /// </summary>
        /// <param name="fileSystems">The <see cref="IFileSystem"/>s to wrap. The order of this array
        /// determines the order in which the file systems will be queried.</param>
        /// <exception cref="ArgumentNullException">Thrown if the given array is null or empty.</exception>
        public AssetLoader(IFileSystem[] fileSystems)
        {
            if (fileSystems is null || fileSystems.Length == 0)
                throw new ArgumentNullException(nameof(fileSystems));

            this.fileSystems = fileSystems;
        }

        /// <summary>
        /// Determines whether any of the wrapped file systems contain a file with 
        /// the specified path.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns><c>true</c> if the caller has the required permissions and <c>path</c>
        /// contains the name of an existing file; otherwise, <c>false</c>.</returns>
        public bool FileExists(string path)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.FileExists(path))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Determines whether any of the wrapped file systems contain a file with 
        /// the specified path.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns><c>true</c> if the caller has the required permissions and <c>path</c>
        /// contains the name of an existing file; otherwise, <c>false</c>.</returns>
        public bool DirectoryExists(string path)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.DirectoryExists(path))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Iterates the wrapped file systems in the given order to find a directory with
        /// the specified path and returns the names of files (including their paths) in 
        /// the first directory found.
        /// </summary>
        /// <param name="path">The absolute path to the directory to search.</param>
        /// <returns>An array of the full names (including paths) for the files in the
        /// specified directory, or an empty array if no files are found.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown if none of the file systems 
        /// contain this directory.</exception>
        public IList<string> GetFiles(string path)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.DirectoryExists(path))
                    return fs.GetFiles(path);
            }
            throw new DirectoryNotFoundException();
        }

        /// <inheritdoc/>
        public string GetParent(string path)
        {
            if (path == "/")
            {
                return null;
            }
            return path[0..path.LastIndexOf(DirectorySeparator)];
        }

        /// <summary>
        /// Iterates the wrapped file systems in the given order to find a file with the 
        /// specified path, then opens a stream on it.
        /// </summary>
        /// <param name="path">The file to open.</param>
        /// <returns>A stream for reading the file.</returns>
        /// <exception cref="FileNotFoundException">Thrown if none of the file systems 
        /// contain this file.</exception>
        public Stream Open(string path)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.FileExists(path))
                    return fs.Open(path);
            }
            throw new FileNotFoundException();
        }

        /// <summary>
        /// Iterates the wrapped file systems in the given order to find a file with the specified path, 
        /// reads the contents of the file into a byte array, and then closes the file.
        /// </summary>
        /// <param name="path">The file to open for reading.</param>
        /// <returns>A byte array containing the contents of the file.</returns>
        /// <exception cref="FileNotFoundException">Thrown if none of the file systems 
        /// contain this file.</exception>
        public byte[] ReadAllBytes(string path)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.FileExists(path))
                    return fs.ReadAllBytes(path);
            }
            throw new FileNotFoundException();
        }

        /// <summary>
        /// Iterates the wrapped file systems in the given order to find a file with the specified path, 
        /// reads all the text in the file into a string, and then closes the file.
        /// </summary>
        /// <param name="path">The file to open for reading.</param>
        /// <returns>A string containing all the text in the file.</returns>
        /// <exception cref="FileNotFoundException">Thrown if none of the file systems 
        /// contain this file.</exception>
        public string ReadAllText(string path)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.FileExists(path))
                    return fs.ReadAllText(path);
            }
            throw new FileNotFoundException();
        }

        /// <summary>
        /// Iterates the wrapped file systems in the given order to find a file with the specified path, 
        /// reads all the text in the file with the specified encoding, and then closes the file.
        /// </summary>
        /// <param name="path">The file to open for reading.</param>
        /// <param name="encoding">The encoding applied to the contents of the file.</param>
        /// <returns>A string containing all the text in the file.</returns>
        /// <exception cref="FileNotFoundException">Thrown if none of the file systems 
        /// contain this file.</exception>
        public string ReadAllText(string path, Encoding encoding)
        {
            foreach (var fs in fileSystems)
            {
                if (fs.FileExists(path))
                    return fs.ReadAllText(path, encoding);
            }
            throw new FileNotFoundException();
        }
    }
}
