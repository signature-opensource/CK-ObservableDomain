using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace CK.Observable
{
    /// <summary>
    /// Implements a <see cref="IObservableDomainClient"/> that collects
    /// transaction events and exposes <see cref="TransactionEvent"/> that captures,
    /// for each transaction, all the transaction <see cref="ObservableEvent"/> as well
    /// as a JSON object that describes them.
    /// </summary>
    public class TransactionEventCollectorClient : IObservableDomainClient
    {
        readonly IObservableDomainClient _next;
        readonly List<TransactionEvent> _events;
        readonly StringWriter _buffer;
        readonly ObjectExporter _exporter;

        /// <summary>
        /// Representation of a successful transaction.
        /// </summary>
        public readonly struct TransactionEvent
        {
            /// <summary>
            /// The transaction number.
            /// </summary>
            public readonly int TransactionNumber;

            /// <summary>
            /// The list of <see cref="ObservableEvent"/> objects that have been emitted during
            /// the transaction.
            /// </summary>
            public readonly IReadOnlyList<ObservableEvent> Events;

            /// <summary>
            /// The date and time of the transaction.
            /// </summary>
            public readonly DateTime TimeUtc;

            /// <summary>
            /// The JSON description of the <see cref="Events"/>.
            /// </summary>
            public readonly string ExportedEvents;

            internal TransactionEvent( int t, DateTime timeUtc, IReadOnlyList<ObservableEvent> e, string exported )
            {
                TransactionNumber = t;
                Events = e;
                TimeUtc = timeUtc;
                ExportedEvents = exported;
            }
        }

        /// <summary>
        /// Initializes a new <see cref="TransactionEventCollectorClient"/>.
        /// </summary>
        /// <param name="next">The next manager (can be null).</param>
        public TransactionEventCollectorClient( IObservableDomainClient next = null )
        {
            _next = next;
            _events = new List<TransactionEvent>();
            _buffer = new StringWriter();
            _exporter = new ObjectExporter( new JSONExportTarget( _buffer ) );
            KeepDuration = TimeSpan.FromHours( 1 );
            KeepLimit = 100;
        }

        /// <summary>
        /// Gets the current transaction events.
        /// </summary>
        public IReadOnlyList<TransactionEvent> TransactionEvents => _events;

        /// <summary>
        /// Gets or sets the maximum time during which events are kept.
        /// Defaults to one hour.
        /// </summary>
        public TimeSpan KeepDuration { get; set; }

        /// <summary>
        /// Gets or sets the minimum number of transaction events that are kept, regardless of <see cref="KeepDuration"/>.
        /// Default to 100.
        /// </summary>
        public int KeepLimit { get; set; }

        /// <summary>
        /// Generates a JSON object that contains all the events from a specified transaction number.
        /// </summary>
        /// <param name="transactionNumber">The transaction number.</param>
        /// <returns>The JSON object.</returns>
        public string WriteEventsFrom( int transactionNumber )
        {
            if( _events.Count == 0 ) throw new InvalidOperationException( "OnTransactionCommit has not been called yet." );
            var last = _events[_events.Count - 1];
            if( transactionNumber >= last.TransactionNumber )
            {
                throw new InvalidOperationException( $"Transaction requested n°{transactionNumber}. Current is {last.TransactionNumber}." );
            }
            _buffer.GetStringBuilder().Clear();
            _exporter.Reset();
            var t = _exporter.Target;
            t.EmitStartObject( -1, ObjectExportedKind.Object );
            t.EmitPropertyName( "N" );
            t.EmitInt32( last.TransactionNumber );
            t.EmitPropertyName( "E" );
            t.EmitStartList();
            if( transactionNumber == last.TransactionNumber - 1 )
            {
                _buffer.Write( last.ExportedEvents );
            }
            else
            {
                foreach( var e in _events )
                {
                    if( e.TransactionNumber <= transactionNumber ) continue;
                    foreach( var ev in e.Events ) ev.Export( _exporter );
                }
            }
            t.EmitEndList();
            t.EmitEndObject( -1, ObjectExportedKind.Object );
            return _buffer.GetStringBuilder().ToString();
        }

        void ApplyKeepDuration()
        {
            int removableMaxIndex = _events.Count - KeepLimit;
            if( removableMaxIndex > 0 )
            {
                var timeLimit = DateTime.UtcNow.Subtract( KeepDuration );
                int i = 0;
                for( ; i < removableMaxIndex; ++i )
                {
                    if( _events[i].TimeUtc >= timeLimit ) break;
                }
                if( i > 0 ) _events.RemoveRange( 0, i );
            }
        }

        void IObservableDomainClient.OnDomainCreated( IActivityMonitor monitor, ObservableDomain d ) => _next?.OnDomainCreated( monitor, d );

        void IObservableDomainClient.OnTransactionCommit( in SuccessfulTransactionContext c )
        {
            _buffer.GetStringBuilder().Clear();
            _exporter.Reset();
            foreach( var e in c.Events ) e.Export( _exporter );
            _events.Add( new TransactionEvent( c.Domain.TransactionSerialNumber, c.CommitTimeUtc, c.Events, _buffer.ToString() ) );
            ApplyKeepDuration();
            _next?.OnTransactionCommit( c );
        }

        void IObservableDomainClient.OnTransactionFailure( IActivityMonitor monitor, ObservableDomain d, IReadOnlyList<CKExceptionData> errors )
        {
            _next?.OnTransactionFailure( monitor, d, errors );
        }

        void IObservableDomainClient.OnTransactionStart( IActivityMonitor monitor, ObservableDomain d, DateTime timeUtc )
        {
            _next?.OnTransactionStart( monitor, d, timeUtc );
        }
    }
}
