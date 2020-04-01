using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.Observable.League
{
    public partial class ObservableLeague
    {
        class Shell : IObservableDomainLoader, IObservableDomainShell, IManagedDomain
        {
            readonly private protected StreamStoreClient Client;
            readonly SemaphoreSlim? _loadLock;
            readonly IActivityMonitor _initialMonitor;
            Type? _domainType;
            Type[] _rootTypes;
            int _refCount;
            ObservableDomain? _domain;

            private protected class IndependentShell : IObservableDomainShell
            {
                readonly protected IObservableDomainShell Shell;
                readonly IActivityMonitor _monitor;

                public IndependentShell( Shell s, IActivityMonitor m )
                {
                    Shell = s;
                    _monitor = m;
                }

                string IObservableDomainShellBase.DomainName => Shell.DomainName;

                bool IObservableDomainShellBase.IsDestroyed => Shell.IsDestroyed;

                Task<bool> IObservableDomainShellBase.SaveAsync( IActivityMonitor monitor )
                {
                    return Shell.SaveAsync( monitor );
                }

                ValueTask IObservableDomainShellBase.DisposeAsync( IActivityMonitor monitor ) => Shell.DisposeAsync( monitor );

                ValueTask IAsyncDisposable.DisposeAsync() => Shell.DisposeAsync( _monitor );

                Task<TransactionResult> IObservableDomainShell.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain>? actions, int millisecondsTimeout )
                {
                    return Shell.ModifyAsync( monitor, actions, millisecondsTimeout );
                }

                void IObservableDomainShell.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain> reader, int millisecondsTimeout )
                {
                    Shell.Read( monitor, reader, millisecondsTimeout );
                }

                T IObservableDomainShell.Read<T>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain, T> reader, int millisecondsTimeout )
                {
                    return Shell.Read( monitor, reader, millisecondsTimeout );
                }

                Task<(TransactionResult, Exception)> IObservableDomainShell.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain>? actions, int millisecondsTimeout )
                {
                    return Shell.SafeModifyAsync( monitor, actions, millisecondsTimeout );
                }

            }

            private protected Shell( IActivityMonitor monitor,
                   string domainName,
                   IStreamStore store,
                   IReadOnlyList<string> rootTypeNames,
                   Type[] rootTypes,
                   Type? domainType )
            {
                if( (_domainType = domainType) != null )
                {
                    _loadLock = new SemaphoreSlim( 1 );
                }
                _rootTypes = rootTypes;
                RootTypes = rootTypeNames;
                _initialMonitor = monitor;
                Client = new StreamStoreClient( domainName, store );
            }

            /// <summary>
            /// Attempts to synthesize the Shell type (with the root types).
            /// </summary>
            /// <param name="monitor">The monitor to use.</param>
            /// <param name="domainName">The name of the domain.</param>
            /// <param name="store">The persistent store.</param>
            /// <param name="rootTypeNames">The root types.</param>
            internal static Shell Create( IActivityMonitor monitor, string domainName, IStreamStore store, IReadOnlyList<string> rootTypeNames )
            {
                Type? domainType = null;
                Type[] rootTypes;
                if( rootTypeNames.Count == 0 )
                {
                    domainType = typeof( ObservableDomain );
                    rootTypes = Type.EmptyTypes;
                }
                else
                {
                    bool success = true;
                    rootTypes = new Type[rootTypeNames.Count];
                    for( int i = 0; i < rootTypeNames.Count; ++i )
                    {
                        if( (rootTypes[i] = SimpleTypeFinder.WeakResolver( rootTypeNames[i], false )) == null )
                        {
                            monitor.Error( $"Unable to resolve root type '{rootTypeNames[i]}' for domain '{domainName}'." );
                            success = false;
                        }
                    }
                    if( success )
                    {
                        Type shellType = rootTypes.Length switch
                        {
                            1 => typeof( Shell<> ).MakeGenericType( rootTypes ),
                            2 => typeof( Shell<,> ).MakeGenericType( rootTypes ),
                            3 => typeof( Shell<,,> ).MakeGenericType( rootTypes ),
                            _ => typeof( Shell<,,,> ).MakeGenericType( rootTypes )
                        };
                        return (Shell)Activator.CreateInstance( shellType, monitor, domainName, store, rootTypeNames, rootTypes );
                    }
                }
                // The domainType is null if the type resilution failed.
                return new Shell( monitor, domainName, store, rootTypeNames, rootTypes, domainType );
            }

            public string DomainName => Client.DomainName;

            public bool IsDestroyed { get; private set; }

            public IReadOnlyList<string> RootTypes { get; }

            public bool IsLoadable => _domainType != null;

            public bool IsLoaded => _refCount != 0;

            internal bool ClosingLeague { get; set; }

            public ManagedDomainOptions Options
            {
                get => new ManagedDomainOptions(
                            c: Client.CompressionKind,
                            snapshotSaveDelay: TimeSpan.FromMilliseconds( Client.SnapshotSaveDelay ),
                            snapshotKeepDuration: Client.SnapshotKeepDuration,
                            snapshotMaximalTotalKiB: Client.SnapshotMaximalTotalKiB,
                            eventKeepDuration: Client.TransactClient.KeepDuration,
                            eventKeepLimit: Client.TransactClient.KeepLimit );
                set
                {
                    Client.CompressionKind = value.CompressionKind;
                    Client.SnapshotSaveDelay = (int)value.SnapshotSaveDelay.TotalMilliseconds;
                    Client.SnapshotKeepDuration = value.SnapshotKeepDuration;
                    Client.SnapshotMaximalTotalKiB = value.SnapshotMaximalTotalKiB;
                    Client.TransactClient.KeepDuration = value.ExportedEventKeepDuration;
                    Client.TransactClient.KeepLimit = value.ExportedEventKeepLimit;
                }
            }

            void IManagedDomain.Destroy( IActivityMonitor monitor, IManagedLeague league )
            {
                IsDestroyed = true;
                league.OnDestroy( monitor, this );
            }

            protected ObservableDomain LoadedDomain => _domain!;

            Task<bool> IObservableDomainShellBase.SaveAsync( IActivityMonitor m )
            {
                return Client.SaveAsync( m );
            }

            ValueTask IObservableDomainShellBase.DisposeAsync( IActivityMonitor monitor ) => DoShellDisposeAsync( monitor );

            ValueTask IAsyncDisposable.DisposeAsync() => DoShellDisposeAsync( _initialMonitor );

            async ValueTask DoShellDisposeAsync( IActivityMonitor monitor )
            {
                if( !IsLoadable ) throw new ObjectDisposedException( nameof( IObservableDomainShell ) );
                await _loadLock!.WaitAsync();
                if( --_refCount < 0 ) throw new ObjectDisposedException( nameof( IObservableDomainShell ) );
                if( _refCount == 0 )
                {
                    try
                    {
                        if( _domain != null )
                        {
                            await( IsDestroyed ? Client.ArchiveAsync( monitor ) : Client.SaveAsync( monitor ) );
                            _domain.Dispose( monitor );
                        }
                    }
                    finally
                    {
                        _domain = null;
                    }
                }
                _loadLock.Release();
            }

            public async Task<IObservableDomainShell?> LoadAsync( IActivityMonitor monitor )
            {
                if( !IsLoadable || IsDestroyed || ClosingLeague ) return null;
                await _loadLock!.WaitAsync();
                if( ++_refCount == 1 )
                {
                    Debug.Assert( _domain == null );
                    try
                    {
                        var d = CreateDomain( monitor );
                        await Client.InitializeAsync( monitor, d );
                        _domain = d;
                    }
                    catch( Exception ex )
                    {
                        Interlocked.Decrement( ref _refCount );
                        monitor.Error( $"Unable to instanciate and load '{DomainName}'.", ex );
                        _refCount = 0;
                    }
                }
                _loadLock.Release();
                if( _domain == null ) return null;
                if( _initialMonitor == monitor ) return this;
                return CreateIndependentShell( monitor );
            }

            private protected virtual ObservableDomain CreateDomain( IActivityMonitor monitor ) => new ObservableDomain( monitor, DomainName, Client );

            private protected virtual IObservableDomainShell CreateIndependentShell( IActivityMonitor monitor ) => new IndependentShell( this, monitor );

            public async Task<IObservableDomainShell<T>?> LoadAsync<T>( IActivityMonitor monitor ) where T : ObservableRootObject
            {
                if( !IsLoadable || IsDestroyed || ClosingLeague ) return null;
                if( _rootTypes.Length != 1 || !typeof( T ).IsAssignableFrom( _rootTypes[0] ) )
                {
                    monitor.Error( $"Typed domain error: Domain {DomainName} cannot be loaded as a ObservableDomain<{typeof( T ).FullName}> (actual type is '{_domainType}')." );
                    return null;
                }
                return (IObservableDomainShell<T>?)await LoadAsync( monitor );
            }

            public async Task<IObservableDomainShell<T1,T2>?> LoadAsync<T1,T2>( IActivityMonitor monitor )
                where T1 : ObservableRootObject
                where T2 : ObservableRootObject
            {
                if( !IsLoadable || IsDestroyed || ClosingLeague ) return null;
                if( _rootTypes.Length != 2
                    || !typeof( T1 ).IsAssignableFrom( _rootTypes[0] )
                    || !typeof( T2 ).IsAssignableFrom( _rootTypes[1] ) )
                {
                    monitor.Error( $"Typed domain error: Domain {DomainName} cannot be loaded as a ObservableDomain<{typeof( T1 ).FullName},{typeof( T2 ).FullName}> (actual type is '{_domainType}')." );
                    return null;
                }
                return (IObservableDomainShell<T1,T2>?)await LoadAsync( monitor );
            }

            public async Task<IObservableDomainShell<T1,T2,T3>?> LoadAsync<T1,T2,T3>( IActivityMonitor monitor )
                where T1 : ObservableRootObject
                where T2 : ObservableRootObject
                where T3 : ObservableRootObject
            {
                if( !IsLoadable || IsDestroyed || ClosingLeague ) return null;
                if( _rootTypes.Length != 3
                    || !typeof( T1 ).IsAssignableFrom( _rootTypes[0] )
                    || !typeof( T2 ).IsAssignableFrom( _rootTypes[1] )
                    || !typeof( T3 ).IsAssignableFrom( _rootTypes[2] ) )
                {
                    monitor.Error( $"Typed domain error: Domain {DomainName} cannot be loaded as a ObservableDomain<{typeof( T1 ).Name},{typeof( T2 ).Name},{typeof( T3 ).Name}> (actual type is '{_domainType}')." );
                    return null;
                }
                return (IObservableDomainShell<T1, T2, T3>?)await LoadAsync( monitor );
            }

            public async Task<IObservableDomainShell<T1,T2,T3,T4>?> LoadAsync<T1,T2, T3, T4>( IActivityMonitor monitor )
                where T1 : ObservableRootObject
                where T2 : ObservableRootObject
                where T3 : ObservableRootObject
                where T4 : ObservableRootObject
            {
                if( !IsLoadable || IsDestroyed || ClosingLeague ) return null;
                if( _rootTypes.Length != 4
                    || !typeof( T1 ).IsAssignableFrom( _rootTypes[0] )
                    || !typeof( T2 ).IsAssignableFrom( _rootTypes[1] )
                    || !typeof( T3 ).IsAssignableFrom( _rootTypes[2] )
                    || !typeof( T4 ).IsAssignableFrom( _rootTypes[3] ) )
                {
                    monitor.Error( $"Typed domain error: Domain {DomainName} cannot be loaded as a ObservableDomain<{typeof( T1 ).Name},{typeof( T2 ).Name},{typeof( T3 ).Name},{typeof( T4 ).Name}> (actual type is '{_domainType}')." );
                    return null;
                }
                return (IObservableDomainShell<T1, T2, T3, T4>?)await LoadAsync( monitor );
            }

            #region IObservableDomainShell (non generic) implementation
            Task<(TransactionResult, Exception)> IObservableDomainShell.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.SafeModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            Task<TransactionResult> IObservableDomainShell.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.ModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            void IObservableDomainShell.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    reader( monitor, d );
                }
            }

            T IObservableDomainShell.Read<T>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain, T> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    return reader( monitor, d );
                }
            }
            #endregion

        }

        class Shell<T> : Shell, IObservableDomainShell<T> where T : ObservableRootObject
        {
            public Shell( IActivityMonitor monitor,
                          string domainName,
                          IStreamStore store,
                          IReadOnlyList<string> rootTypeNames,
                          Type[] rootTypes )
                : base( monitor, domainName, store, rootTypeNames, rootTypes, typeof(ObservableDomain<T>) )
            {
            }

            private protected override ObservableDomain CreateDomain( IActivityMonitor monitor ) => new ObservableDomain<T>( monitor, DomainName, Client );

            class IndependentShellT : IndependentShell, IObservableDomainShell<T>
            {
                public IndependentShellT( Shell<T> s, IActivityMonitor m )
                    : base( s, m )
                {
                }

                new IObservableDomainShell<T> Shell => (Shell<T>)base.Shell;
 
                Task<TransactionResult> IObservableDomainAccess<T>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T>>? actions, int millisecondsTimeout )
                {
                    return Shell.ModifyAsync( monitor, actions, millisecondsTimeout );
                }

                void IObservableDomainAccess<T>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T>> reader, int millisecondsTimeout )
                {
                    Shell.Read( monitor, reader, millisecondsTimeout );
                }

                TInfo IObservableDomainAccess<T>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T>, TInfo> reader, int millisecondsTimeout )
                {
                    return Shell.Read( monitor, reader, millisecondsTimeout );
                }

                Task<(TransactionResult, Exception)> IObservableDomainAccess<T>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T>>? actions, int millisecondsTimeout )
                {
                    return Shell.SafeModifyAsync( monitor, actions, millisecondsTimeout );
                }
            }

            private protected override IObservableDomainShell CreateIndependentShell( IActivityMonitor monitor ) => new IndependentShellT( this, monitor );

            new ObservableDomain<T> LoadedDomain => (ObservableDomain<T>)base.LoadedDomain;

            Task<(TransactionResult, Exception)> IObservableDomainAccess<T>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.SafeModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            Task<TransactionResult> IObservableDomainAccess<T>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.ModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            void IObservableDomainAccess<T>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T>> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    reader( monitor, d );
                }
            }

            TInfo IObservableDomainAccess<T>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T>, TInfo> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    return reader( monitor, d );
                }
            }

        }

        class Shell<T1,T2> : Shell, IObservableDomainShell<T1,T2>
            where T1 : ObservableRootObject
            where T2 : ObservableRootObject
        {
            public Shell( IActivityMonitor monitor,
                          string domainName,
                          IStreamStore store,
                          IReadOnlyList<string> rootTypeNames,
                          Type[] rootTypes )
                : base( monitor, domainName, store, rootTypeNames, rootTypes, typeof( ObservableDomain<T1,T2> ) )
            {
            }

            private protected override ObservableDomain CreateDomain( IActivityMonitor monitor ) => new ObservableDomain<T1,T2>( monitor, DomainName, Client );

            class IndependentShellTT : IndependentShell, IObservableDomainShell<T1, T2>
            {
                public IndependentShellTT( Shell<T1,T2> s, IActivityMonitor m )
                    : base( s, m )
                {
                }

                new IObservableDomainShell<T1,T2> Shell => (Shell<T1, T2>)base.Shell;

                Task<TransactionResult> IObservableDomainShell<T1, T2>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2>>? actions, int millisecondsTimeout )
                {
                    return Shell.ModifyAsync( monitor, actions, millisecondsTimeout );
                }

                void IObservableDomainShell<T1, T2>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2>> reader, int millisecondsTimeout )
                {
                    Shell.Read( monitor, reader, millisecondsTimeout );
                }

                TInfo IObservableDomainShell<T1, T2>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T1, T2>, TInfo> reader, int millisecondsTimeout )
                {
                    return Shell.Read( monitor, reader, millisecondsTimeout );
                }

                Task<(TransactionResult, Exception)> IObservableDomainShell<T1, T2>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2>>? actions, int millisecondsTimeout )
                {
                    return Shell.SafeModifyAsync( monitor, actions, millisecondsTimeout );
                }
            }

            private protected override IObservableDomainShell CreateIndependentShell( IActivityMonitor monitor ) => new IndependentShellTT( this, monitor );


            new ObservableDomain<T1, T2> LoadedDomain => (ObservableDomain<T1, T2>)base.LoadedDomain;

            Task<(TransactionResult, Exception)> IObservableDomainShell<T1, T2>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.SafeModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            Task<TransactionResult> IObservableDomainShell<T1, T2>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.ModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            void IObservableDomainShell<T1, T2>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2>> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    reader( monitor, d );
                }
            }

            TInfo IObservableDomainShell<T1, T2>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T1, T2>, TInfo> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    return reader( monitor, d );
                }
            }

        }

        class Shell<T1, T2, T3> : Shell, IObservableDomainShell<T1, T2, T3>
            where T1 : ObservableRootObject
            where T2 : ObservableRootObject
            where T3 : ObservableRootObject
        {
            public Shell( IActivityMonitor monitor,
                          string domainName,
                          IStreamStore store,
                          IReadOnlyList<string> rootTypeNames,
                          Type[] rootTypes )
                : base( monitor, domainName, store, rootTypeNames, rootTypes, typeof( ObservableDomain<T1, T2, T3> ) )
            {
            }

            private protected override ObservableDomain CreateDomain( IActivityMonitor monitor ) => new ObservableDomain<T1, T2, T3>( monitor, DomainName, Client );

            class IndependentShellTTT : IndependentShell, IObservableDomainShell<T1, T2, T3>
            {
                public IndependentShellTTT( Shell<T1, T2, T3> s, IActivityMonitor m )
                    : base( s, m )
                {
                }

                new IObservableDomainShell<T1, T2, T3> Shell => (Shell<T1, T2, T3>)base.Shell;

                Task<TransactionResult> IObservableDomainShell<T1, T2, T3>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3>>? actions, int millisecondsTimeout )
                {
                    return Shell.ModifyAsync( monitor, actions, millisecondsTimeout );
                }

                void IObservableDomainShell<T1, T2, T3>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3>> reader, int millisecondsTimeout )
                {
                    Shell.Read( monitor, reader, millisecondsTimeout );
                }

                TInfo IObservableDomainShell<T1, T2, T3>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T1, T2, T3>, TInfo> reader, int millisecondsTimeout )
                {
                    return Shell.Read( monitor, reader, millisecondsTimeout );
                }

                Task<(TransactionResult, Exception)> IObservableDomainShell<T1, T2, T3>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3>>? actions, int millisecondsTimeout )
                {
                    return Shell.SafeModifyAsync( monitor, actions, millisecondsTimeout );
                }
            }

            private protected override IObservableDomainShell CreateIndependentShell( IActivityMonitor monitor ) => new IndependentShellTTT( this, monitor );


            new ObservableDomain<T1, T2, T3> LoadedDomain => (ObservableDomain<T1, T2, T3>)base.LoadedDomain;

            Task<(TransactionResult, Exception)> IObservableDomainShell<T1, T2, T3>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.SafeModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            Task<TransactionResult> IObservableDomainShell<T1, T2, T3>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.ModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            void IObservableDomainShell<T1, T2, T3>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3>> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    reader( monitor, d );
                }
            }

            TInfo IObservableDomainShell<T1, T2, T3>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T1, T2, T3>, TInfo> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    return reader( monitor, d );
                }
            }

        }

        class Shell<T1, T2, T3, T4> : Shell, IObservableDomainShell<T1, T2, T3, T4>
            where T1 : ObservableRootObject
            where T2 : ObservableRootObject
            where T3 : ObservableRootObject
            where T4 : ObservableRootObject
        {
            public Shell( IActivityMonitor monitor,
                          string domainName,
                          IStreamStore store,
                          IReadOnlyList<string> rootTypeNames,
                          Type[] rootTypes )
                : base( monitor, domainName, store, rootTypeNames, rootTypes, typeof( ObservableDomain<T1, T2, T3, T4> ) )
            {
            }

            private protected override ObservableDomain CreateDomain( IActivityMonitor monitor ) => new ObservableDomain<T1, T2, T3, T4>( monitor, DomainName, Client );

            class IndependentShellTTTT : IndependentShell, IObservableDomainShell<T1, T2, T3, T4>
            {
                public IndependentShellTTTT( Shell<T1, T2, T3, T4> s, IActivityMonitor m )
                    : base( s, m )
                {
                }

                new IObservableDomainShell<T1, T2, T3, T4> Shell => (Shell<T1, T2, T3, T4>)base.Shell;

                Task<TransactionResult> IObservableDomainShell<T1, T2, T3, T4>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>>? actions, int millisecondsTimeout )
                {
                    return Shell.ModifyAsync( monitor, actions, millisecondsTimeout );
                }

                void IObservableDomainShell<T1, T2, T3, T4>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>> reader, int millisecondsTimeout )
                {
                    Shell.Read( monitor, reader, millisecondsTimeout );
                }

                TInfo IObservableDomainShell<T1, T2, T3, T4>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>, TInfo> reader, int millisecondsTimeout )
                {
                    return Shell.Read( monitor, reader, millisecondsTimeout );
                }

                Task<(TransactionResult, Exception)> IObservableDomainShell<T1, T2, T3, T4>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>>? actions, int millisecondsTimeout )
                {
                    return Shell.SafeModifyAsync( monitor, actions, millisecondsTimeout );
                }
            }

            private protected override IObservableDomainShell CreateIndependentShell( IActivityMonitor monitor ) => new IndependentShellTTTT( this, monitor );


            new ObservableDomain<T1, T2, T3, T4> LoadedDomain => (ObservableDomain<T1, T2, T3, T4>)base.LoadedDomain;

            Task<(TransactionResult, Exception)> IObservableDomainShell<T1, T2, T3, T4>.SafeModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.SafeModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            Task<TransactionResult> IObservableDomainShell<T1, T2, T3, T4>.ModifyAsync( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>>? actions, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                return d.ModifyAsync( monitor, () => actions?.Invoke( monitor, d ), millisecondsTimeout );
            }

            void IObservableDomainShell<T1, T2, T3, T4>.Read( IActivityMonitor monitor, Action<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    reader( monitor, d );
                }
            }

            TInfo IObservableDomainShell<T1, T2, T3, T4>.Read<TInfo>( IActivityMonitor monitor, Func<IActivityMonitor, IObservableDomain<T1, T2, T3, T4>, TInfo> reader, int millisecondsTimeout )
            {
                var d = LoadedDomain;
                using( d.AcquireReadLock( millisecondsTimeout ) )
                {
                    return reader( monitor, d );
                }
            }

        }

    }
}
