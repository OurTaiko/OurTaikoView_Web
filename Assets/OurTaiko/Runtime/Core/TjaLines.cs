using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    public enum TjaLineKind
    {
        Header,  // KEY:VALUE outside a chart block
        Start,   // #START, with its player (P1, P2) as Value
        End,     // #END, or where a block without one ends (Implicit)
        Body,    // a note line or command inside #START … #END
        Other,   // anything else outside a block
    }

    // One meaningful line of a TJA file: without the BOM, `//` comments or surrounding spaces;
    // empty lines are skipped. #START and #END are recognised in any case.
    public readonly struct TjaLine
    {
        public readonly TjaLineKind Kind;
        public readonly string Text;
        // A header's upper-case key and trimmed value; #START's player.
        public readonly string Key, Value;
        // An End the file did not write.
        public readonly bool Implicit;

        public TjaLine(TjaLineKind kind, string text, string key = null, string value = null, bool @implicit = false)
        {
            Kind = kind; Text = text; Key = key; Value = value; Implicit = @implicit;
        }
    }

    // The one line reader behind TjaParser, SongInfo, the local song search and the online
    // playable copy, so they agree on comments, case and block boundaries. Every Start gets an
    // End: as in OpenTaiko, a block without #END ends at the next COURSE:, #START or the file's end.
    public static class TjaLines
    {
        static readonly Regex CourseLine = new Regex(@"^COURSE\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static IEnumerable<TjaLine> Read(string text)
        {
            bool inBlock = false;
            foreach (string raw in text.TrimStart('﻿').Replace("\r", "").Split('\n'))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string line = (comment >= 0 ? raw.Substring(0, comment) : raw).Trim();
                if (line.Length == 0) continue;
                bool start = line.StartsWith("#START", StringComparison.OrdinalIgnoreCase);
                if (inBlock && (start || CourseLine.IsMatch(line)))
                {
                    inBlock = false;
                    yield return new TjaLine(TjaLineKind.End, "#END", @implicit: true);
                }
                if (start)
                {
                    inBlock = true;
                    yield return new TjaLine(TjaLineKind.Start, line, value: line.Substring(6).Trim().ToUpperInvariant());
                }
                else if (line.Equals("#END", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = false;
                    yield return new TjaLine(TjaLineKind.End, line);
                }
                else if (inBlock) yield return new TjaLine(TjaLineKind.Body, line);
                else
                {
                    int colon = line.IndexOf(':');
                    yield return colon < 0 ? new TjaLine(TjaLineKind.Other, line)
                        : new TjaLine(TjaLineKind.Header, line, line.Substring(0, colon).Trim().ToUpperInvariant(), line.Substring(colon + 1).Trim());
                }
            }
            if (inBlock) yield return new TjaLine(TjaLineKind.End, "#END", @implicit: true);
        }
    }
}
