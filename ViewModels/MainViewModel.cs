using System.Collections.ObjectModel;
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

    /// <summary>Represents one mapping row in the "Current Mappings" list for a signal.</summary>
    public class MappingRowViewModel : BaseViewModel
    {
        public SignalMapping Mapping       { get; }
        public RelayCommand  DeleteCommand { get; }

        /// <summary>Eerste regel: apparaatnaam (Arduino COM poort of WinWing controllernaam).</summary>
        public string DeviceLabel =>
            Mapping.TargetDevice == DeviceType.Arduino
                ? $"Arduino  {Mapping.ArduinoComPort}"
                : $"WinWing  {Mapping.WinWingDeviceName}";

        /// <summary>Tweede regel: de lamp of pin — altijd prominent zichtbaar.</summary>
        public string OutputLabel =>
            Mapping.TargetDevice == DeviceType.Arduino
                ? $"Pin {Mapping.ArduinoPin}"
                : string.IsNullOrEmpty(Mapping.WinWingLightName)
                    ? $"LED {Mapping.WinWingLightIndex}"
                    : Mapping.WinWingLightName;

        public MappingRowViewModel(SignalMapping mapping, Action<MappingRowViewModel> onDelete)
        {
            Mapping       = mapping;
            DeleteCommand = new RelayCommand(() => onDelete(this));
        }
    }

    /// <summary>UI representation of a BMS signal with its associated mappings.</summary>
    public class SignalViewModel : BaseViewModel
    {
        private bool _isOn;
        private bool _isSelected;

        public CockpitLight Light { get; }

        /// <summary>All mapping rows for this signal (may be empty, one, or many).</summary>
        public ObservableCollection<MappingRowViewModel> MappingRows { get; } = new();

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (SetProperty(ref _isOn, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsMapped => MappingRows.Count > 0;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string StatusText  => IsOn ? "ON" : "OFF";
        public int    MappingCount => MappingRows.Count;

        public SignalViewModel(CockpitLight light)
        {
            Light = light;
            MappingRows.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(IsMapped));
                OnPropertyChanged(nameof(MappingCount));
            };
        }
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
        public enum BindingMode { Manual, Axis, Buttons }
        private BindingMode _mode = BindingMode.Manual;

        // ── Axis binding UI state ─────────────────────────────────────────
        private JoystickDeviceViewModel? _selectedJoystick;
        private string                   _axisJoystickGuid = "";
        private Models.JoystickAxis      _selectedAxis     = Models.JoystickAxis.Z;
        private bool                     _axisInvert;
        private int                      _liveAxisValue;
        private bool                     _isDetectingAxis;

        // ── Button binding UI state ───────────────────────────────────────
        private JoystickDeviceViewModel? _selectedButtonJoystick;
        private string                   _buttonJoystickGuid = "";
        private int                      _buttonUp           = 0;
        private int                      _buttonDown         = 1;

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
                _selectedAxis     = model.AxisBinding.Axis;
                _axisInvert       = model.AxisBinding.Invert;
                _axisJoystickGuid = model.AxisBinding.DeviceInstanceGuid;
            }
            if (model.ButtonBinding != null)
            {
                _buttonUp           = model.ButtonBinding.ButtonUp;
                _buttonDown         = model.ButtonBinding.ButtonDown;
                _buttonJoystickGuid = model.ButtonBinding.DeviceInstanceGuid;
            }

            // Determine active mode — button takes precedence if both somehow exist.
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
            if (AxisBindingService == null || _selectedJoystick == null || _isDetectingAxis) return;

            IsDetectingAxis = true;
            AxisBindingService.DetectAxis(
                _selectedJoystick.InstanceGuid,
                timeoutMs: 8000,
                onDetected: axis => DispatchToUi?.Invoke(() => { SelectedAxis = axis; IsDetectingAxis = false; }),
                onTimeout:  ()   => DispatchToUi?.Invoke(() =>   IsDetectingAxis = false));
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

        public bool IsDetecting     => _detecting != DetectTarget.None;
        public bool IsDetectingUp   => _detecting == DetectTarget.Up;
        public bool IsDetectingDown => _detecting == DetectTarget.Down;

        // Injected by MainViewModel after construction.
        public Services.AxisBindingService? AxisBindingService { private get; set; }
        public Action<Action>?              DispatchToUi       { private get; set; }

        public RelayCommand DetectButtonUpCommand   { get; }
        public RelayCommand DetectButtonDownCommand { get; }

        private void StartDetect(DetectTarget target)
        {
            if (AxisBindingService == null || _selectedButtonJoystick == null || IsDetecting) return;

            Detecting = target;
            AxisBindingService.DetectButton(
                _selectedButtonJoystick.InstanceGuid,
                timeoutMs: 8000,
                onDetected: idx => DispatchToUi?.Invoke(() =>
                {
                    if (target == DetectTarget.Up) ButtonUp = idx; else ButtonDown = idx;
                    Detecting = DetectTarget.None;
                }),
                onTimeout: () => DispatchToUi?.Invoke(() => Detecting = DetectTarget.None));
        }

        public WinWingBrightnessChannel Model => _model;

        /// <summary>
        /// Resets this channel to Manual mode at 100% brightness, wiping all axis/button bindings.
        /// Called by the "Reset to default" button.
        /// </summary>
        public void ResetToManual()
        {
            _isDetectingAxis        = false;
            _detecting              = DetectTarget.None;
            _selectedJoystick       = null;
            _axisJoystickGuid       = "";
            _selectedAxis           = Models.JoystickAxis.Z;
            _axisInvert             = false;
            _selectedButtonJoystick = null;
            _buttonJoystickGuid     = "";
            _buttonUp               = 0;
            _buttonDown             = 1;
            _mode                   = BindingMode.Manual;
            _model.FixedBrightness  = 255;
            _model.AxisBinding      = null;
            _model.ButtonBinding    = null;

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
        private string _statusMessage    = "Ready. Start BMS and press 'Start Sync'.";
        private SignalViewModel? _selectedSignal;
        private string _searchText       = "";
        private string _selectedCategory = "All";

        public bool IsBmsConnected
        {
            get => _isBmsConnected;
            set
            {
                if (SetProperty(ref _isBmsConnected, value))
                    OnPropertyChanged(nameof(BmsStatusText));
            }
        }

        /// <summary>Computed from IsBmsConnected — no separate backing field needed.</summary>
        public string BmsStatusText => IsBmsConnected ? "Connected to BMS" : "BMS not detected";

        public bool IsSyncing
        {
            get => _isSyncing;
            set
            {
                SetProperty(ref _isSyncing, value);
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
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
            }
        }

        public bool HasSelectedSignal => SelectedSignal != null;
        public bool CanStart          => !IsSyncing;
        public bool CanStop           => IsSyncing;

        // ── ICP Display properties ────────────────────────────────────────
        private bool _icpDedEnabled;

        public bool IcpDedEnabled
        {
            get => _icpDedEnabled;
            set
            {
                SetProperty(ref _icpDedEnabled, value);
                _config.IcpDisplay.IcpDedEnabled = value;
                SaveConfig();
                if (IsSyncing) _syncService.ApplyIcpConfig(value);
                StatusMessage = value
                    ? "ICP DED LCD synchronisation enabled."
                    : "ICP DED LCD synchronisation disabled.";
            }
        }

        public bool IcpIsConnected => _syncService.IcpOutput.IsConnected;

        private bool _showOnlyMapped;
        public bool ShowOnlyMapped
        {
            get => _showOnlyMapped;
            set { SetProperty(ref _showOnlyMapped, value); _config.ShowOnlyMapped = value; SaveConfig(); FilterSignals(); }
        }

        private bool _autoSync;
        public bool AutoSync
        {
            get => _autoSync;
            set { SetProperty(ref _autoSync, value); _config.AutoSync = value; SaveConfig(); }
        }

        public bool StartMinimized
        {
            get => _config.StartMinimized;
            set { _config.StartMinimized = value; OnPropertyChanged(); SaveConfig(); }
        }

        public bool AutoStartOnLaunch
        {
            get => _config.AutoStartOnLaunch;
            set { _config.AutoStartOnLaunch = value; OnPropertyChanged(); SaveConfig(); }
        }

        public int PollingIntervalMs
        {
            get => _config.PollingIntervalMs;
            set { _config.PollingIntervalMs = Math.Clamp(value, 10, 1000); OnPropertyChanged(); SaveConfig(); }
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

        public RelayCommand BrowseHeliosExeCommand     { get; }
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
        private readonly Dictionary<(int pid, int idx), BrightnessChannelViewModel> _brightnessLookup = new();
        public ObservableCollection<BrightnessChannelViewModel> SelectedBrightnessChannels { get; } = new();
        public ObservableCollection<JoystickDeviceViewModel>    AvailableJoysticks         { get; } = new();

        private Models.WinWingDevice? _selectedBrightnessDevice;
        public Models.WinWingDevice? SelectedBrightnessDevice
        {
            get => _selectedBrightnessDevice;
            set { _selectedBrightnessDevice = value; OnPropertyChanged(); UpdateSelectedBrightnessChannels(); }
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
        private DeviceType            _editorDeviceType = DeviceType.Arduino;
        private string                _editorComPort    = "";
        private int                   _editorPin        = 13;
        private Models.WinWingDevice? _editorWinWingDevice;
        private int                   _editorLightIndex = 0;

        // Board-level settings (per COM port, stored in ArduinoDevices)
        private int  _boardBaudRate     = 115200;
        private int  _boardResetDelayMs = 2000;
        private bool _boardDtrEnable    = true;

        public DeviceType EditorDeviceType
        {
            get => _editorDeviceType;
            set { SetProperty(ref _editorDeviceType, value); OnPropertyChanged(nameof(IsArduinoSelected)); OnPropertyChanged(nameof(IsWinWingSelected)); }
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

            var dev = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == _editorComPort)
                      ?? new Models.ArduinoDevice { ComPort = _editorComPort };

            if (!_config.ArduinoDevices.Contains(dev))
                _config.ArduinoDevices.Add(dev);

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

        public List<WinWingLightEntry> WinWingLightIndices =>
            EditorWinWingDevice == null
                ? new List<WinWingLightEntry>()
                : WinWingLightEntry.GetLightsForDevice((ushort)EditorWinWingDevice.ProductId);

        private WinWingLightEntry? _editorLightEntry;
        public WinWingLightEntry? EditorLightEntry
        {
            get => _editorLightEntry;
            set { SetProperty(ref _editorLightEntry, value); if (value != null) EditorLightIndex = value.Index; }
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
            set { SetProperty(ref _isTestMode, value); OnPropertyChanged(nameof(TestButtonLabel)); OnPropertyChanged(nameof(TestButtonIsActive)); }
        }

        public string TestButtonLabel    => IsTestMode ? "Stop Test" : "Test Signal";
        public bool   TestButtonIsActive => IsTestMode;

        // ── Commands ──────────────────────────────────────────────────────
        public RelayCommand                  StartSyncCommand         { get; }
        public RelayCommand                  StopSyncCommand          { get; }
        public RelayCommand                  AddMappingCommand        { get; }
        public RelayCommand                  DeleteMappingCommand     { get; }
        public RelayCommand                  RemoveAllMappingsCommand { get; }
        public RelayCommand                  RefreshDevicesCommand    { get; }
        public RelayCommand                  TestSignalCommand        { get; }
        public RelayCommand                  DiagnosticCommand        { get; }
        public RelayCommand                  TestAllCommand           { get; }
        public RelayCommand                  SaveBrightnessCommand    { get; }
        public RelayCommand                  ResetBrightnessCommand   { get; }
        public RelayCommand<SignalViewModel> SelectSignalCommand      { get; }
        public RelayCommand<CategoryGroup>   ToggleCategoryCommand    { get; }
        public RelayCommand                  ExpandAllCommand         { get; }
        public RelayCommand                  CollapseAllCommand       { get; }

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
            _syncService.IcpConnectionChanged += (_, _) => OnPropertyChanged(nameof(IcpIsConnected));
            _syncService.AxisBindings.BrightnessChanged += OnAxisBrightnessChanged;

            StartSyncCommand         = new RelayCommand(StartSync,         () => CanStart);
            StopSyncCommand          = new RelayCommand(StopSync,          () => CanStop);
            AddMappingCommand        = new RelayCommand(AddMapping,        () => HasSelectedSignal);
            DeleteMappingCommand     = new RelayCommand(DeleteAllMappings, () => SelectedSignal?.IsMapped == true);
            RemoveAllMappingsCommand = new RelayCommand(RemoveAllMappings, () => _config.Mappings.Any());
            RefreshDevicesCommand    = new RelayCommand(RefreshDevices);
            SaveBrightnessCommand    = new RelayCommand(SaveBrightness,    () => SelectedBrightnessDevice != null);
            ResetBrightnessCommand   = new RelayCommand(ResetBrightness,   () => SelectedBrightnessDevice != null);
            TestSignalCommand        = new RelayCommand(ToggleTestSignal,  () => SelectedSignal?.IsMapped == true);
            DiagnosticCommand        = new RelayCommand(RunDiagnostic,     () => SelectedSignal?.IsMapped == true && IsArduinoSelected);
            TestAllCommand           = new RelayCommand(TestAllMappings,   () => _config.Mappings.Any(m => m.IsEnabled));

            BrowseHeliosExeCommand     = new RelayCommand(BrowseHeliosExe);
            BrowseHeliosProfileCommand = new RelayCommand(BrowseHeliosProfile);
            SelectSignalCommand        = new RelayCommand<SignalViewModel>(s => SelectedSignal = s);
            ToggleCategoryCommand      = new RelayCommand<CategoryGroup>(g => { if (g != null) g.IsExpanded = !g.IsExpanded; });
            ExpandAllCommand           = new RelayCommand(() => { foreach (var g in CategoryGroups) g.IsExpanded = true; });
            CollapseAllCommand         = new RelayCommand(() => { foreach (var g in CategoryGroups) g.IsExpanded = false; });

            InitializeSignals();
            RefreshDevices();

            if (_config.AutoStartOnLaunch)
                StartSync();
        }

        // ── Config import / export ────────────────────────────────────────

        /// <summary>
        /// Replaces the current configuration with one loaded from the given file path,
        /// saves it as the active config, and reloads the UI.
        /// Returns false if the file cannot be parsed.
        /// </summary>
        public bool ImportConfig(string filePath)
        {
            var imported = ConfigurationManager.LoadFrom(filePath);
            if (imported == null) return false;

            _config = imported;
            ConfigurationManager.Save(_config);

            // Reset selection state before reinitialising — otherwise the ComboBox
            // fires SelectionChanged with null when Categories is cleared, which causes
            // FilterSignals to filter on null and show an empty signal list.
            _selectedSignal   = null;
            _selectedCategory = "All";
            OnPropertyChanged(nameof(SelectedSignal));
            OnPropertyChanged(nameof(HasSelectedSignal));
            OnPropertyChanged(nameof(SelectedCategory));

            _showOnlyMapped = _config.ShowOnlyMapped;
            _autoSync       = _config.AutoSync;
            _icpDedEnabled  = _config.IcpDisplay.IcpDedEnabled;

            OnPropertyChanged(nameof(AutoSync));
            OnPropertyChanged(nameof(AutoStartOnLaunch));
            OnPropertyChanged(nameof(StartMinimized));
            OnPropertyChanged(nameof(ShowOnlyMapped));
            OnPropertyChanged(nameof(IcpDedEnabled));
            OnPropertyChanged(nameof(HeliosLaunchEnabled));
            OnPropertyChanged(nameof(HeliosShutdownEnabled));
            OnPropertyChanged(nameof(HeliosControlCenterPath));
            OnPropertyChanged(nameof(HeliosProfilePath));
            OnPropertyChanged(nameof(PollingIntervalMs));

            InitializeSignals();
            RefreshDevices();
            return true;
        }

        public void ExportConfig(string filePath) => ConfigurationManager.ExportTo(_config, filePath);

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
                var vm       = new SignalViewModel(light);
                var mappings = _config.Mappings.Where(m => m.BmsSignalName == light.Name).ToList();

                foreach (var mapping in mappings)
                {
                    // Herstel WinWingLightName voor configs opgeslagen zonder naam (backwards compat)
                    if (mapping.TargetDevice == DeviceType.WinWing
                        && string.IsNullOrEmpty(mapping.WinWingLightName)
                        && mapping.WinWingProductId != 0)
                    {
                        mapping.WinWingLightName = new WinWingLightEntry(
                            mapping.WinWingLightIndex, (ushort)mapping.WinWingProductId).Name;
                    }

                    vm.MappingRows.Add(new MappingRowViewModel(mapping, row => DeleteMappingRow(vm, row)));
                }

                _allSignals.Add(vm);
            }

            FilterSignals();
        }

        private void FilterSignals()
        {
            var filtered = _allSignals.AsEnumerable();

            if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "All")
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
                    IsExpanded = expandedState.GetValueOrDefault(group.Key, false)
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

            AvailableJoysticks.Clear();
            foreach (var js in _syncService.AxisBindings.EnumerateJoysticks())
                AvailableJoysticks.Add(new JoystickDeviceViewModel { InstanceGuid = js.InstanceGuid, Name = js.Name });

            RebuildBrightnessChannels();
            ApplyBrightnessChannels(_config.BrightnessChannels);

            foreach (var ch in BrightnessChannels)
                ch.SyncJoystickSelection(AvailableJoysticks);

            _syncService.AxisBindings.UpdateBindings(_config.BrightnessChannels);

            if (SelectedBrightnessDevice == null || !AvailableWinWingDevices.Contains(SelectedBrightnessDevice))
                SelectedBrightnessDevice = AvailableWinWingDevices.FirstOrDefault();

            StatusMessage = $"Found: {AvailableComPorts.Count} COM port(s), {AvailableWinWingDevices.Count} WinWing device(s), {AvailableJoysticks.Count} joystick(s)";
            OnPropertyChanged(nameof(HasWinWingDevices));
        }

        // ── Brightness ────────────────────────────────────────────────────

        private void SaveBrightness()
        {
            if (SelectedBrightnessDevice == null) return;
            foreach (var ch in SelectedBrightnessChannels) ch.SaveBinding();
            SaveConfig();
            _syncService.AxisBindings.UpdateBindings(_config.BrightnessChannels);
            ApplyBrightnessChannels(SelectedBrightnessChannels.Where(ch => ch.ModeIsManual).Select(vm => vm.Model));
            OnPropertyChanged(nameof(BrightnessSummaryText));
            StatusMessage = $"Brightness saved and applied for {SelectedBrightnessDevice.Name}.";
        }

        private void ResetBrightness()
        {
            if (SelectedBrightnessDevice == null) return;
            foreach (var ch in SelectedBrightnessChannels) ch.ResetToManual();
            SaveConfig();
            _syncService.AxisBindings.UpdateBindings(_config.BrightnessChannels);
            ApplyBrightnessChannels(SelectedBrightnessChannels.Select(vm => vm.Model));
            OnPropertyChanged(nameof(BrightnessSummaryText));
            StatusMessage = $"Brightness reset to manual 100% for {SelectedBrightnessDevice.Name}.";
        }

        public string BrightnessSummaryText
        {
            get
            {
                if (SelectedBrightnessDevice == null || !SelectedBrightnessChannels.Any())
                    return string.Empty;

                return string.Join("\n", SelectedBrightnessChannels.Select(ch =>
                {
                    var m = ch.Model;
                    if (m.AxisBinding != null)
                        return $"{ch.Label}: Axis — {m.AxisBinding.DeviceName}  {m.AxisBinding.Axis}"
                               + (m.AxisBinding.Invert ? "  (inverted)" : "");
                    if (m.ButtonBinding != null)
                        return $"{ch.Label}: Buttons — {m.ButtonBinding.DeviceName}"
                               + $"  ↑{m.ButtonBinding.ButtonUp}  ↓{m.ButtonBinding.ButtonDown}";
                    return $"{ch.Label}: Manual — {(int)Math.Round(m.FixedBrightness / 2.55)}%";
                }));
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
            _brightnessLookup.Clear();

            foreach (var device in AvailableWinWingDevices)
            {
                foreach (var pid in WinWingLightEntry.GetGroupPids((ushort)device.ProductId))
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
                                { ProductId = pid, LightIndex = lightIndex, FixedBrightness = 128 };
                            _config.BrightnessChannels.Add(existing);
                        }

                        var captured = existing;
                        var vm = new BrightnessChannelViewModel(existing, label, () =>
                        {
                            SaveConfig();
                            _syncService.ApplyBrightnessNow(captured);
                        })
                        {
                            AxisBindingService = _syncService.AxisBindings,
                            DispatchToUi       = a => System.Windows.Application.Current.Dispatcher.Invoke(a)
                        };
                        BrightnessChannels.Add(vm);
                        _brightnessLookup[(existing.ProductId, existing.LightIndex)] = vm;
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

        /// <summary>Builds the human-readable output description for the current editor state.</summary>
        private string EditorOutputDescription() =>
            EditorDeviceType == DeviceType.WinWing
                ? $"{EditorWinWingDevice?.Name}  {EditorLightEntry?.Name ?? $"LED {EditorLightIndex}"}"
                : $"Arduino {EditorComPort}  Pin {EditorPin}";

        /// <summary>Adds a new mapping entry for the selected signal from the current editor values.</summary>
        private void AddMapping()
        {
            if (SelectedSignal == null) return;

            // ── Duplicaat-check ───────────────────────────────────────────
            SignalMapping? existingConflict = null;

            if (EditorDeviceType == DeviceType.WinWing && EditorWinWingDevice != null)
            {
                existingConflict = _config.Mappings.FirstOrDefault(m =>
                    m.TargetDevice      == DeviceType.WinWing &&
                    m.WinWingProductId  == EditorWinWingDevice.ProductId &&
                    m.WinWingLightIndex == EditorLightIndex);
            }
            else if (EditorDeviceType == DeviceType.Arduino && !string.IsNullOrEmpty(EditorComPort))
            {
                existingConflict = _config.Mappings.FirstOrDefault(m =>
                    m.TargetDevice   == DeviceType.Arduino &&
                    m.ArduinoComPort == EditorComPort &&
                    m.ArduinoPin     == EditorPin);
            }

            if (existingConflict != null)
            {
                string outputDesc = EditorOutputDescription();

                if (existingConflict.BmsSignalName == SelectedSignal.Light.Name)
                {
                    MessageBox.Show(
                        $"This output is already mapped to this signal:\n\n  {outputDesc}",
                        "Duplicate Mapping", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"This output is already mapped to:\n\n" +
                    $"  Signal:  {existingConflict.BmsSignalName}\n" +
                    $"  Output:  {outputDesc}\n\n" +
                    $"Do you want to move this output to '{SelectedSignal.Light.Name}'?\n" +
                    $"The existing mapping will be removed.",
                    "Output Already Mapped", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                // Verwijder de conflicterende mapping uit config én UI
                _config.Mappings.Remove(existingConflict);
                var conflictSignal = _allSignals.FirstOrDefault(s => s.Light.Name == existingConflict.BmsSignalName);
                var conflictRow    = conflictSignal?.MappingRows.FirstOrDefault(r => r.Mapping == existingConflict);
                if (conflictRow != null) conflictSignal!.MappingRows.Remove(conflictRow);
            }

            // ── Mapping aanmaken ──────────────────────────────────────────
            var mapping = new SignalMapping { BmsSignalName = SelectedSignal.Light.Name, TargetDevice = EditorDeviceType };

            if (EditorDeviceType == DeviceType.Arduino)
            {
                mapping.ArduinoComPort = EditorComPort;
                mapping.ArduinoPin     = EditorPin;
            }
            else
            {
                mapping.WinWingLightIndex = EditorLightIndex;
                mapping.WinWingLightName  = EditorLightEntry?.Name ?? $"LED {EditorLightIndex}";
                if (EditorWinWingDevice != null)
                {
                    mapping.WinWingDeviceName = EditorWinWingDevice.Name;
                    mapping.WinWingProductId  = EditorWinWingDevice.ProductId;
                }
            }

            _config.Mappings.Add(mapping);
            SelectedSignal.MappingRows.Add(new MappingRowViewModel(mapping, row => DeleteMappingRow(SelectedSignal, row)));

            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));
            StatusMessage = $"Mapping added: {SelectedSignal.Light.Name} → {EditorOutputDescription()}";
        }

        /// <summary>Deletes one mapping row from a signal (called from the row's Delete button).</summary>
        private void DeleteMappingRow(SignalViewModel signal, MappingRowViewModel row)
        {
            _config.Mappings.Remove(row.Mapping);
            signal.MappingRows.Remove(row);
            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));
            StatusMessage = $"Mapping removed: {signal.Light.Name}";
        }

        /// <summary>Deletes ALL mappings for the selected signal.</summary>
        private void DeleteAllMappings()
        {
            if (SelectedSignal == null) return;

            foreach (var m in SelectedSignal.MappingRows.Select(r => r.Mapping).ToList())
                _config.Mappings.Remove(m);
            SelectedSignal.MappingRows.Clear();

            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));
            StatusMessage = $"All mappings removed: {SelectedSignal.Light.Name}";
        }

        private void RemoveAllMappings()
        {
            int count = _config.Mappings.Count;
            if (MessageBox.Show(
                    $"Are you sure you want to remove all {count} mapping(s)?\nThis cannot be undone.",
                    "Remove All Mappings", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes) return;

            _config.Mappings.Clear();
            foreach (var signal in _allSignals) signal.MappingRows.Clear();

            SaveConfig();
            OnPropertyChanged(nameof(TotalMappings));
            OnPropertyChanged(nameof(MappedSignals));
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
            if (Directory.Exists(defaultDir)) dlg.InitialDirectory = defaultDir;
            if (dlg.ShowDialog() == true) HeliosControlCenterPath = dlg.FileName;
        }

        private void BrowseHeliosProfile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title           = "Select Helios Profile",
                Filter          = "Helios Profile (*.hpf)|*.hpf",
                CheckFileExists = true
            };
            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Helios", "Profiles");
            if (Directory.Exists(defaultDir)) dlg.InitialDirectory = defaultDir;
            if (dlg.ShowDialog() == true) HeliosProfilePath = dlg.FileName;
        }

        private void SaveConfig()
        {
            try   { ConfigurationManager.Save(_config); }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnAxisBrightnessChanged(int productId, int lightIndex, byte brightness)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                if (_brightnessLookup.TryGetValue((productId, lightIndex), out var ch))
                    ch.LiveAxisValue = brightness;
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

        private void OnSyncStateChanged(object? sender, bool syncing) => IsSyncing = syncing;

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
            var mapping = SelectedSignal?.MappingRows
                .Select(r => r.Mapping)
                .FirstOrDefault(m => m.TargetDevice == DeviceType.Arduino);
            if (mapping == null) return;

            var dev = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == mapping.ArduinoComPort);
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
                    MessageBox.Show(log, "Arduino Diagnostic Result", MessageBoxButton.OK, MessageBoxImage.Information);
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

        /// <summary>Returns the set of enabled signal names as an on/off state dictionary.</summary>
        private Dictionary<string, bool> BuildTestStates(bool on) =>
            _config.Mappings
                .Where(m => m.IsEnabled)
                .Select(m => m.BmsSignalName)
                .Distinct()
                .ToDictionary(name => name, _ => on);

        private void TestAllMappings()
        {
            if (_isTestingAll)
            {
                IsTestingAll      = false;
                _arduinoTestReady = false;
                OnPropertyChanged(nameof(CanStopAllTests));

                _syncService.FireTestOutput(_config, BuildTestStates(false));
                foreach (var s in _allSignals.Where(s => s.IsMapped)) s.IsOn = false;
                OnPropertyChanged(nameof(ActiveSignals));
                _syncService.ArduinoOutput.Disconnect();
                StatusMessage = "All test lights turned off. COM ports released.";
                return;
            }

            IsTestingAll      = true;
            _arduinoTestReady = false;
            OnPropertyChanged(nameof(CanStopAllTests));

            var onStates       = BuildTestStates(true);
            var arduinoGroups  = SyncService.BuildArduinoGroups(_config);

            foreach (var s in _allSignals.Where(s => s.IsMapped)) s.IsOn = true;
            OnPropertyChanged(nameof(ActiveSignals));

            _syncService.FireTestOutput(_config, onStates);

            if (!arduinoGroups.Any())
            {
                _arduinoTestReady = true;
                OnPropertyChanged(nameof(CanStopAllTests));
                StatusMessage = $"Testing {onStates.Count} mapped signal(s) — all lights ON. Press again to turn off.";
                return;
            }

            StatusMessage = $"Testing {onStates.Count} mapped signal(s) — waiting for Arduino board(s) to initialise...";

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

        // ── Test signal ───────────────────────────────────────────────────

        private void ToggleTestSignal()
        {
            if (SelectedSignal == null || !SelectedSignal.IsMapped) return;

            IsTestMode          = !IsTestMode;
            SelectedSignal.IsOn = IsTestMode;
            OnPropertyChanged(nameof(ActiveSignals));

            var testStates = new Dictionary<string, bool> { { SelectedSignal.Light.Name, IsTestMode } };

            var arduinoMappings = SelectedSignal.MappingRows
                .Select(r => r.Mapping).Where(m => m.TargetDevice == DeviceType.Arduino).ToList();
            var winwingMappings = SelectedSignal.MappingRows
                .Select(r => r.Mapping).Where(m => m.TargetDevice == DeviceType.WinWing).ToList();

            if (arduinoMappings.Any())
            {
                if (IsTestMode)
                {
                    var allMappings = _config.Mappings.ToList();
                    var cfgSnap     = _config;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        foreach (var grp in arduinoMappings.GroupBy(m => m.ArduinoComPort))
                        {
                            var dev = _config.ArduinoDevices.FirstOrDefault(d => d.ComPort == grp.Key);
                            _syncService.ArduinoOutput.Connect(
                                grp.Key, dev?.BaudRate ?? 115200, dev?.ResetDelayMs ?? 2000, dev?.DtrEnable ?? true, allMappings);
                        }
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                            _syncService.FireTestOutput(cfgSnap, testStates));
                    });
                }
                else
                {
                    _syncService.FireTestOutput(_config, testStates);
                    foreach (var port in arduinoMappings.Select(m => m.ArduinoComPort).Distinct())
                        _syncService.ArduinoOutput.Disconnect(port);
                }
            }

            if (winwingMappings.Any())
            {
                foreach (var wm in winwingMappings)
                    _syncService.WinWingOutput.Connect(wm.WinWingProductId);
                _syncService.FireTestOutput(_config, testStates);
            }

            StatusMessage = IsTestMode
                ? $"TEST ON: '{SelectedSignal.Light.Name}' — output sent to device(s)."
                : $"TEST OFF: '{SelectedSignal.Light.Name}' — COM port(s) released.";
        }

        private void CancelActiveTest()
        {
            if (!IsTestMode) return;

            if (SelectedSignal?.IsMapped == true)
            {
                _syncService.FireTestOutput(_config,
                    new Dictionary<string, bool> { { SelectedSignal.Light.Name, false } });
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
