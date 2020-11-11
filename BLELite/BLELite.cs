using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityMemoryMappedFile;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BLELite
{
    public class BLELite
    {
        public static BLELite Instance { get; private set; }

        Dictionary<string, string> names;
        Dictionary<string, BluetoothLEDevice> connectedDevices;
        Dictionary<(string name, Guid service), GattDeviceService> connectedServices;
        Dictionary<(string name, Guid service, Guid characteristic), GattCharacteristic> connectedCharacteristics;


        string peripheralName;
        Dictionary<string, GattServiceProvider> services;
        class CharacteristicOption
        {
            public string uuid;
            public int properties;
            public int permissions;
            public byte[] data;
        }
        Dictionary<string, CharacteristicOption> characteristics;

        BluetoothLEAdvertisementWatcher watcher;

        bool rssiOnly;
        private bool allowDuplicates;
        private static MemoryMappedFileClient client;

        public static void Initialize(bool asCentral, bool asPeripheral)
        {
            if (Instance != null)
                Instance.watcher.Received -= OnDiscoverPeripheral;

            Instance = new BLELite();

            Instance.names = new Dictionary<string, string>();
            Instance.connectedDevices = new Dictionary<string, BluetoothLEDevice>();
            Instance.connectedServices = new Dictionary<(string, Guid), GattDeviceService>();
            Instance.connectedCharacteristics = new Dictionary<(string, Guid, Guid), GattCharacteristic>();

            Instance.services = new Dictionary<string, GattServiceProvider>();
            Instance.characteristics = new Dictionary<string, CharacteristicOption>();

            Instance.watcher = new BluetoothLEAdvertisementWatcher();
            Instance.watcher.Received += OnDiscoverPeripheral;

            SendMessage("Initialized");
        }

        public static void DeInitialize()
        {
            if (Instance == null)
                return;

            RemoveCharacteristics();
            RemoveServices();

            Instance.watcher.Received -= OnDiscoverPeripheral;
            SendMessage("DeInitialized");
            Instance = null;
        }

        public static void ScanForPeripheralsWithServices(string serviceUUIDsString, bool allowDuplicates, bool rssiOnly, bool clearPeripheralList)
        {
            if (Instance == null) return;

            Instance.rssiOnly = rssiOnly;
            Instance.allowDuplicates = allowDuplicates;

            if (clearPeripheralList)
                Instance.names.Clear();

            if (serviceUUIDsString != null)
            {
                if (Instance.watcher.AdvertisementFilter != null)
                    Instance.watcher.AdvertisementFilter = new BluetoothLEAdvertisementFilter();

                if (Instance.watcher.AdvertisementFilter.Advertisement != null)
                    Instance.watcher.AdvertisementFilter.Advertisement = new BluetoothLEAdvertisement();

                Instance.watcher.AdvertisementFilter.Advertisement.ServiceUuids.Clear();
                foreach (var uuid in serviceUUIDsString.Split('|'))
                {
                    Instance.watcher.AdvertisementFilter.Advertisement.ServiceUuids.Add(new Guid(uuid));
                }
            }

            Instance.watcher.Start();
        }

        public static async void RetrieveListOfPeripheralsWithServices(string serviceUUIDsString)
        {
            if (Instance != null)
            {
                List<Guid> actualUUIDs = null;

                if (serviceUUIDsString != null)
                {
                    var serviceUUIDs = serviceUUIDsString.Split('|');

                    if (serviceUUIDs.Length > 0)
                    {
                        actualUUIDs = new List<Guid>();

                        foreach (string uuid in serviceUUIDs)
                            actualUUIDs.Add(new Guid(uuid));
                    }
                }

                Instance.names?.Clear();
                string selector = "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\"";

                if (actualUUIDs != null)
                    foreach (var uuid in actualUUIDs)
                    {
                        selector += " AND " + GattDeviceService.GetDeviceSelectorFromUuid(uuid);
                    }

                var devices = await DeviceInformation.FindAllAsync(selector, null, DeviceInformationKind.AssociationEndpoint);
                if (devices != null)
                {
                    foreach (var device in devices)
                    {
                        if (device != null)
                        {
                            var bluetoothLEDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
                            string identifier = bluetoothLEDevice.BluetoothAddress.ToString();
                            var name = bluetoothLEDevice.Name;

                            var message = $"RetrievedConnectedPeripheral~{identifier}~{name}";
                            SendMessage(message);

                            Instance.names[identifier] = name;
                        }
                    }
                }
            }
        }

        public static void StopScan()
        {
            Instance?.watcher.Stop();
        }

        public static void StopBeaconScan()
        {

        }

        public static void DisconnectAll()
        {
            if (Instance == null)
                return;

            foreach (var item in Instance.connectedDevices)
            {
                DisconnectPeripheral(item.Key);
            }
        }

        public static async void ConnectToPeripheral(string name)
        {
            if (Instance == null)
                return;

            if (Instance.names.ContainsKey(name))
            {
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(Convert.ToUInt64(name));
                if (device != null)
                {
                    device.ConnectionStatusChanged += OnConnectionStatusChanged;
                    await Connected(device);

                    Instance.connectedDevices[name] = device;
                }
            }
        }

        private static void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            switch (sender.ConnectionStatus)
            {
                case BluetoothConnectionStatus.Disconnected:
                    SendMessage($"DisconnectedPeripheral~{sender.BluetoothAddress}");
                    break;
                case BluetoothConnectionStatus.Connected:
                    break;
                default:
                    break;
            }
        }

        private static async Task Connected(BluetoothLEDevice device)
        {
            SendMessage($"ConnectedPeripheral~{device.Name}");
            var servicesResult = await device.GetGattServicesAsync();

            if (servicesResult.Status == GattCommunicationStatus.Success)
            {
                foreach (var service in servicesResult.Services)
                {
                    SendMessage($"DiscoveredService~{device.Name}~{service.Uuid}");

                    var characteristicsResult = await service.GetCharacteristicsAsync();

                    if (characteristicsResult.Status == GattCommunicationStatus.Success)
                    {
                        foreach (var characteristic in characteristicsResult.Characteristics)
                        {
                            SendMessage($"DiscoveredCharacteristic~{device.Name}~{service.Uuid}~{characteristic.Uuid}");
                        }
                    }
                    else
                    {
                        SendMessage($"Error~{characteristicsResult.Status}");
                    }
                    service.Dispose();
                }
            }
            else
            {
                SendMessage($"Error~{servicesResult.Status}");
            }
        }

        public static void DisconnectPeripheral(string name)
        {
            if (Instance == null)
                return;

            if (Instance.connectedDevices.ContainsKey(name))
            {
                Instance.connectedDevices[name].ConnectionStatusChanged -= OnConnectionStatusChanged;
                Instance.connectedDevices[name].Dispose();
                Instance.connectedDevices.Remove(name);

                SendMessage($"DisconnectedPeripheral~{name}");

                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public static async void ReadCharacteristic(string name, string service, string characteristic)
        {
            if (name != null && Guid.TryParse(service, out var serviceUUID) && Guid.TryParse(characteristic, out var characteristicUUID) && Instance?.names != null)
            {
                if (Instance.connectedDevices.ContainsKey(name))
                {
                    GattDeviceService gattService = null;
                    GattCharacteristic gattCharacteristic = null;
                    bool isSubscribed = false;

                    if (Instance.connectedServices.ContainsKey((name, serviceUUID)))
                    {
                        gattService = Instance.connectedServices[(name, serviceUUID)];
                        isSubscribed = true;
                    }
                    if (Instance.connectedCharacteristics.ContainsKey((name, serviceUUID, characteristicUUID)))
                        gattCharacteristic = Instance.connectedCharacteristics[(name, serviceUUID, characteristicUUID)];

                    if (gattService == null)
                    {
                        var peripheral = Instance.connectedDevices[name];
                        var servicesResult = await peripheral.GetGattServicesAsync();
                        if (servicesResult.Status != GattCommunicationStatus.Success)
                        {
                            SendMessage($"Error~{servicesResult.Status}");
                            return;
                        }
                        gattService = servicesResult.Services.FirstOrDefault(s => s.Uuid == serviceUUID);
                        if (gattService == null)
                            return;
                    }

                    if (gattCharacteristic == null)
                    {
                        var characteristicsResult = await gattService.GetCharacteristicsAsync();
                        if (characteristicsResult.Status != GattCommunicationStatus.Success)
                        {
                            SendMessage($"Error~{characteristicsResult.Status}");
                            return;
                        }
                        gattCharacteristic = characteristicsResult?.Characteristics.FirstOrDefault(c => c.Uuid == characteristicUUID);
                    }

                    if (gattCharacteristic != null)
                    {
                        var result = await gattCharacteristic.ReadValueAsync();
                        if (result.Status == GattCommunicationStatus.Success)
                        {
                            byte[] readBytes = new byte[result.Value.Length];
                            using (DataReader reader = DataReader.FromBuffer(result.Value))
                            {
                                reader.ReadBytes(readBytes);
                                SendMessage($"DidUpdateValueForCharacteristic~{gattService.Device.BluetoothAddress}~{characteristicUUID}~{Convert.ToBase64String(readBytes)}");
                            }
                        }
                        else
                        {
                            SendMessage($"Error~{result.Status}");
                        }
                    }
                    if (!isSubscribed)
                        gattService.Dispose();
                }
            }
        }

        public static async void WriteCharacteristic(string name, string service, string characteristic, byte[] data, bool withResponse)
        {
            if (name != null && Guid.TryParse(service, out var serviceUUID) && Guid.TryParse(characteristic, out var characteristicUUID) && Instance?.names != null && data != null)
            {
                if (Instance.connectedDevices.ContainsKey(name))
                {
                    GattDeviceService gattService = null;
                    GattCharacteristic gattCharacteristic = null;
                    bool isSubscribed = false;

                    if (Instance.connectedServices.ContainsKey((name, serviceUUID)))
                    {
                        gattService = Instance.connectedServices[(name, serviceUUID)];
                        isSubscribed = true;
                    }
                    if (Instance.connectedCharacteristics.ContainsKey((name, serviceUUID, characteristicUUID)))
                        gattCharacteristic = Instance.connectedCharacteristics[(name, serviceUUID, characteristicUUID)];

                    if (gattService == null)
                    {
                        var peripheral = Instance.connectedDevices[name];
                        var servicesResult = await peripheral.GetGattServicesAsync();
                        if (servicesResult.Status != GattCommunicationStatus.Success)
                        {
                            SendMessage($"Error~{servicesResult.Status}");
                            return;
                        }
                        gattService = servicesResult.Services.FirstOrDefault(s => s.Uuid == serviceUUID);
                        if (gattService == null)
                            return;
                    }

                    if (gattCharacteristic == null)
                    {
                        var characteristicsResult = await gattService.GetCharacteristicsAsync();
                        if (characteristicsResult.Status != GattCommunicationStatus.Success)
                        {
                            SendMessage($"Error~{characteristicsResult.Status}");
                            return;
                        }
                        gattCharacteristic = characteristicsResult?.Characteristics.FirstOrDefault(c => c.Uuid == characteristicUUID);
                    }

                    if (gattCharacteristic != null)
                    {
                        GattWriteOption option = GattWriteOption.WriteWithoutResponse;
                        if (withResponse)
                            option = GattWriteOption.WriteWithResponse;

                        using (DataWriter writer = new DataWriter())
                        {
                            writer.WriteBytes(data);
                            var communicationStatus = await gattCharacteristic.WriteValueAsync(writer.DetachBuffer(), option);

                            if (communicationStatus == GattCommunicationStatus.Success)
                            {
                                SendMessage($"DidWriteCharacteristic~{gattCharacteristic.Uuid}");
                            }
                            else
                            {
                                SendMessage($"Error~{communicationStatus}");
                            }
                        }
                    }
                    if (!isSubscribed)
                        gattService.Dispose();
                }
            }
        }

        public static async void SubscribeCharacteristic(string name, string service, string characteristic)
        {
            if (name != null && Guid.TryParse(service, out var serviceUUID) && Guid.TryParse(characteristic, out var characteristicUUID) && Instance?.names != null)
            {
                if (Instance.connectedDevices.ContainsKey(name))
                {
                    GattDeviceService gattService = null;
                    GattCharacteristic gattCharacteristic = null;

                    if (Instance.connectedServices.ContainsKey((name, serviceUUID)))
                        gattService = Instance.connectedServices[(name, serviceUUID)];
                    if (Instance.connectedCharacteristics.ContainsKey((name, serviceUUID, characteristicUUID)))
                        gattCharacteristic = Instance.connectedCharacteristics[(name, serviceUUID, characteristicUUID)];

                    if (gattCharacteristic != null) return;

                    if (gattService == null)
                    {
                        var peripheral = Instance.connectedDevices[name];
                        var servicesResult = await peripheral.GetGattServicesAsync();
                        if (servicesResult.Status != GattCommunicationStatus.Success)
                        {
                            SendMessage($"Error~{servicesResult.Status}");
                            return;
                        }
                        gattService = servicesResult.Services.FirstOrDefault(s => s.Uuid == serviceUUID);
                        if (gattService == null)
                            return;
                    }

                    if (gattCharacteristic == null)
                    {
                        var characteristicsResult = await gattService.GetCharacteristicsAsync();
                        if (characteristicsResult.Status != GattCommunicationStatus.Success)
                        {
                            SendMessage($"Error~{characteristicsResult.Status}");
                            return;
                        }
                        gattCharacteristic = characteristicsResult?.Characteristics.FirstOrDefault(c => c.Uuid == characteristicUUID);
                    }

                    if (gattCharacteristic != null)
                    {
                        var result = await gattCharacteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

                        if (result.Status == GattCommunicationStatus.Success)
                        {
                            gattCharacteristic.ValueChanged += OnValueChanged;
                            Instance.connectedServices[(name, serviceUUID)] = gattService;
                            Instance.connectedCharacteristics[(name, serviceUUID, characteristicUUID)] = gattCharacteristic;
                            SendMessage($"DidUpdateNotificationStateForCharacteristic~{name}~{gattCharacteristic.Uuid}");
                        }
                        else
                        {
                            SendMessage($"Error~{result.Status}");
                        }
                    }
                }
            }
        }

        private static void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var device = sender.Service.Device;

            byte[] readBytes = new byte[args.CharacteristicValue.Length];
            using (DataReader reader = DataReader.FromBuffer(args.CharacteristicValue))
            {
                reader.ReadBytes(readBytes);
                SendMessage($"DidUpdateValueForCharacteristic~{device.BluetoothAddress}~{sender.Uuid}~{Convert.ToBase64String(readBytes)}");
            }
        }

        public static async void UnSubscribeCharacteristic(string name, string service, string characteristic)
        {
            if (name != null && Guid.TryParse(service, out var serviceUUID) && Guid.TryParse(characteristic, out var characteristicUUID) && Instance?.names != null)
            {
                if (Instance.connectedDevices.ContainsKey(name) &&
                    Instance.connectedServices.ContainsKey((name, serviceUUID)) &&
                    Instance.connectedCharacteristics.ContainsKey((name, serviceUUID, characteristicUUID)))
                {
                    try
                    {
                        GattDeviceService gattService = Instance.connectedServices[(name, serviceUUID)];
                        var gattCharacteristic = Instance.connectedCharacteristics[(name, serviceUUID, characteristicUUID)];
                        if (gattCharacteristic != null)
                        {

                            var result = await gattCharacteristic.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                            if (result.Status == GattCommunicationStatus.Success)
                            {
                                gattCharacteristic.ValueChanged -= OnValueChanged;
                            }
                            else
                            {
                                SendMessage($"Error~{result.Status}");
                            }
                        }
                        gattService.Dispose();
                    }
                    catch { }
                    if (Instance.connectedCharacteristics.Keys.Count(key => key.service == serviceUUID) == 1)
                        Instance.connectedServices.Remove((name, serviceUUID));
                    Instance.connectedCharacteristics.Remove((name, serviceUUID, characteristicUUID));
                }
            }
        }

        public static void PeripheralName(string newName)
        {
            Instance.peripheralName = newName;
        }

        public static async void CreateService(string uuid, bool primary)
        {
            if (Instance == null)
                return;

            var serviceProviderResult = await GattServiceProvider.CreateAsync(new Guid(uuid));
            if (serviceProviderResult.Error == BluetoothError.Success)
            {
                var service = serviceProviderResult.ServiceProvider.Service;

                foreach (var characteristic in Instance.characteristics)
                {
                    using (DataWriter writer = new DataWriter())
                    {
                        writer.WriteBytes(characteristic.Value.data);
                        await service.CreateCharacteristicAsync(new Guid(characteristic.Key), new GattLocalCharacteristicParameters()
                        {
                            CharacteristicProperties = (GattCharacteristicProperties)characteristic.Value.properties,
                            ReadProtectionLevel = (GattProtectionLevel)characteristic.Value.properties,
                            WriteProtectionLevel = (GattProtectionLevel)characteristic.Value.permissions,
                            StaticValue = writer.DetachBuffer(),
                        });
                    }
                }

                Instance.services[uuid] = serviceProviderResult.ServiceProvider;
            }
        }

        public static void RemoveService(string uuid)
        {
            if (Instance?.services.ContainsKey(uuid) ?? false)
            {
                Instance?.services[uuid].StopAdvertising();
            }
            Instance?.services.Remove(uuid);
        }

        public static void RemoveServices()
        {
            StopAdvertising();
            Instance?.services.Clear();
        }

        public static void CreateCharacteristic(string uuid, int properties, int permissions, byte[] data)
        {
            if (Instance == null)
                return;

            var characteristic = new CharacteristicOption()
            {
                uuid = uuid,
                properties = properties,
                permissions = permissions,
                data = data
            };

            Instance.characteristics[uuid] = characteristic;
        }

        public static void RemoveCharacteristic(string uuid)
        {
            Instance?.characteristics.Remove(uuid);
        }

        public static void RemoveCharacteristics()
        {
            Instance?.characteristics.Clear();
        }

        public static void StartAdvertising()
        {
            if (Instance == null)
                return;

            GattServiceProviderAdvertisingParameters advParameters = new GattServiceProviderAdvertisingParameters
            {
                IsDiscoverable = true,
                IsConnectable = true
            };

            foreach (var service in Instance.services)
            {
                service.Value.StartAdvertising(advParameters);
            }
        }

        public static void StopAdvertising()
        {
            if (Instance == null)
                return;

            foreach (var service in Instance.services)
            {
                service.Value.StopAdvertising();
            }

            SendMessage("StoppedAdvertising");
        }

        public static async void UpdateCharacteristicValue(string uuid, byte[] data)
        {
            if (Instance?.characteristics != null)
            {
                foreach (var characteristic in Instance.services.Values.SelectMany(s => s.Service.Characteristics).Where(c => c.Uuid.ToString() == uuid))
                {
                    using (DataWriter writer = new DataWriter())
                    {
                        writer.WriteBytes(data);
                        await characteristic.NotifyValueAsync(writer.DetachBuffer());
                    }
                }
            }
        }

        private static async void OnDiscoverPeripheral(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            BLELite instance = Instance;
            if (instance != null && instance.names != null && args.Advertisement != null)
            {
                string name = args.Advertisement.LocalName;
                if (name == null || name == "")
                {
                    var device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                    name = device?.Name;
                }

                if (name != null)
                {
                    string identifier = args.BluetoothAddress.ToString();

                    if (instance.allowDuplicates && instance.names.ContainsKey(identifier))
                        return;

                    string message;

                    if (args.Advertisement.DataSections.Any(data => data.DataType == 0xff))
                    {
                        string base64Data = Convert.ToBase64String(args.Advertisement.DataSections.FirstOrDefault(data => data.DataType == 0xff)?.Data.ToArray());
                        message = $"DiscoveredPeripheral~{identifier}~{name}~{args.RawSignalStrengthInDBm}~{base64Data}";
                    }
                    else if (args.RawSignalStrengthInDBm != 0 && instance.rssiOnly)
                    {
                        message = $"DiscoveredPeripheral~{identifier}~{name}~{args.RawSignalStrengthInDBm}~";
                    }
                    else
                    {
                        message = $"DiscoveredPeripheral~{identifier}~{name}";
                    }

                    if (message != null)
                        SendMessage(message);

                    instance.names[identifier] = name;
                }
            }
        }

        public static void Main(string[] args)
        {
            client = new MemoryMappedFileClient();
            client.ReceivedEvent += Client_Received;
            client.Start("BLE");

            while (true)
            {
                Thread.Sleep(10);
            }
        }

        private static void Client_Received(object sender, DataReceivedEventArgs e)
        {
#if DEBUG
            Console.WriteLine("Receive: " + e.Data.ToString());
#endif

            if (e.CommandType == typeof(PipeCommands.Initialize))
            {
                var d = (PipeCommands.Initialize)e.Data;
                Initialize(d.asCentral, d.asPeripheral);
            }
            else if (e.CommandType == typeof(PipeCommands.DeInitialize))
            {
                var d = (PipeCommands.DeInitialize)e.Data;
                DeInitialize();
            }
            else if (e.CommandType == typeof(PipeCommands.ScanForPeripheralsWithServices))
            {
                var d = (PipeCommands.ScanForPeripheralsWithServices)e.Data;
                ScanForPeripheralsWithServices(d.serviceUUIDsString, d.allowDuplicates, d.rssiOnly, d.clearPeripheralList);
            }
            else if (e.CommandType == typeof(PipeCommands.RetrieveListOfPeripheralsWithServices))
            {
                var d = (PipeCommands.RetrieveListOfPeripheralsWithServices)e.Data;
                RetrieveListOfPeripheralsWithServices(d.serviceUUIDsString);
            }
            else if (e.CommandType == typeof(PipeCommands.StopScan))
            {
                var d = (PipeCommands.StopScan)e.Data;
                StopScan();
            }
            else if (e.CommandType == typeof(PipeCommands.StopBeaconScan))
            {
                var d = (PipeCommands.StopBeaconScan)e.Data;
                StopBeaconScan();
            }
            else if (e.CommandType == typeof(PipeCommands.DisconnectAll))
            {
                var d = (PipeCommands.DisconnectAll)e.Data;
                DisconnectAll();
            }
            else if (e.CommandType == typeof(PipeCommands.ConnectToPeripheral))
            {
                var d = (PipeCommands.ConnectToPeripheral)e.Data;
                ConnectToPeripheral(d.name);
            }
            else if (e.CommandType == typeof(PipeCommands.DisconnectPeripheral))
            {
                var d = (PipeCommands.DisconnectPeripheral)e.Data;
                DisconnectPeripheral(d.name);
            }
            else if (e.CommandType == typeof(PipeCommands.ReadCharacteristic))
            {
                var d = (PipeCommands.ReadCharacteristic)e.Data;
                ReadCharacteristic(d.name, d.service, d.characteristic);
            }
            else if (e.CommandType == typeof(PipeCommands.WriteCharacteristic))
            {
                var d = (PipeCommands.WriteCharacteristic)e.Data;
                WriteCharacteristic(d.name, d.service, d.characteristic, d.data, d.withResponse);
            }
            else if (e.CommandType == typeof(PipeCommands.SubscribeCharacteristic))
            {
                var d = (PipeCommands.SubscribeCharacteristic)e.Data;
                SubscribeCharacteristic(d.name, d.service, d.characteristic);
            }
            else if (e.CommandType == typeof(PipeCommands.UnSubscribeCharacteristic))
            {
                var d = (PipeCommands.UnSubscribeCharacteristic)e.Data;
                UnSubscribeCharacteristic(d.name, d.service, d.characteristic);
            }
            else if (e.CommandType == typeof(PipeCommands.PeripheralName))
            {
                var d = (PipeCommands.PeripheralName)e.Data;
                PeripheralName(d.newName);
            }
            else if (e.CommandType == typeof(PipeCommands.CreateService))
            {
                var d = (PipeCommands.CreateService)e.Data;
                CreateService(d.uuid, d.primary);
            }
            else if (e.CommandType == typeof(PipeCommands.RemoveService))
            {
                var d = (PipeCommands.RemoveService)e.Data;
                RemoveService(d.uuid);
            }
            else if (e.CommandType == typeof(PipeCommands.RemoveServices))
            {
                var d = (PipeCommands.RemoveServices)e.Data;
                RemoveServices();
            }
            else if (e.CommandType == typeof(PipeCommands.CreateCharacteristic))
            {
                var d = (PipeCommands.CreateCharacteristic)e.Data;
                CreateCharacteristic(d.uuid, d.properties, d.permissions, d.data);
            }
            else if (e.CommandType == typeof(PipeCommands.RemoveCharacteristic))
            {
                var d = (PipeCommands.RemoveCharacteristic)e.Data;
                RemoveCharacteristic(d.uuid);
            }
            else if (e.CommandType == typeof(PipeCommands.RemoveCharacteristics))
            {
                var d = (PipeCommands.RemoveCharacteristics)e.Data;
                RemoveCharacteristics();
            }
            else if (e.CommandType == typeof(PipeCommands.StartAdvertising))
            {
                var d = (PipeCommands.StartAdvertising)e.Data;
                StartAdvertising();
            }
            else if (e.CommandType == typeof(PipeCommands.StopAdvertising))
            {
                var d = (PipeCommands.StopAdvertising)e.Data;
                StopAdvertising();
            }
            else if (e.CommandType == typeof(PipeCommands.UpdateCharacteristicValue))
            {
                var d = (PipeCommands.UpdateCharacteristicValue)e.Data;
                UpdateCharacteristicValue(d.uuid, d.data);
            }
        }

        private static void SendMessage(string message)
        {
            client?.SendCommand(new PipeCommands.SendMessage() { Message = message });
#if DEBUG
            Console.WriteLine("Send Message: " + message);
#endif
        }
    }
}
