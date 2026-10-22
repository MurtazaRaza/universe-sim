using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity.Tests.EditMode
{
    /// <summary>
    /// The raw C ABI bindings (P4-06): struct layouts, and a smoke test through the default pack in the
    /// repository's data/packs (skipped when it is absent or only a Git LFS pointer).
    /// </summary>
    public class NativeAbiTests
    {
        [Test]
        public void StructLayoutsMatchTheLibrary()
        {
            Assert.That(CoreLibrary.StructSizeMismatches(), Is.Empty);
        }

        private static string DefaultPackPath()
        {
            var packs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "packs"));
            if (!Directory.Exists(packs))
            {
                return null;
            }
            // The default pack, solar-system-<version>.uspack (not the optional moons pack).
            var path = Directory.GetFiles(packs, "solar-system-*.uspack")
                .Where(p => char.IsDigit(Path.GetFileName(p)["solar-system-".Length]))
                .OrderBy(p => p).LastOrDefault();
            if (path == null || new FileInfo(path).Length < 1024)
            {
                return null; // missing, or a Git LFS pointer
            }
            return path;
        }

        private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

        [Test]
        public void LoadsTheDefaultPackAndFillsASnapshot()
        {
            var path = DefaultPackPath();
            if (path == null)
            {
                Assert.Ignore("no default pack in data/packs (run git lfs pull)");
            }

            Assert.That(NativeMethods.SimulationLoadFile(Utf8(path), out var sim), Is.EqualTo((int)Status.Ok), CoreLibrary.LastErrorMessage);
            try
            {
                Assert.That(NativeMethods.BodyCount(sim, out var count), Is.EqualTo((int)Status.Ok));
                Assert.That(count, Is.GreaterThan(0));
                Assert.That(NativeMethods.BodyInfoAt(sim, 0, out var first), Is.EqualTo((int)Status.Ok));
                Assert.That(Marshal.PtrToStringUTF8(first.Name), Is.Not.Empty);

                Assert.That(NativeMethods.TimeParse(sim, Utf8("2000-01-01T12:00:00 TDB"), NativeConstants.CalendarMixed, out var j2000),
                            Is.EqualTo((int)Status.Ok));
                Assert.That(j2000.Days, Is.EqualTo(0));
                Assert.That(NativeMethods.ClockSetTime(sim, j2000), Is.EqualTo((int)Status.Ok));

                var request = new UsimSnapshotRequest
                {
                    Center = UsimBodyId.Naif(399),
                    Frame = UsimFrame.EclipJ2000,
                    KmPerRenderUnit = 1.0e6,
                    Axes = NativeConstants.AxesUnity,
                    ScaleMode = NativeConstants.ScaleModeTrue,
                    BodyScale = 1.0,
                };
                var snapshots = new UsimBodySnapshot[count];
                Assert.That(NativeMethods.FillSnapshot(sim, ref request, snapshots, count, out var filled), Is.EqualTo((int)Status.Ok),
                            CoreLibrary.LastErrorMessage);
                Assert.That(filled, Is.EqualTo(count));
                var earth = snapshots.Single(s => s.Id.Value == 399);
                Assert.That(earth.RenderPosition.X, Is.EqualTo(0.0)); // the center itself
                Assert.That(earth.Flags & NativeConstants.SnapshotNoPosition, Is.EqualTo(0u));

                var buffer = new byte[64];
                Assert.That(NativeMethods.TimeFormat(sim, j2000, NativeConstants.ScaleTdb, NativeConstants.CalendarGregorian, 0,
                                                     buffer, buffer.Length, out var length), Is.EqualTo((int)Status.Ok));
                Assert.That(Encoding.UTF8.GetString(buffer, 0, length), Is.EqualTo("2000-01-01T12:00:00 TDB"));

                Assert.That(NativeMethods.StateAt(sim, UsimBodyId.Naif(399), UsimBodyId.Naif(4242), UsimFrame.Icrf, j2000, out _),
                            Is.EqualTo((int)Status.NotFound));
                StringAssert.Contains("naif:4242", CoreLibrary.LastErrorMessage);
            }
            finally
            {
                NativeMethods.SimulationDestroy(sim);
            }
        }
    }
}
