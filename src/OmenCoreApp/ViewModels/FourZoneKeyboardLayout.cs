using System.Collections.Generic;

namespace OmenCore.ViewModels
{
    /// <summary>
    /// One key (or key-shaped cluster, e.g. an arrow key) in the synthetic four-zone keyboard
    /// visual. Static and side-effect free so the geometry can be tested without a window - see
    /// <see cref="FourZoneKeyboardLayout"/>.
    /// </summary>
    public sealed class FourZoneKeyVisual
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public string Label { get; init; } = string.Empty;

        /// <summary>1-4, matching <c>LightingViewModel</c>'s existing Zone1..Zone4 color commands.</summary>
        public int ZoneIndex { get; init; }
    }

    /// <summary>
    /// Generates a standard TKL-style laptop keyboard shape for the four-zone RGB editor - not a
    /// measurement of any specific board (unlike <see cref="KeyboardMapViewModel"/>, which draws
    /// from a real per-key hardware map), just a recognizable keyboard silhouette that makes the
    /// existing zone pickers something you click on a keyboard instead of four labelled rectangles.
    ///
    /// Row layout and zone boundaries were built from the exact key lists already used in
    /// LightingView.xaml's zone-schematic text (e.g. Zone 1 "TAB Q W E R T", Zone 2 "Y U I") rather
    /// than an even proportional split - a few of those hints overlap at their edges (both the
    /// existing Zone 3 and Zone 4 hints mention the backslash/bracket keys), so the boundaries here
    /// make the closest clean, consistent choice rather than reproducing that ambiguity.
    /// </summary>
    public static class FourZoneKeyboardLayout
    {
        private const double UnitPx = 42.0;
        private const double RowHeightPx = 34.0;
        private const double RowGapPx = 4.0;
        private const double KeyInset = 1.5;

        /// <summary>Generated once; the layout is constant, not read from any device.</summary>
        public static IReadOnlyList<FourZoneKeyVisual> Keys { get; } = Build();

        // Main block (15u) + gap (0.3u) + the utility column - 3u wide to fit the arrow cluster
        // row (Left/Down/Right side by side), the widest thing out there; Del and Up sit in the
        // same column but only need 1u each.
        public static double CanvasWidth { get; } = (15.0 + 0.3 + 3.0) * UnitPx;
        public static double CanvasHeight { get; } = 6 * RowHeightPx + 5 * RowGapPx;

        private readonly record struct KeySpec(string Label, double WidthUnits, int Zone);

        private static List<FourZoneKeyVisual> Build()
        {
            var keys = new List<FourZoneKeyVisual>();

            // Row 0: function row. Del sits in the far-right utility slot, matching the existing
            // Zone 4 hint text ("Del Ins").
            AddRow(keys, row: 0, startX: 0, new[]
            {
                new KeySpec("Esc", 1, 1), new KeySpec("F1", 1, 1), new KeySpec("F2", 1, 1), new KeySpec("F3", 1, 1), new KeySpec("F4", 1, 1),
                new KeySpec("F5", 1, 2), new KeySpec("F6", 1, 2), new KeySpec("F7", 1, 2), new KeySpec("F8", 1, 2),
                new KeySpec("F9", 1, 3), new KeySpec("F10", 1, 3), new KeySpec("F11", 1, 3), new KeySpec("F12", 1, 3),
            });
            AddKey(keys, "Del", startX: (0.3 + 15.0) * UnitPx, row: 0, widthUnits: 1.0, zone: 4);

            // Row 1: number row.
            AddRow(keys, row: 1, startX: 0, new[]
            {
                new KeySpec("`", 1, 1), new KeySpec("1", 1, 1), new KeySpec("2", 1, 1), new KeySpec("3", 1, 1), new KeySpec("4", 1, 1), new KeySpec("5", 1, 1),
                new KeySpec("6", 1, 2), new KeySpec("7", 1, 2), new KeySpec("8", 1, 2),
                new KeySpec("9", 1, 3), new KeySpec("0", 1, 3), new KeySpec("-", 1, 3), new KeySpec("=", 1, 3),
                new KeySpec("Backspace", 2, 4),
            });

            // Row 2: QWERTY row.
            AddRow(keys, row: 2, startX: 0, new[]
            {
                new KeySpec("Tab", 1.5, 1), new KeySpec("Q", 1, 1), new KeySpec("W", 1, 1), new KeySpec("E", 1, 1), new KeySpec("R", 1, 1), new KeySpec("T", 1, 1),
                new KeySpec("Y", 1, 2), new KeySpec("U", 1, 2), new KeySpec("I", 1, 2),
                new KeySpec("O", 1, 3), new KeySpec("P", 1, 3), new KeySpec("[", 1, 3), new KeySpec("]", 1, 3),
                new KeySpec("\\", 1.5, 4),
            });

            // Row 3: ASDF row.
            AddRow(keys, row: 3, startX: 0, new[]
            {
                new KeySpec("Caps", 1.75, 1), new KeySpec("A", 1, 1), new KeySpec("S", 1, 1), new KeySpec("D", 1, 1), new KeySpec("F", 1, 1), new KeySpec("G", 1, 1),
                new KeySpec("H", 1, 2), new KeySpec("J", 1, 2), new KeySpec("K", 1, 2),
                new KeySpec("L", 1, 3), new KeySpec(";", 1, 3), new KeySpec("'", 1, 3),
                new KeySpec("Enter", 2.25, 4),
            });

            // Row 4: ZXCV row. Up-arrow sits in the far-right utility slot, above the arrow cluster
            // on the row below.
            AddRow(keys, row: 4, startX: 0, new[]
            {
                new KeySpec("Shift", 2.25, 1), new KeySpec("Z", 1, 1), new KeySpec("X", 1, 1), new KeySpec("C", 1, 1), new KeySpec("V", 1, 1),
                new KeySpec("B", 1, 2), new KeySpec("N", 1, 2), new KeySpec("M", 1, 2),
                new KeySpec(",", 1, 3), new KeySpec(".", 1, 3), new KeySpec("/", 1, 3),
                new KeySpec("Shift", 2.75, 4),
            });
            AddKey(keys, "▲", startX: (0.3 + 15.0) * UnitPx, row: 4, widthUnits: 1.0, zone: 4);

            // Row 5: bottom row + arrow cluster. Space bar physically sits under all three of
            // Zone 1-3 on real hardware; a single key can only carry one zone here, so it's shown
            // as Zone 2 (the middle of the span) rather than picking an edge zone.
            AddRow(keys, row: 5, startX: 0, new[]
            {
                new KeySpec("Ctrl", 1.25, 1), new KeySpec("Win", 1.25, 1), new KeySpec("Alt", 1.25, 1),
                new KeySpec("Space", 6.25, 2),
                new KeySpec("Alt", 1.25, 4), new KeySpec("Fn", 1.25, 4), new KeySpec("Ctrl", 1.25, 4),
            });
            var arrowStartX = (0.3 + 15.0) * UnitPx;
            AddKey(keys, "◄", startX: arrowStartX, row: 5, widthUnits: 1.0, zone: 4);
            AddKey(keys, "▼", startX: arrowStartX + UnitPx, row: 5, widthUnits: 1.0, zone: 4);
            AddKey(keys, "►", startX: arrowStartX + 2 * UnitPx, row: 5, widthUnits: 1.0, zone: 4);

            return keys;
        }

        private static void AddRow(List<FourZoneKeyVisual> keys, int row, double startX, IReadOnlyList<KeySpec> specs)
        {
            var x = startX;
            foreach (var spec in specs)
            {
                AddKey(keys, spec.Label, x, row, spec.WidthUnits, spec.Zone);
                x += spec.WidthUnits * UnitPx;
            }
        }

        private static void AddKey(List<FourZoneKeyVisual> keys, string label, double startX, int row, double widthUnits, int zone)
        {
            keys.Add(new FourZoneKeyVisual
            {
                X = startX + KeyInset,
                Y = row * (RowHeightPx + RowGapPx) + KeyInset,
                Width = widthUnits * UnitPx - 2 * KeyInset,
                Height = RowHeightPx - 2 * KeyInset,
                Label = label,
                ZoneIndex = zone
            });
        }
    }
}
