using CK.DeviceModel;

namespace CK.Observable.Device
{
    /// <summary>
    /// Abstract base class for device.
    /// </summary>
    [BinarySerialization.SerializationVersion( 0 )]
    public abstract class ObservableDeviceObject<TSidekick> : ObservableDeviceObject, ISidekickClientObject<TSidekick> where TSidekick : ObservableDomainSidekick
    {
        /// <summary>
        /// Initializes a new observable object device.
        /// </summary>
        /// <param name="deviceName">The device name.</param>
        protected ObservableDeviceObject( string deviceName )
            : base( deviceName )
        {
        }

        #region Old Deserialization
        ObservableDeviceObject( IBinaryDeserializer r, TypeReadInfo? info )
                : base( BinarySerialization.Sliced.Instance )
        {
        }
        #endregion

        protected ObservableDeviceObject( BinarySerialization.Sliced _ ) : base( _ ) { }

        ObservableDeviceObject( BinarySerialization.IBinaryDeserializer r, BinarySerialization.ITypeReadInfo info )
                : base( BinarySerialization.Sliced.Instance )
        {
        }

        public static void Write( BinarySerialization.IBinarySerializer s, in ObservableDeviceObject<TSidekick> o )
        {
        }

    }
}
