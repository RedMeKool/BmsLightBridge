namespace BmsLightBridge.Services.Icp
{
    /// <summary>
    /// A logical DED command serialised into the HID send buffer by IcpHidDevice.
    ///
    /// Command layout (little-endian):
    ///   Bytes  0- 3 : ProductId  (0x0000BF06)
    ///   Bytes  4- 7 : CommandType
    ///   Bytes  8-11 : Timestamp
    ///   Byte  12    : 0x00 (padding)
    ///   Bytes 13-16 : DataBuffer length
    ///   Bytes 17+   : DataBuffer
    /// </summary>
    internal sealed class DedCommand
    {
        public const uint CMD_WRITE_DISPLAY_MEM = 0x0102;
        public const uint CMD_REFRESH_DISPLAY   = 0x0103;

        /// <summary>Pre-allocated payload for CMD_REFRESH_DISPLAY — avoids a new byte[] on every 10 Hz frame.</summary>
        public static readonly byte[] RefreshPayload = { 0 };

        public uint   ProductId   { get; init; } = 0x0000BF06;
        public uint   CommandType { get; init; } = CMD_REFRESH_DISPLAY;
        public uint   TimeStamp   { get; init; }
        public byte[] DataBuffer  { get; init; } = Array.Empty<byte>();

        /// <summary>Total serialised size of this command in bytes.</summary>
        public int SerializedSize => 17 + DataBuffer.Length;

        /// <summary>
        /// Writes the serialised command into <paramref name="dest"/> at <paramref name="offset"/>
        /// without allocating a new byte array.
        /// </summary>
        public void WriteTo(byte[] dest, int offset)
        {
            BitConverter.TryWriteBytes(dest.AsSpan(offset,      4), ProductId);
            BitConverter.TryWriteBytes(dest.AsSpan(offset +  4, 4), CommandType);
            BitConverter.TryWriteBytes(dest.AsSpan(offset +  8, 4), TimeStamp);
            dest[offset + 12] = 0x00;
            BitConverter.TryWriteBytes(dest.AsSpan(offset + 13, 4), (uint)DataBuffer.Length);
            DataBuffer.CopyTo(dest, offset + 17);
        }

    }
}
