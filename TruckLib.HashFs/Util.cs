using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs
{
    internal class Util
    {
        /// <summary>
        /// Hashes a file path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="salt">The salt to prepend to the path.</param>
        /// <returns>The hash of the path.</returns>
        public static ulong HashPath(string path, ushort salt)
        {
            if (path != "" && path.StartsWith('/'))
                path = path[1..];

            // TODO do salts work the same way in v2?
            if (salt != 0)
                path = salt + path;

            var bytes = Encoding.UTF8.GetBytes(path);
            var hash = CityHash.CityHash64(bytes, (ulong)bytes.Length);
            return hash;
        }
    }
}
