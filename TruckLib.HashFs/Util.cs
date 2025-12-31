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

        /// <summary>
        /// Rounds up <paramref name="x"/> to the nearest multiple of <paramref name="n"/>.
        /// </summary>
        /// <param name="x">The number to round.</param>
        /// <param name="n">The multiple to round to.</param>
        /// <returns>The smallest multiple of <paramref name="n"/> that is greater
        /// than or equal to <paramref name="x"/>.</returns>
        public static int NearestMultiple(int x, int n) => 
            n == 0 
                ? x 
                : (x + n - 1) / n * n;

        /// <summary>
        /// Rounds up <paramref name="x"/> to the nearest multiple of <paramref name="n"/>.
        /// </summary>
        /// <param name="x">The number to round.</param>
        /// <param name="n">The multiple to round to.</param>
        /// <returns>The smallest multiple of <paramref name="n"/> that is greater
        /// than or equal to <paramref name="x"/>.</returns>
        public static long NearestMultiple(long x, long n) =>
            n == 0L
                ? x
                : (x + n - 1L) / n * n;
    }
}
