using System;
using System.Globalization;

namespace AssetStudioCLI
{
    /// <summary>Format angka OBJ: selalu titik desimal, tanpa notasi eksponen, tidak bergantung bahasa HP.</summary>
    internal static class ObjFmt
    {
        public static string N(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f) || Math.Abs(f) < 0.0000005f) return "0";
            return f.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
