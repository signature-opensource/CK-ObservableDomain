using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace CK.Serialization.Tests
{
     [TestFixture]
    public class DictionarySerializationTests
    {
        [Test]
        public void standard_serialization()
        {
            var d = new Dictionary<string, int>( StringComparer.InvariantCultureIgnoreCase );
            d.Add( "A", 1 );
            d["a"].Should().Be( 1 );

            var d2 = (Dictionary<string, int>)SaveAndLoadStandard( d );
            d2["a"].Should().Be( 1 );
            d2.Add( "B", 2 );
            d2["b"].Should().Be( 2 );

            d2.Comparer.Should().NotBeSameAs( StringComparer.InvariantCultureIgnoreCase );
        }

        internal static object SaveAndLoadStandard( object o )
        {
            using( var s = new MemoryStream() )
            {
                new BinaryFormatter().Serialize( s, o );
                s.Position = 0;
                return new BinaryFormatter().Deserialize( s );
            }
        }

        [Test]
        public void basic_types_dictionary_serialization()
        {
            var int2String = new Dictionary<int,string>
            {
                { 12, "Twelve" },
                { 11, "Eleven" },
                { 10, "Ten" },
                { 9, "Nine" },
                { 8, "Eight" }
            };
            object back = SaveAndLoad( int2String );
            back.Should().BeAssignableTo<Dictionary<int, string>>();
            var b = (Dictionary<int, string>)back;
            b.Should().BeEquivalentTo( int2String );
        }

        [Test]
        public void dictionary_with_comparer_serialization()
        {
            var string2Int = new Dictionary<string,int>( StringComparer.InvariantCultureIgnoreCase )
            {
                { "Twelve", 12 },
                { "Eleven", 11 },
                { "Ten", 10 },
                { "Nine", 9 },
                { "Eight", 8 }
            };
            object back = SaveAndLoad( string2Int );
            back.Should().BeAssignableTo<Dictionary<string,int>>();
            var b = (Dictionary<string, int>)back;
            b.Should().BeEquivalentTo( string2Int );
            b["TWELVE"].Should().Be( 12 );
        }

        static object SaveAndLoad( object o ) => ArraySerializationTests.SaveAndLoad( o );

    }
}
