namespace Mesen.Logic;

//The user's rule (2026-10-03): "every wait must have an animation so it does
//not look broken". Starting or stopping a recording blocks in the core -
//Remaster's builder exports every ROM tile to PNG (365-978 ms measured on
//Contra, Castlevania, SMB3 and Mega Man 2) and Share's replay power-cycles the
//game - so Remaster and Share call the core off the UI thread and show the
//transition (a sentence and an indeterminate bar) until it answers.
public enum RecordingTransition
{
	None,
	Starting,
	Stopping
}

public static class RecordingTransitions
{
	//A Record/Stop click (or Esc) while a start or stop is in flight does
	//nothing: no second start, no stop of a start the core has not answered.
	public static bool AcceptsClick(RecordingTransition transition) => transition == RecordingTransition.None;

	public static bool ShowsWait(RecordingTransition transition) => transition != RecordingTransition.None;
}
