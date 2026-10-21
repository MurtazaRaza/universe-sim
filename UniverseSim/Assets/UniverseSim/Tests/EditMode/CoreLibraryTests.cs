using System;
using System.Linq;
using System.Text.RegularExpressions;
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

        [Test]
        public void StatusEnumMatchesTheNativeStatusCodes()
        {
            // Every C# value must have the same name in the core (PascalCase here, snake_case there).
            foreach (Status status in Enum.GetValues(typeof(Status)))
            {
                var expected = Regex.Replace(status.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
                Assert.That(CoreLibrary.StatusName(status), Is.EqualTo(expected), status.ToString());
            }

            // And the core must not define codes this binding lacks.
            var next = (Status)(Enum.GetValues(typeof(Status)).Cast<int>().Max() + 1);
            Assert.That(CoreLibrary.StatusName(next), Is.EqualTo("unknown"));
        }
    }
}
