using System;
using UniverseSim.Unity.Native;
using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// A double-precision vector for the camera's offset from its focus body (km, host axes). Camera math
    /// only; positions of bodies always come from the core.
    /// </summary>
    public readonly struct Double3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Double3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Double3 operator *(Double3 a, double s) => new Double3(a.X * s, a.Y * s, a.Z * s);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Double3 Normalized
        {
            get
            {
                var length = Length;
                return length > 0.0 ? this * (1.0 / length) : new Double3(0.0, 0.0, 1.0);
            }
        }

        public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);

        public static Double3 From(Vector3 v) => new Double3(v.x, v.y, v.z);

        public static Double3 From(UsimVec3d v) => new Double3(v.X, v.Y, v.Z);

        public UsimVec3d ToUsim() => new UsimVec3d { X = X, Y = Y, Z = Z };
    }
}
