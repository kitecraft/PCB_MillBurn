namespace MillBurn.Core;

/// <summary>
/// How much a check matters, which is the one thing a reader needs before deciding whether to
/// read the rest of it.
///
/// Three levels rather than a `bool IsError`, because the middle one is the whole point. A refusal
/// is not an error — the file that was written is correct — but something the operator asked for
/// is not in it, and finding that out at the machine is too late. Before this existed the only way
/// to tell a refusal from a note was to read to the end of the sentence and recognise the wording.
/// </summary>
public enum CheckSeverity
{
    /// <summary>
    /// Worth knowing, and the operator is entitled to ignore it. Every threshold in
    /// <see cref="ToolAdvice"/> is this: rules of thumb for FR4 on a hobby machine.
    /// </summary>
    Advice,

    /// <summary>
    /// Something that was asked for is not being done, and the emitted file does not contain it.
    /// A slot too narrow for any cutter, a layer that could not be levelled.
    /// </summary>
    Refusal,

    /// <summary>
    /// Do not run this. A rapid at cutting depth, a layer that would not parse.
    /// </summary>
    Error,
}

/// <summary>
/// What sort of question a check answers, so that a reader can skip the kinds they have already
/// satisfied themselves about.
///
/// Deliberately coarse. These are the groupings an operator would make standing at the machine —
/// *is the board read right*, *is the tool sane*, *will the copper come apart* — and not the
/// producing subsystem, which is an implementation detail nobody outside the code cares about.
/// </summary>
public enum CheckKind
{
    /// <summary>A setting this build will not honour was changed on the operator's behalf.</summary>
    Project,

    /// <summary>The board as it was read: a missing outline, a file that would not open.</summary>
    Board,

    /// <summary>What a parser could not make sense of, and is therefore not drawing.</summary>
    Parse,

    /// <summary>This layer's own output settings — its mirror, its polarity, what it will become.</summary>
    Layer,

    /// <summary>What the tool's own numbers say about how it will behave.</summary>
    Tool,

    /// <summary>Copper that will not come apart, and gaps no cutter here can reach.</summary>
    Electrical,

    /// <summary>The emitted program itself: gouges, tabs, what the machine will actually do.</summary>
    Program,

    /// <summary>The blank, and the stock it is cut from.</summary>
    Stock,
}

/// <summary>
/// The thing a check is about.
///
/// **It carries the layer rather than a sentence naming it.** Every check used to begin with its
/// own source by convention — `Stock: …`, `Not levelled: …` — or, more often, not at all, and the
/// only way to know what a line referred to was to read it. A field cannot be forgotten, cannot
/// disagree with the message beside it, and can be matched against something: the window colours
/// a layer's checks in that layer's own colour by finding the row this names, which is not
/// possible when the name is buried in prose.
///
/// <see cref="FileName"/> is the key that matching uses, because it is the only thing about a
/// layer that is unique — two files can claim the same <see cref="LayerRole"/>, and one of the
/// board's own checks exists to say so when they do.
/// </summary>
public sealed record CheckSource
{
    /// <summary>
    /// What to show at the head of the line: "Top copper", "Stock", "Export".
    ///
    /// Written for the operator rather than derived from a type name, because "InnerCopper" is not
    /// what anybody calls it.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>The layer's role, when the check is about a layer. Null when it is not.</summary>
    public LayerRole? Role { get; init; }

    /// <summary>
    /// The file this is about, when it is about one — the key a reader and the window both use to
    /// find the thing being complained about.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>The whole job rather than any one part of it.</summary>
    public static CheckSource Export { get; } = new() { Label = "Export" };

    /// <summary>The project's own settings, as opposed to anything on the board.</summary>
    public static CheckSource Project { get; } = new() { Label = "Project" };

    /// <summary>The blank and the stock it comes from.</summary>
    public static CheckSource Stock { get; } = new() { Label = "Stock" };

    /// <summary>The board as a whole, when no single layer is at fault.</summary>
    public static CheckSource Board { get; } = new() { Label = "Board" };

    /// <summary>A layer, named as the operator would name it and keyed by its file.</summary>
    public static CheckSource Layer(LayerRole role, string label, string? fileName = null) =>
        new() { Label = label, Role = role, FileName = fileName };

    /// <summary>A file the export is about to write, or has refused to.</summary>
    public static CheckSource File(string fileName) =>
        new() { Label = fileName, FileName = fileName };
}

/// <summary>
/// Something the operator should look at before trusting what is on screen or running what was
/// written.
///
/// **This was an `ObservableCollection&lt;string&gt;` and five separate requests were all asking for
/// the same thing.** You cannot colour a label that does not exist, cannot fold a list into a count
/// of things it cannot categorise, cannot link a status message to a line it cannot name, and
/// cannot give a check a view of its own when the check is a sentence. Source, kind and severity
/// are the three fields that make all five possible, and none of them can be recovered from the
/// text afterwards.
///
/// **The proof that the old shape was wrong was already in the code.** `ReplaceExportWarnings` kept
/// a second parallel list of its own lines so it could take them out of the panel again, and did
/// it with string equality — a shadow list existing because the item had no field saying where it
/// came from, and an ambiguous removal the moment two checks read the same. With a source on the
/// item, that is a filter.
///
/// **Deliberately not an identifier.** Determinism is a hard requirement here — golden files depend
/// on it — so a check carries no `Guid` and no counter. What makes one findable is what it is
/// about, which is stable across two runs of the same job and is also the only thing a reader can
/// match against what they can see.
/// </summary>
public sealed record Check
{
    public required CheckSource Source { get; init; }

    public required CheckKind Kind { get; init; }

    public required CheckSeverity Severity { get; init; }

    /// <summary>
    /// What is wrong.
    ///
    /// **It does not name its own source, and <see cref="Line"/> is why.** Every surface now puts
    /// <see cref="CheckSource.Label"/> in front of this, so a sentence that begins by naming the
    /// thing it is about says it twice. Anything that reads like a source belongs in the source.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>Advice, and the most common kind by a distance.</summary>
    public static Check Advice(CheckSource source, CheckKind kind, string message) =>
        new() { Source = source, Kind = kind, Severity = CheckSeverity.Advice, Message = message };

    /// <summary>Something asked for that is not in the file.</summary>
    public static Check Refusal(CheckSource source, CheckKind kind, string message) =>
        new() { Source = source, Kind = kind, Severity = CheckSeverity.Refusal, Message = message };

    /// <summary>Do not run this.</summary>
    public static Check Error(CheckSource source, CheckKind kind, string message) =>
        new() { Source = source, Kind = kind, Severity = CheckSeverity.Error, Message = message };

    /// <summary>Advice — the operator is entitled to read this one last, or not at all.</summary>
    public bool IsAdvice => Severity == CheckSeverity.Advice;

    /// <summary>
    /// Something asked for is not in the file, or the file should not be run.
    ///
    /// **The two loud severities read as one thing to a reader and as two to the panel.** Both mean
    /// *stop and look*, which is what this answers; the panel still marks them apart, because "not
    /// everything you asked for" and "do not run this" are not the same instruction. It exists so
    /// that the common question — is this line worth interrupting the job for — has one name.
    /// </summary>
    public bool IsRefusal => Severity is CheckSeverity.Refusal or CheckSeverity.Error;

    /// <summary>Do not run this.</summary>
    public bool IsError => Severity == CheckSeverity.Error;

    /// <summary>
    /// The line as a reader sees it: **the thing it is about, then what is wrong with it.**
    ///
    /// One implementation, because the window, the companion page and the CLI all showed these and
    /// all three built the sentence themselves. A check that reads one way in the panel and another
    /// on the page is a check somebody has to reconcile by hand.
    ///
    /// **The source is rendered here, and this is the line that made it possible.** From the bench,
    /// twice in one sitting: *"Each item should list its source first, then the message."* Before
    /// <see cref="CheckSource"/> existed there was nothing to put first — provenance was whatever
    /// the sentence happened to begin with, which for most of them was nothing at all. The dash is
    /// the separator the request asked for and the one the rest of the app's prose already uses.
    ///
    /// **A message may still name a topic after the dash** — "Bottom copper — Mirrored: …". That is
    /// not the duplication story 1 left behind: "Mirrored" is what the check is about, not what it
    /// is *on*, and the two together read as they should. What was removed was a message repeating
    /// its own source, which is a different thing and is now impossible to write without seeing it
    /// twice in the panel.
    ///
    /// The panel does not use this. It needs the two halves apart to colour one of them, so it
    /// binds <see cref="CheckSource.Label"/> and <see cref="Message"/> separately and this stays
    /// the single answer for every surface that wants one string — the companion page, the CLI, the
    /// export window, and a golden baseline.
    /// </summary>
    public string Line => Source.Label + " — " + Message;

    public override string ToString() => Line;
}
