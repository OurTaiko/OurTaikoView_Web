using System.Globalization;

namespace OurTaiko
{
    // Numbers in TJA headers and commands. A trailing comma is dropped (`#BPMCHANGE 120,`) and a
    // comma inside is a decimal point (`BPM:120,5` is 120.5, as OpenTaiko reads it), never a
    // thousands separator.
    public static class TjaNumber
    {
        static string Normalize(string value) => value.Trim().TrimEnd(',', ' ').Replace(',', '.');

        public static bool TryParse(string value, out double result)
            => double.TryParse(Normalize(value), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

        public static double Parse(string value)
            => double.Parse(Normalize(value), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
