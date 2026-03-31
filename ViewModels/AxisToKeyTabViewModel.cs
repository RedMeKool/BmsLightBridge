using System.Collections.ObjectModel;
using BmsLightBridge.Models;
using BmsLightBridge.Services;

namespace BmsLightBridge.ViewModels
{
    public class AxisToKeyTabViewModel : BaseViewModel
    {
        private readonly AppConfiguration _config;
        private readonly AxisToKeyService _service;
        private readonly Action           _saveConfig;
        private readonly Action<Action>   _dispatchToUi;

        public ObservableCollection<AxisToKeyBindingViewModel> Bindings           { get; } = new();
        public ObservableCollection<JoystickDeviceViewModel>   AvailableJoysticks { get; } = new();

        public static IReadOnlyList<JoystickAxis> AllAxes { get; } =
            Enum.GetValues<JoystickAxis>().ToList();

        public RelayCommand AddBindingCommand                                  { get; }
        public RelayCommand<AxisToKeyBindingViewModel> DeleteBindingCommand    { get; }

        public AxisToKeyTabViewModel(AppConfiguration config, AxisToKeyService service,
                                     Action saveConfig, Action<Action> dispatchToUi)
        {
            _config       = config;
            _service      = service;
            _saveConfig   = saveConfig;
            _dispatchToUi = dispatchToUi;

            AddBindingCommand    = new RelayCommand(_ => AddBinding());
            DeleteBindingCommand = new RelayCommand<AxisToKeyBindingViewModel>(vm =>
            {
                if (vm == null) return;
                _config.AxisToKeyBindings.Remove(vm.Model);
                Bindings.Remove(vm);
                PushToService();
                _saveConfig();
            });

            foreach (var m in _config.AxisToKeyBindings)
                Bindings.Add(CreateVm(m));
        }

        public void RefreshJoysticks(IEnumerable<JoystickDeviceInfo> devices)
        {
            AvailableJoysticks.Clear();
            foreach (var d in devices)
                AvailableJoysticks.Add(new JoystickDeviceViewModel { InstanceGuid = d.InstanceGuid, Name = d.Name });
            foreach (var vm in Bindings)
            {
                vm.AvailableJoysticks = AvailableJoysticks;
                vm.RestoreSelection();
            }
        }

        public void PushToService() => _service.UpdateBindings(_config.AxisToKeyBindings);

        private void AddBinding()
        {
            var model = new AxisToKeyBinding { Label = "New Binding" };
            _config.AxisToKeyBindings.Add(model);
            var vm = CreateVm(model);
            vm.AvailableJoysticks = AvailableJoysticks;
            Bindings.Add(vm);
            _saveConfig();
        }

        private AxisToKeyBindingViewModel CreateVm(AxisToKeyBinding model)
        {
            var vm = new AxisToKeyBindingViewModel(model, () => { PushToService(); _saveConfig(); })
            {
                KeyService        = _service,
                DispatchToUi      = _dispatchToUi,
                AvailableJoysticks = AvailableJoysticks
            };
            vm.RestoreSelection();
            return vm;
        }
    }
}
