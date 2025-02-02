using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.Observable;

/// <summary>
/// Timer object that raises its <see cref="ObservableTimedEventBase{T}.Elapsed"/> event repeatedly.
/// The event time is based on the <see cref="DueTimeUtc"/>: we try to always raise the event based on a multiple
/// of the <see cref="IntervalMilliSeconds"/> from <see cref="DueTimeUtc"/>.
/// </summary>
[SerializationVersion(0)]
public sealed class ObservableTimer : ObservableTimedEventBase<ObservableTimerEventArgs>
{
    int _milliSeconds;
    bool _isActive;

    /// <summary>
    /// Initializes a new unnamed <see cref="ObservableTimer"/> bound to the current <see cref="ObservableDomain"/>.
    /// </summary>
    /// <param name="firstDueTimeUtc">The first time where the event must be fired.</param>
    /// <param name="intervalMilliSeconds">The interval in millisecond (defaults to 1 second). Must be positive.</param>
    /// <param name="isActive">False to initially deactivate this timer. By default, <see cref="IsActive"/> is true.</param>
    public ObservableTimer( DateTime firstDueTimeUtc, int intervalMilliSeconds = 1000, bool isActive = true )
    {
        CheckArguments( firstDueTimeUtc, intervalMilliSeconds );
        ExpectedDueTimeUtc = firstDueTimeUtc;
        _milliSeconds = intervalMilliSeconds;
        _isActive = isActive;
        ReusableArgs = new ObservableTimerEventArgs( this );
    }

    /// <summary>
    /// Initializes a new named <see cref="ObservableTimer"/> bound to the current <see cref="ObservableDomain"/>.
    /// </summary>
    /// <param name="name">Name of the timer. Can be null.</param>
    /// <param name="firstDueTimeUtc">The first time where the event must be fired.</param>
    /// <param name="intervalMilliSeconds">The interval in millisecond (defaults to 1 second). Must be positive.</param>
    /// <param name="isActive">False to initially deactivate this timer. By default, <see cref="IsActive"/> is true.</param>
    public ObservableTimer( string name, DateTime firstDueTimeUtc, int intervalMilliSeconds = 1000, bool isActive = true )
        : this( firstDueTimeUtc, intervalMilliSeconds, isActive )
    {
        Throw.CheckNotNullArgument( name );
        Name = name;
    }

    ObservableTimer( BinarySerialization.IBinaryDeserializer d, BinarySerialization.ITypeReadInfo info )
        : base( BinarySerialization.Sliced.Instance )
    {
        Debug.Assert( !IsDestroyed );
        _milliSeconds = d.Reader.ReadInt32();
        if( _milliSeconds < 0 )
        {
            _milliSeconds = -_milliSeconds;
            _isActive = true;
        }
        Name = d.Reader.ReadNullableString();
        ReusableArgs = new ObservableTimerEventArgs( this );
    }

    public static void Write( BinarySerialization.IBinarySerializer s, in ObservableTimer o )
    {
        Debug.Assert( !o.IsDestroyed );
        s.Writer.Write( o._isActive ? -o._milliSeconds : o._milliSeconds );
        s.Writer.WriteNullableString( o.Name );
    }

    private protected override bool GetIsActiveFlag() => _isActive;

    private protected override ObservableTimerEventArgs ReusableArgs { get; }

    /// <summary>
    /// Gets or sets whether this timer is active. Note that to be active <see cref="DueTimeUtc"/> must not be <see cref="Util.UtcMinValue"/>
    /// nor <see cref="Util.UtcMaxValue"/>.
    /// </summary>
    public new bool IsActive
    {
        get => base.IsActive;
        set
        {
            if( _isActive != value )
            {
                this.CheckDestroyed();
                if( _isActive = value )
                {
                    ExpectedDueTimeUtc = DateTime.UtcNow;
                }
                TimeManager.OnChanged( this );
            }
        }
    }

    /// <summary>
    /// Gets the next due time.
    /// If this is <see cref="Util.UtcMinValue"/> or <see cref="Util.UtcMaxValue"/>, then <see cref="IsActive">IsActive</see>
    /// is false.
    /// </summary>
    public DateTime DueTimeUtc => ExpectedDueTimeUtc;

    /// <summary>
    /// Gets or sets the <see cref="ObservableTimerMode"/> to apply.
    /// Defaults to <see cref="ObservableTimerMode.Relaxed"/> (<see cref="DueTimeUtc"/> is allowed to shift by any numer of <see cref="IntervalMilliSeconds"/> steps,
    /// only a warning is emitted).
    /// </summary>
    public ObservableTimerMode Mode { get; set; }

    /// <summary>
    /// Gets or sets an optional name for this timer.
    /// Default to null.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the interval, expressed in milliseconds, at which the <see cref="ObservableTimedEventBase{T}.Elapsed"/> event must repeatedly fire.
    /// The value must be greater than zero.
    /// </summary>
    public int IntervalMilliSeconds
    {
        get => _milliSeconds;
        set
        {
            if( _milliSeconds != value )
            {
                this.CheckDestroyed();
                if( ExpectedDueTimeUtc == Util.UtcMinValue || ExpectedDueTimeUtc == Util.UtcMaxValue )
                {
                    Throw.CheckOutOfRangeArgument( value > 0 );
                    _milliSeconds = value;
                }
                else Reconfigure( DueTimeUtc, value );
            }
        }
    }

    /// <summary>
    /// Reconfigures this <see cref="ObservableTimer"/> with a new <see cref="DueTimeUtc"/> and <see cref="IntervalMilliSeconds"/>.
    /// </summary>
    /// <param name="firstDueTimeUtc">The first time where the event must be fired.</param>
    /// <param name="intervalMilliSeconds">The interval in millisecond (defaults to 1 second).</param>
    public void Reconfigure( DateTime firstDueTimeUtc, int intervalMilliSeconds )
    {
        this.CheckDestroyed();
        CheckArguments( firstDueTimeUtc, intervalMilliSeconds );
        ExpectedDueTimeUtc = firstDueTimeUtc;
        _milliSeconds = intervalMilliSeconds;
        TimeManager.OnChanged( this );
    }

    private protected override void OnRaising( IActivityMonitor monitor, int deltaMilliSeconds, bool throwException )
    {
        if( deltaMilliSeconds >= _milliSeconds )
        {
            int stepCount = deltaMilliSeconds / _milliSeconds;
            var mode = Mode & ~ObservableTimerMode.ThrowException;
            throwException &= (Mode & ObservableTimerMode.ThrowException) != 0;

            if( stepCount == 1 )
            {
                var msg = "Lost one event.";
                if( mode == ObservableTimerMode.Critical ) RaiseError( monitor, mode, throwException, msg );
                else monitor.Warn( msg );
            }
            else
            {
                var msg = $"{stepCount} event(s) lost!";
                if( mode == ObservableTimerMode.Relaxed ) monitor.Warn( msg );
                else RaiseError( monitor, mode, throwException, msg );
            }
        }
    }

    internal override void OnAfterRaiseUnchanged( DateTime current, IActivityMonitor m )
    {
        Debug.Assert( IsActive );
        ExpectedDueTimeUtc = DueTimeUtc.AddMilliseconds( _milliSeconds );
    }

    /// <summary>
    /// Called whenever the next due time appear to be before or at current time: there must be an adjustment.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="forwarded">The forwarded time.</param>
    internal override void ForwardExpectedDueTime( IActivityMonitor monitor, DateTime forwarded )
    {
        Debug.Assert( ExpectedDueTimeUtc < forwarded );
        int stepCount = (int)Math.Ceiling( (forwarded - ExpectedDueTimeUtc).TotalMilliseconds / _milliSeconds );
        Debug.Assert( stepCount > 0, "Math.Ceiling does the job." );
        var mode = Mode & ~ObservableTimerMode.ThrowException;
        var throwEx = (Mode & ObservableTimerMode.ThrowException) != 0;
        var msg = $"{ToString()}: next due time '{ExpectedDueTimeUtc.ToString( "o" )}' has been forwarded to '{forwarded.ToString( "o" )}'. ";

        if( stepCount == 1 )
        {
            if( mode == ObservableTimerMode.AllowSlidingAdjustment )
            {
                msg += "No event lost.";
                ExpectedDueTimeUtc = forwarded;
            }
            else
            {
                msg += "One event lost.";
                ExpectedDueTimeUtc = ExpectedDueTimeUtc.AddMilliseconds( _milliSeconds );
                msg += $" DueTimeUtc aligned to {ExpectedDueTimeUtc.ToString( "o" )}.";
            }
            if( mode == ObservableTimerMode.Critical ) RaiseError( monitor, mode, throwEx, msg );
            else monitor.Warn( msg );
        }
        else
        {
            msg += $"{stepCount} event(s) lost!";
            ExpectedDueTimeUtc = ExpectedDueTimeUtc.AddMilliseconds( stepCount * _milliSeconds );
            msg += $" DueTimeUtc aligned to {ExpectedDueTimeUtc.ToString( "o" )}.";
            if( mode == ObservableTimerMode.Relaxed ) monitor.Warn( msg );
            else RaiseError( monitor, mode, throwEx, msg );
        }
    }

    static void RaiseError( IActivityMonitor monitor, ObservableTimerMode mode, bool throwEx, string msg )
    {
        msg += $" This is an error since Mode is {mode}.";
        if( throwEx ) throw new CKException( msg );
        else monitor.Error( msg );
    }

    /// <summary>
    /// Ensures that the <paramref name="firstDueTimeUtc"/> will occur after or on <paramref name="timeNowUtc"/>.
    /// </summary>
    /// <param name="timeNowUtc">Typically equals <see cref="DateTime.UtcNow"/>. Must be in Utc.</param>
    /// <param name="firstDueTimeUtc">The first due time. Must be in Utc. When <see cref="Util.UtcMinValue"/> or <see cref="Util.UtcMaxValue"/> it is returned as-is.</param>
    /// <param name="intervalMilliSeconds">The interval. Must be positive.</param>
    /// <returns>The adjusted first due time, necessarily after the <paramref name="timeNowUtc"/>.</returns>
    public static DateTime AdjustNextDueTimeUtc( DateTime timeNowUtc, DateTime firstDueTimeUtc, int intervalMilliSeconds )
    {
        CheckArguments( firstDueTimeUtc, intervalMilliSeconds );
        if( firstDueTimeUtc != Util.UtcMinValue && firstDueTimeUtc != Util.UtcMaxValue )
        {
            if( firstDueTimeUtc < timeNowUtc )
            {
                int adjust = ((int)Math.Ceiling( (timeNowUtc - firstDueTimeUtc).TotalMilliseconds / intervalMilliSeconds )) * intervalMilliSeconds;
                firstDueTimeUtc = firstDueTimeUtc.AddMilliseconds( adjust );
            }
        }
        return firstDueTimeUtc;
    }

    static void CheckArguments( DateTime firstDueTimeUtc, int intervalMilliSeconds )
    {
        Throw.CheckArgument( firstDueTimeUtc.Kind == DateTimeKind.Utc );
        Throw.CheckOutOfRangeArgument( intervalMilliSeconds > 0 );
    }

    /// <summary>
    /// Overridden to return the <see cref="Name"/> of this timer.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{(IsDestroyed ? "[Destroyed]" : "")}ObservableTimer '{Name ?? "<no name>"}' ({IntervalMilliSeconds} ms)";

}
