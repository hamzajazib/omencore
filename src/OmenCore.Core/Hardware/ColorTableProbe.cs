using System;
using System.Collections.Generic;

namespace OmenCore.Hardware
{
    /// <summary>
    /// GitHub #212 (Victus 16-s0xxx, board 8BD4, firmware topology OneZoneWithNumpad): the declared zone count in byte 0
    /// reaches the hardware but the keyboard still does not change colour. The real single-zone payload is not known,
    /// so this holds a short list of candidate layouts. Each differs from the baseline in exactly one way, so the owner
    /// can report which numbered step (if any) lit the keyboard. These are hypotheses to test, not fixes.
    /// </summary>
    public static class ColorTableProbe
    {
        public sealed record Variant(string Id, string Description, Func<byte, byte, byte, byte[]> Build);

        private const int Size = 128;

        public static IReadOnlyList<Variant> Variants { get; } = new[]
        {
            new Variant("baseline-count1-offset25",
                "What 4.4.1 sends: zone count 1, colour at offset 25 (control, known not to work on 8BD4)",
                (r, g, b) => Table(count: 1, colourAt: 25, r, g, b)),
            new Variant("count1-offset1",
                "Zone count 1, colour directly after the count byte (no 24-byte pad)",
                (r, g, b) => Table(count: 1, colourAt: 1, r, g, b)),
            new Variant("count0-offset25",
                "Zone count 0 (some firmware treats 0 as 'single/all'), colour at offset 25",
                (r, g, b) => Table(count: 0, colourAt: 25, r, g, b)),
            new Variant("count1-both",
                "Zone count 1, colour at both offset 1 and offset 25",
                (r, g, b) => { var t = Table(1, 25, r, g, b); Put(t, 1, r, g, b); return t; }),
            new Variant("count1-offset25-rgbx",
                "Zone count 1, colour at offset 25 as four bytes R,G,B,255 (a padded pixel)",
                (r, g, b) => { var t = Table(1, 25, r, g, b); t[28] = 0xFF; return t; }),
        };

        private static byte[] Table(byte count, int colourAt, byte r, byte g, byte b)
        {
            var t = new byte[Size];
            t[0] = count;
            Put(t, colourAt, r, g, b);
            return t;
        }

        private static void Put(byte[] t, int at, byte r, byte g, byte b)
        {
            t[at] = r;
            t[at + 1] = g;
            t[at + 2] = b;
        }
    }
}
