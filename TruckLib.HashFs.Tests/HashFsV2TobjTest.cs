using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.Models;

namespace TruckLib.HashFs.Tests
{
    public class HashFsV2TobjTest : IDisposable
    {
        IHashFsReader reader;

        public HashFsV2TobjTest()
        {
            reader = HashFsReader.Open("Data/tobj_v2.scs");
        }

        [Fact]
        public void EntryExists()
        {
            Assert.Equal(EntryType.Directory, reader.EntryExists("/"));
            Assert.Equal(EntryType.File, reader.EntryExists("/sample.tobj"));
            Assert.Equal(EntryType.NotFound, reader.EntryExists("/sample.dds"));
        }

        [Fact]
        public void ExtractTobj()
        {
            var data = reader.Extract("/sample.tobj");
            var tobjBytes = data[0];
            var ddsBytes = data[1];

            var tobj = Tobj.Load(tobjBytes);
            Assert.Equal(TobjType.Map2D, tobj.Type);
            Assert.Equal(TobjFilter.Default, tobj.MagFilter);
            Assert.Equal(TobjFilter.Default, tobj.MinFilter);
            Assert.Equal(TobjMipFilter.Default, tobj.MipFilter);
            Assert.Equal(TobjAddr.Repeat, tobj.AddrU);
            Assert.Equal(TobjAddr.Repeat, tobj.AddrV);
            Assert.Equal(TobjAddr.Repeat, tobj.AddrW);
            Assert.Equal("/sample.dds", tobj.TexturePath);

            // TODO test DDS
        }

        public void Dispose()
        {
            reader.Dispose();
        }
    }
}
