using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RiveTT.Tools.Utilities;

public static class GridLabels
{
    public static string Generate(string start, int index, string style)
    {
        if (string.IsNullOrWhiteSpace(start) || index < 0)
            throw new ArgumentException("Grid start label must not be empty; index must be non-negative.");
        if (!string.Equals(style, "numeric", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(style, "alphabetic", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Grid naming style must be numeric or alphabetic.");
        if (string.Equals(style, "numeric", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(start, @"^(.*?)([0-9]+)$");
            if (!match.Success)
                throw new ArgumentException("Numeric grid labels must end in digits (1, 01, A1). Use alphabetic for letters.");
            var number = checked(long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) + index);
            return match.Groups[1].Value + number.ToString(
                new string('0', match.Groups[2].Length), CultureInfo.InvariantCulture);
        }
        if (!Regex.IsMatch(start, "^[A-Za-z]+$"))
            throw new ArgumentException("Alphabetic grid labels must contain A-Z only (A, K, RK). Use numeric for A1/01, or rename for custom labels.");
        long value = 0;
        foreach (var c in start.ToUpperInvariant())
            value = checked(value * 26 + c - 'A' + 1);
        value = checked(value + index);
        var label = "";
        while (value > 0)
        {
            value--;
            label = (char)('A' + value % 26) + label;
            value /= 26;
        }
        return label;
    }
}
