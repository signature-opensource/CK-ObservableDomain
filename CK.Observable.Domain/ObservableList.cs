using System;
using System.Collections;
using System.Collections.Generic;

namespace CK.Observable
{
    /// <summary>
    /// Implements a simple observable <see cref="List{T}"/>.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    [SerializationVersion(0)]
    public class ObservableList<T> : ObservableObject, IList<T>, IReadOnlyList<T>
    {
        readonly List<T> _list;
        ObservableEventHandler<ListSetAtEvent> _itemSet;
        ObservableEventHandler<ListInsertEvent> _itemInserted;
        ObservableEventHandler<ListRemoveAtEvent> _itemRemovedAt;
        ObservableEventHandler<CollectionClearEvent> _collectionCleared;

        /// <summary>
        /// Raised when an existing item has been updated by <see cref="this[int]"/> to a different value.
        /// </summary>
        public event SafeEventHandler<ListSetAtEvent> ItemSet
        {
            add => _itemSet.Add( value, nameof( ItemSet ) );
            remove => _itemSet.Remove( value );
        }

        /// <summary>
        /// Raised by <see cref="Add(T)"/> or <see cref="Insert(int, T)"/>.
        /// </summary>
        public event SafeEventHandler<ListInsertEvent> ItemInserted
        {
            add => _itemInserted.Add( value, nameof( ItemInserted ) );
            remove => _itemInserted.Remove( value );
        }

        /// <summary>
        /// Raised by <see cref="Remove(T)"/> or <see cref="RemoveAt(int)"/>.
        /// </summary>
        public event SafeEventHandler<ListRemoveAtEvent> ItemRemovedAt
        {
            add => _itemRemovedAt.Add( value, nameof( ItemRemovedAt ) );
            remove => _itemRemovedAt.Remove( value );
        }

        /// <summary>
        /// Raised by <see cref="Clear"/>.
        /// </summary>
        public event SafeEventHandler<CollectionClearEvent> CollectionCleared
        {
            add => _collectionCleared.Add( value, nameof( CollectionCleared ) );
            remove => _collectionCleared.Remove( value );
        }

        /// <summary>
        /// Initializes a new empty observable list.
        /// </summary>
        public ObservableList()
        {
            _list = new List<T>();
        }

        /// <summary>
        /// Deserailization constructor.
        /// </summary>
        /// <param name="d">The deserialization context.</param>
        protected ObservableList( IBinaryDeserializerContext d )
            : base( d )
        {
            var r = d.StartReading().Reader;
            _list = (List<T>)r.ReadObject();
            _itemSet = new ObservableEventHandler<ListSetAtEvent>( r );
            _itemInserted = new ObservableEventHandler<ListInsertEvent>( r );
            _itemRemovedAt = new ObservableEventHandler<ListRemoveAtEvent>( r );
            _collectionCleared = new ObservableEventHandler<CollectionClearEvent>( r );
        }

        /// <summary>
        /// The serialization method.
        /// </summary>
        /// <param name="s">The target binary serializer.</param>
        void Write( BinarySerializer s )
        {
            s.WriteObject( _list );
            _itemSet.Write( s );
            _itemInserted.Write( s );
            _itemRemovedAt.Write( s );
            _collectionCleared.Write( s );
        }

        /// <summary>
        /// Gets or sets an item at a given position.
        /// Note that <paramref name="index"/> can be equal to <see cref="Count"/>: the item is added.
        /// If the setting is actually a no-change (the new value is the same as the current one
        /// according to <see cref="EqualityComparer{T}.Default"/>), then no event is raised.
        /// </summary>
        /// <param name="index">The target index. Must be greater than 0 and at most equal to <see cref="Count"/>.</param>
        /// <returns>The get or set item.</returns>
        public T this[int index]
        {
            get => _list[index];
            set
            {
                if( index == _list.Count ) Add( value );
                else
                {
                    var current = _list[index];
                    if( !EqualityComparer<T>.Default.Equals( current, value ) )
                    {
                        var e = ActualDomain.OnListSetAt( this, index, value );
                        _list[index] = value;
                        if( e != null && _itemSet.HasHandlers ) _itemSet.Raise( this, e );
                    }
                }
            }
        }

        /// <summary>
        /// Gets the number of items contained in this list.
        /// </summary>
        public int Count => _list.Count;

        internal override ObjectExportedKind ExportedKind => ObjectExportedKind.List;

        bool ICollection<T>.IsReadOnly => false;

        /// <summary>
        /// Adds a new item.
        /// </summary>
        /// <param name="item">Item to add.</param>
        public void Add( T item )
        {
            var e = ActualDomain.OnListInsert( this, _list.Count, item );
            _list.Add( item );
            if( e != null && _itemInserted.HasHandlers ) _itemInserted.Raise( this, e );
        }

        /// <summary>
        /// Adds multiple items at once (simple helper that calls <see cref="Add(T)"/> for each of them).
        /// </summary>
        /// <param name="items">Set of items to append.</param>
        public void AddRange( IEnumerable<T> items )
        {
            foreach( var i in items ) Add( i );
        }

        /// <summary>
        /// Clears this list of all its items.
        /// </summary>
        public void Clear()
        {
            if( _list.Count > 0 )
            {
                var e = ActualDomain.OnCollectionClear( this );
                _list.Clear();
                if( e != null && _collectionCleared.HasHandlers ) _collectionCleared.Raise( this, e );
            }
        }

        /// <summary>
        /// Inserts an item at a given position.
        /// </summary>
        /// <param name="index">The target position.</param>
        /// <param name="item">The item to insert.</param>
        public void Insert( int index, T item )
        {
            var e = ActualDomain.OnListInsert( this, index, item );
            _list.Insert( index, item );
            if( e != null && _itemInserted.HasHandlers ) _itemInserted.Raise( this, e );
        }

        /// <summary>
        /// Inserts multiple items at once (simple helper that calls <see cref="Insert(int,T)"/> for each of them).
        /// </summary>
        /// <param name="index">Index of the insertion.</param>
        /// <param name="items">Set of items to append.</param>
        public void InsertRange( int index, IEnumerable<T> items )
        {
            foreach( var i in items ) Insert( index++, i );
        }

        /// <summary>
        /// Removes an item if it is found (<see cref="IndexOf(T)"/> is called).
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if item has been successfully removed.</returns>
        public bool Remove( T item )
        {
            int index = _list.IndexOf( item );
            if( index >= 0 )
            {
                RemoveAt( index );
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes an item at a given position.
        /// </summary>
        /// <param name="index">The index to remove.</param>
        public void RemoveAt( int index )
        {
            var e = ActualDomain.OnListRemoveAt( this, index );
            _list.RemoveAt( index );
            if( e != null && _itemRemovedAt.HasHandlers ) _itemRemovedAt.Raise( this, e );
        }

        /// <summary>
        /// Gets whether the given item can be found in this list.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>True if the item can be found in this list.</returns>
        public bool Contains( T item ) => _list.Contains( item );

        /// <summary>
        /// Copies the entire list to a compatible one-dimensional
        ///  array, starting at the specified index of the target array.
        /// </summary>
        /// <param name="array">
        /// The one-dimensional System.Array that is the destination of the elements copied
        /// from this list. The System.Array must have zero-based indexing.
        /// </param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins. Must not be negative.</param>
        public void CopyTo( T[] array, int arrayIndex ) => _list.CopyTo( array, arrayIndex );

        /// <summary>
        /// Returns an enumerator that iterates through this list.
        /// </summary>
        /// <returns>The set of items.</returns>
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        /// <summary>
        /// Searches for the specified object and returns the zero-based index of the first
        /// occurrence within this entire list.
        /// </summary>
        /// <param name="item">The object to locate in this list. The value can be null for reference types.</param>
        /// <returns>
        /// The zero-based index of the first occurrence of item within the entire list, if found; otherwise, –1.
        /// </returns>
        public int IndexOf( T item ) => _list.IndexOf( item );

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
        
    }
}
