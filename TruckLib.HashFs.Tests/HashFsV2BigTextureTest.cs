using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.Models;

namespace TruckLib.HashFs.Tests
{

    public class HashFsV2BigTextureTest : IDisposable
    {
        // Loads a tobj/dds entry larger than 2^24 bytes to ensure that
        // CompressedSize has been read correctly.

        IHashFsReader reader;

        public HashFsV2BigTextureTest()
        {
            reader = HashFsReader.Open("Data/bigtexture_v2.scs");
        }

        [Fact]
        public void CompressedSizeIsCorrect()
        {
            var entry = reader.GetEntry("/sample.tobj");
            Assert.Equal(10024356u, entry.CompressedSize);
        }

        [Fact]
        public void ReconstructedSizeIsCorrect()
        {
            var data = reader.Extract("/sample.tobj");
            var ddsBytes = data[1];

            // This may vary by a few bytes because the reconstruction process
            // does not produce precisely the same DDS file as the one you had
            // before packing.
            // In other words, if this fails because it's off by like 10 bytes,
            // you can probably just adjust the test.
            Assert.Equal(22_369_796, ddsBytes.Length);
        }

        public void Dispose()
        {
            reader.Dispose();
        }
    }
}
