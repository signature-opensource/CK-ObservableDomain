using CK.Core;
using CK.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Observable
{

    public partial class ObservableDomain
    {
        /// <summary>
        /// Immutable capture of object graph issues.
        /// This is (efficiently) computed by the <see cref="Save"/> method. Note that because of concurrent executions,
        /// unreachable objects appearing in these lists may already be destroyed when this object is exposed.
        /// </summary>
        public class LostObjectTracker
        {
            readonly ObservableDomain _d;

            internal LostObjectTracker( ObservableDomain d,
                                        IReadOnlyList<ObservableObject>? observables,
                                        IReadOnlyList<InternalObject>? internals,
                                        IReadOnlyList<ObservableTimedEventBase>? timed,
                                        IReadOnlyList<IDestroyable>? refDestroyed,
                                        int unusedPooledReminders,
                                        int pooledReminderCount )
            {
                _d = d;
                TransactionNumber = d.TransactionSerialNumber;
                UnreacheableObservables = observables ?? Array.Empty<ObservableObject>();
                UnreacheableInternals = internals ?? Array.Empty<InternalObject>();
                UnreacheableTimedObjects = timed ?? Array.Empty<ObservableTimedEventBase>();
                ReferencedDestroyed = refDestroyed ?? Array.Empty<IDestroyable>();
                UnusedPooledReminderCount = unusedPooledReminders;
                PooledReminderCount = pooledReminderCount;
            }

            /// <summary>
            /// Gets the transaction number of the domain that has been captured.
            /// </summary>
            public int TransactionNumber { get; }

            /// <summary>
            /// Gets a list of <see cref="IDestroyable"/> that are destroyed but are
            /// still referenced from non destroyed objects.
            /// </summary>
            public IReadOnlyList<IDestroyable> ReferencedDestroyed { get; }

            /// <summary>
            /// Gets a list of non destroyed <see cref="ObservableObject"/> that are no more reachable
            /// from any of <see cref="ObservableDomain.AllRoots"/>.
            /// <para>
            /// Note that when there is no defined root, this list is empty and that, because of concurrent executions,
            /// some of these object may be already destroyed.
            /// </para>
            /// </summary>
            public IReadOnlyList<ObservableObject> UnreacheableObservables { get; }

            /// <summary>
            /// Gets a list of non destroyed <see cref="InternalObject"/> that are no more reachable
            /// from any of <see cref="ObservableDomain.AllRoots"/>.
            /// <para>
            /// Note that when there is no defined root, this list is empty and that, because of concurrent executions,
            /// some of these object may be already destroyed.
            /// </para>
            /// </summary>
            public IReadOnlyList<InternalObject> UnreacheableInternals { get; }

            /// <summary>
            /// Gets a list of non destroyed <see cref="ObservableTimedEventBase"/> that are no more reachable
            /// from any of <see cref="ObservableDomain.AllRoots"/> (and are not pooled reminders).
            /// <para>
            /// Note that when there is no defined root, this list is empty and that, because of concurrent executions,
            /// some of these object may be already destroyed.
            /// </para>
            /// </summary>
            public IReadOnlyList<ObservableTimedEventBase> UnreacheableTimedObjects { get; }

            /// <summary>
            /// Gets the number of unused pooled reminders.
            /// </summary>
            public int UnusedPooledReminderCount { get; }

            /// <summary>
            /// Gets the total number of pooled reminders.
            /// </summary>
            public int PooledReminderCount { get; }

            /// <summary>
            /// When <see cref="UnusedPooledReminderCount"/> is greater than 16 and greater than half of the <see cref="PooledReminderCount"/>,
            /// the <see cref="ObservableDomain.GarbageCollectAsync"/> will trim the number of pooled reminders.
            /// </summary>
            public bool ShouldTrimPooledReminders => UnusedPooledReminderCount > 16 && (2 * UnusedPooledReminderCount) > PooledReminderCount;

            /// <summary>
            /// Gets whether one or more issues have been detected.
            /// When false, then there is nothing to do (it is useless to call <see cref="ObservableDomain.GarbageCollectAsync(IActivityMonitor, int)"/>).
            /// </summary>
            public bool HasIssues => ReferencedDestroyed.Count > 0
                                     || UnreacheableObservables.Count > 0
                                     || UnreacheableInternals.Count > 0
                                     || UnreacheableTimedObjects.Count > 0
                                     || ShouldTrimPooledReminders;

            /// <summary>
            /// Dumps the messages to the monitor. Only the <see cref="ReferencedDestroyed"/> are errors.
            /// Other issues are expressed as warnings.
            /// </summary>
            /// <param name="monitor">The target monitor.</param>
            /// <param name="dumpReferencedDestroyed">False to skip <see cref="ReferencedDestroyed"/> errors.</param>
            public void DumpLog( IActivityMonitor monitor, bool dumpReferencedDestroyed = true )
            {
                if( dumpReferencedDestroyed )
                {
                    if( ReferencedDestroyed.Count > 0 )
                    {
                        using( monitor.OpenError( $"{ReferencedDestroyed.Count} destroyed objects are referenced by one or more non destroyed objects." ) )
                        {
                            monitor.Error( ReferencedDestroyed.GroupBy( r => r.GetType() ).Select( g => $"{g.Count()} of type '{g.Key.Name}'" ).Concatenate() );
                        }
                    }
                    else
                    {
                        monitor.Trace( "No reference to destroyed objects." );
                    }
                }
                if( UnreacheableObservables.Count > 0 )
                {
                    using( monitor.OpenWarn( $"{UnreacheableObservables.Count} Observable objects are not reachable from any of the domain's roots." ) )
                    {
                        monitor.Warn( UnreacheableObservables.GroupBy( r => r.GetType() ).Select( g => $"{g.Count()} of type '{g.Key.Name}'" ).Concatenate() );
                    }
                }
                else
                {
                    monitor.Trace( "No unreachable Observable objects found." );
                }
                if( UnreacheableInternals.Count > 0 )
                {
                    using( monitor.OpenWarn( $"{UnreacheableInternals.Count} Internal objects are not reachable from any of the domain's roots." ) )
                    {
                        monitor.Warn( UnreacheableInternals.GroupBy( r => r.GetType() ).Select( g => $"{g.Count()} of type '{g.Key.Name}'" ).Concatenate() );
                    }
                }
                else
                {
                    monitor.Trace( "No unreachable Internal objects found." );
                }
                if( UnreacheableTimedObjects.Count > 0 )
                {
                    using( monitor.OpenWarn( $"{UnreacheableTimedObjects.Count} Timer or Reminder objects are not reachable from any of the domain's roots." ) )
                    {
                        monitor.Warn( UnreacheableTimedObjects.GroupBy( r => r.GetType() ).Select( g => $"{g.Count()} of type '{g.Key.Name}'" ).Concatenate() );
                    }
                }
                else
                {
                    monitor.Trace( "No unreachable Timer or Reminder objects found." );
                }
                if( ShouldTrimPooledReminders )
                {
                    monitor.Warn( $"There are {UnusedPooledReminderCount} unused pooled reminders out of {PooledReminderCount}. The set of pooled reminders should be trimmed." );
                }
            }

            /// <summary>
            /// Overridden to return the count of the different lists.
            /// </summary>
            /// <returns>A readable string.</returns>
            public override string ToString() => $"{ReferencedDestroyed.Count} ReferencedDestroyed, {UnreacheableObservables.Count} UnreacheableObservables, {UnreacheableInternals.Count} UnreacheableInternals, {UnreacheableTimedObjects.Count} UnreacheableTimedObjects.";
        }

        /// <summary>
        /// Gets the <see cref="LostObjectTracker"/> that has been computed by the last <see cref="Save"/> call.
        /// Use <see cref="EnsureLostObjectTracker(IActivityMonitor, int)"/> to refresh it.
        /// </summary>
        public LostObjectTracker? CurrentLostObjectTracker { get; private set; }


        class NullStream : Stream
        {
            long _p;

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position { get => _p; set => throw new NotSupportedException(); }

            public override void Flush() { }

            public override int Read( byte[] buffer, int offset, int count )
            {
                throw new NotSupportedException();
            }

            public override long Seek( long offset, SeekOrigin origin )
            {
                throw new NotSupportedException();
            }

            public override void SetLength( long value )
            {
                throw new NotSupportedException();
            }

            public override void Write( byte[] buffer, int offset, int count )
            {
                _p += count;
            }
        }

        /// <summary>
        /// Triggers a garbage collection on this domain.
        /// First, <see cref="EnsureLostObjectTracker(IActivityMonitor, int)"/> is called to update the <see cref="CurrentLostObjectTracker"/>
        /// and then, the detected lost objects are unloaded in a <see cref="ModifyAsync"/>.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="millisecondsTimeout">
        /// The maximum number of milliseconds to wait for the write access before giving up (this is also used for
        /// the read access if <see cref="EnsureLostObjectTracker(IActivityMonitor, int)"/> must update the <see cref="CurrentLostObjectTracker"/>).
        /// Waits indefinitely by default.
        /// </param>
        /// <returns>True on success, false if timeout or an error occurred.</returns>
        public async Task<bool> GarbageCollectAsync( IActivityMonitor monitor, int millisecondsTimeout = -1 )
        {
            CheckDisposed();
            using( monitor.OpenInfo( $"Garbage collecting." ) )
            {
                var c = EnsureLostObjectTracker( monitor, millisecondsTimeout );
                if( c == null ) return false;
                if( !c.HasIssues )
                {
                    monitor.CloseGroup( "There is nothing to do." );
                    return true;
                }
                c.DumpLog( monitor, false );
                int count = 0;
                var (ex, result) = await ModifyNoThrowAsync( monitor, () =>
                {
                    Debug.Assert( c != null );

                    // Destroyed objects can only transition from alive to destroyed: using
                    // the lost objects captured here is fine since the only risk is to forget
                    // some objects.
                    foreach( var o in c.UnreacheableObservables )
                    {
                        if( !o.IsDestroyed )
                        {
                            o.Unload( true );
                            ++count;
                        }
                    }
                    foreach( var o in c.UnreacheableInternals )
                    {
                        if( !o.IsDestroyed )
                        {
                            o.Unload( true );
                            ++count;
                        }
                    }
                    foreach( var o in c.UnreacheableTimedObjects )
                    {
                        if( !o.IsDestroyed )
                        {
                            o.Destroy();
                            ++count;
                        }
                    }
                    //// Pool reminders may have been added/removed to the pool by transactions
                    //// before we enter this ModifyAsync.
                    //// We should theoretically reanalyze the data but since we ask to
                    //// remove only half of the unused (at most), we do it directly.
                    if( c.ShouldTrimPooledReminders )
                    {
                        TimeManager.TrimPooledReminders( monitor, c.UnusedPooledReminderCount / 2 );
                    }
                }, millisecondsTimeout );
                if( ex != null )
                {
                    monitor.Error( ex );
                    return false;
                }
                monitor.CloseGroup( $"Removed {count} objects." );
                return result.Success;
            }
        }

        /// <summary>
        /// Updates <see cref="CurrentLostObjectTracker"/> if its <see cref="LostObjectTracker.TransactionNumber"/> is
        /// not the current <see cref="TransactionSerialNumber"/>.
        /// On error (or if a read access failed to be obtained), returns null.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="millisecondsTimeout">
        /// The maximum number of milliseconds to wait for a read access before giving up.
        /// Wait indefinitely by default.
        /// </param>
        /// <returns>True on success, false if timeout occurred.</returns>
        /// <returns></returns>
        public LostObjectTracker? EnsureLostObjectTracker( IActivityMonitor monitor, int millisecondsTimeout = -1 )
        {
            // We don't need synchronization code here: the "CurrentLostObjectTracker" may have been
            // updated by another save and we absolutely don't care since the LostObjectTracker creation is
            // not parametrized: it's the same for everyone.
            var current = CurrentLostObjectTracker;
            if( current == null || current.TransactionNumber != TransactionSerialNumber )
            {
                using var s = new NullStream();
                monitor.Trace( "Saving objects in a null stream to track lost objects." );
                if( !Save( monitor, s, millisecondsTimeout: millisecondsTimeout ) )
                {
                    return null;
                }
            }
            return CurrentLostObjectTracker;
        }

        /// <inheritdoc/>
        public bool Save( IActivityMonitor monitor,
                          Stream stream,
                          bool leaveOpen = false,
                          bool debugMode = false,
                          Encoding? encoding = null,
                          int millisecondsTimeout = -1 )
        {
            if( monitor == null ) throw new ArgumentNullException( nameof( monitor ) );
            if( stream == null ) throw new ArgumentNullException( nameof( stream ) );
            CheckDisposed();

            // Since we only need the read lock, whenever multiple threads Save() concurrently,
            // the monitor (of the fake transaction) is at risk. This is why we secure the Save with its own lock: since
            // only one Save at a time can be executed and no other "read with a monitor (even in a fake transaction)" exists.
            // Since this is clearly an edge case, we use a lock with the same timeout and we don't care of a potential 2x wait time.
            if( !Monitor.TryEnter( _saveLock, millisecondsTimeout ) ) return false;

            List<IDestroyable>? destroyedRefList = null;

            void Track( IDestroyable o )
            {
                if( destroyedRefList == null ) destroyedRefList = new List<IDestroyable>();
                Debug.Assert( !destroyedRefList.Contains( o ) );
                destroyedRefList.Add( o );
            }

            using( var w = new BinarySerializer( stream, _serializers, leaveOpen, encoding, Track ) )
            {
                bool isWrite = _lock.IsWriteLockHeld;
                bool isRead = _lock.IsReadLockHeld;
                if( !isWrite && !isRead && !_lock.TryEnterReadLock( millisecondsTimeout ) )
                {
                    Monitor.Exit( _saveLock );
                    return false;
                }
                bool needFakeTran = _currentTran == null || _currentTran.Monitor != monitor;
                if( needFakeTran ) new InitializationTransaction( monitor, this, false );
                try
                {
                    using( monitor.OpenInfo( $"Saving domain ({_actualObjectCount} objects, {_internalObjectCount} internals, {_timeManager.AllObservableTimedEvents.Count} timed events)." ) )
                    {
                        w.WriteSmallInt32( CurrentSerializationVersion );
                        w.DebugWriteMode( debugMode ? (bool?)debugMode : null );
                        w.Write( _currentObjectUniquifier );
                        w.Write( _domainSecret );
                        if( debugMode ) monitor.Trace( $"Domain {DomainName}: Tran #{_transactionSerialNumber} - {_transactionCommitTimeUtc:o}, {_actualObjectCount} objects." );
                        w.Write( DomainName );
                        w.Write( _transactionSerialNumber );
                        w.Write( _transactionCommitTimeUtc );
                        w.Write( _actualObjectCount );

                        w.DebugWriteSentinel();
                        w.WriteNonNegativeSmallInt32( _freeList.Count );
                        foreach( var i in _freeList ) w.WriteNonNegativeSmallInt32( i );

                        w.DebugWriteSentinel();
                        w.WriteNonNegativeSmallInt32( _properties.Count );
                        foreach( var p in _propertiesByIndex )
                        {
                            w.Write( p.PropertyName );
                        }
                        w.DebugWriteSentinel();
                        Debug.Assert( _objectsListCount == _actualObjectCount + _freeList.Count );

                        // First writes the roots: any reachable objects (including
                        // Internal and Timed objects) will be written. Event callbacks' object will
                        // not if they are destroyed => any non saved objects after these roots
                        // are de facto not reachable from the roots.
                        w.WriteNonNegativeSmallInt32( _roots.Count );
                        foreach( var r in _roots )
                        {
                            w.WriteObject( r );
                        }
                        // The tracking list of non reachable objects from roots.
                        bool trackLostObjects = _roots.Count > 0;
                        List<ObservableObject>? lostObservableObjects = null;
                        List<InternalObject>? lostInternalObjects = null;
                        // Then writes all the Observable objects: track the non reachable
                        // objects if we have at least one root.
                        for( int i = 0; i < _objectsListCount; ++i )
                        {
                            var o = _objects[i];
                            Debug.Assert( o == null || !o.IsDestroyed, "Either it is a free cell (that appears in the free list) or the object is NOT disposed." );
                            if( w.WriteObject( o ) && o != null && trackLostObjects )
                            {
                                if( lostObservableObjects == null ) lostObservableObjects = new List<ObservableObject>();
                                lostObservableObjects.Add( o );
                            }
                        }
                        w.DebugWriteSentinel();
                        w.WriteNonNegativeSmallInt32( _internalObjectCount );
                        var f = _firstInternalObject;
                        while( f != null )
                        {
                            Debug.Assert( !f.IsDestroyed, "Disposed internal objects are removed from the list." );
                            if( w.WriteObject( f ) && trackLostObjects )
                            {
                                if( lostInternalObjects == null ) lostInternalObjects = new List<InternalObject>();
                                lostInternalObjects.Add( f );
                            }
                            f = f.Next;
                        }
                        w.DebugWriteSentinel();
                        var (lostTimedObjects, unusedPooledReminders, pooledReminderCount) = _timeManager.Save( monitor, w, trackLostObjects );
                        w.DebugWriteSentinel();
                        _sidekickManager.Save( w );
                        var data = new LostObjectTracker( this,
                                                          lostObservableObjects,
                                                          lostInternalObjects,
                                                          lostTimedObjects,
                                                          destroyedRefList,
                                                          unusedPooledReminders,
                                                          pooledReminderCount );
                        if( data.HasIssues ) data.DumpLog( monitor );
                        CurrentLostObjectTracker = data;
                        return true;
                    }
                }
                finally
                {
                    if( needFakeTran ) _currentTran.Dispose();
                    if( !isWrite && !isRead ) _lock.ExitReadLock();
                    Monitor.Exit( _saveLock );
                }
            }
        }

        bool DoRealLoad( IActivityMonitor monitor, BinaryDeserializer r, string expectedName, bool? startTimer )
        {
            Debug.Assert( _lock.IsWriteLockHeld );
            _deserializeOrInitializing = true;
            try
            {
                UnloadDomain( monitor );
                int version = r.ReadSmallInt32();
                if( version < 5 || version > CurrentSerializationVersion )
                {
                    throw new InvalidDataException( $"Version must be between 5 and {CurrentSerializationVersion}. Version read: {version}." );
                }
                r.SerializationVersion = version;
                r.DebugReadMode();
                _currentObjectUniquifier = r.ReadInt32();
                _domainSecret = r.ReadBytes( DomainSecretKeyLength );
                var loaded = r.ReadString();
                if( loaded != expectedName ) throw new InvalidDataException( $"Domain name mismatch: loading domain named '{loaded}' but expected '{expectedName}'." );

                _transactionSerialNumber = r.ReadInt32();
                _transactionCommitTimeUtc = r.ReadDateTime();
                _actualObjectCount = r.ReadInt32();

                r.DebugCheckSentinel();

                // Clears and read the new free list.
                _freeList.Clear();
                int count = r.ReadNonNegativeSmallInt32();
                while( --count >= 0 )
                {
                    _freeList.Add( r.ReadNonNegativeSmallInt32() );
                }

                r.DebugCheckSentinel();

                // Read the properties index.
                count = r.ReadNonNegativeSmallInt32();
                for( int iProp = 0; iProp < count; iProp++ )
                {
                    string name = r.ReadString();
                    var p = new ObservablePropertyChangedEventArgs( iProp, name );
                    _properties.Add( name, p );
                    _propertiesByIndex.Add( p );
                }

                r.DebugCheckSentinel();

                // Resize _objects array.
                _objectsListCount = count = _actualObjectCount + _freeList.Count;
                while( _objectsListCount > _objects.Length )
                {
                    Array.Resize( ref _objects, _objects.Length * 2 );
                }

                if( version == 5 )
                {
                    // Reads objects. This can read Internal and Timed objects.
                    for( int i = 0; i < count; ++i )
                    {
                        _objects[i] = (ObservableObject)r.ReadObject();
                        Debug.Assert( _objects[i] == null || !_objects[i].IsDestroyed );
                    }

                    // Fill roots array.
                    r.DebugCheckSentinel();
                    count = r.ReadNonNegativeSmallInt32();
                    while( --count >= 0 )
                    {
                        _roots.Add( _objects[r.ReadNonNegativeSmallInt32()] as ObservableRootObject );
                    }
                }
                else
                {
                    // Reading roots first (including Internal and Timed objects).
                    int rootCount = r.ReadNonNegativeSmallInt32();
                    for( int i = 0; i < rootCount; ++i )
                    {
                        _roots.Add( (ObservableRootObject)r.ReadObject() );
                    }
                    // Reads all the objects. 
                    for( int i = 0; i < count; ++i )
                    {
                        _objects[i] = (ObservableObject)r.ReadObject();
                        Debug.Assert( _objects[i] == null || !_objects[i].IsDestroyed );
                    }
                }

                // Reading InternalObjects.
                r.DebugCheckSentinel();
                count = r.ReadNonNegativeSmallInt32();
                while( --count >= 0 )
                {
                    var o = (InternalObject)r.ReadObject();
                    Debug.Assert( !o.IsDestroyed );
                    Register( o );
                }

                // Reading Timed events.
                r.DebugCheckSentinel();
                bool timerRunning = _timeManager.Load( monitor, r );
                r.DebugCheckSentinel();
                _sidekickManager.Load( r );
                // This is where specialized typed ObservableDomain bind their roots.
                OnLoaded();
                // Calling PostDeserializationActions finalizes the object's graph.
                r.ImplementationServices.ExecutePostDeserializationActions();

                if( startTimer.HasValue ) timerRunning = startTimer.Value;
                if( !_sidekickManager.CreateWaitingSidekicks( monitor, ex => { }, true ) )
                {
                    var msg = "At least one critical error occurred while activating sidekicks. The error should be investigated since this may well be a blocking error.";
                    if( timerRunning )
                    {
                        timerRunning = false;
                        msg += " The TimeManager (that should have ran) has been stopped.";
                    }
                    monitor.Error( msg );
                }
                return timerRunning;
            }
            catch( Exception ex )
            {
                if( ex is InvalidDataException invalid && invalid.Message.StartsWith( "[See DebugDumpContext]" ) )
                {
                    using( monitor.OpenError( ex ) )
                    {
                        monitor.Trace( r.DebugDumpContext );
                    }
                }
                throw;
            }
            finally
            {
                _deserializeOrInitializing = false;
            }
        }

        /// <summary>
        /// Unloads this domain by clearing all internal state: it is ready to be reloaded
        /// or to be forgotten.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        void UnloadDomain( IActivityMonitor monitor )
        {
            Debug.Assert( _lock.IsWriteLockHeld );
            // Call Unload( false ) on all objects.
            for( int i = 0; i < _objectsListCount; ++i )
            {
                var o = _objects[i];
                if( o != null )
                {
                    Debug.Assert( !o.IsDestroyed );
                    o.Unload( false );
                }
            }
            // Empty _objects completely.
            Array.Clear( _objects, 0, _objectsListCount );
            _objectsListCount = 0;
            _actualObjectCount = 0;

            // Clears root list.
            _roots.Clear();

            // Free sidekicks and IObservableDomainActionTracker.
            _trackers.Clear();
            _sidekickManager.Clear( monitor );

            // Clears any internal objects.
            var internalObj = _firstInternalObject;
            while( internalObj != null )
            {
                internalObj.Unload( false );
                internalObj = internalObj.Next;
            }
            _firstInternalObject = _lastInternalObject = null;
            _internalObjectCount = 0;

            // Clears any time event objects.
            _timeManager.ClearAndStop( monitor );

            // Clears and read the properties index.
            _properties.Clear();
            _propertiesByIndex.Clear();

            _currentObjectUniquifier = 0;
        }
    }
}
