using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TruckLib.HashFs.Tests
{
    public class UtilTest
    {
        [Fact]
        public void HashPath()
        {
            var expected = 8645157520230346068UL;

            var actual = Util.HashPath("/käsefondue.txt", 0);
            Assert.Equal(expected, actual);

            actual = Util.HashPath("käsefondue.txt", 0);
            Assert.Equal(expected, actual);
        }
    }
}
