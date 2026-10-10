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

	//The string typed through the on-screen keyboard the application shows - the
	//one pad keyboard (ADR-0262) - by walking that keyboard's grid and pressing
	//each key, the way a player types. Never an OS input path (#1281). Null when
	//the text was typed, otherwise why it was not: no keyboard is open, or the
	//keyboard has no key for a character.
	string? TypeText(string text);

	//A PNG of the application window only, never the desktop.
	CaptureResult Capture(string path);

	//Shut down the way the application's own Exit does, so settings are written.
	void Quit();
}
