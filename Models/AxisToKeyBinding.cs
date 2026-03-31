namespace BmsLightBridge.Models
{
    public class AxisToKeyBinding
    {
        public Guid    Id             { get; set; } = Guid.NewGuid();
        public string  Label          { get; set; } = "New Binding";
        public string  DeviceInstanceGuid { get; set; } = "";
        public string  DeviceName     { get; set; } = "";
        public JoystickAxis Axis      { get; set; } = JoystickAxis.Z;
        public bool    Invert         { get; set; } = false;
        public int     KeyUp          { get; set; } = 0;
        public string  KeyUpLabel     { get; set; } = "";
        public bool    KeyUpCtrl      { get; set; } = false;
        public bool    KeyUpShift     { get; set; } = false;
        public bool    KeyUpAlt       { get; set; } = false;
        public int     KeyDown        { get; set; } = 0;
        public string  KeyDownLabel   { get; set; } = "";
        public bool    KeyDownCtrl    { get; set; } = false;
        public bool    KeyDownShift   { get; set; } = false;
        public bool    KeyDownAlt     { get; set; } = false;
        public int     RepeatDelayMs  { get; set; } = 400;
        /// <summary>
        /// Number of key presses fired when the axis moves from minimum to maximum (full range).
        /// The step threshold is calculated as 65535 / Steps.
        /// Range 1–50, default 10.
        /// </summary>
        public int     Steps          { get; set; } = 10;
        public bool    IsEnabled      { get; set; } = true;
    }
}
