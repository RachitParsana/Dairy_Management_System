using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dairy_Management_System.Common
{
    /// <summary>
    /// High-performance RFC 4180 compliant CSV serialization utility with UTF-8 BOM encoding
    /// and spreadsheet formula injection (CSV Injection) mitigation.
    /// </summary>
    public static class CsvExportHelper
    {
        private static readonly char[] SpecialChars = { ',', '"', '\r', '\n' };
        private static readonly char[] FormulaLeadingChars = { '=', '+', '-', '@', '\t', '\r' };

        /// <summary>
        /// Serializes tabular data (headers and rows) into an RFC 4180 CSV byte array with UTF-8 BOM.
        /// </summary>
        public static byte[] BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
        {
            using var memoryStream = new MemoryStream();
            // UTF-8 with BOM for seamless Microsoft Excel rendering of Gujarati and multilingual Unicode text
            using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(true), 4096, leaveOpen: true))
            {
                // Write Header row
                for (int i = 0; i < headers.Count; i++)
                {
                    if (i > 0) writer.Write(',');
                    writer.Write(EscapeField(headers[i], isFormulaCheckRequired: false));
                }
                writer.WriteLine();

                // Write Data rows
                foreach (var row in rows)
                {
                    for (int i = 0; i < row.Count; i++)
                    {
                        if (i > 0) writer.Write(',');
                        writer.Write(EscapeField(row[i], isFormulaCheckRequired: true));
                    }
                    writer.WriteLine();
                }

                writer.Flush();
            }

            return memoryStream.ToArray();
        }

        /// <summary>
        /// Escapes a single CSV field adhering strictly to RFC 4180 and formula injection mitigation.
        /// </summary>
        public static string EscapeField(string? field, bool isFormulaCheckRequired = true)
        {
            if (string.IsNullOrEmpty(field))
            {
                return string.Empty;
            }

            string sanitized = field;

            // Spreadsheet formula injection mitigation:
            // If the field starts with an active formula trigger symbol and is not a plain decimal/negative number,
            // neutralize it with a single quote prefix so Excel/Calc treats it as inert string literal.
            if (isFormulaCheckRequired && sanitized.Length > 0)
            {
                char firstChar = sanitized[0];
                if (Array.IndexOf(FormulaLeadingChars, firstChar) >= 0)
                {
                    // Allow normal negative numbers (e.g., -120.50) without mangling
                    if (firstChar == '-' && decimal.TryParse(sanitized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        // Valid number; safe from formula injection
                    }
                    else
                    {
                        sanitized = "'" + sanitized;
                    }
                }
            }

            // RFC 4180 quoting rules:
            // If field contains comma, quote, or newline, enclose in quotes and double internal quotes
            if (sanitized.IndexOfAny(SpecialChars) >= 0)
            {
                return "\"" + sanitized.Replace("\"", "\"\"") + "\"";
            }

            return sanitized;
        }
    }
}
