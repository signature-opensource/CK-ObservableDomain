using CK.Core;
using System;

namespace CK.Observable.Domain.Tests.Sample
{
    [SerializationVersion(0)]
    public class Car : ObservableObject
    {
        ObservableEventHandler _speedChanged;

        public Car( string name )
        {
            Monitor.Info( $"Creating Car '{name}'." );
            Name = name;
        }

        public Car( IBinaryDeserializerContext d ) : base( d )
        {
            var r = d.StartReading();
            Name = r.ReadNullableString();
            Speed = r.ReadInt32();
            Position = (Position)r.ReadObject();
            _speedChanged = new ObservableEventHandler( r );
        }

        void Write( BinarySerializer w )
        {
            w.WriteNullableString( Name );
            w.Write( Speed );
            w.WriteObject( Position );
            _speedChanged.Write( w );
        }

        public string Name { get; }

        public int Speed { get; set; }

        /// <summary>
        /// Defining this event is enough: it will be automatically fired whenever Speed has changed.
        /// The private field MUST be a <see cref="ObservableEventHandler"/> exacly named _[propName]Changed.
        /// This is fired before <see cref="ObservableObject.PropertyChanged"/> event with property's name.
        /// </summary>
        public event SafeEventHandler SpeedChanged
        {
            add => _speedChanged.Add( value, nameof( SpeedChanged ) );
            remove => _speedChanged.Remove( value );
        }

        /// <summary>
        /// Defining this event is enough: it will be automatically fired whenever Position has changed.
        /// Its type MUST be EventHandler BUT, a SafeEventHandler should be used whenever possible.
        /// This is fired before <see cref="ObservableObject.PropertyChanged"/> event with property's name.
        /// </summary>
        public event EventHandler PositionChanged;

        public Position Position { get; set; }

        public Mechanic CurrentMechanic { get; set; }

        public override string ToString() => $"'Car {Name}'";

        void OnCurrentMechanicChanged( object before, object after )
        {
            if( IsDeserializing ) return;
            Monitor.Info( $"{ToString()} is now repaired by: {CurrentMechanic?.ToString() ?? "null"}." );
            if( CurrentMechanic != null ) CurrentMechanic.CurrentCar = this;
            else ((Mechanic)before).CurrentCar = null;
        }

    }
}
