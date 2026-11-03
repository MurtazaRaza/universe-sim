using System.IO;
using System.Linq;
using NUnit.Framework;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// Artistic assets (A-04, ADR-0015 decision 6): ATTRIBUTION.md ends with its artistic assets section, and every
    /// file under <c>Assets/UniverseSim/Art/</c> is listed there with its path, as <c>usim-pipeline attribution</c>
    /// writes it from <c>data/catalog/artistic-assets.json</c> (the pipeline checks author, origin, license, and an
    /// artist's impression's body and references). No artistic asset can enter the project unlisted.
    /// </summary>
    public class ArtisticAttributionTests
    {
        private const string AttributionPath = "Assets/UniverseSim/ATTRIBUTION.md";
        private const string ArtFolder = "Assets/UniverseSim/Art";
        private const string Heading = "## Artistic assets";

        [Test]
        public void EveryFileInTheArtFolderIsListedInTheAttribution()
        {
            // Line endings depend on the checkout (Git converts them on Windows).
            var attribution = File.ReadAllText(AttributionPath).Replace("\r\n", "\n");
            var at = attribution.IndexOf("\n" + Heading + "\n", System.StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThan(0), "ATTRIBUTION.md has its artistic assets section (usim-pipeline attribution)");
            var section = attribution.Substring(at);
            Assert.That(section.IndexOf("\n## ", 1, System.StringComparison.Ordinal), Is.EqualTo(-1),
                        "the artistic assets come last");

            var files = Directory.Exists(ArtFolder)
                ? Directory.GetFiles(ArtFolder, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".meta", System.StringComparison.Ordinal))
                    .Select(f => f.Replace('\\', '/').Substring("Assets/UniverseSim/".Length))
                    .ToArray()
                : new string[0];
            foreach (var file in files)
            {
                StringAssert.Contains($"(`{file}`)", section, file + " is not in ATTRIBUTION.md");
            }
            if (files.Length == 0)
            {
                StringAssert.Contains("None yet", section);
            }
        }
    }
}
