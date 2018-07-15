using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ColorChord.NET
{
    public class WASAPI
    {
        public void RecordStream(object outputStream)
        {

        }

        public static IList<AudioDevice> GetAllDevices()
        {
            List<AudioDevice> DeviceList = new List<AudioDevice>();
            IMMDeviceEnumerator DeviceEnumerator = null;
            try { DeviceEnumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator()); }
            catch { }
            if (DeviceEnumerator == null) { return DeviceList; }

            DeviceEnumerator.EnumAudioEndpoints(EDataFlow.eAll, AudioDeviceState.MaskAll, out IMMDeviceCollection DeviceCollection);
            if (DeviceCollection == null) { return DeviceList; }

            DeviceCollection.GetCount(out int DeviceCount);
            for (int i = 0; i < DeviceCount; i++)
            {
                DeviceCollection.Item(i, out IMMDevice Device);
                if (Device != null) { DeviceList.Add(CreateDevice(Device)); }
            }
            return DeviceList;
        }

        private static AudioDevice CreateDevice(IMMDevice device)
        {
            if (device == null) { return null; }
            device.GetId(out string DeviceID);
            device.GetState(out AudioDeviceState DeviceState);
            Dictionary<string, object> DeviceProperties = new Dictionary<string, object>();
            device.OpenPropertyStore(STGM.STGM_READ, out IPropertyStore PropertyStore);
            if (PropertyStore != null)
            {
                PropertyStore.GetCount(out int PropertyCount);
                for (int j = 0; j < PropertyCount; j++)
                {
                    if (PropertyStore.GetAt(j, out PROPERTYKEY Key) == 0)
                    {
                        PROPVARIANT Value = new PROPVARIANT();
                        int Result = PropertyStore.GetValue(ref Key, ref Value);
                        object ValueObj = Value.GetValue();
                        try
                        {
                            if (Value.vt != VARTYPE.VT_BLOB) // for some reason, this fails?
                            {
                                PropVariantClear(ref Value);
                            }
                        }
                        catch { }
                        string KeyName = Key.ToString();
                        DeviceProperties[KeyName] = ValueObj;
                    }
                }
            }
            return new AudioDevice(DeviceID, DeviceState, DeviceProperties);
        }

        public sealed class AudioDevice
        {
            public string ID { get; private set; }
            public AudioDeviceState State { get; private set; }
            public IDictionary<string, object> Properties { get; private set; }

            internal AudioDevice(string id, AudioDeviceState state, IDictionary<string, object> properties)
            {
                this.ID = id;
                this.State = state;
                this.Properties = properties;
            }

            public string Description
            {
                get { return GetProperty("{a45c254e-df1c-4efd-8020-67d146a850e0} 2"); }
            }

            public string ContainerID
            {
                get { return GetProperty("{8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c} 2"); }
            }

            public string EnumeratorName
            {
                get { return GetProperty("{a45c254e-df1c-4efd-8020-67d146a850e0} 24"); }
            }

            public string InterfaceFriendlyName
            {
                get { return GetProperty("{026e516e-b814-414b-83cd-856d6fef4822} 2"); }
            }

            public string FriendlyName
            {
                get { return GetProperty("{a45c254e-df1c-4efd-8020-67d146a850e0} 14"); }
            }

            private string GetProperty(string PropertyKey)
            {
                this.Properties.TryGetValue(PropertyKey, out object Value);
                return string.Format("{0}", Value);
            }

            public override string ToString() { return this.FriendlyName; }
        }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, AudioDeviceState dwStateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
            //int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);
            //int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out int pcDevices);
            int Item(int nDevice, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid riid, CLSCTX dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
            int OpenPropertyStore(STGM stgmAccess, out IPropertyStore ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
            int GetState(out AudioDeviceState pdwState);
        }

        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out int cProps);
            int GetAt(int iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public int pid;

            public override string ToString() { return this.fmtid.ToString("B") + " " + this.pid; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public VARTYPE vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public PROPVARIANTunion union;

            public object GetValue()
            {
                switch (this.vt)
                {
                    case VARTYPE.VT_BOOL: { return this.union.boolVal != 0; }
                    case VARTYPE.VT_LPWSTR: { return Marshal.PtrToStringUni(this.union.pwszVal); }
                    case VARTYPE.VT_UI4: { return this.union.lVal; }
                    case VARTYPE.VT_CLSID: { return (Guid)Marshal.PtrToStructure(this.union.puuid, typeof(Guid)); }
                    default: { return this.vt.ToString() + ":?"; }
                }
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANTunion
        {
            [FieldOffset(0)]
            public int lVal;

            [FieldOffset(0)]
            public ulong uhVal;

            [FieldOffset(0)]
            public short boolVal;

            [FieldOffset(0)]
            public IntPtr pwszVal;

            [FieldOffset(0)]
            public IntPtr puuid;
        }

        private enum EDataFlow { eRender, eCapture, eAll }

        private enum ERole { eConsole, eMultimedia, eCommunications }

        public enum AudioDeviceState
        {
            Active = 0x1,
            Disabled = 0x2,
            NotPresent = 0x4,
            Unplugged = 0x8,
            MaskAll = 0xF
        }

        [Flags]
        private enum CLSCTX
        {
            CLSCTX_INPROC_SERVER = 0x1,
            CLSCTX_INPROC_HANDLER = 0x2,
            CLSCTX_LOCAL_SERVER = 0x4,
            CLSCTX_REMOTE_SERVER = 0x10,
            CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER | CLSCTX_REMOTE_SERVER
        }

        [Flags]
        private enum VARTYPE : short
        {
            VT_I4 = 3,
            VT_BOOL = 11,
            VT_UI4 = 19,
            VT_LPWSTR = 31,
            VT_BLOB = 65,
            VT_CLSID = 72,
        }

        private enum STGM { STGM_READ = 0x00000000 }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PROPVARIANT pvar);
    }
}
