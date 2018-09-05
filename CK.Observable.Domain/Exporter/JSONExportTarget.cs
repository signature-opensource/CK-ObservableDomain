using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Observable
{
    public class JSONExportTarget : IObjectExporterTarget
    {
        readonly JSONExportTargetOptions _options;
        readonly TextWriter _w;
        bool _commaNeeded;

        public JSONExportTarget( TextWriter w, JSONExportTargetOptions options = null )
        {
            _w = w;
            _options = options ?? JSONExportTargetOptions.EmptyPrefix;
        }

        /// <summary>
        /// Resets any internal state so that any contextual information are lost.
        /// </summary>
        public void ResetContext()
        {
            _commaNeeded = false;
        }

        public void EmitBool( bool o )
        {
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( o ? "true" : "false" );
            _commaNeeded = true;
        }

        void EmitObjectStartWithNum( int num )
        {
            Debug.Assert( num >= 0 );
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( _options.ObjectNumberPrefix );
            _w.Write( num );
            _commaNeeded = true;
        }

        public void EmitEmptyObject( int num )
        {
            Debug.Assert( num >= 0 );
            EmitObjectStartWithNum( num );
            _w.Write( "}" );
            Debug.Assert( _commaNeeded );
        }

        public void EmitStartObject( int num, ObjectExportedKind kind )
        {
            Debug.Assert( kind != ObjectExportedKind.None );
            if( kind == ObjectExportedKind.Object )
            {
                if( num >= 0 ) EmitObjectStartWithNum( num );
                else
                {
                    if( _commaNeeded ) _w.Write( ',' );
                    _w.Write( '{' );
                    _commaNeeded = false;
                }
            }
            else
            {
                if( _commaNeeded ) _w.Write( ',' );
                if( num >= 0 )
                {
                    _w.Write( _options.GetPrefixTypeFormat( kind ), num );
                    _commaNeeded = true;
                }
                else
                {
                    if( kind != ObjectExportedKind.List )
                    {
                        throw new InvalidOperationException( $"Only List and Object export support untracked (non numbered) objects: ObjectExportedKind = {kind}" );
                    }
                    _w.Write( "[" );
                    _commaNeeded = false;
                }
            }
        }

        public void EmitEndObject( int num, ObjectExportedKind kind )
        {
            Debug.Assert( kind != ObjectExportedKind.None );
            _w.Write( kind == ObjectExportedKind.Object ? '}': ']' );
            _commaNeeded = true;
        }

        public void EmitNull()
        {
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( "null" );
            _commaNeeded = true;
        }

        public void EmitPropertyName( string name )
        {
            EmitString( name );
            _w.Write( ':' );
            _commaNeeded = false;
        }

        public void EmitReference( int num )
        {
            Debug.Assert( num >= 0 );
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( _options.ObjectReferencePrefix );
            _w.Write( num );
            _w.Write( '}' );
            _commaNeeded = true;
        }

        public void EmitDouble( double o )
        {
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( o.ToString( CultureInfo.InvariantCulture ) );
            _commaNeeded = true;
        }

        public void EmitString( string value )
        {
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( '"' );
            _w.Write( value.Replace( "\"", "\\\"" ) );
            _w.Write( '"' );
            _commaNeeded = true;
        }

        public void EmitStartMapEntry()
        {
            if( _commaNeeded ) _w.Write( ',' );
            _w.Write( '[' );
            _commaNeeded = false;
        }

        public void EmitEndMapEntry()
        {
            _w.Write( ']' );
            _commaNeeded = true;
        }

        public void EmitChar( char o ) => EmitString( o.ToString() );

        public void EmitSingle( float o ) => EmitDouble( o );

        public void EmitDateTime( DateTime o ) => EmitString( o.ToString() );

        public void EmitTimeSpan( TimeSpan o ) => EmitString( o.ToString() );

        public void EmitDateTimeOffset( DateTimeOffset o ) => EmitString( o.ToString() );

        public void EmitGuid( Guid o ) => EmitString( o.ToString() );

        public void EmitByte( byte o ) => EmitDouble( o );

        public void EmitSByte( decimal o ) => EmitDouble( (double)o );

        public void EmitInt16( short o ) => EmitDouble( o );

        public void EmitUInt16( ushort o ) => EmitDouble( o );

        public void EmitInt32( int o ) => EmitDouble( o );

        public void EmitUInt32( uint o ) => EmitDouble( o );

        public void EmitInt64( long o ) => EmitDouble( o );

        public void EmitUInt64( ulong o ) => EmitDouble( o );

    }
}
