using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using System;
using System.Diagnostics;
using System.IO;

namespace Mesen.Utilities
{
	//The Record-and-share action as the UI drives it (ADR-0205 sections 2 and 6).
	//
	//Start asks the Core for the one configuration that yields a publishable
	//.mmo (MovieRecordAndShare: from power-on, deterministic power-on state, the
	//player's settings restored when the recording ends). Stop finishes it the
	//only way the delivery rules allow: write the file to a known folder, reveal
	//it in the file manager and open the pre-filled issue form in the user's own
	//browser, so the drag into the form is one motion from windows that are
	//already open. No upload, no credential, no headless browser (section 6).
	public static class ShareRecordingSession
	{
		private static string? _file;

		//The user chooses nothing: no path, no "record from" mode. Author and
		//description are the ones already kept for the ordinary movie recorder
		//(MovieInfo.txt is the artifact's attribution, section 1).
		public static void Start()
		{
			if(!EmuApi.IsRunning() || RecordApi.MovieRecording() || RecordApi.MoviePlaying() || NetplayApi.IsConnected()) {
				return;
			}

			string folder = Path.Combine(ConfigManager.MovieFolder, "Shared");
			Directory.CreateDirectory(folder);
			string file = Path.Combine(folder, ReplayShare.FileName(EmuApi.GetRomInfo().GetRomName(), DateTime.Now));

			RecordMovieOptions options = new RecordMovieOptions(
				file,
				ConfigManager.Config.MovieRecord.Author,
				ConfigManager.Config.MovieRecord.Description,
				RecordMovieFrom.StartWithoutSaveData
			);
			bool started = RecordApi.MovieRecordAndShare(options);
			_file = started ? file : null;
			EmuApi.WriteLogEntry("[RecordAndShare] UI: start -> " + (started ? "recording to " + file : "REFUSED"));
		}

		//Stops any recording or playback; when it was a Record and share session,
		//hands the file over to the author.
		public static void Stop()
		{
			//Core ends a shared recording itself (ROM unload, state load); a plain
			//recording started afterwards must not reveal the old file.
			string? file = RecordApi.MovieSharing() ? _file : null;
			_file = null;
			RecordApi.MovieStop();

			if(file != null && File.Exists(file)) {
				EmuApi.DisplayMessage("Movies", "MovieShareReady", Path.GetFileName(file));
				Reveal(file);
				ApplicationHelper.OpenBrowser(ReplayShare.BuildIssueUrl(ConfigManager.Config.MovieRecord.Description));
			}
		}

		private static void Reveal(string file)
		{
			try {
				if(OperatingSystem.IsMacOS()) {
					Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", file } })?.Dispose();
				} else if(OperatingSystem.IsWindows()) {
					Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + file + "\"" })?.Dispose();
				} else {
					Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Path.GetDirectoryName(file) ?? "." } })?.Dispose();
				}
			} catch(Exception ex) {
				EmuApi.WriteLogEntry("[RecordAndShare] UI: could not reveal " + file + ": " + ex.Message);
			}
		}
	}
}
