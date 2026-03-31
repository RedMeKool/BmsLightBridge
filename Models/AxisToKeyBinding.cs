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
        public double  DeadZone       { get; set; } = 0.25;
        public int     RepeatDelayMs  { get; set; } = 400;
        public int     Sensitivity    { get; set; } = 5;  // 1=grof (weinig stappen) tot 10=gevoelig (veel stappen)
        public bool    IsEnabled      { get; set; } = true;
    }
}
