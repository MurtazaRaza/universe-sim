namespace UniverseSim.Unity.Native
{
    /// <summary>
    /// Status codes returned by fallible C ABI functions. Mirrors the USIM_STATUS_* values in
    /// core/capi/include/usim/usim.h (and usim::ErrorCode in the core). Codes are never renumbered, but the
    /// core may add new ones, so treat any unknown non-zero value as a generic failure.
    /// </summary>
    public enum Status
    {
        /// <summary>Success.</summary>
        Ok = 0,

        /// <summary>An argument is outside its domain.</summary>
        InvalidArgument = 1,

        /// <summary>Text could not be parsed.</summary>
        ParseError = 2,

        /// <summary>A value lies outside the range a model supports (for example UTC before 1972).</summary>
        OutOfRange = 3,

        /// <summary>No data covers the requested instant.</summary>
        OutOfCoverage = 4,

        /// <summary>An identifier (body, frame, source) is unknown.</summary>
        NotFound = 5,

        /// <summary>The request is valid but not implemented by this version of the core.</summary>
        Unsupported = 6,

        /// <summary>Input data is malformed or inconsistent.</summary>
        DataFormat = 7,

        /// <summary>Input data uses a format version the core cannot read.</summary>
        IncompatibleVersion = 8,

        /// <summary>A file could not be read.</summary>
        IoError = 9,

        /// <summary>A bug in the core.</summary>
        Internal = 10,
    }
}
