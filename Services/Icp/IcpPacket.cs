namespace BmsLightBridge.Services.Icp
{
    /// <summary>
    /// One 64-byte USB HID output report sent to the WinWing ICP.
    /// Layout: [0xF0][OpType][SeqNum][PayloadLen][Payload 0..59]
    ///
    /// HidSharp on Windows passes the full buffer directly to WriteFile().
    /// The ICP uses Report ID 0 (no report-ID prefix), so the 64 bytes are
    /// sent as-is — 0xF0 is the WinWing frame marker, not a HID Report ID.
    /// </summary>
    internal class IcpPacket
    {
        public byte   OpType       = 0x00;
        public byte   SequenceNum  = 0x00;
        public byte[] PacketBuffer = Array.Empty<byte>();

        public byte PacketLength => (byte)PacketBuffer.Length;

        public byte[] GetBytes()
        {
            var buf = new byte[64];
            buf[0] = 0xF0;
            buf[1] = OpType;
            buf[2] = SequenceNum;
            buf[3] = PacketLength;
            PacketBuffer.CopyTo(buf, 4);
            return buf;
        }
    }
}
