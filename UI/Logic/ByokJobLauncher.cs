using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Mesen.Logic;

//F14.20 / ADR-0242 Decision 4, ADR-0247 Decision 1: starts an external script
//(scripts/jev_harness.py, under the W-R0b Python gate) with the user's key in
//the child's environment and nowhere else. The key is read from the store here,
//when the job starts; it is refused on the command line; it is removed from the
//ProcessStartInfo once the child has its copy; and any line the child prints
//reaches the app with the key replaced by Redacted, so a child that misbehaves
//still cannot put it in a log or on screen. An OS-level dump of either process
//can still contain it - ADR-0247 scopes the guarantee to what the app writes.
public static class ByokJobLauncher
{
	public const string Redacted = "[redacted]";

	public static ProcessStartInfo BuildStartInfo(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, ByokVendor vendor, string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		if(fileName.Contains(key) || (workingDirectory?.Contains(key) ?? false)) {
			throw new ByokLaunchException($"Refused to start {SafeName(fileName, key)}: the {vendor.DisplayName} key may only travel in the environment.");
		}
		foreach(string argument in arguments) {
			if(argument.Contains(key)) {
				throw new ByokLaunchException($"Refused to start {SafeName(fileName, key)}: the {vendor.DisplayName} key may only travel in the environment, never on the command line.");
			}
		}

		ProcessStartInfo info = new() {
			FileName = fileName,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = false,
		};
		foreach(string argument in arguments) {
			info.ArgumentList.Add(argument);
		}
		if(workingDirectory != null) {
			info.WorkingDirectory = workingDirectory;
		}
		info.Environment[vendor.EnvironmentVariable] = key;
		return info;
	}

	//Reads the vendor's key from the store, starts the child, and forwards each
	//line of its stdout/stderr to onOutputLine with the key redacted.
	public static ByokJob Start(IByokKeyStore store, ByokVendor vendor, string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine)
	{
		string? stored = store.Read(vendor);
		if(string.IsNullOrWhiteSpace(stored)) {
			throw new ByokKeyMissingException(vendor);
		}
		//#681: the bare key the child uses (jev_client strips it) is the one to redact.
		string key = ByokKey.Normalize(stored);

		ProcessStartInfo info = BuildStartInfo(fileName, arguments, workingDirectory, vendor, key);
		Process process = new() { StartInfo = info, EnableRaisingEvents = true };
		ByokJob job = new(process, key, onOutputLine);
		try {
			process.Start();
		} catch(Exception ex) {
			//Not wrapped as the inner exception: its text is the OS's and is
			//rebuilt here, redacted, so ex.ToString() (what the log records) is
			//only what this line says.
			job.Dispose();
			throw new ByokLaunchException($"Could not start {SafeName(fileName, key)}: {Redact(ex.Message, key)}");
		} finally {
			//The child has its own copy now; the StartInfo the Process keeps
			//referencing must not hold one for the job's lifetime.
			info.Environment.Remove(vendor.EnvironmentVariable);
		}
		job.BeginReading();
		return job;
	}

	public static string Redact(string text, string key)
	{
		return string.IsNullOrEmpty(key) ? text : text.Replace(key, Redacted, StringComparison.Ordinal);
	}

	private static string SafeName(string fileName, string key)
	{
		return Redact(Path.GetFileName(fileName), key);
	}
}

//A running BYOK child. It keeps the key only to redact the child's output, and
//only until the child exits.
public sealed class ByokJob : IDisposable
{
	private readonly Action<string>? _onOutputLine;
	private Func<string, string>? _redact;
	private readonly object _lock = new();

	public Process Process { get; }

	internal ByokJob(Process process, string key, Action<string>? onOutputLine)
	{
		Process = process;
		_onOutputLine = onOutputLine;
		_redact = line => ByokJobLauncher.Redact(line, key);
	}

	internal void BeginReading()
	{
		Process.OutputDataReceived += (_, e) => Forward(e.Data);
		Process.ErrorDataReceived += (_, e) => Forward(e.Data);
		Process.BeginOutputReadLine();
		Process.BeginErrorReadLine();
	}

	private void Forward(string? line)
	{
		if(line == null) {
			return;
		}
		Func<string, string>? redact;
		lock(_lock) {
			redact = _redact;
		}
		//After the redactor is dropped nothing is forwarded at all, rather than a
		//line that was never checked
		if(redact != null) {
			_onOutputLine?.Invoke(redact(line));
		}
	}

	public async Task<int> WaitForExitAsync()
	{
		//The parameterless overload also waits for the redirected streams to
		//drain, so every line has been forwarded when it returns
		await Process.WaitForExitAsync().ConfigureAwait(false);
		DropKey();
		return Process.ExitCode;
	}

	public void Stop()
	{
		try {
			if(!Process.HasExited) {
				Process.Kill(entireProcessTree: true);
			}
		} catch(InvalidOperationException) {
			//Not started, or already gone
		}
	}

	private void DropKey()
	{
		lock(_lock) {
			_redact = null;
		}
	}

	public void Dispose()
	{
		DropKey();
		Process.Dispose();
	}
}

public sealed class ByokKeyMissingException : Exception
{
	public ByokKeyMissingException(ByokVendor vendor)
		: base($"No {vendor.DisplayName} key is stored on this computer.")
	{
	}
}

public sealed class ByokLaunchException : Exception
{
	public ByokLaunchException(string message) : base(message)
	{
	}
}
