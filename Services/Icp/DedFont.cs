using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;

namespace BmsLightBridge.Services.Icp
{
    /// <summary>
    /// Renders DED text (5x24 chars) to a 200x65 pixel frame buffer for the ICP LCD.
    /// Uses two embedded BMP fonts (normal + inverted) from DedSharp, compiled in as
    /// EmbeddedResource (Resources/DedFont.bmp and Resources/DedFontInverted.bmp).
    /// </summary>
    internal static class DedFont
    {
        private const int CHAR_W   = 8;
        private const int CHAR_H   = 13;
        private const int DISP_W   = 200;
        private const int DISP_H   = 65;
        private const int DED_ROWS = 5;
        private const int DED_COLS = 24;

        // Character map in the order the glyphs appear left-to-right in the font PNG.
        private static readonly byte[] FontChars = Encoding.ASCII.GetBytes(
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890\x01()<>[]+-\x02/=^|~\x7f.,!?:;&_'\"%#@{} \x7f\x7f\x7f\x7f");

        private const byte GLYPH_FALLBACK = 0x7F;

        private static Dictionary<byte, byte[]>? _normal;
        private static Dictionary<byte, byte[]>? _inverted;

        // Pre-allocated pixel buffer — size is always (DISP_H * DISP_W/8) + 4 header bytes.
        // Reusing it avoids a ~1.6 KB heap allocation on every 10 Hz render cycle.
        // NOT THREAD-SAFE: this buffer is intentionally static and unsynchronised.
        // It is safe only because exactly one IcpService (and therefore one DED timer thread)
        // ever exists at runtime. If a second IcpService is ever instantiated this becomes a
        // data race — move _frameBuffer to an instance field in DedFont at that point.
        private static readonly byte[] _frameBuffer = new byte[4 + DISP_H * (DISP_W / 8)];

        // Lock used only for the one-time lazy font initialisation in EnsureGlyphs().
        private static readonly object _initLock = new();

        // ----------------------------------------------------------------

        public static byte[] Render(string[] dedLines, string[] invertLines)
        {
            EnsureGlyphs();

            // Clear pixel area (bytes 4+); leave the 4-byte header as zeroes.
            Array.Clear(_frameBuffer, 4, _frameBuffer.Length - 4);

            for (int row = 0; row < DED_ROWS; row++)
            {
                string line  = Fit(row < dedLines.Length    ? dedLines[row]    : "");
                string inv   = Fit(row < invertLines.Length ? invertLines[row] : "");
                int pixelY0  = row * CHAR_H;

                for (int col = 0; col < DED_COLS; col++)
                {
                    byte ch    = (byte)(col < line.Length ? line[col] : ' ');
                    bool isInv = col < inv.Length && inv[col] != ' ';
                    var map    = isInv ? _inverted! : _normal!;

                    // Glyph lookup: exact → uppercase fallback → fallback glyph.
                    byte[] glyph = map.TryGetValue(ch, out var g)                              ? g
                                 : map.TryGetValue((byte)char.ToUpper((char)ch), out var g2)   ? g2
                                 : map[GLYPH_FALLBACK];

                    // Each glyph row is one packed byte (8 pixels = 8 bits).
                    // Bit 0 = leftmost pixel, bit 7 = rightmost pixel.
                    int pixelX0  = col * CHAR_W;
                    int byteCol0 = pixelX0 / 8;   // first destination byte for this column
                    int bitShift = pixelX0 % 8;   // how far left to shift within that byte

                    for (int r = 0; r < CHAR_H; r++)
                    {
                        int py = pixelY0 + r;
                        if (py >= DISP_H) break;

                        int destBase = 4 + py * (DISP_W / 8);
                        byte glyphRow = glyph[r];

                        // Because CHAR_W == 8 and columns are byte-aligned (col*8),
                        // the shift is always 0 — direct OR without masking needed.
                        // Written generically anyway for robustness.
                        _frameBuffer[destBase + byteCol0]     |= (byte)(glyphRow << bitShift);
                        if (bitShift > 0 && byteCol0 + 1 < DISP_W / 8)
                            _frameBuffer[destBase + byteCol0 + 1] |= (byte)(glyphRow >> (8 - bitShift));
                    }
                }
            }

            // Return the shared static buffer directly.
            // IMPORTANT: the caller must consume the data before the next Render() call.
            // In practice this is safe because IcpService calls Render() and WriteDedCommands()
            // sequentially on a single timer thread with no intervening yields.
            return _frameBuffer;
        }

        // ----------------------------------------------------------------

        private static void EnsureGlyphs()
        {
            if (_normal != null) return;
            lock (_initLock)
            {
                if (_normal != null) return;
                _normal   = LoadFont(false);
                _inverted = LoadFont(true);
            }
        }

        private static Dictionary<byte, byte[]> LoadFont(bool inverted)
        {
            using var stream = OpenFontStream(inverted);
            using var bmp    = (Bitmap)Image.FromStream(stream);
            var map          = new Dictionary<byte, byte[]>();

            for (int i = 0; i < FontChars.Length; i++)
            {
                byte   key   = FontChars[i];
                byte[] glyph = ExtractGlyph(bmp, i);

                if (!map.ContainsKey(key))
                    map[key] = glyph;
                if (key == 0x02 && !map.ContainsKey((byte)'*'))
                    map[(byte)'*'] = glyph;
                if (key == GLYPH_FALLBACK)
                    map[GLYPH_FALLBACK] = glyph;   // always overwrite with last fallback
            }

            if (!map.ContainsKey(GLYPH_FALLBACK))
                map[GLYPH_FALLBACK] = ExtractGlyph(bmp, FontChars.Length - 1);

            return map;
        }

        /// <summary>
        /// Extracts one glyph as a packed byte array: one byte per row, bit 0 = leftmost pixel.
        /// Uses 13 bytes instead of the 104 bytes a bool[,] would need.
        /// </summary>
        private static byte[] ExtractGlyph(Bitmap bmp, int index)
        {
            var rows = new byte[CHAR_H];
            for (int r = 0; r < CHAR_H; r++)
            {
                byte packed = 0;
                for (int c = 0; c < CHAR_W; c++)
                {
                    if (bmp.GetPixel(index * CHAR_W + c, r).GetBrightness() > 0f)
                        packed |= (byte)(1 << c);   // bit c = pixel at column c
                }
                rows[r] = packed;
            }
            return rows;
        }

        private static Stream OpenFontStream(bool inverted)
        {
            string name   = inverted
                ? "BmsLightBridge.Resources.DedFontInverted.bmp"
                : "BmsLightBridge.Resources.DedFont.bmp";
            var stream    = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null)
                throw new FileNotFoundException(
                    $"Embedded resource '{name}' not found. " +
                    "Add DedFont.bmp + DedFontInverted.bmp as <EmbeddedResource> to the project.");
            return stream;
        }

        private static string Fit(string s) =>
            s.Length >= DED_COLS ? s[..DED_COLS] : s.PadRight(DED_COLS);
    }
}
