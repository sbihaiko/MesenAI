using System.Text.Json.Nodes;

namespace Mesen.Logic.TestHook;

public sealed record CaptureResult(string Path, int Width, int Height, string Sha256);

//What the hook asks of the application. The window-bound implementation lives in
//UI/Windows; the protocol only ever talks to this, so it is tested host-free.
public interface ITestHookTarget
{
	//screen, dialogs, focus, controls, options, window - named controls, never
	//pixels and never display text a check may read. The protocol adds the two
	//counters.
	JsonObject State();

	//#1282: a step begins, on the request that starts it (an `inject`). What is
	//counted "since the last step" - the haptic ticks the menu asked for - restarts
	//here, so a step's own check reads what that step caused and the state reads in
	//between (a wait polls state, ADR-0272 item 4) consume nothing.
	void Step();

	//A PNG of the application window only, never the desktop.
	CaptureResult Capture(string path);

	//Shut down the way the application's own Exit does, so settings are written.
	void Quit();
}
