using CK.Core;
using FluentAssertions;
using System;

namespace CK.Observable.Domain.Tests.Sample
{
    [SerializationVersion(0)]
    public class Car : ObservableObject
    {
        ObservableEventHandler<ObservableDomainEventArgs> _testSpeedChanged;
        ObservableEventHandler _positionChanged;
        ObservableEventHandler _powerChanged;
        Position _position;
        int _power;

        public Car( string name )
        {
            Domain.Monitor.Info( $"Creating Car '{name}'." );
            Name = name;
        }

        protected Car( IBinaryDeserializerContext d )
            : base( d )
        {
            var r = d.StartReading().Reader;
            Name = r.ReadNullableString();
            TestSpeed = r.ReadInt32();
            _position = (Position)r.ReadObject();
            _testSpeedChanged = new ObservableEventHandler<ObservableDomainEventArgs>( r );
        }

        void Write( BinarySerializer w )
        {
            w.WriteNullableString( Name );
            w.Write( TestSpeed );
            w.WriteObject( _position );
            _testSpeedChanged.Write( w );
        }

        public string Name { get; }

        /// <summary>
        /// Gets or sets an automatic property: this is automatically handled (currently by PropertyChanged.Fody).
        /// The setter can be private.
        /// </summary>
        public int TestSpeed { get; set; }

        /// <summary>
        /// Defining this event is enough: it will be automatically fired whenever TestSpeed has changed.
        /// The private field MUST be a <see cref="ObservableEventHandler"/>, a <see cref="ObservableEventHandler{EventMonitoredArgs}"/>
        /// or a <see cref="ObservableEventHandler{ObservableDomainEventArgs}"/> exacly named _[propName]Changed.
        /// This is fired before <see cref="ObservableObject.PropertyChanged"/> event with property's name.
        /// </summary>
        public event SafeEventHandler<ObservableDomainEventArgs> TestSpeedChanged
        {
            add => _testSpeedChanged.Add( value, nameof( TestSpeedChanged ) );
            remove => _testSpeedChanged.Remove( value );
        }

        /// <summary>
        /// Gets or sets a property with a specific setter, skipping PropertyChanged.Fody weaving.
        /// The [PropertyChanged.DoNotNotify] skips the weaving: the protected OnPropertyChanged must
        /// manually be called.
        /// </summary>
        [PropertyChanged.DoNotNotify]
        public Position Position
        {
            get => _position;
            set
            {
                if( _position != value )
                {
                    _position = value;
                    OnPropertyChanged( nameof( Position ), value );
                }
            }
        }

        public event SafeEventHandler PositionChanged
        {
            add => _positionChanged.Add( value, nameof( PositionChanged ) );
            remove => _positionChanged.Remove( value );
        }

        bool __onPowerChanged;

        /// <summary>
        /// Gets or sets a property with a specific setter.
        /// The PropertyChanged.Fody magically tracks the private field set.
        /// </summary>
        public int Power
        {
            get => _power;
            set
            {
                if( _power != value )
                {
                    __onPowerChanged = false;
                    Domain.Monitor.Info( "Before Power setting." );
                    _power = value;
                    Domain.Monitor.Info( "After Power setting." );
                    __onPowerChanged.Should().BeTrue();
                }
            }
        }

        // This is called by PropertyChanged.Fody.
        void OnPowerChanged()
        {
            Domain.Monitor.Info( "Power set." );
            __onPowerChanged = true;
        }

        public event SafeEventHandler PowerChanged
        {
            add => _powerChanged.Add( value, nameof( PowerChanged ) );
            remove => _powerChanged.Remove( value );
        }


        public Mechanic CurrentMechanic { get; set; }

        public override string ToString() => $"'Car {Name}'";

        /// <summary>
        /// This is called by PropertyChanged.Fody.
        /// </summary>
        /// <param name="before"></param>
        /// <param name="after"></param>
        void OnCurrentMechanicChanged( object before, object after )
        {
            if( Domain.IsDeserializing ) return;
            Domain.Monitor.Info( $"{ToString()} is now repaired by: {CurrentMechanic?.ToString() ?? "null"}." );
            if( CurrentMechanic != null ) CurrentMechanic.CurrentCar = this;
            else ((Mechanic)before).CurrentCar = null;
        }

    }
}
