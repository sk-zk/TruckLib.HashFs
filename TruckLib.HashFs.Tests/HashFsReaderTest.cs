using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs.Tests
{
    public class HashFsReaderTest
    {
        [Fact]
        public void ThrowsIfNotHashFs()
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                using var reader = HashFsReader.Open("Data/not_a_hashfs_file.scs");
            });
        }

        [Fact]
        public void ThrowsIfUnsupportedVersion()
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                using var reader = HashFsReader.Open("Data/unsupported_version.scs");
            });
        }
    }
}
