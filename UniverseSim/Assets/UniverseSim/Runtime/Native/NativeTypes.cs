using System;
using System.Runtime.InteropServices;

namespace UniverseSim.Unity.Native
{
    // Blittable mirrors of the C structs in core/capi/include/usim/usim.h. Field order, types and sizes must
    // match the header exactly; CoreLibrary.StructSizeMismatches() compares the sizes with the library's
    // usim_struct_size() at startup and in the EditMode tests. Pointers (strings owned by the library) are
    // IntPtr. Units: km, s, km/s, rad, km^3/s^2.

    /// <summary>usim_vec3d: three doubles.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimVec3d
    {
        public double X;
        public double Y;
        public double Z;
    }

    /// <summary>usim_quatd: a unit quaternion, scalar first.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimQuatd
    {
        public double W;
        public double X;
        public double Y;
        public double Z;
    }

    /// <summary>usim_body_id: an identifier namespace (0 for none, 1 NAIF) and a value.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimBodyId
    {
        public int IdNamespace;
        public int Reserved;
        public long Value;

        /// <summary>The body with NAIF ID <paramref name="naifId"/>.</summary>
        public static UsimBodyId Naif(long naifId) => new UsimBodyId { IdNamespace = NativeConstants.IdNamespaceNaif, Value = naifId };
    }

    /// <summary>usim_instant: TDB whole days since J2000.0 plus seconds of day in [0, 86400).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimInstant
    {
        public long Days;
        public double Seconds;
    }

    /// <summary>usim_frame: a frame kind and, for body-fixed frames, the body.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimFrame
    {
        public int Kind;
        public int Reserved;
        public UsimBodyId Body;

        public static UsimFrame Icrf => new UsimFrame { Kind = NativeConstants.FrameIcrf };
        public static UsimFrame EclipJ2000 => new UsimFrame { Kind = NativeConstants.FrameEclipJ2000 };
        public static UsimFrame BodyFixed(UsimBodyId body) => new UsimFrame { Kind = NativeConstants.FrameBodyFixed, Body = body };
    }

    /// <summary>usim_coverage: a TDB span, inclusive at both ends.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimCoverage
    {
        public UsimInstant Start;
        public UsimInstant End;
    }

    /// <summary>usim_body_info: a catalog body. Values are 0 when the matching flag is not set.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimBodyInfo
    {
        public UsimBodyId Id;
        public UsimBodyId Primary;
        public int Kind;
        public uint Flags;
        public IntPtr Name; // UTF-8, owned by the simulation
        public double GmKm3S2;
        public double MeanRadiusKm;
        public double RadiusAKm;
        public double RadiusBKm;
        public double RadiusCKm;
    }

    /// <summary>usim_quantity: a sourced value with its provenance.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimQuantity
    {
        public double Value;
        public double Uncertainty;
        public int HasValue;
        public int HasUncertainty;
        public int Unit;
        public int Reserved;
        public IntPtr SourceId;   // UTF-8, owned by the simulation
        public IntPtr Locator;    // UTF-8, owned by the simulation
        public IntPtr Derivation; // UTF-8, owned by the simulation
    }

    /// <summary>usim_source: a registered data source.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimSource
    {
        public IntPtr Id;
        public IntPtr Title;
        public IntPtr Provider;
        public IntPtr Url;
        public IntPtr Citation;
        public IntPtr License;
        public int Tier;
        public int Reserved;
    }

    /// <summary>usim_clock_state.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimClockState
    {
        public UsimInstant Now;
        public double Rate;
        public int Paused;
        public int Limit;
        public UsimCoverage Limits;
    }

    /// <summary>usim_state: position (km) and velocity (km/s) with quality.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimState
    {
        public UsimVec3d PositionKm;
        public UsimVec3d VelocityKmS;
        public int Model;
        public uint Flags;
    }

    /// <summary>usim_snapshot_request: how positions become render coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimSnapshotRequest
    {
        public UsimBodyId Center;
        public UsimFrame Frame;
        public UsimVec3d OriginKm;
        public double KmPerRenderUnit;
        public int Axes;
        public int ScaleMode;
        public double BodyScale;
        public double CompressionDistance; // render units; 0 turns distance compression off (ADR-0009)
    }

    /// <summary>usim_body_snapshot: one body, ready to render.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimBodySnapshot
    {
        public UsimBodyId Id;
        public UsimVec3d RenderPosition;
        public UsimQuatd Orientation;
        public double RenderRadius;
        public UsimVec3d RenderRadii; // along the body's own axes, as lengths along the host axes
        public UsimVec3d LightDirection; // unit vector toward the star, host axes; zero without light
        public uint Flags;
        public uint Reserved;
    }

    /// <summary>usim_star: one star of the sky background (ABI 3).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimStar
    {
        public UsimVec3d Direction; // unit vector toward the star at the clock's time, display frame, host axes
        public double VMagnitude;
        public double BMinusV;      // 0 without StarHasColor
        public uint Flags;
        public uint Reserved;
    }

    /// <summary>usim_ring: a ring region or gap in a body's equatorial plane, visual-only (ABI 3).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UsimRing
    {
        public int BodyIndex;
        public uint Flags;
        public double InnerRadiusKm;
        public double OuterRadiusKm;
        public double OpticalDepthSmallest;
        public double OpticalDepthLargest;
        public int RadiiSourceIndex;
        public int OpticalDepthSourceIndex; // -1 without an optical depth
        public IntPtr Name;                 // UTF-8, owned by the simulation
        public IntPtr RadiiLocator;
        public IntPtr OpticalDepthLocator;
    }

    /// <summary>The constants of usim.h that bindings need.</summary>
    public static class NativeConstants
    {
        public const int IdNamespaceNaif = 1;

        public const int FrameIcrf = 0;
        public const int FrameEclipJ2000 = 1;
        public const int FrameBodyFixed = 2;

        public const int StructVec3d = 1;
        public const int StructQuatd = 2;
        public const int StructBodyId = 3;
        public const int StructInstant = 4;
        public const int StructFrame = 5;
        public const int StructBodyInfo = 6;
        public const int StructQuantity = 7;
        public const int StructSource = 8;
        public const int StructClockState = 9;
        public const int StructState = 10;
        public const int StructSnapshotRequest = 11;
        public const int StructBodySnapshot = 12;
        public const int StructCoverage = 13;
        public const int StructStar = 14;
        public const int StructRing = 15;

        public const uint BodyHasEphemeris = 0x1;
        public const uint BodyApproximate = 0x2;
        public const uint BodyHasRotation = 0x4;
        public const uint BodyHasMeanRadius = 0x8;
        public const uint BodyHasRadii = 0x10;
        public const uint BodyHasGm = 0x20;

        public const int FieldGm = 1;
        public const int FieldMeanRadius = 2;
        public const int FieldRadiusA = 3;
        public const int FieldRadiusB = 4;
        public const int FieldRadiusC = 5;

        public const int ScaleTdb = 0;
        public const int ScaleTt = 1;
        public const int ScaleTai = 2;
        public const int ScaleUtc = 3;
        public const int CalendarGregorian = 0;
        public const int CalendarJulian = 1;
        public const int CalendarMixed = 2;

        public const int ClockLimitNone = 0;
        public const int ClockLimitStart = 1;
        public const int ClockLimitEnd = 2;

        public const int ModelEphemeris = 0;
        public const uint StateApproximate = 0x1;

        public const int AxesCore = 0;
        public const int AxesUnity = 1;
        public const int ScaleModeTrue = 0;
        public const int ScaleModeEnlarged = 1;

        public const uint SnapshotApproximate = 0x1;
        public const uint SnapshotNoPosition = 0x2;
        public const uint SnapshotNoOrientation = 0x4;
        public const uint SnapshotNoRadius = 0x8;
        public const uint SnapshotCompressed = 0x10;
        public const uint SnapshotNoLight = 0x20;

        public const uint OrbitFullRevolution = 0x1;
        public const uint OrbitApproximate = 0x2;

        public const int IdNamespaceHr = 4;
        public const uint StarHasColor = 0x1;
        public const uint RingGap = 0x1;
        public const uint RingHasOpticalDepth = 0x2;
    }
}
