using System.Windows.Input;
using BmsLightBridge.Models;
using BmsLightBridge.Services;

namespace BmsLightBridge.ViewModels
{
    public class AxisToKeyBindingViewModel : BaseViewModel
    {
        private readonly AxisToKeyBinding _model;
        private readonly Action           _onChanged;

        public AxisToKeyService?  KeyService   { private get; set; }
        public Action<Action>?    DispatchToUi { private get; set; }

        private IReadOnlyList<JoystickDeviceViewModel> _availableJoysticks = Array.Empty<JoystickDeviceViewModel>();
        public  IReadOnlyList<JoystickDeviceViewModel>  AvailableJoysticks
        {
            get => _availableJoysticks;
            set { SetProperty(ref _availableJoysticks, value); RestoreSelection(); }
        }

        private JoystickDeviceViewModel? _selectedJoystick;
        private bool _isDetectingAxis;
        private bool _isDetectingKeyUp;
        private bool _isDetectingKeyDown;

        public Guid   Id        => _model.Id;
        public string Label     { get => _model.Label;     set { _model.Label     = value; OnPropertyChanged(); _onChanged(); } }
        public bool   IsEnabled { get => _model.IsEnabled; set { _model.IsEnabled = value; OnPropertyChanged(); _onChanged(); } }
        public JoystickAxis SelectedAxis { get => _model.Axis;   set { _model.Axis   = value; OnPropertyChanged(); _onChanged(); } }
        public bool AxisInvert           { get => _model.Invert; set { _model.Invert = value; OnPropertyChanged(); _onChanged(); } }

        public int RepeatDelayMs
        {
            get => _model.RepeatDelayMs;
            set { _model.RepeatDelayMs = Math.Clamp(value, 100, 2000); OnPropertyChanged(); _onChanged(); }
        }

        public int Steps
        {
            get => _model.Steps;
            set { _model.Steps = Math.Clamp(value, 1, 50); OnPropertyChanged(); _onChanged(); }
        }

        public string KeyUpLabel
        {
            get => _model.KeyUpLabel;
            private set { _model.KeyUpLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyUpCombo)); }
        }

        public string KeyUpCombo
        {
            get
            {
                if (_model.KeyUp == 0) return "Not set";
                var parts = new System.Collections.Generic.List<string>();
                if (_model.KeyUpCtrl)  parts.Add("Ctrl");
                if (_model.KeyUpShift) parts.Add("Shift");
                if (_model.KeyUpAlt)   parts.Add("Alt");
                if (!string.IsNullOrEmpty(_model.KeyUpLabel)) parts.Add(_model.KeyUpLabel);
                return string.Join("+", parts);
            }
        }
        public string ManualKeyUpVk
        {
            get => _model.KeyUp == 0 ? "" : _model.KeyUp.ToString();
            set
            {
                if (int.TryParse(value?.Trim(), out int vk) && vk > 0 && vk < 256)
                { _model.KeyUp = vk; KeyUpLabel = BuildKeyLabel(vk); }
                else if (string.IsNullOrWhiteSpace(value))
                { _model.KeyUp = 0; KeyUpLabel = ""; }
                OnPropertyChanged(); _onChanged();
            }
        }

        public string KeyDownLabel
        {
            get => _model.KeyDownLabel;
            private set { _model.KeyDownLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyDownCombo)); }
        }

        public string KeyDownCombo
        {
            get
            {
                if (_model.KeyDown == 0) return "Not set";
                var parts = new System.Collections.Generic.List<string>();
                if (_model.KeyDownCtrl)  parts.Add("Ctrl");
                if (_model.KeyDownShift) parts.Add("Shift");
                if (_model.KeyDownAlt)   parts.Add("Alt");
                if (!string.IsNullOrEmpty(_model.KeyDownLabel)) parts.Add(_model.KeyDownLabel);
                return string.Join("+", parts);
            }
        }

        public bool KeyUpCtrl  { get => _model.KeyUpCtrl;  set { _model.KeyUpCtrl  = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyUpCombo));   _onChanged(); } }
        public bool KeyUpShift { get => _model.KeyUpShift; set { _model.KeyUpShift = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyUpCombo));   _onChanged(); } }
        public bool KeyUpAlt   { get => _model.KeyUpAlt;   set { _model.KeyUpAlt   = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyUpCombo));   _onChanged(); } }
        public bool KeyDownCtrl  { get => _model.KeyDownCtrl;  set { _model.KeyDownCtrl  = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyDownCombo)); _onChanged(); } }
        public bool KeyDownShift { get => _model.KeyDownShift; set { _model.KeyDownShift = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyDownCombo)); _onChanged(); } }
        public bool KeyDownAlt   { get => _model.KeyDownAlt;   set { _model.KeyDownAlt   = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeyDownCombo)); _onChanged(); } }

        public string ManualKeyDownVk
        {
            get => _model.KeyDown == 0 ? "" : _model.KeyDown.ToString();
            set
            {
                if (int.TryParse(value?.Trim(), out int vk) && vk > 0 && vk < 256)
                { _model.KeyDown = vk; KeyDownLabel = BuildKeyLabel(vk); }
                else if (string.IsNullOrWhiteSpace(value))
                { _model.KeyDown = 0; KeyDownLabel = ""; }
                OnPropertyChanged(); _onChanged();
            }
        }

        public JoystickDeviceViewModel? SelectedJoystick
        {
            get => _selectedJoystick;
            set
            {
                SetProperty(ref _selectedJoystick, value);
                _model.DeviceInstanceGuid = value?.InstanceGuid ?? _model.DeviceInstanceGuid;
                _model.DeviceName         = value?.Name         ?? _model.DeviceName;
                _onChanged();
            }
        }

        public bool IsDetectingAxis    { get => _isDetectingAxis;    private set { SetProperty(ref _isDetectingAxis,    value); CommandManager.InvalidateRequerySuggested(); } }
        public bool IsDetectingKeyUp   { get => _isDetectingKeyUp;   private set { SetProperty(ref _isDetectingKeyUp,   value); CommandManager.InvalidateRequerySuggested(); } }
        public bool IsDetectingKeyDown { get => _isDetectingKeyDown; private set { SetProperty(ref _isDetectingKeyDown, value); CommandManager.InvalidateRequerySuggested(); } }
        public bool IsDetecting        => IsDetectingAxis || IsDetectingKeyUp || IsDetectingKeyDown;

        public RelayCommand DetectAxisCommand    { get; }
        public RelayCommand DetectKeyUpCommand   { get; }
        public RelayCommand DetectKeyDownCommand { get; }

        public AxisToKeyBindingViewModel(AxisToKeyBinding model, Action onChanged)
        {
            _model     = model;
            _onChanged = onChanged;
            DetectAxisCommand    = new RelayCommand(_ => StartDetectAxis(),    _ => !IsDetecting && _selectedJoystick != null);
            DetectKeyUpCommand   = new RelayCommand(_ => StartDetectKey(true), _ => !IsDetecting);
            DetectKeyDownCommand = new RelayCommand(_ => StartDetectKey(false),_ => !IsDetecting);
        }

        public AxisToKeyBinding Model => _model;

        private void StartDetectAxis()
        {
            if (KeyService == null || _selectedJoystick == null || IsDetecting) return;
            IsDetectingAxis = true;
            KeyService.DetectAxis(_selectedJoystick.InstanceGuid, 8000,
                axis => DispatchToUi?.Invoke(() => { SelectedAxis = axis; IsDetectingAxis = false; }),
                ()   => DispatchToUi?.Invoke(() =>   IsDetectingAxis = false));
        }

        private void StartDetectKey(bool isUp)
        {
            if (IsDetecting) return;
            if (isUp) IsDetectingKeyUp   = true;
            else      IsDetectingKeyDown = true;
            OnPropertyChanged(nameof(IsDetecting));
            System.Threading.Tasks.Task.Run(() => ListenForKey(isUp));
        }

        private void ListenForKey(bool isUp)
        {
            var deadline  = DateTime.UtcNow.AddSeconds(8);
            var modifierVks = new HashSet<int> { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };
            System.Threading.Thread.Sleep(200);

            while (DateTime.UtcNow < deadline)
            {
                bool ctrl  = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
                bool alt   = (GetAsyncKeyState(0x12) & 0x8000) != 0;

                for (int vk = 1; vk < 255; vk++)
                {
                    if (modifierVks.Contains(vk)) continue;
                    if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                    {
                        string label = BuildKeyLabel(vk);
                        int    capturedVk    = vk;
                        bool   capturedCtrl  = ctrl;
                        bool   capturedShift = shift;
                        bool   capturedAlt   = alt;

                        DispatchToUi?.Invoke(() =>
                        {
                            if (isUp)
                            {
                                _model.KeyUp      = capturedVk;   KeyUpLabel      = label;
                                _model.KeyUpCtrl  = capturedCtrl;
                                _model.KeyUpShift = capturedShift;
                                _model.KeyUpAlt   = capturedAlt;
                                IsDetectingKeyUp  = false;
                                OnPropertyChanged(nameof(ManualKeyUpVk));
                                OnPropertyChanged(nameof(KeyUpCtrl));
                                OnPropertyChanged(nameof(KeyUpShift));
                                OnPropertyChanged(nameof(KeyUpAlt));
                                OnPropertyChanged(nameof(KeyUpCombo));
                            }
                            else
                            {
                                _model.KeyDown      = capturedVk;   KeyDownLabel      = label;
                                _model.KeyDownCtrl  = capturedCtrl;
                                _model.KeyDownShift = capturedShift;
                                _model.KeyDownAlt   = capturedAlt;
                                IsDetectingKeyDown  = false;
                                OnPropertyChanged(nameof(ManualKeyDownVk));
                                OnPropertyChanged(nameof(KeyDownCtrl));
                                OnPropertyChanged(nameof(KeyDownShift));
                                OnPropertyChanged(nameof(KeyDownAlt));
                                OnPropertyChanged(nameof(KeyDownCombo));
                            }
                            OnPropertyChanged(nameof(IsDetecting));
                            _onChanged();
                        });
                        return;
                    }
                }
                System.Threading.Thread.Sleep(16);
            }
            DispatchToUi?.Invoke(() =>
            {
                IsDetectingKeyUp = false; IsDetectingKeyDown = false;
                OnPropertyChanged(nameof(IsDetecting));
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static string BuildKeyLabel(int vk) => vk switch
        {
            0x08 => "Backspace", 0x09 => "Tab",    0x0D => "Enter",
            0x1B => "Escape",    0x20 => "Space",
            0x25 => "Left",      0x26 => "Up",      0x27 => "Right", 0x28 => "Down",
            0x2E => "Delete",    0xBF => "Slash",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            _                   => $"VK {vk}"
        };

        public void RestoreSelection()
        {
            if (!string.IsNullOrEmpty(_model.DeviceInstanceGuid))
            {
                var match = _availableJoysticks.FirstOrDefault(j => j.InstanceGuid == _model.DeviceInstanceGuid);
                if (match != null && _selectedJoystick?.InstanceGuid != match.InstanceGuid)
                {
                    _selectedJoystick = match;
                    OnPropertyChanged(nameof(SelectedJoystick));
                }
            }
        }
    }
}
