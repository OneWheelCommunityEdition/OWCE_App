﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Java.Util;
using OWCE.DependencyInterfaces;
using OWCE.Droid.Extensions;
using Xamarin.Forms;

[assembly: Dependency(typeof(OWCE.Droid.DependencyImplementations.OWBLE))]

namespace OWCE.Droid.DependencyImplementations
{
    public class OWBLE : IOWBLE
    {
        private enum OWBLE_QueueItemOperationType
        {
            Read,
            Write,
            Subscribe,
            Unsubscribe,
        }

        private Queue<OWBLE_QueueItem> _gattOperationQueue = new Queue<OWBLE_QueueItem>();
        private bool _gattOperationQueueProcessing = false;

        private class OWBLE_QueueItem
        {
            public OWBLE_QueueItemOperationType OperationType { get; private set; }
            public BluetoothGattCharacteristic Characteristic { get; private set; }
            public byte[] Data { get; set; }

            public OWBLE_QueueItem(BluetoothGattCharacteristic characteristic, OWBLE_QueueItemOperationType operationType)
            {
                Characteristic = characteristic;
                OperationType = OperationType;
            }
        }

        private class OWBLE_ScanCallback : ScanCallback
        {
            private OWBLE _owble;

            public OWBLE_ScanCallback(OWBLE owble)
            {
                _owble = owble;
            }

            public override void OnBatchScanResults(IList<ScanResult> results)
            {
                Console.WriteLine("OnBatchScanResults");
                base.OnBatchScanResults(results);
            }

            public override void OnScanResult(ScanCallbackType callbackType, ScanResult result)
            {
                Console.WriteLine("OnScanResult");
                
                OWBoard board = new OWBoard()
                {
                    ID = result.Device.Address,
                    Name = result.Device.Name ?? "Onewheel",
                    IsAvailable = true,
                    NativePeripheral = result.Device,
                };

                _owble.BoardDiscovered?.Invoke(board);
            }

            public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode)
            {
                Console.WriteLine("OnScanFailed");
                base.OnScanFailed(errorCode);
            }
        }
        
        private class OWBLE_LeScanCallback : Java.Lang.Object, BluetoothAdapter.ILeScanCallback
        {
            private OWBLE _owble;

            public OWBLE_LeScanCallback(OWBLE owble)
            {
                _owble = owble;
            }

            public void OnLeScan(BluetoothDevice device, int rssi, byte[] scanRecord)
            {
                Console.WriteLine("OnLeScan");
                
                OWBoard board = new OWBoard()
                {
                    ID = device.JniIdentityHashCode.ToString(),
                    Name = device.Name ?? "Onewheel",
                    IsAvailable = true,
                    NativePeripheral = device,
                };

                _owble.BoardDiscovered?.Invoke(board);
            }
        }

        private class OWBLE_BluetoothGattCallback : BluetoothGattCallback
        {
            private OWBLE _owble;

            public OWBLE_BluetoothGattCallback(OWBLE owble)
            {
                _owble = owble;
            }
                        
            public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
            {
                Console.WriteLine("OnServicesDiscovered: " + status);
                _owble.OnServicesDiscovered(gatt, status);
            }

            public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
            {
                Console.WriteLine("OnConnectionStateChange: " + status);
                _owble.OnConnectionStateChange(gatt, status, newState);
            }

            public override void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
            {
                Console.WriteLine("OnCharacteristicRead: " + characteristic.Uuid);
                _owble.OnCharacteristicRead(gatt, characteristic, status);
            }

            public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
            {
                Console.WriteLine("OnCharacteristicWrite: " + characteristic.Uuid);
                _owble.OnCharacteristicWrite(gatt, characteristic, status);
            }

            public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
            {
                Console.WriteLine("OnCharacteristicChanged: " + characteristic.Uuid);
                _owble.OnCharacteristicChanged(gatt, characteristic);
            }
        }


        Dictionary<UUID, BluetoothGattCharacteristic> _characteristics = new Dictionary<UUID, BluetoothGattCharacteristic>();
        Dictionary<UUID, TaskCompletionSource<byte[]>> _readQueue = new Dictionary<UUID, TaskCompletionSource<byte[]>>();
        Dictionary<UUID, TaskCompletionSource<byte[]>> _writeQueue = new Dictionary<UUID, TaskCompletionSource<byte[]>>();

        private void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
        {
            //BTA_GATTC_CONN_MAX
            //BTA_GATTC_NOTIF_REG_MAX

            var service = gatt.GetService(OWBoard.ServiceUUID.ToUUID());

            if (service == null)
                return;
            
            foreach (var characteristic in service.Characteristics)
            {
                _characteristics.Add(characteristic.Uuid, characteristic);
            }

            if (_connectTaskCompletionSource.Task.IsCanceled == false)
            {
                _connectTaskCompletionSource.SetResult(true);
                BoardConnected?.Invoke(_board);
            }
        }

        /*
        private class OWBLE_BroadcastReceiver : BroadcastReceiver
        {
            private OWBLE _owble;

            public OWBLE_BroadcastReceiver(OWBLE owble)
            {
                _owble = owble;
            }

            public override void OnReceive(Context context, Intent intent)
            {
                Console.WriteLine("OnReceive: " + intent.Action);

                if (BluetoothAdapter.ActionStateChanged.Equals(intent.Action))
                {
                    var stateInt = intent.GetIntExtra(BluetoothAdapter.ExtraState, -1);

                    Console.WriteLine("stateInt: " + stateInt);
                    if (stateInt == -1)
                    {
                        return;
                    }

                    var state = (State)stateInt;
                    var bluetoothState = BluetoothState.Unknown;

                    switch (state)
                    {
                        case State.Connected:
                            bluetoothState = BluetoothState.Connected;
                            break;
                        case State.Connecting:
                            bluetoothState = BluetoothState.Connecting;
                            break;
                        case State.Disconnected:
                            bluetoothState = BluetoothState.Disconnected;
                            break;
                        case State.Disconnecting:
                            bluetoothState = BluetoothState.Disconnecting;
                            break;
                        case State.Off:
                            bluetoothState = BluetoothState.Off;
                            break;
                        case State.On:
                            bluetoothState = BluetoothState.On;
                            break;
                        case State.TurningOff:
                            bluetoothState = BluetoothState.TurningOff;
                            break;
                        case State.TurningOn:
                            bluetoothState = BluetoothState.TurningOn;
                            break;
                    }

                    Xamarin.Essentials.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _owble?.BLEStateChanged?.Invoke(bluetoothState);
                    });
                }
            }
        }
        */


        private bool _isScanning = false;
        private BluetoothAdapter _adapter;
        private BluetoothLeScanner _bleScanner;


        TaskCompletionSource<bool> _connectTaskCompletionSource = null;
        private OWBoard _board = null;

        //private OWBLE_BroadcastReceiver _broadcastReceiver;
        private OWBLE_ScanCallback _scanCallback;
        private OWBLE_LeScanCallback _leScanCallback;
        private OWBLE_BluetoothGattCallback _gattCallback;
        private BluetoothGatt _bluetoothGatt;

        // Moved to be its own property for debugging.
        private BuildVersionCodes _sdkInt = Build.VERSION.SdkInt;

        public OWBLE()
        {
            //_sdkInt = BuildVersionCodes.JellyBeanMr1;

            /*
            _broadcastReceiver = new OWBLE_BroadcastReceiver(this);
            IntentFilter filter = new IntentFilter(BluetoothAdapter.ActionStateChanged);
            Plugin.CurrentActivity.CrossCurrentActivity.Current.AppContext.RegisterReceiver(_broadcastReceiver, filter);
            */

            BluetoothManager manager = Plugin.CurrentActivity.CrossCurrentActivity.Current.Activity.GetSystemService(Context.BluetoothService) as BluetoothManager;
            _adapter = manager.Adapter;
        }

        public bool IsEnabled()
        {
            return !(_adapter == null || !_adapter.IsEnabled);
        }

        public void RequestPermission()
        {
            // TODO: Request location.

            // Ensures Bluetooth is available on the device and it is enabled. If not,
            // displays a dialog requesting user permission to enable Bluetooth.
            if (_adapter == null || !_adapter.IsEnabled)
            {
                Intent enableBtIntent = new Intent(BluetoothAdapter.ActionRequestEnable);
                Plugin.CurrentActivity.CrossCurrentActivity.Current.Activity.StartActivityForResult(enableBtIntent, MainActivity.REQUEST_ENABLE_BT);
            }
        }
        


        private void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
        {
            if (_connectTaskCompletionSource.Task.IsCanceled)
                return;

            if (status == GattStatus.Success)
            {
                gatt.DiscoverServices();
            }
            else
            {
                _connectTaskCompletionSource.SetResult(false);
            }
        }
               
        private void ProcessQueue()
        {
            Console.WriteLine($"ProcessQueue: {_gattOperationQueue.Count}");
            if (_gattOperationQueue.Count == 0)
            {
                _gattOperationQueueProcessing = false;
                return;
            }

            if (_gattOperationQueueProcessing)
                return;

            _gattOperationQueueProcessing = true;

            var item = _gattOperationQueue.Dequeue();
            switch (item.OperationType)
            {
                case OWBLE_QueueItemOperationType.Read:
                    bool didRead = _bluetoothGatt.ReadCharacteristic(item.Characteristic);
                    if (didRead == false)
                    {
                        Console.WriteLine($"ERROR: Unable to read {item.Characteristic.Uuid}");
                    }
                    break;
                case OWBLE_QueueItemOperationType.Write:
                    item.Characteristic.SetValue(item.Data);
                    bool didWrite = _bluetoothGatt.WriteCharacteristic(item.Characteristic);
                    if (didWrite == false)
                    {
                        Console.WriteLine($"ERROR: Unable to write {item.Characteristic.Uuid}");
                    }
                    break;
                case OWBLE_QueueItemOperationType.Subscribe:
                    bool didSubscribe = _bluetoothGatt.SetCharacteristicNotification(item.Characteristic, true);
                    if (didSubscribe == false)
                    {
                        Console.WriteLine($"ERROR: Unable to subscribe {item.Characteristic.Uuid}");
                    }

                    /* This is also sometimes required (e.g. for heart rate monitors) to enable notifications/indications
        // see: https://developer.bluetooth.org/gatt/descriptors/Pages/DescriptorViewer.aspx?u=org.bluetooth.descriptor.gatt.client_characteristic_configuration.xml

                    BluetoothGattDescriptor descriptor = ch.getDescriptor(UUID.fromString("00002902-0000-1000-8000-00805f9b34fb"));
        if(descriptor != null) {
            byte[] val = enabled ? BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE : BluetoothGattDescriptor.DISABLE_NOTIFICATION_VALUE;
            descriptor.setValue(val);
            mBluetoothGatt.writeDescriptor(descriptor);
        }
        */
                    break;
                case OWBLE_QueueItemOperationType.Unsubscribe:
                    bool didUnsubscribe = _bluetoothGatt.SetCharacteristicNotification(item.Characteristic, false);
                    if (didUnsubscribe == false)
                    {
                        Console.WriteLine($"ERROR: Unable to unsubscribe {item.Characteristic.Uuid}");
                    }
                    break;
            }
        }

        private void OnCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            var uuid = characteristic.Uuid;

            if (_readQueue.ContainsKey(uuid))
            {
                var readItem = _readQueue[uuid];
                _readQueue.Remove(uuid);
                readItem.SetResult(characteristic.GetValue());
            }

            _gattOperationQueueProcessing = false;
            ProcessQueue();
        }


        private void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, GattStatus status)
        {
            var uuid = characteristic.Uuid;

            if (_writeQueue.ContainsKey(uuid))
            {
                var writeItem = _writeQueue[uuid];
                _writeQueue.Remove(uuid);
                writeItem.SetResult(characteristic.GetValue());
            }

            _gattOperationQueueProcessing = false;
            ProcessQueue();
        }


        private void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic)
        {
            Console.WriteLine($"OnCharacteristicChanged: {characteristic.Uuid}, {characteristic.GetValue()}");

        }


        #region IOWBLE
        public Action<BluetoothState> BLEStateChanged { get; set; }
        public Action<OWBoard> BoardDiscovered { get; set; }
        public Action<OWBoard> BoardConnected { get; set; }

        public Task<bool> Connect(OWBoard board)
        {
            _board = board;

            _connectTaskCompletionSource = new TaskCompletionSource<bool>();

            if (board.NativePeripheral is BluetoothDevice device)
            {
                _gattCallback = new OWBLE_BluetoothGattCallback(this);
                _bluetoothGatt = device.ConnectGatt(Plugin.CurrentActivity.CrossCurrentActivity.Current.Activity, false, _gattCallback);
            }

            return _connectTaskCompletionSource.Task;
        }

        public Task Disconnect()
        {
            if (_connectTaskCompletionSource != null && _connectTaskCompletionSource.Task.IsCanceled == false)
            {
                _connectTaskCompletionSource.SetCanceled();
            }

            // TODO: Handle is connecting.
            if (_bluetoothGatt != null)
            {
                _bluetoothGatt.Disconnect();
            }

            _board = null;

            return Task.CompletedTask;
        }
        public async Task StartScanning(int timeout = 15)
        {
            if (_isScanning)
                return;

            _isScanning = true;

            // TODO: Handle power on state.

            if (_sdkInt >= BuildVersionCodes.Lollipop) // 21
            {
                _bleScanner = _adapter.BluetoothLeScanner;
                _scanCallback = new OWBLE_ScanCallback(this);
                var scanFilters = new List<ScanFilter>();
                var scanSettingsBuilder = new ScanSettings.Builder();

                var scanFilterBuilder = new ScanFilter.Builder();
                scanFilterBuilder.SetServiceUuid(OWBoard.ServiceUUID.ToParcelUuid());
                scanFilters.Add(scanFilterBuilder.Build());
                _bleScanner.StartScan(scanFilters, scanSettingsBuilder.Build(), _scanCallback);
            }
            else if (_sdkInt >= BuildVersionCodes.JellyBeanMr2) // 18
            {
                _leScanCallback = new OWBLE_LeScanCallback(this);
#pragma warning disable 0618
                _adapter.StartLeScan(new Java.Util.UUID[] { OWBoard.ServiceUUID.ToUUID() }, _leScanCallback);
#pragma warning restore 0618
            }
            else
            {
                throw new NotImplementedException("Can't run bluetooth scans on device lower than Android 4.3");
            }

            await Task.Delay(timeout * 1000);

            StopScanning();
        }

        public void StopScanning()
        {
            if (_isScanning == false)
                return;


            if (_sdkInt >= BuildVersionCodes.Lollipop) // 21
            {
                _bleScanner.StopScan(_scanCallback);
            }
            else
            {
#pragma warning disable 0618
                _adapter.StopLeScan(_leScanCallback);
#pragma warning restore 0618
            }

            _isScanning = false;
        }


        public Task<byte[]> ReadValue(string characteristicGuid, bool important = false)
        {
            Console.WriteLine($"ReadValue: {characteristicGuid}");

            if (_bluetoothGatt == null)
                return null;

            var uuid = UUID.FromString(characteristicGuid);

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            // Already awaiting it.
            if (_readQueue.ContainsKey(uuid))
            {
                return _readQueue[uuid].Task;
            }

            var taskCompletionSource = new TaskCompletionSource<byte[]>();

            if (important)
            {
                // TODO: Put this at the start of the queue.
                _readQueue.Add(uuid, taskCompletionSource);
            }
            else
            {
                _readQueue.Add(uuid, taskCompletionSource);
            }

            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Read));

            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task<byte[]> WriteValue(string characteristicGuid, byte[] data, bool important = false)
        {
            Console.WriteLine($"WriteValue: {characteristicGuid}");
            if (_bluetoothGatt == null)
                return null;

            if (data.Length > 20)
            {
                // TODO: Error, some Android BLE devices do not handle > 20byte packets well.
                return null;
            }

            var uuid = UUID.FromString(characteristicGuid);

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            // TODO: Handle this.
            /*
            if (_readQueue.ContainsKey(uuid))
            {
                return _readQueue[uuid].Task;
            }
            */

            var taskCompletionSource = new TaskCompletionSource<byte[]>();

            if (important)
            {
                // TODO: Put this at the start of the queue.
                _writeQueue.Add(uuid, taskCompletionSource);
            }
            else
            {
                _writeQueue.Add(uuid, taskCompletionSource);
            }


            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Write));

            ProcessQueue();

            return taskCompletionSource.Task;
        }

        public Task SubscribeValue(string characteristicGuid, bool important = false)
        {
            Console.WriteLine($"SubscribeValue: {characteristicGuid}");
            if (_bluetoothGatt == null)
                return null;

            var uuid = UUID.FromString(characteristicGuid);

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }


            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Subscribe));

            ProcessQueue();

            return Task.CompletedTask;
        }

        public Task UnsubscribeValue(string characteristicGuid, bool important = false)
        {
            Console.WriteLine($"UnsubscribeValue: {characteristicGuid}");
            if (_bluetoothGatt == null)
                return null;

            var uuid = UUID.FromString(characteristicGuid);

            // TODO: Check for connected devices?
            if (_characteristics.ContainsKey(uuid) == false)
            {
                // TODO Error?
                return null;
            }

            _gattOperationQueue.Enqueue(new OWBLE_QueueItem(_characteristics[uuid], OWBLE_QueueItemOperationType.Unsubscribe));

            ProcessQueue();

            return Task.CompletedTask;
        }
        #endregion
    }
}
