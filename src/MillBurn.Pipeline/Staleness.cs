namespace MillBurn.Pipeline;

/// <summary>
/// What made the programs on screen stop being a picture of this job.
/// </summary>
/// <remarks>
/// One member per edit that can do it. Named for the change rather than for its consequence, so a
/// new kind of edit has to be added here — and adding it here is what makes the question
/// *"and what should that clear?"* unavoidable rather than optional.
/// </remarks>
public enum StaleBecause
{
    /// <summary>Safe height, rapid rate, decimals — read when a request is built.</summary>
    MachineChanged,

    /// <summary>The start and end G-code that tops and tails every program.</summary>
    FramingChanged,

    /// <summary>A layer's output, or the settings that decide how it is cut.</summary>
    OutputChanged,

    /// <summary>Every layer put back to what a freshly imported board starts with.</summary>
    LayersReset,

    /// <summary>The standalone program being viewed was closed.</summary>
    ProgramClosed,

    /// <summary>The board's source bytes were re-read from the folder they came from.</summary>
    SourceRefreshed,

    /// <summary>A different project, or none: importing, opening, closing.</summary>
    BoardReplaced,

    /// <summary>
    /// A layer was told what it is, by hand (6.31). The board is rebuilt from the project with that
    /// layer realised as its new role, so every program drawn of the old one is of a different board.
    /// </summary>
    RoleChanged,
}

/// <summary>
/// What has to happen when the drawn programs stop describing the job — stated once.
///
/// **Six methods used to decide this for themselves, and the decisions had drifted.** Some stopped
/// the run, some cleared the backplot, some rebuilt, some set a status, one raised property
/// notifications and the rest did not. The bug was never in the clearing: it was always in a path
/// that forgot one of them. 6.33 came from the bench and fixed the first, a review found the next
/// two, a later review found the last two and a missing pair of notifications in the fourth, and
/// none was found by a test — because nothing could reach the view model.
///
/// **The value is the seventh path, not the six.** The six are correct now. The seventh is the one
/// nobody has thought of yet, and it cannot be written wrong if there is nowhere to write it.
/// </summary>
/// <param name="StopRun">
/// Whether a preview still building has to be abandoned.
///
/// Always true, and kept as a field rather than assumed because it is the one that was missed
/// most. A run in flight read the settings that have just changed, so left alone it finishes and
/// publishes programs cut to numbers nobody chose any more — and writes its own status over
/// whatever the edit was going to say.
/// </param>
/// <param name="ClearPrograms">
/// Whether the drawn programs and their G-code go.
///
/// Means the *previewed* programs. The backplot holds either a preview or a program opened off
/// disk and never both, so a reason that clears without also forgetting an opened one leaves that
/// one alone — see <paramref name="ForgetOpenedProgram"/>.
/// </param>
/// <param name="ForgetOpenedProgram">
/// Whether the standalone program being viewed goes too.
///
/// Only for the three edits that replace what is underneath it: closing it, refreshing the board's
/// bytes, and swapping the project. A settings change leaves an opened program alone, because it
/// came off disk and was not made from these settings.
/// </param>
/// <param name="Rebuild">Whether the scene is rebuilt from the board afterwards.</param>
/// <param name="OnlyWhenDrawn">
/// Whether all of the above is skipped when there was nothing drawn and no run to stop.
///
/// **A performance guard, not a correctness one**, and it belongs to the reason rather than to the
/// caller: `OutputChanged` arrives on every tick of a slider, so clearing nothing and rebuilding a
/// scene for it is the fault 6.49 describes. `LayersReset` is one button press and does not need
/// the guard. Getting this backwards is cheap in one direction and expensive in the other, which
/// is why it is written down next to the reason instead of being re-decided at each call.
/// </param>
public readonly record struct Staleness(
    bool StopRun,
    bool ClearPrograms,
    bool ForgetOpenedProgram,
    bool Rebuild,
    bool OnlyWhenDrawn)
{
    /// <summary>
    /// What that change means, as one table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StaleBecause.MachineChanged"/> does not clear the programs, and that is not an
    /// oversight: its caller re-previews immediately so the confirmation lands on a picture made
    /// with the new numbers, and the run it starts replaces the backplot rather than leaving a
    /// hole where one was. It still has to stop whatever was already building, which is the half
    /// that was missing — a run in flight has no `Gcode` yet, so the condition that path tested
    /// was false in exactly the case that needed it.
    /// </para>
    /// <para>
    /// **Three reasons do not rebuild here, and it is the same reason three times.**
    /// <see cref="StaleBecause.BoardReplaced"/>, <see cref="StaleBecause.SourceRefreshed"/> and
    /// <see cref="StaleBecause.RoleChanged"/> all have a caller that replaces the thing a scene is
    /// built *from* — the project, the source bytes, a layer's role — and then rebuilds itself. Rebuilding here as well would run the whole thing
    /// twice, the first time against geometry that is about to be thrown away.
    /// </para>
    /// <para>
    /// `BoardReplaced` additionally needs its caller's rebuild rather than one here, because that
    /// one passes `fresh: true`: the rows on screen still belong to the project being replaced,
    /// and none of what was hidden in that one is a statement about this one.
    /// </para>
    /// <para>
    /// **The stop and the clearing still happen before the swap**, which is why this is not simply
    /// a matter of calling later. A run in flight is reading the project the caller is about to
    /// mutate underneath it.
    /// </para>
    /// </remarks>
    public static Staleness Of(StaleBecause why) => why switch
    {
        StaleBecause.MachineChanged => new(
            StopRun: true,
            ClearPrograms: false,
            ForgetOpenedProgram: false,
            Rebuild: false,
            OnlyWhenDrawn: false),

        StaleBecause.FramingChanged => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: false,
            Rebuild: true,
            OnlyWhenDrawn: true),

        StaleBecause.OutputChanged => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: false,
            Rebuild: true,
            OnlyWhenDrawn: true),

        StaleBecause.LayersReset => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: false,
            Rebuild: true,
            OnlyWhenDrawn: false),

        StaleBecause.ProgramClosed => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: true,
            Rebuild: true,
            OnlyWhenDrawn: false),

        StaleBecause.SourceRefreshed => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: true,
            Rebuild: false,
            OnlyWhenDrawn: false),

        StaleBecause.BoardReplaced => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: true,
            Rebuild: false,
            OnlyWhenDrawn: false),

        // The third that its caller rebuilds, for the same reason as the other two: the caller
        // changes the project the scene is built from, and a run in flight is reading it, so the stop
        // comes first and the rebuild after. An opened program stays — it came off disk and no role
        // made it — and it is one press, so it is not guarded like an output change.
        StaleBecause.RoleChanged => new(
            StopRun: true,
            ClearPrograms: true,
            ForgetOpenedProgram: false,
            Rebuild: false,
            OnlyWhenDrawn: false),

        _ => throw new ArgumentOutOfRangeException(nameof(why), why, "No rule for this change."),
    };

    /// <summary>
    /// Whether this change does anything at all, given what is on screen.
    ///
    /// **The guard that 6.33 got wrong, in the one place it can now be got wrong.** That path
    /// asked whether anything was drawn and returned before it could stop a run — and a run in
    /// flight is exactly when nothing is drawn, because the edit that started it cleared the last
    /// picture. So the condition was false precisely when the work mattered most.
    /// </summary>
    /// <param name="anythingDrawn">Whether any programs are currently on screen.</param>
    /// <param name="runInFlight">Whether a preview is still building.</param>
    public bool Applies(bool anythingDrawn, bool runInFlight) =>
        !OnlyWhenDrawn || anythingDrawn || runInFlight;
}
