namespace Mesen.Logic;

//ADR-0237 / PRD slice P.8. Host-free (ADR-0123): the two decisions the window
//makes about the renderer, so they can be asserted without Avalonia or a core.
//Stateful partners: UI/Windows/MainWindow.axaml.cs (asks for the renderer) and
//UI/Interop/ConfigApi.cs (CheckShaderSupport, which gates the shader group).
public static class RendererPolicy
{
	//True when InitializeEmu should be told "software renderer". Only the user
	//setting decides: macOS used to be forced to software, and now gets the
	//native Metal renderer by default like the other two platforms get theirs
	//(ADR-0237 section 2). isMacOs stays a parameter so the test pins that rule.
	public static bool UsesSoftwareRenderer(bool softwareRequested, bool isMacOs)
	{
		return softwareRequested;
	}

	//The shader settings are only worth showing when something will apply them.
	//Off macOS this keeps the inherited behavior (the core's answer, as is). On
	//macOS the software renderer never runs a shader, so offering the group
	//there would be a setting that does nothing.
	public static bool ShaderGroupAvailable(bool coreReportsShaderSupport, bool isMacOs, bool usesSoftwareRenderer)
	{
		return coreReportsShaderSupport && !(isMacOs && usesSoftwareRenderer);
	}
}
