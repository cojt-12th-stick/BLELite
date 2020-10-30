using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace UnityMemoryMappedFile
{
    public class PipeCommands
    {
        public static Type GetCommandType(string commandStr)
        {
            var commands = typeof(PipeCommands).GetNestedTypes(System.Reflection.BindingFlags.Public);
            foreach (var command in commands)
            {
                if (command.Name == commandStr) return command;
            }
            return null;
        }

        public class SendMessage
        {
            public string Message { get; set; }
        }

        public class Initialize
        {
            public bool asCentral { get; set; }
            public bool asPeripheral { get; set; }
        }

        public class DeInitialize
        {
        }

        public class ScanForPeripheralsWithServices
        {
            public string serviceUUIDsString { get; set; }
            public bool allowDuplicates { get; set; }
            public bool rssiOnly { get; set; }
            public bool clearPeripheralList { get; set; }
        }

        public class RetrieveListOfPeripheralsWithServices
        {
            public string serviceUUIDsString { get; set; }
        }

        public class StopScan { }

        public class StopBeaconScan { }

        public class DisconnectAll { }

        public class ConnectToPeripheral
        {
            public string name { get; set; }
        }

        public class DisconnectPeripheral
        {
            public string name { get; set; }
        }

        public class ReadCharacteristic
        {
            public string name { get; set; }
            public string service { get; set; }
            public string characteristic { get; set; }
        }

        public class WriteCharacteristic
        {
            public string name { get; set; }
            public string service { get; set; }
            public string characteristic { get; set; }
            public byte[] data { get; set; }
            public bool withResponse { get; set; }
        }

        public class SubscribeCharacteristic
        {
            public string name { get; set; }
            public string service { get; set; }
            public string characteristic { get; set; }
        }

        public class UnSubscribeCharacteristic
        {
            public string name { get; set; }
            public string service { get; set; }
            public string characteristic { get; set; }
        }

        public class PeripheralName
        {
            public string newName { get; set; }
        }

        public class CreateService
        {
            public string uuid { get; set; }
            public bool primary { get; set; }
        }

        public class RemoveService
        {
            public string uuid { get; set; }
        }

        public class RemoveServices { }

        public class CreateCharacteristic
        {
            public string uuid { get; set; }
            public int properties { get; set; }
            public int permissions { get; set; }
            public byte[] data { get; set; }
        }

        public class RemoveCharacteristic
        {
            public string uuid { get; set; }
        }

        public class RemoveCharacteristics { }

        public class StartAdvertising { }

        public class StopAdvertising { }

        public class UpdateCharacteristicValue
        {
            public string uuid { get; set; }
            public byte[] data { get; set; }
        }
    }
}
