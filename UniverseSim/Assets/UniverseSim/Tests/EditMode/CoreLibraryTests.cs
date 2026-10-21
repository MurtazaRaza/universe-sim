using NUnit.Framework;
using UniverseSim.Unity.Native;

namespace UniverseSim.Unity.Tests.EditMode
{
    public class CoreLibraryTests
    {
        [Test]
        public void NativeLibraryLoadsWithExpectedAbi()
        {
            Assert.That(CoreLibrary.AbiVersion, Is.EqualTo(CoreLibrary.ExpectedAbiVersion));
        }

        [Test]
        public void VersionIsSemantic()
        {
            StringAssert.IsMatch(@"^\d+\.\d+\.\d+$", CoreLibrary.Version);
        }
    }
}
