using System;
using System.Collections.Generic;
using System.Text;

namespace CK.Observable.League.Tests.MicroMachine
{
    [BinarySerialization.SerializationVersion( 0 )]
    public class Root : ObservableRootObject
    {
        public Root()
        {
            Machine = new SpecializedMachine( "Artemis", new MachineConfiguration() );
        }

        Root( BinarySerialization.IBinaryDeserializer r, BinarySerialization.ITypeReadInfo? info )
                : base( BinarySerialization.Sliced.Instance )
        {
            Machine = r.ReadObject<SpecializedMachine>();
        }

        public static void Write( BinarySerialization.IBinarySerializer s, in Root o )
        {
            s.WriteObject( o.Machine );
        }

        /// <summary>
        /// Gets the "Artemis" machine.
        /// </summary>
        public SpecializedMachine Machine { get; }

    }
}
