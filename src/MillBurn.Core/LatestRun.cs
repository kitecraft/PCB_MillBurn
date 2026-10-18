namespace MillBurn.Core;

/// <summary>
/// One run at a time, where a new one cancels the last.
///
/// An edit that has been superseded is not worth finishing: the operator has already moved the
/// stock, changed the tool or switched the layer, and the answer being computed describes a board
/// that no longer exists. Letting it run to completion costs a core and, worse, can arrive after
/// the newer answer and overwrite it.
///
/// <see cref="Begin"/> hands out the token the next run should carry and cancels the one before it.
/// Every caller is expected to abandon its work when its own token is cancelled rather than to
/// check whether it is still the latest — the token *is* that question, asked in the one place
/// where the answer cannot go stale between the check and the use of it.
/// </summary>
public sealed class LatestRun : IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    /// <summary>
    /// True while a run is still working: begun, and not yet finished, superseded, cancelled or
    /// stopped.
    ///
    /// A *stopped* run reads false here while it is still the latest and about to publish — the
    /// work is over, the announcement is not. Callers gating busy indicators on this get the
    /// right answer (nothing is computing any more); a caller that needs "has the stopped run
    /// said its piece yet" is asking about <see cref="Finish"/>, which no property can answer
    /// without the answer going stale in the caller's hand.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _current is { IsCancellationRequested: false };
            }
        }
    }

    /// <summary>
    /// Cancels whatever was running and returns the token for the run replacing it.
    ///
    /// The returned token is already cancelled if this has been disposed, so a caller that races
    /// with shutdown abandons its work rather than starting it.
    /// </summary>
    public CancellationToken Begin()
    {
        lock (_gate)
        {
            var previous = _current;
            _current = _disposed ? null : new CancellationTokenSource();

            // Cancel after the swap, never before: a continuation that runs inline on Cancel would
            // otherwise see the run it is replacing still installed as the current one.
            Cancel(previous);

            return _disposed ? new CancellationToken(canceled: true) : _current!.Token;
        }
    }

    /// <summary>
    /// Marks a run finished, and says whether it was still the current one.
    ///
    /// A run that has already been superseded must not clear the newer run's state — which is the
    /// whole reason this takes the token rather than simply clearing whatever is there. The answer
    /// is returned rather than merely acted on because the caller has the same question to ask
    /// about its own work: a superseded run must not publish its result either, and finishing is
    /// the one place where "am I still the latest?" can be asked without the answer going stale
    /// between the asking and the use of it.
    /// </summary>
    /// <returns>True if this was the current run; false if something had already replaced it.</returns>
    public bool Finish(CancellationToken token)
    {
        lock (_gate)
        {
            if (_current is null || _current.Token != token)
            {
                return false;
            }

            _current.Dispose();
            _current = null;
            return true;
        }
    }

    /// <summary>Cancels the current run and leaves nothing running.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            Cancel(_current);
            _current = null;
        }
    }

    /// <summary>
    /// Asks the current run to stop, without disowning it.
    ///
    /// The difference from <see cref="Cancel()"/> is the whole feature (6.54). A run that was
    /// *superseded or abandoned* must publish nothing — the newer run owns the status line, and
    /// an invalidated preview describes a board that no longer exists — so <see cref="Cancel()"/>
    /// clears the current run and <see cref="Finish"/> then refuses it. A run the operator
    /// *stopped* is a different thing: it is still the latest, nobody is replacing its answer,
    /// and what it has so far is worth showing as the partial answer it is, named as one. So a
    /// stop cancels the token and leaves the run installed: its own <see cref="Finish"/> still
    /// returns true, and the "stopped" status line and banner — written the day the flags were,
    /// reachable for the first time today — are its to publish. A successor still wins: a stop
    /// followed by <see cref="Begin"/> is supersession, exactly as if nothing had stopped.
    /// </summary>
    /// <returns>True if there was a run to stop; false when nothing was running, so a caller can
    /// leave a keystroke unswallowed.</returns>
    public bool Stop()
    {
        lock (_gate)
        {
            if (_current is not { IsCancellationRequested: false })
            {
                return false;
            }

            Cancel(_current);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Cancel(_current);
            _current = null;
        }
    }

    /// <summary>
    /// Cancels a run without disposing what it handed out.
    ///
    /// The token from <see cref="Begin"/> is documented as the thing every caller carries, and a
    /// superseded caller is by definition still holding one on another thread. Disposing the source
    /// the instant it is cancelled turns that caller's next use of the token —
    /// <c>Register</c>, <c>WaitHandle</c>, passing it to <c>Task.Delay</c> — into an
    /// <see cref="ObjectDisposedException"/> instead of the cancellation it asked for. Polling
    /// survives disposal, so nothing here needs it today; the next thing moved onto the pool would
    /// have found out the hard way. A cancelled source holds no timer and no handle unless one was
    /// asked for, so letting the collector take it costs nothing.
    /// </summary>
    private static void Cancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already gone; nothing to stop.
        }
    }
}
