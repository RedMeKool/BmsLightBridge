using HidSharp;

namespace BmsLightBridge.Services.Icp
{
    /// <summary>
    /// Low-level USB HID communication with the WinWing ViperAce ICP.
    /// Opens the device non-exclusively so it can coexist with the WinWing
    /// background service (WWTHID) that keeps the HID handle open.
    ///
    /// All buffers used in the normal send path are pre-allocated at construction
    /// time so the 10 Hz DED render loop runs without any heap allocations.
    /// </summary>
    internal class IcpHidDevice : IDisposable
    {
        private const ushort ICP_VENDOR_ID  = 0x4098;
        private const ushort ICP_PRODUCT_ID = 0xBF06;
        private const int    PAYLOAD_SIZE   = 60;
        private const int    PACKET_SIZE    = 64;

        // Pre-allocated send buffer sized for the largest possible DED frame:
        //   CMD_WRITE: 17 header + DedFont frame (1629 bytes) = 1646 bytes
        //   CMD_REFRESH: 17 header + 1 byte              =   18 bytes
        //   Total: 1664 bytes
        // Using 2048 bytes gives headroom if frame size ever grows.
        private const int SEND_BUFFER_SIZE = 2048;

        private readonly HidStream _stream;
        private byte _seqNum = 1;

        // Pre-allocated buffers — reused every write cycle.
        private readonly byte[] _sendBuffer   = new byte[SEND_BUFFER_SIZE];
        private readonly byte[] _packetBuffer = new byte[PACKET_SIZE];

        public IcpHidDevice()
        {
            var device = DeviceList.Local
                .GetHidDevices(ICP_VENDOR_ID, ICP_PRODUCT_ID)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "WinWing ICP (VID 0x4098 / PID 0xBF06) not found.");

            // Non-exclusive: allow the WinWing background service to keep
            // its own handle open simultaneously.
            var cfg = new OpenConfiguration();
            cfg.SetOption(OpenOption.Exclusive, false);

            _stream = device.Open(cfg);
            _stream.WriteTimeout = 500;

            // Session-start packet: OpType 0x02
            WritePacket(0x02, 0x00, new byte[] { 0 });
        }

        /// <summary>
        /// Serialises all commands into <see cref="_sendBuffer"/> and sends the data
        /// as a sequence of 60-byte HID payload packets — zero heap allocations.
        /// </summary>
        public void WriteDedCommands(IEnumerable<DedCommand> commands)
        {
            // Write all commands sequentially into the pre-allocated send buffer.
            int totalBytes = 0;
            foreach (var cmd in commands)
            {
                cmd.WriteTo(_sendBuffer, totalBytes);
                totalBytes += cmd.SerializedSize;
            }

            // Send in PAYLOAD_SIZE chunks, each wrapped in a 64-byte HID packet.
            for (int start = 0; start < totalBytes; start += PAYLOAD_SIZE)
            {
                int payloadLen = Math.Min(PAYLOAD_SIZE, totalBytes - start);
                WritePacket(0x00, _seqNum, _sendBuffer, start, payloadLen);
                _seqNum = (byte)((_seqNum + 1) & 0xFF);
            }
        }

        /// <summary>Writes a packet using a pre-allocated byte[] as payload source.</summary>
        private void WritePacket(byte opType, byte seqNum, byte[] source, int sourceOffset, int length)
        {
            // Build the 64-byte packet in the pre-allocated buffer.
            Array.Clear(_packetBuffer, 0, PACKET_SIZE);
            _packetBuffer[0] = 0xF0;
            _packetBuffer[1] = opType;
            _packetBuffer[2] = seqNum;
            _packetBuffer[3] = (byte)length;
            Array.Copy(source, sourceOffset, _packetBuffer, 4, length);
            _stream.Write(_packetBuffer);
        }

        /// <summary>Writes a one-off packet from a small byte[] (used only for session-start).</summary>
        private void WritePacket(byte opType, byte seqNum, byte[] payload)
            => WritePacket(opType, seqNum, payload, 0, payload.Length);

        public void Dispose() => _stream.Dispose();
    }
}
