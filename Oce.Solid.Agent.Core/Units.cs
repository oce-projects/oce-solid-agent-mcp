using System;

namespace Oce.Solid.Agent.Core
{
    /// <summary>
    /// The only place in the program where we convert between millimeters and meters.
    ///
    /// WHY A WHOLE FILE FOR THREE DIVISIONS
    /// The SOLIDWORKS API wants METERS and RADIANS, always, however the
    /// document's units are set. Passing it millimeters produces
    /// no error: it throws no exceptions, does not prevent the rebuild, and
    /// builds a part A THOUSAND TIMES bigger. Only whoever looks at the
    /// dimensions notices — that is perhaps nobody, until the part reaches the
    /// shop.
    ///
    /// It is the most dangerous class of error of this whole work, and the only
    /// defense that holds over time is that the conversion lives in one place and
    /// that this place is visible. On the protocol, in the table and in front of the user
    /// we ALWAYS speak in millimeters and degrees; in here, and only here, we switch
    /// to what SOLIDWORKS wants.
    ///
    /// RULE FOR WHOEVER REVIEWS THE CODE
    /// A division by 1000, a multiplication by 0.001 or a Math.PI/180
    /// outside this file is a defect, even if the result looks
    /// right: it means a second road was born, and the second road
    /// sooner or later diverges.
    /// </summary>
    public static class Units
    {
        public const double MmPerMeter = 1000.0;

        /// <summary>Millimeters -> meters, to go toward SOLIDWORKS.</summary>
        public static double M(double mm) => mm / MmPerMeter;

        /// <summary>Degrees -> radians, to go toward SOLIDWORKS.</summary>
        public static double Rad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Meters -> millimeters, to report to the server what was measured.</summary>
        public static double Mm(double meters) => meters * MmPerMeter;

        /// <summary>
        /// Square meters -> square millimeters, for the areas read by SOLIDWORKS.
        /// It lives here and not next to the reading because the rule holds for
        /// areas too: a scattered conversion is the first step toward two roads.
        /// </summary>
        public static double Mm2(double m2) => m2 * MmPerMeter * MmPerMeter;

        /// <summary>Radians -> degrees, to report to the server.</summary>
        public static double Degrees(double radians) => radians * 180.0 / Math.PI;
    }
}
