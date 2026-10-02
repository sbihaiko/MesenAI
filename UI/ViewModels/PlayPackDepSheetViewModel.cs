using CommunityToolkit.Mvvm.ComponentModel;
using Mesen.Localization;
using Mesen.Logic;
using Mesen.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Mesen.ViewModels
{
	//G.5 (PRD Part B §8, ADR-0241, §13.5.2 W-P16): a pack waits for a file only
	//the user can add. The data is CommunityPackInstallService's
	//CommunityPackDepPrompt (Hints, License, DropFolder, Sha256); the sheet
	//replaces the OSD line in Player mode. The app never fetches the file: a
	//file the user drops or picks is checked by content hash, copied into the
	//drop folder, and the game is reloaded so the pack re-resolves (until
	//ADR-0244's P.9 applies it in place).
	public partial class PlayPackDepSheetViewModel : ViewModelBase
	{
		[ObservableProperty] public partial bool IsVisible { get; private set; }
		[ObservableProperty] public partial string Title { get; private set; } = "";
		[ObservableProperty] public partial string FileTitle { get; private set; } = "";
		[ObservableProperty] public partial string LicenseText { get; private set; } = "";
		[ObservableProperty] public partial string ErrorText { get; private set; } = "";
		[ObservableProperty] public partial string PrimaryLabel { get; private set; } = "";
		[ObservableProperty] public partial string DropHint { get; private set; } = "";
		[ObservableProperty] public partial bool IsBusy { get; private set; }

		public PackDepNoticeState Notice { get; } = new();

		private IReadOnlyList<CommunityPackDepPrompt> _pending = Array.Empty<CommunityPackDepPrompt>();

		//Play Without It (or Esc): back to the pause overlay (rule 8).
		public event Action? Closed;
		//The file is in the drop folder: the owner reloads the game.
		public event Action? FileAdded;

		public CommunityPackDepPrompt? Current => _pending.Count > 0 ? _pending[0] : null;

		public void SetPending(string packName, IReadOnlyList<CommunityPackDepPrompt> pending)
		{
			_pending = pending;
			Notice.Pending(packName, pending.Count);
		}

		public void Clear()
		{
			_pending = Array.Empty<CommunityPackDepPrompt>();
			Notice.Clear();
			IsVisible = false;
		}

		public void Open()
		{
			if(Current is not CommunityPackDepPrompt dep) {
				return;
			}
			Notice.MarkShown();
			Title = Notice.FileCount == 1
				? ResourceHelper.GetMessage("PackDepSheetTitleOne", Notice.PackName)
				: ResourceHelper.GetMessage("PackDepSheetTitleMany", Notice.PackName, Notice.FileCount);
			FileTitle = string.IsNullOrWhiteSpace(dep.Hints) ? dep.DepId : dep.Hints;
			LicenseText = ResourceHelper.GetMessage("PackDepSheetLicense", string.IsNullOrWhiteSpace(dep.License) ? CommunityPackDepResolver.LicenseNotDeclared : dep.License);
			bool inPlace = PlayPackDepPrompt.PrimaryAction(PlayPackDepPrompt.AppliesInPlace) == PackDepPrimaryAction.Add;
			PrimaryLabel = ResourceHelper.GetMessage(inPlace ? "PackDepSheetAdd" : "PackDepSheetAddAndRestart");
			DropHint = ResourceHelper.GetMessage(inPlace ? "PackDepSheetDropHint" : "PackDepSheetDropHintRestart");
			ErrorText = "";
			IsVisible = true;
		}

		public void PlayWithoutIt()
		{
			if(!IsVisible) {
				return;
			}
			IsVisible = false;
			Closed?.Invoke();
		}

		//Esc from the sheet: same as Play Without It, without raising Closed
		//(the Esc router re-shows the overlay itself).
		public void CloseOnEsc() => IsVisible = false;

		public async Task TryFile(string path)
		{
			if(!IsVisible || IsBusy || Current is not CommunityPackDepPrompt dep || !File.Exists(path)) {
				return;
			}
			IsBusy = true;
			ErrorText = "";
			try {
				string actual = await Task.Run(() => Sha256Of(path));
				//The prompt can change (another game, another pack) while the
				//file is hashed or copied: the result belongs to the old one.
				if(!ReferenceEquals(Current, dep)) {
					return;
				}
				if(PlayPackDepPrompt.Check(actual, dep.Sha256) != PackDepFileCheck.Accepted) {
					ErrorText = ResourceHelper.GetMessage("PackDepSheetWrongFile");
					return;
				}
				Directory.CreateDirectory(dep.DropFolder);
				string target = PlayPackDepPrompt.TargetPath(dep.DropFolder, path);
				if(!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.Ordinal)) {
					PackDepDropTarget drop = await Task.Run(() => {
						bool exists = File.Exists(target);
						return PlayPackDepPrompt.CheckTarget(exists, exists ? Sha256Of(target) : "", dep.Sha256);
					});
					if(drop == PackDepDropTarget.NameTaken) {
						ErrorText = ResourceHelper.GetMessage("PackDepSheetNameTaken", Path.GetFileName(target));
						return;
					}
					if(drop == PackDepDropTarget.Copy) {
						//overwrite: false - a file that appeared since the check is
						//never replaced (it throws, and the sentence says so).
						await Task.Run(() => File.Copy(path, target, false));
					}
				}
				if(!ReferenceEquals(Current, dep)) {
					return;
				}
			} catch(Exception ex) {
				ErrorText = ResourceHelper.GetMessage("PackDepSheetCopyFailed", ex.Message);
				return;
			} finally {
				IsBusy = false;
			}
			Clear();
			FileAdded?.Invoke();
		}

		private static string Sha256Of(string path)
		{
			using FileStream stream = File.OpenRead(path);
			return Convert.ToHexString(SHA256.HashData(stream));
		}

		public void ShowFolder()
		{
			if(Current is not CommunityPackDepPrompt dep) {
				return;
			}
			try {
				Directory.CreateDirectory(dep.DropFolder);
				string opener = OperatingSystem.IsWindows() ? "explorer.exe" : (OperatingSystem.IsMacOS() ? "open" : "xdg-open");
				Process.Start(new ProcessStartInfo(opener) { ArgumentList = { dep.DropFolder } })?.Dispose();
			} catch(Exception ex) {
				ErrorText = ResourceHelper.GetMessage("PackDepSheetCopyFailed", ex.Message);
			}
		}
	}
}
