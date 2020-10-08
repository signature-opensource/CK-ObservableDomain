using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CK.Observable
{
    /// <summary>
    /// Encapsulates the result of a successful <see cref="ObservableDomain.Transaction.Commit"/>.
    /// This is available from <see cref="IObservableDomainClient.OnTransactionCommit(in SuccessfulTransactionEventArgs)"/>
    /// and <see cref="IObservableDomainSafeClient"/>
    /// </summary>
    public class SuccessfulTransactionEventArgs : EventMonitoredArgs 
    {
        readonly ObservableDomain _domain;
        readonly Func<string, int?> _propertyId;
        internal readonly ActionRegistrar<PostActionContext> _postActions;
        internal readonly List<object> _commands;

        /// <summary>
        /// Gets the observable domain.
        /// </summary>
        public IObservableDomain Domain => _domain;

        /// <summary>
        /// Gets the start time (UTC) of the transaction.
        /// </summary>
        public DateTime StartTimeUtc { get; }

        /// <summary>
        /// Gets the time (UTC) of the transaction commit.
        /// </summary>
        public DateTime CommitTimeUtc { get; }

        /// <summary>
        /// Gets the next due time (UTC) of the <see cref="ObservableTimedEventBase"/>.
        /// </summary>
        public DateTime NextDueTimeUtc { get; }

        /// <summary>
        /// Gets the events that the transaction generated (all <see cref="ObservableObject"/> changes).
        /// Can be empty.
        /// </summary>
        public IReadOnlyList<ObservableEvent> Events { get; }

        /// <summary>
        /// Tries to return the property identifier that is associated to the property name if this
        /// property name has already been used in the domain.
        /// </summary>
        /// <param name="propertyName">THe property name to look for.</param>
        /// <returns>The property identifier or null.</returns>
        public int? FindPropertyId( string propertyName ) => _propertyId( propertyName );

        /// <summary>
        /// Adds a command to the ones already enqueued by <see cref="DomainView.SendCommand(object)"/>.
        /// </summary>
        public void SendCommand( object command ) => _commands.Add( command );

        /// <summary>
        /// Registrar for actions (that can be synchronous as well as asynchronous) that must be executed after
        /// the transaction itself.
        /// </summary>
        public IActionRegistrar<PostActionContext> PostActions => _postActions;

        internal SuccessfulTransactionEventArgs( ObservableDomain d, Func<string,int?> propertyId, IReadOnlyList<ObservableEvent> e, List<object> c, DateTime startTime, DateTime nextDueTime )
            : base( d.CurrentMonitor )
        {
            _domain = d;
            _propertyId = propertyId;
            _postActions = new ActionRegistrar<PostActionContext>();
            _commands = c;
            NextDueTimeUtc = nextDueTime;
            StartTimeUtc = startTime;
            CommitTimeUtc = DateTime.UtcNow;
            Events = e;
        }

    }
}
