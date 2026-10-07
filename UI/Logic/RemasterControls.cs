using System;

namespace Mesen.Logic;

public sealed record RemasterControlViewModel(bool IsEnabled, string Reason)
{
	public bool HasReason => Reason.Length > 0;
	public static RemasterControlViewModel Hidden { get; } = new(false, "");
}

//#984/#992 (rule 10): W-R1's five controls from the screen state. With the
//wrong game the row above says it once, so the controls stay disabled
//without repeating a reason under each; the owner maps a reason to its sentence.
public sealed record RemasterControls(
	bool IsWrongGame,
	RemasterControlViewModel Record,
	RemasterControlViewModel RecordFromTas,
	RemasterControlViewModel LetTheAiPlay,
	RemasterControlViewModel PrepareFigures,
	RemasterControlViewModel BuildAndShow
)
{
	public static RemasterControls From(RemasterScreenState s, Func<RemasterReason, string> sentence)
	{
		bool wrongGame = s.View == RemasterView.Project && s.Record.Reason == RemasterReason.NotThisProjectsGame;
		RemasterControlViewModel Control(RemasterControl c) => new(c.Enabled, c.Enabled || wrongGame ? "" : sentence(c.Reason));
		return new(wrongGame, Control(s.Record), Control(s.RecordFromTas), Control(s.LetTheAiPlay),
			Control(s.PrepareFigures), Control(s.BuildAndShow));
	}
}
