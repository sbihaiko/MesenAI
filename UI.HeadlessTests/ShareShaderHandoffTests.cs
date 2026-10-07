using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Mesen.Config;
using Mesen.Debugger.Utilities;
using Mesen.Utilities;
using Xunit;

namespace Mesen.HeadlessTests;

//#987: Record and share's hand-off (reveal the .mmo, open the pre-filled
//issue form) and the shader menu's Open Shader Folder pass the path or URL to
//the injected launcher. Stubs record what they were handed, so nothing opens.
//No case calls the core, but both helpers reach EmuApi/ConfigApi, so the
//class shares the core collection (NativeCoreCollectionGuardTests).
[Collection(NativeCoreCollection.Name)]
public class ShareShaderHandoffTests : IDisposable
{
	private readonly Action<string> _reveal = ShareRecordingSession.RevealLauncher;
	private readonly Action<string> _browser = ShareRecordingSession.BrowserLauncher;
	private readonly Action<string> _shaderFolder = ShaderMenuHelper.FolderLauncher;
	private readonly List<string> _revealed = new();
	private readonly List<string> _browsed = new();
	private readonly List<string> _folders = new();
	private Window? _window;

	public ShareShaderHandoffTests()
	{
		ShareRecordingSession.RevealLauncher = _revealed.Add;
		ShareRecordingSession.BrowserLauncher = _browsed.Add;
		ShaderMenuHelper.FolderLauncher = _folders.Add;
	}

	public void Dispose()
	{
		ShareRecordingSession.RevealLauncher = _reveal;
		ShareRecordingSession.BrowserLauncher = _browser;
		ShaderMenuHelper.FolderLauncher = _shaderFolder;
		_window?.Close();
	}

	[AvaloniaFact]
	public void Finishing_a_shared_recording_reveals_the_file_and_opens_the_issue_form()
	{
		string file = Path.Combine(Path.GetTempPath(), "Shared", "Contra 2026-10-07.mmo");

		ShareRecordingSession.HandOver(file, "beat level 1");

		Assert.Equal(new[] { file }, _revealed);
		string url = Assert.Single(_browsed);
		Assert.StartsWith("https://github.com/sbihaiko/MesenAI/issues/new?template=replay.yml&", url);
		Assert.EndsWith("&notes=beat%20level%201", url);
	}

	[AvaloniaFact]
	public void Reveal_from_the_replay_saved_sheet_hands_over_the_file_and_opens_no_browser()
	{
		string file = Path.Combine(Path.GetTempPath(), "Shared", "Contra.mmo");

		ShareRecordingSession.Reveal(file);

		Assert.Equal(new[] { file }, _revealed);
		Assert.Empty(_browsed);
	}

	[AvaloniaFact]
	public void Open_shader_folder_hands_over_the_shaders_folder()
	{
		_window = new Window();
		MainMenuAction menu = ShaderMenuHelper.GetShaderMenu(_window, () => "", _ => { }, false);
		MainMenuAction open = menu.SubActions!.OfType<MainMenuAction>().Single(a => a.ActionType == ActionType.OpenShaderFolder);
		Assert.Empty(_folders);

		open.OnClick();

		Assert.Equal(new[] { Path.Combine(ConfigManager.HomeFolder, "Shaders") }, _folders);
	}
}
