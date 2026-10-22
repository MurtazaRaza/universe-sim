using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace UniverseSim.Unity.Native
{
    /// <summary>A failed call into the core: its status and the core's message.</summary>
    public sealed class CoreException : Exception
    {
        public CoreException(Status status, string message) : base($"{message} ({CoreLibrary.StatusName(status)})")
        {
            Status = status;
        }

        /// <summary>The status the core returned.</summary>
        public Status Status { get; }
    }

    /// <summary>Owns a usim_simulation* and destroys it exactly once, even if Dispose is never called.</summary>
    internal sealed class SimulationHandle : SafeHandle
    {
        public SimulationHandle() : base(IntPtr.Zero, true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        internal void Adopt(IntPtr pointer) => SetHandle(pointer);

        protected override bool ReleaseHandle()
        {
            NativeMethods.SimulationDestroy(handle);
            return true;
        }
    }

    /// <summary>A catalog body as loaded once from the core (strings copied to managed memory).</summary>
    public sealed class BodyInfo
    {
        public int Index { get; internal set; }
        public UsimBodyId Id { get; internal set; }
        public UsimBodyId Primary { get; internal set; } // IdNamespace 0 for a root
        public int Kind { get; internal set; }           // usim::catalog::BodyKind
        public uint Flags { get; internal set; }         // NativeConstants.Body*
        public string Name { get; internal set; }
        public string ApproximationReason { get; internal set; } // "" unless approximate
        public double MeanRadiusKm { get; internal set; }

        /// <summary>The largest of the mean and triaxial radii, km (0 without a radius).</summary>
        public double MaxRadiusKm { get; internal set; }

        public bool HasPosition => (Flags & NativeConstants.BodyHasEphemeris) != 0;
        public bool IsApproximate => (Flags & NativeConstants.BodyApproximate) != 0;
        public bool HasMeanRadius => (Flags & NativeConstants.BodyHasMeanRadius) != 0;
        public bool HasRotation => (Flags & NativeConstants.BodyHasRotation) != 0;

        /// <summary>The kind's display name ("planet", "dwarf planet", ...).</summary>
        public string KindName => Kind switch
        {
            0 => "barycenter",
            1 => "star",
            2 => "planet",
            3 => "dwarf planet",
            4 => "moon",
            5 => "asteroid",
            6 => "comet",
            7 => "spacecraft",
            _ => "body",
        };
    }

    /// <summary>A sourced value with its provenance (copied from usim_quantity).</summary>
    public readonly struct QuantityInfo
    {
        public QuantityInfo(UsimQuantity q)
        {
            HasValue = q.HasValue != 0;
            Value = q.Value;
            Uncertainty = q.HasUncertainty != 0 ? q.Uncertainty : (double?)null;
            Unit = q.Unit;
            SourceId = Marshal.PtrToStringUTF8(q.SourceId) ?? string.Empty;
            Locator = Marshal.PtrToStringUTF8(q.Locator) ?? string.Empty;
            Derivation = Marshal.PtrToStringUTF8(q.Derivation) ?? string.Empty;
        }

        public bool HasValue { get; }
        public double Value { get; }
        public double? Uncertainty { get; }
        public int Unit { get; }
        public string SourceId { get; }
        public string Locator { get; }
        public string Derivation { get; }

        /// <summary>The unit symbol ("km", "km^3/s^2", ...).</summary>
        public string UnitSymbol => Unit switch
        {
            1 => "km",
            2 => "s",
            3 => "km/s",
            4 => "rad",
            5 => "rad/s",
            6 => "km^3/s^2",
            _ => string.Empty,
        };
    }

    /// <summary>A registered data source (copied from usim_source).</summary>
    public sealed class SourceInfo
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public string Provider { get; internal set; }
        public string Url { get; internal set; }
        public string Citation { get; internal set; }
        public string License { get; internal set; }
        public int Tier { get; internal set; }
    }

    /// <summary>A ring region or gap in a body's equatorial plane: visual-only geometry with its sources (P6-04).</summary>
    public sealed class RingInfo
    {
        public int BodyIndex { get; internal set; }
        public string Name { get; internal set; }
        public bool IsGap { get; internal set; }
        public double InnerRadiusKm { get; internal set; }
        public double OuterRadiusKm { get; internal set; }
        public bool HasOpticalDepth { get; internal set; }
        public double OpticalDepthSmallest { get; internal set; } // normal optical depth range, dimensionless
        public double OpticalDepthLargest { get; internal set; }
        public SourceInfo RadiiSource { get; internal set; }
        public string RadiiLocator { get; internal set; }
        public SourceInfo OpticalDepthSource { get; internal set; } // null without an optical depth
        public string OpticalDepthLocator { get; internal set; }
    }

    /// <summary>
    /// A loaded data pack with its catalog, ephemeris, frames and clock: the managed face of the C ABI
    /// (usim.h, docs/API.md). Per-frame calls (<see cref="Advance"/>, <see cref="FillSnapshot"/>,
    /// <see cref="OrbitPath"/>) take caller-owned buffers and allocate no managed memory unless they fail.
    /// Not thread-safe: use it from the main thread.
    /// </summary>
    public sealed class CoreSimulation : IDisposable
    {
        private readonly SimulationHandle handle;
        private readonly List<BodyInfo> bodies = new List<BodyInfo>();
        private readonly List<SourceInfo> sources = new List<SourceInfo>();
        private readonly List<RingInfo> rings = new List<RingInfo>();
        private readonly byte[] formatBuffer = new byte[96];

        private CoreSimulation(SimulationHandle handle)
        {
            this.handle = handle;
            LoadCatalog();
        }

        /// <summary>Loads the data pack at <paramref name="path"/>. Throws <see cref="CoreException"/>.</summary>
        public static CoreSimulation LoadFile(string path)
        {
            var handle = new SimulationHandle();
            Check(NativeMethods.SimulationLoadFile(Utf8(path), out var pointer));
            handle.Adopt(pointer);
            return new CoreSimulation(handle);
        }

        /// <summary>The catalog bodies, in catalog order (the order of snapshots).</summary>
        public IReadOnlyList<BodyInfo> Bodies => bodies;

        /// <summary>The registered sources the pack cites.</summary>
        public IReadOnlyList<SourceInfo> Sources => sources;

        /// <summary>Ring regions and gaps, sorted by body and inner radius (empty without rings).</summary>
        public IReadOnlyList<RingInfo> Rings => rings;

        /// <summary>The number of stars of the sky background (0 without a star catalog).</summary>
        public int StarCount { get; private set; }

        /// <summary>The star catalog's source, or null without one.</summary>
        public SourceInfo StarSource { get; private set; }

        /// <summary>The star catalog's file and columns ("" without one).</summary>
        public string StarLocator { get; private set; } = string.Empty;

        private IntPtr Pointer
        {
            get
            {
                if (handle.IsClosed)
                {
                    throw new ObjectDisposedException(nameof(CoreSimulation));
                }
                return handle.DangerousGetHandle();
            }
        }

        public void Dispose() => handle.Dispose();

        internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes((text ?? string.Empty) + "\0");

        internal static void Check(int status)
        {
            if (status != (int)Status.Ok)
            {
                throw new CoreException((Status)status, CoreLibrary.LastErrorMessage);
            }
        }

        private void LoadCatalog()
        {
            Check(NativeMethods.BodyCount(Pointer, out var count));
            for (var i = 0; i < count; ++i)
            {
                Check(NativeMethods.BodyInfoAt(Pointer, i, out var info));
                Check(NativeMethods.BodyApproximation(Pointer, i, out var reason));
                bodies.Add(new BodyInfo
                {
                    Index = i,
                    Id = info.Id,
                    Primary = info.Primary,
                    Kind = info.Kind,
                    Flags = info.Flags,
                    Name = Marshal.PtrToStringUTF8(info.Name) ?? string.Empty,
                    ApproximationReason = Marshal.PtrToStringUTF8(reason) ?? string.Empty,
                    MeanRadiusKm = info.MeanRadiusKm,
                    MaxRadiusKm = Math.Max(info.MeanRadiusKm, Math.Max(info.RadiusAKm, Math.Max(info.RadiusBKm, info.RadiusCKm))),
                });
            }
            Check(NativeMethods.SourceCount(Pointer, out var sourceCount));
            for (var i = 0; i < sourceCount; ++i)
            {
                Check(NativeMethods.SourceAt(Pointer, i, out var s));
                sources.Add(new SourceInfo
                {
                    Id = Marshal.PtrToStringUTF8(s.Id) ?? string.Empty,
                    Title = Marshal.PtrToStringUTF8(s.Title) ?? string.Empty,
                    Provider = Marshal.PtrToStringUTF8(s.Provider) ?? string.Empty,
                    Url = Marshal.PtrToStringUTF8(s.Url) ?? string.Empty,
                    Citation = Marshal.PtrToStringUTF8(s.Citation) ?? string.Empty,
                    License = Marshal.PtrToStringUTF8(s.License) ?? string.Empty,
                    Tier = s.Tier,
                });
            }
            Check(NativeMethods.RingCount(Pointer, out var ringCount));
            for (var i = 0; i < ringCount; ++i)
            {
                Check(NativeMethods.RingAt(Pointer, i, out var r));
                var hasDepth = (r.Flags & NativeConstants.RingHasOpticalDepth) != 0;
                rings.Add(new RingInfo
                {
                    BodyIndex = r.BodyIndex,
                    Name = Marshal.PtrToStringUTF8(r.Name) ?? string.Empty,
                    IsGap = (r.Flags & NativeConstants.RingGap) != 0,
                    InnerRadiusKm = r.InnerRadiusKm,
                    OuterRadiusKm = r.OuterRadiusKm,
                    HasOpticalDepth = hasDepth,
                    OpticalDepthSmallest = r.OpticalDepthSmallest,
                    OpticalDepthLargest = r.OpticalDepthLargest,
                    RadiiSource = r.RadiiSourceIndex >= 0 && r.RadiiSourceIndex < sources.Count ? sources[r.RadiiSourceIndex] : null,
                    RadiiLocator = Marshal.PtrToStringUTF8(r.RadiiLocator) ?? string.Empty,
                    OpticalDepthSource = hasDepth && r.OpticalDepthSourceIndex >= 0 && r.OpticalDepthSourceIndex < sources.Count
                        ? sources[r.OpticalDepthSourceIndex] : null,
                    OpticalDepthLocator = Marshal.PtrToStringUTF8(r.OpticalDepthLocator) ?? string.Empty,
                });
            }
            Check(NativeMethods.StarCount(Pointer, out var starCount));
            StarCount = starCount;
            if (starCount > 0)
            {
                Check(NativeMethods.StarCatalogSource(Pointer, out var starSource, out var locator, out _));
                StarSource = starSource >= 0 && starSource < sources.Count ? sources[starSource] : null;
                StarLocator = Marshal.PtrToStringUTF8(locator) ?? string.Empty;
            }
        }

        /// <summary>The index of the body with <paramref name="id"/>, or -1.</summary>
        public int IndexOf(UsimBodyId id)
        {
            for (var i = 0; i < bodies.Count; ++i)
            {
                if (bodies[i].Id.IdNamespace == id.IdNamespace && bodies[i].Id.Value == id.Value)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>A value of a body with its provenance (field: NativeConstants.Field*).</summary>
        public QuantityInfo Quantity(int bodyIndex, int field)
        {
            Check(NativeMethods.BodyQuantity(Pointer, bodyIndex, field, out var q));
            return new QuantityInfo(q);
        }

        /// <summary>The span in which every body with a position is covered: the clock's limits.</summary>
        public UsimCoverage Coverage
        {
            get
            {
                Check(NativeMethods.SimulationCoverage(Pointer, out var coverage));
                return coverage;
            }
        }

        /// <summary>The clock: current instant, rate, pause, the limit reached and the limits.</summary>
        public UsimClockState Clock
        {
            get
            {
                Check(NativeMethods.ClockGet(Pointer, out var state));
                return state;
            }
        }

        /// <summary>Moves the clock by real seconds times the rate, unless paused.</summary>
        public void Advance(double realSeconds) => Check(NativeMethods.ClockAdvance(Pointer, realSeconds));

        /// <summary>Sets the clock's time (clamped to the coverage).</summary>
        public void SetTime(UsimInstant t) => Check(NativeMethods.ClockSetTime(Pointer, t));

        /// <summary>Sets the clock's rate in simulated seconds per real second (negative runs backward).</summary>
        public void SetRate(double rate) => Check(NativeMethods.ClockSetRate(Pointer, rate));

        /// <summary>Pauses or resumes the clock.</summary>
        public void SetPaused(bool paused) => Check(NativeMethods.ClockSetPaused(Pointer, paused ? 1 : 0));

        /// <summary>The instant of a UTC date and time (DateTimeKind is ignored; the fields are read as UTC).</summary>
        public UsimInstant FromUtc(DateTime utc)
        {
            var second = utc.Second + (utc.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;
            Check(NativeMethods.TimeFromFields(Pointer, NativeConstants.ScaleUtc, utc.Year, utc.Month, utc.Day, utc.Hour,
                                               utc.Minute, second, out var t));
            return t;
        }

        /// <summary>Parses "2026-10-21T12:00:00 UTC" style text (a scale suffix is required).</summary>
        public bool TryParseTime(string text, out UsimInstant t, out string error)
        {
            var status = NativeMethods.TimeParse(Pointer, Utf8(text), NativeConstants.CalendarMixed, out t);
            error = status == (int)Status.Ok ? string.Empty : CoreLibrary.LastErrorMessage;
            return status == (int)Status.Ok;
        }

        /// <summary>
        /// Formats an instant on a scale (NativeConstants.Scale*), for example "2026-10-21T12:00:00 UTC".
        /// Returns false (and leaves <paramref name="text"/> empty) when the scale cannot show the instant,
        /// such as UTC before 1972.
        /// </summary>
        public bool TryFormatTime(UsimInstant t, int scale, int decimals, out string text)
        {
            var status = NativeMethods.TimeFormat(Pointer, t, scale, NativeConstants.CalendarMixed, decimals, formatBuffer,
                                                  formatBuffer.Length, out var length);
            text = status == (int)Status.Ok ? Encoding.UTF8.GetString(formatBuffer, 0, Math.Min(length, formatBuffer.Length - 1))
                                            : string.Empty;
            return status == (int)Status.Ok;
        }

        /// <summary>The state of a body relative to a center. Throws <see cref="CoreException"/>.</summary>
        public UsimState State(UsimBodyId body, UsimBodyId center, UsimFrame frame, UsimInstant t)
        {
            Check(NativeMethods.StateAt(Pointer, body, center, frame, t, out var state));
            return state;
        }

        /// <summary>Like <see cref="State"/>, without throwing.</summary>
        public bool TryState(UsimBodyId body, UsimBodyId center, UsimFrame frame, UsimInstant t, out UsimState state) =>
            NativeMethods.StateAt(Pointer, body, center, frame, t, out state) == (int)Status.Ok;

        /// <summary>
        /// Fills <paramref name="buffer"/> with every body at the clock's time; returns the count. No managed
        /// allocation. Throws <see cref="CoreException"/> for an invalid request.
        /// </summary>
        public int FillSnapshot(ref UsimSnapshotRequest request, UsimBodySnapshot[] buffer)
        {
            Check(NativeMethods.FillSnapshot(Pointer, ref request, buffer, buffer.Length, out var count));
            return count;
        }

        /// <summary>
        /// Fills <paramref name="buffer"/> with the stars at the clock's time, in the request's frame and axes;
        /// returns the count. No managed allocation. Throws <see cref="CoreException"/> for an invalid request.
        /// </summary>
        public int FillStars(ref UsimSnapshotRequest request, UsimStar[] buffer)
        {
            Check(NativeMethods.FillStars(Pointer, ref request, buffer, buffer.Length, out var count));
            return count;
        }

        /// <summary>The body an orbit line of <paramref name="body"/> is usually drawn around, if any.</summary>
        public bool TryDefaultOrbitCenter(UsimBodyId body, out UsimBodyId center) =>
            NativeMethods.DefaultOrbitCenter(Pointer, body, out center) == (int)Status.Ok;

        /// <summary>
        /// One revolution of <paramref name="body"/> around <paramref name="center"/> as render positions for
        /// the view in <paramref name="request"/>. Returns the full point count (more than the buffer holds
        /// if it did not fit), or -1 if no path is available. No managed allocation.
        /// </summary>
        public int OrbitPath(UsimBodyId body, UsimBodyId center, ref UsimSnapshotRequest request, UsimVec3d[] buffer,
                             out uint flags)
        {
            var status = NativeMethods.OrbitPath(Pointer, body, center, ref request, buffer, buffer.Length, out var count,
                                                 out flags);
            return status == (int)Status.Ok ? count : -1;
        }

        /// <summary>A vector from the host's axes to the core's (the inverse of the snapshot's mapping).</summary>
        public static UsimVec3d AxesFromHost(int axes, UsimVec3d host)
        {
            Check(NativeMethods.AxesFromHost(axes, host, out var core));
            return core;
        }

        /// <summary>A vector from the core's axes to the host's.</summary>
        public static UsimVec3d AxesToHost(int axes, UsimVec3d core)
        {
            Check(NativeMethods.AxesToHost(axes, core, out var host));
            return host;
        }
    }
}
