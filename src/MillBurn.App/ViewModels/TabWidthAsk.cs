using MillBurn.Cam;

namespace MillBurn.App.ViewModels;

/// <summary>
/// What the width popup was opened on: the tab's letter, its width now, the width every tab has
/// unless it says otherwise, and the plan those were read from — so an answer that arrives after
/// the tabs were re-planned can be refused rather than landing on whichever tab now has the slot.
/// </summary>
/// <param name="Letter">The tab's name, as its handle shows it.</param>
/// <param name="WidthMm">Its width when the popup opened.</param>
/// <param name="BoardWidthMm">The board-wide width.</param>
/// <param name="Plan">The plan the handle indexed into.</param>
/// <param name="Handle">The tab's place in that plan's list.</param>
public sealed record TabWidthAsk(string Letter, double WidthMm, double BoardWidthMm, TabPlanning Plan, int Handle);
