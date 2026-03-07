using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using BmsLightBridge.Models;
using BmsLightBridge.Services;

namespace BmsLightBridge.ViewModels
{
    /// <summary>A category group with collapsible state.</summary>
    public class CategoryGroup : BaseViewModel
    {
        public string Name { get; }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public ObservableCollection<SignalViewModel> Signals { get; } = new();

        public CategoryGroup(string name) => Name = name;
    }

    /// <summary>UI representation of a BMS signal with its associated mapping.</summary>
    public class SignalViewModel : BaseViewModel
    {
        private bool _isOn;
        private bool _isMapped;
        private bool _isSelected;

        public CockpitLight  Light   { get; }
        public SignalMapping? Mapping { get; set; }

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (SetProperty(ref _isOn, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsMapped
        {
            get => _isMapped;
            set
            {
                if (SetProperty(ref _isMapped, value))
                    OnPropertyChanged(nameof(MappingText));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string StatusText  => IsOn ? "ON" : "OFF";
        public string MappingText => Mapping == null
            ? "Not mapped"
            : Mapping.TargetDevice == DeviceType.Arduino
                ? $"Arduino {Mapping.ArduinoComPort} Pin {Mapping.ArduinoPin}"
                : $"WinWing {Mapping.WinWingDeviceName} LED {Mapping.WinWingLightIndex}";

        public SignalViewModel(CockpitLight light) => Light = light;
    }

    /// <summary>ViewModel for one available joystick device (shown in the axis picker combo-box).</summary>
    public class JoystickDeviceViewModel
    {
        public string InstanceGuid { get; init; } = "";
        public string Name         { get; init; } = "";
        public override string ToString() => Name;
    }

    /// <summary>ViewModel for a WinWing brightness channel slider.</summary>
    public class BrightnessChannelViewModel : BaseViewModel
    {
        private readonly WinWingBrightnessChannel _model;
        private readonly Action _onChanged;
        private readonly string _label;

        // ── Binding mode ──────────────────────────────────────────────────
        // Three exclusive modes: Manual (slider), Axis, Buttons.
        public enum BindingMode { Manual, Axis, Buttons }

        private BindingMode _mode = BindingMode.Manual;

        // ── Axis binding UI state ─────────────────────────────────────────
        private JoystickDeviceViewModel? _selectedJoystick;
        private string                   _axisJoystickGuid = "";   // persisted separately — model may be null
        private Models.JoystickAxis      _selectedAxis = Models.JoystickAxis.Z;
        private bool                     _axisInvert;
        private int                      _liveAxisValue;
        private bool                     _isDetectingAxis;

        // ── Button binding UI state ───────────────────────────────────────
        private JoystickDeviceViewModel? _selectedButtonJoystick;
        private string                   _buttonJoystickGuid = "";  // persisted separately
        private int                      _buttonUp      = 0;
        private int                      _buttonDown    = 1;

        public BrightnessChannelViewModel(WinWingBrightnessChannel model, string label, Action onChanged)
        {
            _model     = model;
            _label     = label;
            _onChanged = onChanged;

            DetectButtonUpCommand   = new RelayCommand(_ => StartDetect(DetectTarget.Up),
                                                       _ => !IsDetecting && _selectedButtonJoystick != null);
            DetectButtonDownCommand = new RelayCommand(_ => StartDetect(DetectTarget.Down),
                                                       _ => !IsDetecting && _selectedButtonJoystick != null);
            DetectAxisCommand       = new RelayCommand(_ => StartDetectAxis(),
                                                       _ => !_isDetectingAxis && _selectedJoystick != null);

            // Load all persisted binding fields into ViewModel state regardless of which is active.
            // This ensures switching back to a mode always shows the last saved values.
            if (model.AxisBinding != null)
            {
                _selectedAxis      = model.AxisBinding.Axis;
                _axisInvert        = model.AxisBinding.Invert;
                _axisJoystickGuid  = model.AxisBinding.DeviceInstanceGuid;
            }
            if (model.ButtonBinding != null)
            {
                _buttonUp           = model.ButtonBinding.ButtonUp;
                _buttonDown         = model.ButtonBinding.ButtonDown;
                _buttonJoystickGuid = model.ButtonBinding.DeviceInstanceGuid;
            }

            // Determine active mode — button takes precedence if both somehow exist.
            // Wipe the non-active one from the model so config stays clean.
            if (model.ButtonBinding != null)
            {
                _mode             = BindingMode.Buttons;
                model.AxisBinding = null;
            }
            else if (model.AxisBinding != null)
            {
                _mode               = BindingMode.Axis;
                model.ButtonBinding = null;
            }
        }

        public string Label      => _label;
        public int    LightIndex => _model.LightIndex;

        // ── Mode properties ───────────────────────────────────────────────

        public bool ModeIsManual  { get => _mode == BindingMode.Manual;  set { if (value) SetMode(BindingMode.Manual);  } }
        public bool ModeIsAxis    { get => _mode == BindingMode.Axis;    set { if (value) SetMode(BindingMode.Axis);    } }
        public bool ModeIsButtons { get => _mode == BindingMode.Buttons; set { if (value) SetMode(BindingMode.Buttons); } }

        private void SetMode(BindingMode mode)
        {
            // Cancel any in-progress detection when leaving a mode
            if (mode != BindingMode.Axis && _isDetectingAxis)
            {
                _isDetectingAxis = false;
                OnPropertyChanged(nameof(IsDetectingAxis));
            }
            if (mode != BindingMode.Buttons && _detecting != DetectTarget.None)
            {
                _detecting = DetectTarget.None;
                OnPropertyChanged(nameof(IsDetecting));
                OnPropertyChanged(nameof(IsDetectingUp));
                OnPropertyChanged(nameof(IsDetectingDown));
            }

            _mode = mode;
            OnPropertyChanged(nameof(ModeIsManual));
            OnPropertyChanged(nameof(ModeIsAxis));
            OnPropertyChanged(nameof(ModeIsButtons));
            OnPropertyChanged(nameof(SliderIsReadOnly));
            OnPropertyChanged(nameof(AxisPanelVisible));
            OnPropertyChanged(nameof(ButtonPanelVisible));

            // Wipe the non-active binding from the model so the saved config stays clean,
            // but keep the ViewModel fields intact so switching back restores the last values.
            SaveBinding();
            _onChanged();
        }

        // Keep AxisBindingEnabled as a compat shim used by UpdateBindings filtering
        public bool AxisBindingEnabled => _mode == BindingMode.Axis;
        public bool SliderIsReadOnly   => _mode != BindingMode.Manual;
        public bool AxisPanelVisible   => _mode == BindingMode.Axis;
        public bool ButtonPanelVisible => _mode == BindingMode.Buttons;

        // ── Brightness value ──────────────────────────────────────────────

        public int FixedBrightness
        {
            get => _model.FixedBrightness;
            set
            {
                if (_mode != BindingMode.Manual) return;
                _model.FixedBrightness = Math.Clamp(value, 0, 255);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayBrightness));
                _onChanged();
            }
        }

        // ── Axis binding properties ───────────────────────────────────────

        public JoystickDeviceViewModel? SelectedJoystick
        {
            get => _selectedJoystick;
            set
            {
                SetProperty(ref _selectedJoystick, value);
                _axisJoystickGuid = value?.InstanceGuid ?? _axisJoystickGuid;
                if (_mode == BindingMode.Axis) { SaveBinding(); _onChanged(); }
            }
        }

        public Models.JoystickAxis SelectedAxis
        {
            get => _selectedAxis;
            set { SetProperty(ref _selectedAxis, value); if (_mode == BindingMode.Axis) { SaveBinding(); _onChanged(); } }
        }

        public bool AxisInvert
        {
            get => _axisInvert;
            set { SetProperty(ref _axisInvert, value); if (_mode == BindingMode.Axis) { SaveBinding(); _onChanged(); } }
        }

        /// <summary>Live brightness value driven by the axis poll thread (0–255).</summary>
        public int LiveAxisValue
        {
            get => _liveAxisValue;
            set
            {
                if (SetProperty(ref _liveAxisValue, value))
                {
                    _model.FixedBrightness = value;
                    // DisplayBrightness mirrors FixedBrightness in all modes
                    OnPropertyChanged(nameof(FixedBrightness));
                    OnPropertyChanged(nameof(DisplayBrightness));
                }
            }
        }

        /// <summary>
        /// Read-only brightness value shown by the universal live slider.
        /// In Manual mode this equals FixedBrightness (editable via the slider itself).
        /// In Axis/Buttons mode it reflects the last value pushed by the service.
        /// </summary>
        public int DisplayBrightness => _model.FixedBrightness;

        // ── Button binding properties ─────────────────────────────────────

        public bool IsDetectingAxis
        {
            get => _isDetectingAxis;
            private set
            {
                SetProperty(ref _isDetectingAxis, value);
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        public RelayCommand DetectAxisCommand { get; }

        private void StartDetectAxis()
        {
            if (AxisBindingService == null || _selectedJoystick == null) return;
            if (_isDetectingAxis) return;

            IsDetectingAxis = true;

            AxisBindingService.DetectAxis(
                _selectedJoystick.InstanceGuid,
                timeoutMs: 8000,
                onDetected: axis =>
                {
                    DispatchToUi?.Invoke(() =>
                    {
                        SelectedAxis    = axis;
                        IsDetectingAxis = false;
                    });
                },
                onTimeout: () =>
                {
                    DispatchToUi?.Invoke(() => IsDetectingAxis = false);
                });
        }

        public JoystickDeviceViewModel? SelectedButtonJoystick
        {
            get => _selectedButtonJoystick;
            set
            {
                SetProperty(ref _selectedButtonJoystick, value);
                _buttonJoystickGuid = value?.InstanceGuid ?? _buttonJoystickGuid;
                if (_mode == BindingMode.Buttons) { SaveBinding(); _onChanged(); }
            }
        }

        public int ButtonUp
        {
            get => _buttonUp;
            set { SetProperty(ref _buttonUp, Math.Max(0, value)); if (_mode == BindingMode.Buttons) { SaveBinding(); _onChanged(); } }
        }

        public int ButtonDown
        {
            get => _buttonDown;
            set { SetProperty(ref _buttonDown, Math.Max(0, value)); if (_mode == BindingMode.Buttons) { SaveBinding(); _onChanged(); } }
        }

        // ── Button detection ──────────────────────────────────────────────

        public enum DetectTarget { None, Up, Down }

        private DetectTarget _detecting = DetectTarget.None;
        public DetectTarget Detecting
        {
            get => _detecting;
            private set
            {
                SetProperty(ref _detecting, value);
                OnPropertyChanged(nameof(IsDetectingUp));
                OnPropertyChanged(nameof(IsDetectingDown));
                OnPropertyChanged(nameof(IsDetecting));
            }
        }

        public bool IsDetecting    => _detecting != DetectTarget.None;
        public bool IsDetectingUp  => _detecting == DetectTarget.Up;
        public bool IsDetectingDown=> _detecting == DetectTarget.Down;

        // Injected by MainViewModel after construction so the ViewModel can call back into the service.
        public Services.AxisBindingService? AxisBindingService { private get; set; }

        // Injected dispatcher action so UI updates happen on the correct thread.
        public Action<Action>? DispatchToUi { private get; set; }

        public RelayCommand DetectButtonUpCommand   { get; }
        public RelayCommand DetectButtonDownCommand { get; }

        private void StartDetect(DetectTarget target)
        {
            if (AxisBindingService == null || _selectedButtonJoystick == null) return;
            if (IsDetecting) return;  // already listening

            Detecting = target;

            AxisBindingService.DetectButton(
                _selectedButtonJoystick.InstanceGuid,
                timeoutMs: 8000,
                onDetected: idx =>
                {
                    DispatchToUi?.Invoke(() =>
                    {
                        if (target == DetectTarget.Up)
                            ButtonUp = idx;
                        else
                            ButtonDown = idx;
                        Detecting = DetectTarget.None;
                    });
                },
                onTimeout: () =>
                {
                    DispatchToUi?.Invoke(() => Detecting = DetectTarget.None);
                });
        }

        public WinWingBrightnessChannel Model => _model;

        /// <summary>
        /// Resets this channel to Manual mode at 100% brightness, wiping all axis/button bindings
        /// from both the ViewModel fields and the model. Called by the "Reset to default" button.
        /// </summary>
        public void ResetToManual()
        {
            // Cancel any in-progress detection
            _isDetectingAxis = false;
            _detecting       = DetectTarget.None;

            // Wipe ViewModel fields for both binding types
            _selectedJoystick       = null;
            _axisJoystickGuid       = "";
            _selectedAxis           = Models.JoystickAxis.Z;
            _axisInvert             = false;
            _selectedButtonJoystick = null;
            _buttonJoystickGuid     = "";
            _buttonUp               = 0;
            _buttonDown             = 1;

            // Switch to Manual and set brightness to 255
            _mode                  = BindingMode.Manual;
            _model.FixedBrightness = 255;
            _model.AxisBinding     = null;
            _model.ButtonBinding   = null;

            // Notify all affected properties
            OnPropertyChanged(nameof(ModeIsManual));
            OnPropertyChanged(nameof(ModeIsAxis));
            OnPropertyChanged(nameof(ModeIsButtons));
            OnPropertyChanged(nameof(SliderIsReadOnly));
            OnPropertyChanged(nameof(AxisPanelVisible));
            OnPropertyChanged(nameof(ButtonPanelVisible));
            OnPropertyChanged(nameof(FixedBrightness));
            OnPropertyChanged(nameof(DisplayBrightness));
            OnPropertyChanged(nameof(SelectedJoystick));
            OnPropertyChanged(nameof(SelectedAxis));
            OnPropertyChanged(nameof(AxisInvert));
            OnPropertyChanged(nameof(SelectedButtonJoystick));
            OnPropertyChanged(nameof(ButtonUp));
            OnPropertyChanged(nameof(ButtonDown));
            OnPropertyChanged(nameof(IsDetectingAxis));
            OnPropertyChanged(nameof(IsDetecting));
            OnPropertyChanged(nameof(IsDetectingUp));
            OnPropertyChanged(nameof(IsDetectingDown));
        }

        /// <summary>Persists only the active binding mode to the model. The inactive mode is always cleared.</summary>
        public void SaveBinding()
        {
            switch (_mode)
            {
                case BindingMode.Axis:
                    _model.ButtonBinding = null;
                    // Use _selectedJoystick if connected, otherwise fall back to stored GUID
                    string axisGuid = _selectedJoystick?.InstanceGuid ?? _axisJoystickGuid;
                    _model.AxisBinding = string.IsNullOrEmpty(axisGuid) ? null : new Models.AxisBinding
                    {
                        DeviceInstanceGuid = axisGuid,
                        DeviceName         = _selectedJoystick?.Name ?? axisGuid,
                        Axis               = _selectedAxis,
                        Invert             = _axisInvert
                    };
                    break;

                case BindingMode.Buttons:
                    _model.AxisBinding = null;
                    string btnGuid = _selectedButtonJoystick?.InstanceGuid ?? _buttonJoystickGuid;
                    _model.ButtonBinding = string.IsNullOrEmpty(btnGuid) ? null : new Models.ButtonBinding
                    {
                        DeviceInstanceGuid = btnGuid,
                        DeviceName         = _selectedButtonJoystick?.Name ?? btnGuid,
                        ButtonUp           = _buttonUp,
                        ButtonDown         = _buttonDown
                    };
                    break;

                default: // Manual
                    _model.AxisBinding   = null;
                    _model.ButtonBinding = null;
                    break;
            }
        }

        /// <summary>
        /// Called by MainViewModel when the joystick list is refreshed.
        /// Restores previously selected joystick(s) from the new list.
        /// </summary>
        public void SyncJoystickSelection(IEnumerable<JoystickDeviceViewModel> available)
        {
            var list = available.ToList();

            // Always restore both joystick selections from stored GUIDs,
            // regardless of which mode is currently active.
            if (!string.IsNullOrEmpty(_axisJoystickGuid))
            {
                _selectedJoystick = list.FirstOrDefault(j => j.InstanceGuid == _axisJoystickGuid);
                OnPropertyChanged(nameof(SelectedJoystick));
            }
            if (!string.IsNullOrEmpty(_buttonJoystickGuid))
            {
                _selectedButtonJoystick = list.FirstOrDefault(j => j.InstanceGuid == _buttonJoystickGuid);
                OnPropertyChanged(nameof(SelectedButtonJoystick));
            }
        }

        public static readonly IReadOnlyList<Models.JoystickAxis> AllAxes =
            Enum.GetValues<Models.JoystickAxis>().ToList();
    }

    /// <summary>Main ViewModel — manages application state, configuration, and sync lifecycle.</summary>
    public class MainViewModel : BaseViewModel, IDisposable
    {
        // ── Services ──────────────────────────────────────────────────────
        private readonly SyncService _syncService;

        // ── Configuration ─────────────────────────────────────────────────
        private AppConfiguration _config;

        // ── Bindable properties ───────────────────────────────────────────
        private bool   _isBmsConnected;
        private bool   _isSyncing;
        private string _statusMessage = "Ready. Start BMS and press 'Start Sync'.";
        private string _bmsStatusText = "Not connected";
        private SignalViewModel? _selectedSignal;
        private string _searchText       = "";
        private string _selectedCategory = "All";

        public bool IsBmsConnected
        {
            get => _isBmsConnected;
            set
            {
                SetProperty(ref _isBmsConnected, value);
                BmsStatusText = value ? "Connected to BMS" : "BMS not detected";
            }
        }

        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                SetProperty(ref _isSyncing, value);
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                // Force WPF to re-evaluate CanExecute on Start/Stop buttons.
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    new System.Action(System.Windows.Input.CommandManager.InvalidateRequerySuggested));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string BmsStatusText
        {
            get => _bmsStatusText;
            set => SetProperty(ref _bmsStatusText, value);
        }

        public SignalViewModel? SelectedSignal
        {
            get => _selectedSignal;
            set
            {
                if (_isTestMode && _selectedSignal != null)
                    CancelActiveTest();

                if (_selectedSignal != null)
                    _selectedSignal.IsSelected = false;

                SetProperty(ref _selectedSignal, value);

                if (_selectedSignal != null)
                    _selectedSignal.IsSelected = true;

                OnPropertyChanged(nameof(HasSelectedSignal));
                OnPropertyChanged(nameof(TestButtonIsActive));
                OnPropertyChanged(nameof(CurrentMappingText));

                if (value?.Mapping != null)
                    LoadMappingToEditor(value.Mapping);
            }
        }

        public bool HasSelectedSignal => SelectedSignal != null;

        public string CurrentMappingText
        {
            get
            {
                var m = SelectedSignal?.Mapping;
                if (m == null) return "Not mapped";
                return m.TargetDevice == DeviceType.Arduino
                    ? $"Arduino {m.ArduinoComPort}  Pin {m.ArduinoPin}"
                    : $"WinWing {m.WinWingDeviceName}  LED {m.WinWingLightIndex}";
            }
        }

        public bool CanStart => !IsSyncing;
        public bool CanStop  => IsSyncing;

        // ── ICP Display properties ─────────────────────────────────────────
        private bool _icpDedEnabled;

        /// <summary>Whether DED LCD sync is enabled for the WinWing ICP.</summary>
        public bool IcpDedEnabled
        {
            get => _icpDedEnabled;
            set
            {
                SetProperty(ref _icpDedEnabled, value);
                _config.IcpDisplay.IcpDedEnabled = value;
                SaveConfig();

                // Apply live if sync is already running
                if (IsSyncing)
                    _syncService.ApplyIcpConfig(value);

                StatusMessage = value
                    ? "ICP DED LCD synchronisation enabled."
                    : "ICP DED LCD synchronisation disabled.";
            }
        }

        /// <summary>True when the ICP HID device is physically connected.</summary>
        public bool IcpIsConnected => _syncService.IcpOutput.IsConnected;

        private bool _showOnlyMapped;
        public bool ShowOnlyMapped
        {
            get => _showOnlyMapped;
            set
            {
                SetProperty(ref _showOnlyMapped, value);
                _config.ShowOnlyMapped = value;
                SaveConfig();
                FilterSignals();
            }
        }

        private bool _autoSync;
        public bool AutoSync
        {
            get => _autoSync;
            set
            {
                SetProperty(ref _autoSync, value);
                _config.AutoSync = value;
                SaveConfig();
            }
        }

        // ── Helios launch ─────────────────────────────────────────────────

        public bool HeliosLaunchEnabled
        {
            get => _config.HeliosLaunch.Enabled;
            set { _config.HeliosLaunch.Enabled = value; OnPropertyChanged(); SaveConfig(); }
        }

        public bool HeliosShutdownEnabled
        {
            get => _config.HeliosLaunch.AutoShutdown;
            set { _config.HeliosLaunch.AutoShutdown = value; OnPropertyChanged(); SaveConfig(); }
        }

        public string HeliosControlCenterPath
        {
            get => _config.HeliosLaunch.ControlCenterPath;
            set { _config.HeliosLaunch.ControlCenterPath = value; OnPropertyChanged(); SaveConfig(); }
        }

        public string HeliosProfilePath
        {
            get => _config.HeliosLaunch.ProfilePath;
            set { _config.HeliosLaunch.ProfilePath = value; OnPropertyChanged(); SaveConfig(); }
        }

        public RelayCommand BrowseHeliosExeCommand  { get; }
        public RelayCommand BrowseHeliosProfileCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); FilterSignals(); }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { SetProperty(ref _selectedCategory, value); FilterSignals(); }
        }

        // ── Collections ───────────────────────────────────────────────────
        public ObservableCollection<SignalViewModel>        Signals                  { get; } = new();
        public ObservableCollection<CategoryGroup>          CategoryGroups           { get; } = new();
        private List<SignalViewModel>                       _allSignals              = new();

        public ObservableCollection<string>                 Categories               { get; } = new();
        public ObservableCollection<string>                 AvailableComPorts        { get; } = new();
        public ObservableCollection<Models.WinWingDevice>   AvailableWinWingDevices  { get; } = new();
        public bool HasWinWingDevices => AvailableWinWingDevices.Count > 0;

        public ObservableCollection<BrightnessChannelViewModel> BrightnessChannels         { get; } = new();
        public ObservableCollection<BrightnessChannelViewModel> SelectedBrightnessChannels { get; } = new();
        public ObservableCollection<JoystickDeviceViewModel>    AvailableJoysticks         { get; } = new();

        private Models.WinWingDevice? _selectedBrightnessDevice;
        public Models.WinWingDevice? SelectedBrightnessDevice
        {
            get => _selectedBrightnessDevice;
            set
            {
                _selectedBrightnessDevice = value;
                OnPropertyChanged();
                UpdateSelectedBrightnessChannels();
            }
        }

        private void UpdateSelectedBrightnessChannels()
        {
            SelectedBrightnessChannels.Clear();
            if (_selectedBrightnessDevice == null) return;

            var pids = WinWingLightEntry.GetGroupPids((ushort)_selectedBrightnessDevice.ProductId)
                                        .Select(p => (int)p)
                                        .ToHashSet();

            foreach (var ch in BrightnessChannels.Where(c => pids.Contains(c.Model.ProductId)))
                SelectedBrightnessChannels.Add(ch);

            OnPropertyChanged(nameof(BrightnessSummaryText));
        }

        // ── Mapping editor properties ─────────────────────────────────────
        private DeviceType             _editorDeviceType  = DeviceType.Arduino;
        private string                 _editorComPort     = "";
        private int                    _editorPin         = 13;
        private Models.WinWingDevice?  _editorWinWingDevice;
        private int                    _editorLightIndex  = 0;

        // Board-level settings (per COM port, stored in ArduinoDevices)
        private int  _boardBaudRate     = 115200;
        private int  _boardResetDelayMs = 2000;
        private bool _boardDtrEnable    = true;

        public DeviceType EditorDeviceType
        {
            get => _editorDeviceType;
            set
            {
                SetProperty(ref _editorDeviceType, value);
                OnPropertyChanged(nameof(IsArduinoSelected));
                OnPropertyChanged(nameof(IsWinWingSelected));
            }
        }

        public bool IsArduinoSelected
        {
            get => EditorDeviceType == DeviceType.Arduino;
            set { if (value) EditorDeviceType = DeviceType.Arduino; }
        }

        public bool IsWinWingSelected
        {
            get => EditorDeviceType == DeviceType.WinWing;
            set { if (value) EditorDeviceType = DeviceType.WinWing; }
        }

        public string EditorComPort
        {
            get => _editorComPort;
            set { SetProperty(ref _editorComPort, value); LoadBoardSettings(value); }
        }

        public int BoardBaudRate
        {
            get => _boardBaudRate;
            set { SetProperty(ref _boardBaudRate, value); SaveBoardSettings(); }
        }

        public int BoardResetDelayMs
        {
            get => _boardResetDelayMs;
            set { SetProperty(ref _boardResetDelayMs, value); SaveBoardSettings(); }
        }

        public bool BoardDtrEnable
        {
            get => _boardDtrEnable;
            set { SetProperty(ref _boardDtrEnable, value); SaveBoardSettings(); }
        }

        private void LoadBoardSettings(string comPort)
        {
            var dev = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == comPort);
            _boardBaudRate     = dev?.BaudRate     ?? 115200;
            _boardResetDelayMs = dev?.ResetDelayMs ?? 2000;
            _boardDtrEnable    = dev?.DtrEnable    ?? true;
            OnPropertyChanged(nameof(BoardBaudRate));
            OnPropertyChanged(nameof(BoardResetDelayMs));
            OnPropertyChanged(nameof(BoardDtrEnable));
        }

        private void SaveBoardSettings()
        {
            if (string.IsNullOrEmpty(_editorComPort)) return;

            var dev = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == _editorComPort);
            if (dev == null)
            {
                dev = new Models.ArduinoDevice { ComPort = _editorComPort };
                _config.ArduinoDevices.Add(dev);
            }

            dev.BaudRate     = _boardBaudRate;
            dev.ResetDelayMs = _boardResetDelayMs;
            dev.DtrEnable    = _boardDtrEnable;

            SaveConfig();
        }

        public int EditorPin
        {
            get => _editorPin;
            set => SetProperty(ref _editorPin, value);
        }

        public Models.WinWingDevice? EditorWinWingDevice
        {
            get => _editorWinWingDevice;
            set { SetProperty(ref _editorWinWingDevice, value); OnPropertyChanged(nameof(WinWingLightIndices)); }
        }

        public int EditorLightIndex
        {
            get => _editorLightIndex;
            set => SetProperty(ref _editorLightIndex, value);
        }

        public List<WinWingLightEntry> WinWingLightIndices
        {
            get
            {
                if (EditorWinWingDevice == null) return new List<WinWingLightEntry>();
                return WinWingLightEntry.GetLightsForDevice((ushort)EditorWinWingDevice.ProductId);
            }
        }

        private WinWingLightEntry? _editorLightEntry;
        public WinWingLightEntry? EditorLightEntry
        {
            get => _editorLightEntry;
            set
            {
                SetProperty(ref _editorLightEntry, value);
                if (value != null) EditorLightIndex = value.Index;
            }
        }

        // ── Statistics ────────────────────────────────────────────────────
        public int TotalMappings => _config.Mappings.Count;
        public int ActiveSignals => _allSignals.Count(s => s.IsOn);
        public int MappedSignals => _allSignals.Count(s => s.IsMapped);

        // ── Test mode ─────────────────────────────────────────────────────
        private bool _isTestMode;

        public bool IsTestMode
        {
            get => _isTestMode;
            set
            {
                SetProperty(ref _isTestMode, value);
                OnPropertyChanged(nameof(TestButtonLabel));
                OnPropertyChanged(nameof(TestButtonIsActive));
            }
        }

        public string TestButtonLabel    => IsTestMode ? "Stop Test" : "Test Signal";
        public bool   TestButtonIsActive => IsTestMode;

        // ── Commands ──────────────────────────────────────────────────────
        public RelayCommand                    StartSyncCommand         { get; }
        public RelayCommand                    StopSyncCommand          { get; }
        public RelayCommand                    SaveMappingCommand       { get; }
        public RelayCommand                    DeleteMappingCommand     { get; }
        public RelayCommand                    RemoveAllMappingsCommand { get; }
        public RelayCommand                    RefreshDevicesCommand    { get; }
        public RelayCommand                    TestSignalCommand        { get; }
        public RelayCommand                    DiagnosticCommand        { get; }
        public RelayCommand                    TestAllCommand           { get; }
        public RelayCommand                    SaveBrightnessCommand    { get; }
        public RelayCommand                    ResetBrightnessCommand   { get; }
        public RelayCommand<SignalViewModel>   SelectSignalCommand      { get; }
        public RelayCommand<CategoryGroup>     ToggleCategoryCommand    { get; }
        public RelayCommand                    ExpandAllCommand         { get; }
        public RelayCommand                    CollapseAllCommand       { get; }

        // ── Constructor ───────────────────────────────────────────────────
        public MainViewModel()
        {
            _config         = ConfigurationManager.Load();
            _showOnlyMapped = _config.ShowOnlyMapped;
            _autoSync       = _config.AutoSync;
            _icpDedEnabled  = _config.IcpDisplay.IcpDedEnabled;

            _syncService = new SyncService();
            _syncService.BmsConnectionChanged += OnBmsConnectionChanged;
            _syncService.SyncStateChanged     += OnSyncStateChanged;
            _syncService.LightStatesUpdated   += OnLightStatesUpdated;
            _syncService.IcpConnectionChanged += OnIcpConnectionChanged;

            // Relay live axis values back to the channel view-model so the slider updates.
            _syncService.AxisBindings.BrightnessChanged += OnAxisBrightnessChanged;

            StartSyncCommand         = new RelayCommand(StartSync,          () => CanStart);
            StopSyncCommand          = new RelayCommand(StopSync,           () => CanStop);
            SaveMappingCommand       = new RelayCommand(SaveMapping,        () => HasSelectedSignal);
            DeleteMappingCommand     = new RelayCommand(DeleteMapping,      () => SelectedSignal?.IsMapped == true);
            RemoveAllMappingsCommand = new RelayCommand(RemoveAllMappings,  () => _config.Mappings.Any());
            RefreshDevicesCommand    = new RelayCommand(RefreshDevices);
            SaveBrightnessCommand    = new RelayCommand(SaveBrightness,     () => SelectedBrightnessDevice != null);
            ResetBrightnessCommand   = new RelayCommand(ResetBrightness,    () => SelectedBrightnessDevice != null);
            TestSignalCommand        = new RelayCommand(ToggleTestSignal,   () => SelectedSignal?.IsMapped == true);
            DiagnosticCommand        = new RelayCommand(RunDiagnostic,      () => SelectedSignal?.IsMapped == true && IsArduinoSelected);
            TestAllCommand           = new RelayCommand(TestAllMappings,    () => _config.Mappings.Any(m => m.IsEnabled));

            BrowseHeliosExeCommand     = new RelayCommand(BrowseHeliosExe);
            BrowseHeliosProfileCommand = new RelayCommand(BrowseHeliosProfile);
            SelectSignalCommand      = new RelayCommand<SignalViewModel>(s => SelectedSignal = s);
            ToggleCategoryCommand    = new RelayCommand<CategoryGroup>(g => { if (g != null) g.IsExpanded = !g.IsExpanded; });
            ExpandAllCommand         = new RelayCommand(() => { foreach (var g in CategoryGroups) g.IsExpanded = true; });
            CollapseAllCommand       = new RelayCommand(() => { foreach (var g in CategoryGroups) g.IsExpanded = false; });

            InitializeSignals();
            RefreshDevices();

            if (_config.AutoStartOnLaunch)
                StartSync();
        }

        // ── Initialisation ────────────────────────────────────────────────

        private void InitializeSignals()
        {
            _allSignals.Clear();
            Categories.Clear();
            Categories.Add("All");

            foreach (var category in BmsLights.Categories)
                Categories.Add(category);

            foreach (var light in BmsLights.All)
            {
                var vm      = new SignalViewModel(light);
                var mapping = _config.Mappings.FirstOrDefault(m => m.BmsSignalName == light.Name);

                if (mapping != null)
                {
                    vm.Mapping  = mapping;
                    vm.IsMapped = true;
                }

                _allSignals.Add(vm);
            }

            FilterSignals();
        }

        private void FilterSignals()
        {
            var filtered = _allSignals.AsEnumerable();

            if (SelectedCategory != "All")
                filtered = filtered.Where(s => s.Light.Category == SelectedCategory);

            if (!string.IsNullOrWhiteSpace(SearchText))
                filtered = filtered.Where(s => s.Light.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

            if (ShowOnlyMapped)
                filtered = filtered.Where(s => s.IsMapped);

            var filteredList = filtered.ToList();

            Signals.Clear();
            foreach (var s in filteredList)
                Signals.Add(s);

            // Rebuild CategoryGroups, preserving expanded/collapsed state.
            var expandedState = CategoryGroups.ToDictionary(g => g.Name, g => g.IsExpanded);
            CategoryGroups.Clear();

            foreach (var group in filteredList.GroupBy(s => s.Light.Category))
            {
                var cat = new CategoryGroup(group.Key)
                {
                    IsExpanded = expandedState.TryGetValue(group.Key, out bool was) ? was : false
                };
                foreach (var s in group)
                    cat.Signals.Add(s);
                CategoryGroups.Add(cat);
            }
        }

        // ── Device detection ──────────────────────────────────────────────

        private void RefreshDevices()
        {
            AvailableComPorts.Clear();
            foreach (var port in ArduinoService.GetAvailableComPorts())
                AvailableComPorts.Add(port);

            if (AvailableComPorts.Any() && string.IsNullOrEmpty(EditorComPort))
                EditorComPort = AvailableComPorts.First();

            AvailableWinWingDevices.Clear();
            foreach (var device in WinWingService.EnumerateDevices())
                AvailableWinWingDevices.Add(device);

            // ── Joystick enumeration ──────────────────────────────────────
            AvailableJoysticks.Clear();
            foreach (var js in _syncService.AxisBindings.EnumerateJoysticks())
                AvailableJoysticks.Add(new JoystickDeviceViewModel
                {
                    InstanceGuid = js.InstanceGuid,
                    Name         = js.Name
                });

            RebuildBrightnessChannels();
            ApplyBrightnessChannels(_config.BrightnessChannels);

            // Restore joystick selection and push binding config to the service.
            foreach (var ch in BrightnessChannels)
                ch.SyncJoystickSelection(AvailableJoysticks);

            _syncService.AxisBindings.UpdateBindings(_config.BrightnessChannels);

            if (SelectedBrightnessDevice == null || !AvailableWinWingDevices.Contains(SelectedBrightnessDevice))
                SelectedBrightnessDevice = AvailableWinWingDevices.FirstOrDefault();

            StatusMessage = $"Found: {AvailableComPorts.Count} COM port(s), {AvailableWinWingDevices.Count} WinWing device(s), {AvailableJoysticks.Count} joystick(s)";
            OnPropertyChanged(nameof(HasWinWingDevices));
        }

        private void SaveBrightness()
        {
            if (SelectedBrightnessDevice == null) return;

            foreach (var ch in SelectedBrightnessChannels)
                ch.SaveBinding();

            SaveConfig();
            _syncService.AxisBindings.UpdateBindings(_config.BrightnessChannels);
            ApplyBrightnessChannels(SelectedBrightnessChannels
                .Where(ch => ch.ModeIsManual)
                .Select(vm => vm.Model));
            OnPropertyChanged(nameof(BrightnessSummaryText));
            StatusMessage = $"Brightness saved and applied for {SelectedBrightnessDevice.Name}.";
        }

        private void ResetBrightness()
        {
            if (SelectedBrightnessDevice == null) return;

            foreach (var ch in SelectedBrightnessChannels)
                ch.ResetToManual();

            SaveConfig();
            _syncService.AxisBindings.UpdateBindings(_config.BrightnessChannels);
            ApplyBrightnessChannels(SelectedBrightnessChannels.Select(vm => vm.Model));
            OnPropertyChanged(nameof(BrightnessSummaryText));
            StatusMessage = $"Brightness reset to manual 100% for {SelectedBrightnessDevice.Name}.";
        }

        /// <summary>
        /// Human-readable summary of the saved brightness settings for the selected device,
        /// shown in the status block below the save/reset buttons.
        /// </summary>
        public string BrightnessSummaryText
        {
            get
            {
                if (SelectedBrightnessDevice == null || !SelectedBrightnessChannels.Any())
                    return string.Empty;

                var lines = SelectedBrightnessChannels.Select(ch =>
                {
                    var m = ch.Model;
                    if (m.AxisBinding != null)
                        return $"{ch.Label}: Axis — {m.AxisBinding.DeviceName}  {m.AxisBinding.Axis}" +
                               (m.AxisBinding.Invert ? "  (inverted)" : "");
                    if (m.ButtonBinding != null)
                        return $"{ch.Label}: Buttons — {m.ButtonBinding.DeviceName}" +
                               $"  ↑{m.ButtonBinding.ButtonUp}  ↓{m.ButtonBinding.ButtonDown}";
                    return $"{ch.Label}: Manual — {(int)Math.Round(m.FixedBrightness / 2.55)}%";
                });

                return string.Join("\n", lines);
            }
        }

        private void ApplyBrightnessChannels(IEnumerable<WinWingBrightnessChannel> channels)
        {
            foreach (var ch in channels)
                _syncService.ApplyBrightnessNow(ch);
        }

        private void RebuildBrightnessChannels()
        {
            BrightnessChannels.Clear();

            foreach (var device in AvailableWinWingDevices)
            {
                var pids = WinWingLightEntry.GetGroupPids((ushort)device.ProductId).ToList();

                foreach (var pid in pids)
                {
                    if (!WinWingLightEntry.BrightnessSliderChannels.TryGetValue(pid, out var sliderChannels))
                        continue;

                    foreach (var (lightIndex, label) in sliderChannels)
                    {
                        var existing = _config.BrightnessChannels
                            .FirstOrDefault(c => c.ProductId == pid && c.LightIndex == lightIndex);

                        if (existing == null)
                        {
                            existing = new WinWingBrightnessChannel
                            {
                                ProductId       = pid,
                                LightIndex      = lightIndex,
                                FixedBrightness = 128
                            };
                            _config.BrightnessChannels.Add(existing);
                        }

                        var capturedExisting = existing;
                        var vm = new BrightnessChannelViewModel(existing, label, () =>
                        {
                            SaveConfig();
                            _syncService.ApplyBrightnessNow(capturedExisting);
                        })
                        {
                            AxisBindingService = _syncService.AxisBindings,
                            DispatchToUi       = a => System.Windows.Application.Current.Dispatcher.Invoke(a)
                        };
                        BrightnessChannels.Add(vm);
                    }
                }
            }
        }

        // ── Synchronisation ───────────────────────────────────────────────

        private void StartSync()
        {
            _syncService.Start(_config);
            StatusMessage = "Synchronisation active. Waiting for BMS connection...";
            OnPropertyChanged(nameof(IcpIsConnected));
        }

        private void StopSync()
        {
            _syncService.Stop(_config);
            StatusMessage = "Synchronisation stopped. All lights have been turned off.";
            OnPropertyChanged(nameof(IcpIsConnected));
        }

        // ── Mapping management ────────────────────────────────────────────

        private void LoadMappingToEditor(SignalMapping mapping)
        {
            EditorDeviceType = mapping.TargetDevice;
            _editorComPort   = mapping.ArduinoComPort;
            OnPropertyChanged(nameof(EditorComPort));
            EditorPin        = mapping.ArduinoPin;
            EditorLightIndex = mapping.WinWingLightIndex;
            EditorLightEntry = new WinWingLightEntry(mapping.WinWingLightIndex, (ushort)mapping.WinWingProductId);

            EditorWinWingDevice = AvailableWinWingDevices
                .FirstOrDefault(d => d.ProductId == mapping.WinWingProductId);

            LoadBoardSettings(mapping.ArduinoComPort);
        }

        private void SaveMapping()
        {
            if (SelectedSignal == null) return;

            var mapping = SelectedSignal.Mapping ?? new SignalMapping
            {
                BmsSignalName = SelectedSignal.Light.Name
            };

            mapping.TargetDevice = EditorDeviceType;

            if (EditorDeviceType == DeviceType.Arduino)
            {
                mapping.ArduinoComPort    = EditorComPort;
                mapping.ArduinoPin        = EditorPin;
                mapping.WinWingDeviceName = "";
                mapping.WinWingProductId  = 0;
                mapping.WinWingLightIndex = 0;
            }
            else
            {
                mapping.WinWingLightIndex = EditorLightIndex;
                if (EditorWinWingDevice != null)
                {
                    mapping.WinWingDeviceName = EditorWinWingDevice.Name;
                    mapping.WinWingProductId  = EditorWinWingDevice.ProductId;
                }
                mapping.ArduinoComPort = "";
                mapping.ArduinoPin     = 0;
            }

            if (SelectedSignal.Mapping == null)
                _config.Mappings.Add(mapping);

            SelectedSignal.Mapping  = mapping;
            SelectedSignal.IsMapped = true;
            OnPropertyChanged(nameof(CurrentMappingText));

            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));

            StatusMessage = $"Mapping saved: {SelectedSignal.Light.Name} → " +
                $"{(EditorDeviceType == DeviceType.Arduino ? $"Arduino {EditorComPort} Pin {EditorPin}" : $"WinWing LED {EditorLightIndex}")}";
        }

        private void DeleteMapping()
        {
            if (SelectedSignal?.Mapping == null) return;

            _config.Mappings.Remove(SelectedSignal.Mapping);
            SelectedSignal.Mapping  = null;
            SelectedSignal.IsMapped = false;
            OnPropertyChanged(nameof(CurrentMappingText));

            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));
            StatusMessage = $"Mapping removed: {SelectedSignal.Light.Name}";
        }

        private void RemoveAllMappings()
        {
            int count  = _config.Mappings.Count;
            var result = MessageBox.Show(
                $"Are you sure you want to remove all {count} mapping(s)?\nThis cannot be undone.",
                "Remove All Mappings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            _config.Mappings.Clear();

            foreach (var signal in _allSignals)
            {
                signal.Mapping  = null;
                signal.IsMapped = false;
            }

            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));
            OnPropertyChanged(nameof(CurrentMappingText));
            StatusMessage = "All mappings removed.";
        }

        // ── Helios file browser ───────────────────────────────────────────

        private void BrowseHeliosExe()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title           = "Select Helios Control Center",
                Filter          = "Control Center.exe|Control Center.exe|Executables (*.exe)|*.exe",
                CheckFileExists = true
            };

            string defaultDir = @"C:\Program Files\Helios Virtual Cockpit\Helios";
            if (Directory.Exists(defaultDir))
                dlg.InitialDirectory = defaultDir;

            if (dlg.ShowDialog() == true)
                HeliosControlCenterPath = dlg.FileName;
        }

        private void BrowseHeliosProfile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title            = "Select Helios Profile",
                Filter           = "Helios Profile (*.hpf)|*.hpf",
                CheckFileExists  = true
            };

            // Pre-navigate to the default Helios profiles folder if it exists.
            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Helios", "Profiles");
            if (Directory.Exists(defaultDir))
                dlg.InitialDirectory = defaultDir;

            if (dlg.ShowDialog() == true)
                HeliosProfilePath = dlg.FileName;
        }

        private void SaveConfig()
        {
            try
            {
                ConfigurationManager.Save(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnIcpConnectionChanged(object? sender, bool connected)
        {
            OnPropertyChanged(nameof(IcpIsConnected));
        }

        /// <summary>
        /// Called from the AxisBindingService poll thread (background). Routes the live brightness
        /// value to the matching BrightnessChannelViewModel so the slider updates in the UI.
        /// </summary>
        private void OnAxisBrightnessChanged(int productId, int lightIndex, byte brightness)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var ch in BrightnessChannels)
                {
                    if (ch.Model.ProductId == productId && ch.Model.LightIndex == lightIndex)
                    {
                        ch.LiveAxisValue = brightness;
                        break;
                    }
                }
            });
        }

        private void OnBmsConnectionChanged(object? sender, bool connected)
        {
            IsBmsConnected = connected;
            OnPropertyChanged(nameof(IcpIsConnected));

            if (AutoSync)
            {
                if (connected && !IsSyncing)
                {
                    _syncService.Start(_config);
                    StatusMessage = "BMS connected — sync started automatically.";
                }
                else if (!connected && IsSyncing)
                {
                    _syncService.Stop(_config);
                    StatusMessage = "BMS connection lost — sync stopped automatically.";
                }
            }
            else
            {
                StatusMessage = connected
                    ? "Connected to Falcon BMS. Lights are being synchronised."
                    : "BMS connection lost. Waiting for reconnection...";
            }
        }

        private void OnSyncStateChanged(object? sender, bool syncing)
        {
            IsSyncing = syncing;
        }

        private void OnLightStatesUpdated(object? sender, Dictionary<string, bool> states)
        {
            foreach (var signal in _allSignals)
            {
                if (states.TryGetValue(signal.Light.Name, out bool isOn))
                    signal.IsOn = isOn;
            }

            OnPropertyChanged(nameof(ActiveSignals));
        }

        // ── Arduino diagnostic ────────────────────────────────────────────

        private void RunDiagnostic()
        {
            if (SelectedSignal?.Mapping == null) return;

            var mapping = SelectedSignal.Mapping;
            var dev     = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == mapping.ArduinoComPort);

            StatusMessage = $"Running diagnostic on {mapping.ArduinoComPort}...";

            System.Threading.Tasks.Task.Run(() =>
            {
                string log = _syncService.ArduinoOutput.RunDiagnostic(
                    mapping.ArduinoComPort,
                    dev?.BaudRate     ?? 115200,
                    dev?.ResetDelayMs ?? 2000,
                    dev?.DtrEnable    ?? true,
                    mapping.BmsSignalName,
                    mapping.ArduinoPin,
                    _config.Mappings);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(log, "Arduino Diagnostic Result",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusMessage = "Diagnostic complete.";
                });
            });
        }

        // ── Test all mappings ─────────────────────────────────────────────

        private bool _isTestingAll;
        public bool IsTestingAll
        {
            get => _isTestingAll;
            set { SetProperty(ref _isTestingAll, value); OnPropertyChanged(nameof(TestAllButtonLabel)); OnPropertyChanged(nameof(CanStopAllTests)); }
        }
        public string TestAllButtonLabel => IsTestingAll ? "Stop All Tests" : "Test All Mappings";

        private bool _arduinoTestReady;
        public bool CanStopAllTests => IsTestingAll && _arduinoTestReady;

        private void TestAllMappings()
        {
            if (_isTestingAll)
            {
                IsTestingAll    = false;
                _arduinoTestReady = false;
                OnPropertyChanged(nameof(CanStopAllTests));

                var offStates = _config.Mappings
                    .Where(m => m.IsEnabled)
                    .ToDictionary(m => m.BmsSignalName, _ => false);
                _syncService.FireTestOutput(_config, offStates);

                foreach (var s in _allSignals.Where(s => s.IsMapped))
                    s.IsOn = false;

                OnPropertyChanged(nameof(ActiveSignals));
                _syncService.ArduinoOutput.Disconnect();
                StatusMessage = "All test lights turned off. COM ports released.";
                return;
            }

            IsTestingAll      = true;
            _arduinoTestReady = false;
            OnPropertyChanged(nameof(CanStopAllTests));

            var onStates = _config.Mappings
                .Where(m => m.IsEnabled)
                .ToDictionary(m => m.BmsSignalName, _ => true);

            foreach (var s in _allSignals.Where(s => s.IsMapped))
                s.IsOn = true;
            OnPropertyChanged(nameof(ActiveSignals));

            var arduinoGroups = SyncService.BuildArduinoGroups(_config);

            bool hasArduino = arduinoGroups.Count > 0;

            if (!hasArduino)
            {
                // No Arduino boards — WinWing only, can stop immediately.
                _syncService.FireTestOutput(_config, onStates);
                _arduinoTestReady = true;
                OnPropertyChanged(nameof(CanStopAllTests));
                StatusMessage = $"Testing {onStates.Count} mapped signal(s) — all lights ON. Press again to turn off.";
            }
            else
            {
                StatusMessage = $"Testing {onStates.Count} mapped signal(s) — waiting for Arduino board(s) to initialise...";

                // WinWing: fire immediately.
                _syncService.FireTestOutput(_config, onStates);

                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Tasks.Parallel.ForEach(arduinoGroups, group =>
                        _syncService.ArduinoOutput.Connect(
                            group.ComPort, group.BaudRate, group.ResetDelayMs, group.DtrEnable, group.Mappings));

                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        _syncService.FireTestOutput(_config, onStates);

                        _arduinoTestReady = true;
                        OnPropertyChanged(nameof(CanStopAllTests));
                        StatusMessage = $"Testing {onStates.Count} mapped signal(s) — all lights ON. Press again to turn off.";
                    });
                });
            }
        }

        // ── Test signal ───────────────────────────────────────────────────

        private void ToggleTestSignal()
        {
            if (SelectedSignal == null || SelectedSignal.Mapping == null) return;

            IsTestMode = !IsTestMode;
            SelectedSignal.IsOn = IsTestMode;
            OnPropertyChanged(nameof(ActiveSignals));

            var testStates = new Dictionary<string, bool>
            {
                { SelectedSignal.Light.Name, IsTestMode }
            };

            if (SelectedSignal.Mapping.TargetDevice == DeviceType.Arduino)
            {
                var comPort  = SelectedSignal.Mapping.ArduinoComPort;
                var mappings = _config.Mappings.ToList();
                var cfgSnap  = _config;
                var dev      = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == comPort);

                if (IsTestMode)
                {
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        _syncService.ArduinoOutput.Connect(
                            comPort, dev?.BaudRate ?? 115200, dev?.ResetDelayMs ?? 2000, dev?.DtrEnable ?? true, mappings);
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                            _syncService.FireTestOutput(cfgSnap, testStates));
                    });
                }
                else
                {
                    _syncService.FireTestOutput(_config, testStates);
                    _syncService.ArduinoOutput.Disconnect(comPort);
                }
            }
            else if (SelectedSignal.Mapping.TargetDevice == DeviceType.WinWing)
            {
                _syncService.WinWingOutput.Connect(SelectedSignal.Mapping.WinWingProductId);
                _syncService.FireTestOutput(_config, testStates);
            }

            StatusMessage = IsTestMode
                ? $"TEST ON: '{SelectedSignal.Light.Name}' — output sent to device."
                : $"TEST OFF: '{SelectedSignal.Light.Name}' — COM port released.";
        }

        private void CancelActiveTest()
        {
            if (!IsTestMode) return;

            if (SelectedSignal?.Mapping != null)
            {
                var offStates = new Dictionary<string, bool> { { SelectedSignal.Light.Name, false } };
                _syncService.FireTestOutput(_config, offStates);
                SelectedSignal.IsOn = false;
            }

            IsTestMode = false;
            OnPropertyChanged(nameof(ActiveSignals));
        }

        // ── IDisposable ───────────────────────────────────────────────────

        public void Dispose()
        {
            if (IsTestMode) CancelActiveTest();
            _syncService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
