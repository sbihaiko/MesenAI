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
		//Returns the file it records to, or null when nothing started.
		public static string? Start()
		{
			if(!EmuApi.IsRunning() || RecordApi.MovieRecording() || RecordApi.MoviePlaying() || NetplayApi.IsConnected()) {
				return null;
			}

			string folder = Path.Combine(ConfigManager.MovieFolder, "Shared");
			try {
				Directory.CreateDirectory(folder);
			} catch(Exception ex) when(ex is UnauthorizedAccessException || ex is IOException) {
				EmuApi.WriteLogEntry("[RecordAndShare] UI: cannot create " + folder + ": " + ex.Message);
				EmuApi.DisplayMessage("Movies", "MovieShareFolderError", folder);
				return null;
			}
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
			return _file;
		}

		//Stops any recording or playback; when it was a Record and share session,
		//hands the file over to the author.
		public static void Stop()
		{
			string? file = StopAndKeep();
			if(file != null) {
				EmuApi.DisplayMessage("Movies", "MovieShareReady", Path.GetFileName(file));
				Reveal(file);
				ApplicationHelper.OpenBrowser(ReplayShare.BuildIssueUrl(ConfigManager.Config.MovieRecord.Description));
			}
		}

		//G.8 (W-H4): stops like Stop, but only hands the file back - Share's
		//"Replay saved" sheet reveals it and opens the form on the user's click.
		public static string? StopAndKeep()
		{
			//Core ends a shared recording itself (ROM unload, state load); a plain
			//recording started afterwards must not reveal the old file.
			string? file = RecordApi.MovieSharing() ? _file : null;
			_file = null;
			RecordApi.MovieStop();
			return file != null && File.Exists(file) ? file : null;
		}

		public static void Reveal(string file)
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

	//G.8: the session as Share's ViewModel sees it (UI/Logic/ShareScreen.cs).
	public sealed class CoreReplayRecorder : IReplayRecorder
	{
		public bool IsSharing => RecordApi.MovieSharing();
		public string? Start() => ShareRecordingSession.Start();
		public string? StopAndKeep() => ShareRecordingSession.StopAndKeep();
		public string IssueUrl() => ReplayShare.BuildIssueUrl(ConfigManager.Config.MovieRecord.Description);
	}
}
