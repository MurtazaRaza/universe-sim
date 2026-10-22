using System;
using System.Runtime.InteropServices;

namespace UniverseSim.Unity.Native
{
    /// <summary>
    /// Raw P/Invoke declarations for the core's C ABI (core/capi/include/usim/usim.h).
    /// Keep this file in sync with that header. Only blittable types cross the boundary: strings going in
    /// are NUL-terminated UTF-8 byte arrays, strings coming out are IntPtr owned by the library, and
    /// output arrays are caller-owned (pinned, not copied, because their elements are blittable).
    /// Statuses are the int values of <see cref="Status"/>.
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Library name without prefix or extension. The core builds it as universesim.dylib,
        /// universesim.dll or universesim.so.
        /// </summary>
        private const string Library = "universesim";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_abi_version")]
        internal static extern uint AbiVersion();

        // Returns a pointer to a static, null-terminated UTF-8 string owned by the library.
        // Marshalled as IntPtr on purpose: a string return type would make the runtime free it.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_version_string")]
        internal static extern IntPtr VersionString();

        // Returns a pointer to a static, null-terminated string ("ok", "invalid_argument", ..., or "unknown").
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_status_name")]
        internal static extern IntPtr StatusName(int status);

        // The last failing call's message on this thread; owned by the library.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_last_error_message")]
        internal static extern IntPtr LastErrorMessage();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_struct_size")]
        internal static extern uint StructSize(int structId);

        // Lifecycle. utf8Path is NUL-terminated UTF-8.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_simulation_load_file")]
        internal static extern int SimulationLoadFile(byte[] utf8Path, out IntPtr simulation);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_simulation_load_memory")]
        internal static extern int SimulationLoadMemory(byte[] data, ulong size, out IntPtr simulation);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_simulation_destroy")]
        internal static extern void SimulationDestroy(IntPtr simulation);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_simulation_coverage")]
        internal static extern int SimulationCoverage(IntPtr simulation, out UsimCoverage coverage);

        // Catalog.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_body_count")]
        internal static extern int BodyCount(IntPtr simulation, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_body_info_at")]
        internal static extern int BodyInfoAt(IntPtr simulation, int index, out UsimBodyInfo info);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_body_find")]
        internal static extern int BodyFind(IntPtr simulation, UsimBodyId id, out int index);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_body_quantity")]
        internal static extern int BodyQuantity(IntPtr simulation, int index, int field, out UsimQuantity quantity);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_body_approximation")]
        internal static extern int BodyApproximation(IntPtr simulation, int index, out IntPtr reason);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_source_count")]
        internal static extern int SourceCount(IntPtr simulation, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_source_at")]
        internal static extern int SourceAt(IntPtr simulation, int index, out UsimSource source);

        // Time.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_instant_make")]
        internal static extern int InstantMake(long days, double seconds, out UsimInstant instant);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_instant_from_seconds")]
        internal static extern int InstantFromSeconds(double tdbSeconds, out UsimInstant instant);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_instant_seconds_between")]
        internal static extern double InstantSecondsBetween(UsimInstant a, UsimInstant b);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_time_from_fields")]
        internal static extern int TimeFromFields(IntPtr simulation, int scale, int year, int month, int day, int hour,
                                                  int minute, double second, out UsimInstant instant);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_time_parse")]
        internal static extern int TimeParse(IntPtr simulation, byte[] utf8Text, int calendar, out UsimInstant instant);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_time_format")]
        internal static extern int TimeFormat(IntPtr simulation, UsimInstant t, int scale, int calendar, int decimals,
                                              [Out] byte[] buffer, int capacity, out int length);

        // Clock.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_clock_get")]
        internal static extern int ClockGet(IntPtr simulation, out UsimClockState state);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_clock_advance")]
        internal static extern int ClockAdvance(IntPtr simulation, double realSeconds);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_clock_set_time")]
        internal static extern int ClockSetTime(IntPtr simulation, UsimInstant t);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_clock_set_rate")]
        internal static extern int ClockSetRate(IntPtr simulation, double rate);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_clock_set_paused")]
        internal static extern int ClockSetPaused(IntPtr simulation, int paused);

        // States and orientation.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_state_at")]
        internal static extern int StateAt(IntPtr simulation, UsimBodyId body, UsimBodyId center, UsimFrame frame,
                                           UsimInstant t, out UsimState state);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_orientation_at")]
        internal static extern int OrientationAt(IntPtr simulation, UsimBodyId body, UsimFrame frame, UsimInstant t,
                                                 out UsimQuatd orientation);

        // Axis conventions: vectors between the core's axes and the host's.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_axes_to_host")]
        internal static extern int AxesToHost(int axes, UsimVec3d core, out UsimVec3d host);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_axes_from_host")]
        internal static extern int AxesFromHost(int axes, UsimVec3d host, out UsimVec3d core);

        // Snapshots and orbit paths.
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_fill_snapshot")]
        internal static extern int FillSnapshot(IntPtr simulation, ref UsimSnapshotRequest request,
                                                [Out] UsimBodySnapshot[] snapshots, int capacity, out int count);

        // Sky background and rings (ABI 3).
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_star_count")]
        internal static extern int StarCount(IntPtr simulation, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_star_catalog_source")]
        internal static extern int StarCatalogSource(IntPtr simulation, out int sourceIndex, out IntPtr locator,
                                                     out IntPtr derivation);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_fill_stars")]
        internal static extern int FillStars(IntPtr simulation, ref UsimSnapshotRequest request, [Out] UsimStar[] stars,
                                             int capacity, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_ring_count")]
        internal static extern int RingCount(IntPtr simulation, out int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_ring_at")]
        internal static extern int RingAt(IntPtr simulation, int index, out UsimRing ring);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_default_orbit_center")]
        internal static extern int DefaultOrbitCenter(IntPtr simulation, UsimBodyId body, out UsimBodyId center);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "usim_orbit_path")]
        internal static extern int OrbitPath(IntPtr simulation, UsimBodyId body, UsimBodyId orbitCenter,
                                             ref UsimSnapshotRequest request, [Out] UsimVec3d[] positions, int capacity,
                                             out int count, out uint flags);
    }
}
