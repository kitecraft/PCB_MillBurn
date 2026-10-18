namespace MillBurn.Core;

/// <summary>
/// What the status line says when running checks are stopped: one line for one Escape (6.54).
///
/// **Two runs, one line.** Escape stops the board check and the findings search together, and each
/// stopped run says its piece on its own way out. Written straight to the status line, whichever
/// finished last was the one the operator read — one Escape could end on "stopped before the search
/// found anything" while the board check it also stopped went unmentioned (sprint 6 story 3's review
/// left this as a race; story 6 closed it). Gathered here, the line names both, in a fixed order,
/// however the two finishes interleave.
///
/// No Avalonia in it, so the ordering is a unit test rather than a timing someone has to catch.
/// </summary>
/// <param name="order">The sentences this can be asked to say, in the order a joint line puts
/// them. A sentence not listed goes after the listed ones.</param>
public sealed class StopAnnouncement(IReadOnlyList<string> order)
{
    private readonly IReadOnlyList<string> _order = order ?? throw new ArgumentNullException(nameof(order));
    private readonly List<string> _said = [];
    private bool _joint;

    /// <summary>A run has started: whatever an earlier Escape gathered is over.</summary>
    public void RunStarted()
    {
        _said.Clear();
        _joint = false;
    }

    /// <summary>One Escape stopped both runs: their sentences are to be said together.</summary>
    public void StoppedBoth()
    {
        _said.Clear();
        _joint = true;
    }

    /// <summary>
    /// A run says it was stopped; returns the line the status bar should now show. Alone, that is
    /// the sentence. After <see cref="StoppedBoth"/>, it is every sentence said so far, in order —
    /// and once both have spoken the joint line is complete and the next stop stands alone again.
    /// A run that finished on its own before the stop reached it never calls this; its own result
    /// is what it says.
    /// </summary>
    public string Say(string sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);

        if (!_joint)
        {
            return sentence;
        }

        if (!_said.Contains(sentence))
        {
            _said.Add(sentence);
        }

        var line = string.Join(" ", _said.OrderBy(Rank));

        if (_said.Count >= 2)
        {
            _joint = false;
        }

        return line;
    }

    private int Rank(string sentence)
    {
        var index = -1;

        for (var i = 0; i < _order.Count && index < 0; i++)
        {
            if (string.Equals(_order[i], sentence, StringComparison.Ordinal))
            {
                index = i;
            }
        }

        return index < 0 ? _order.Count : index;
    }
}
