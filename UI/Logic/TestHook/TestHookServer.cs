using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mesen.Logic.TestHook;

//The local endpoint (the GUI test hook ADR, PR #1202, item 1): a Unix domain
//socket on macOS and Linux (0600 socket, in a 0700 directory when the hook creates it), a named pipe on
//Windows. No TCP port is opened on loopback or anywhere else.
public sealed class TestHookServer : IDisposable
{
	private const int MaxLineChars = 1024 * 1024;

	private readonly CancellationTokenSource _stop = new();
	private readonly Func<string, string> _handler;
	private readonly string _endpoint;
	private readonly Socket? _listener;

	private TestHookServer(string endpoint, Func<string, string> handler, Action<string>? afterBind)
	{
		_endpoint = endpoint;
		_handler = handler;
		if(OperatingSystem.IsWindows()) {
			Task.Run(() => AcceptPipes());
			return;
		}
		string folder = Path.GetDirectoryName(Path.GetFullPath(endpoint))!;
		//Only a directory this process creates is locked to 0700; one that already
		//exists (/tmp, a shared runner folder) is not ours to chmod, and the 0600
		//socket is what keeps other users out of it.
		if(!Directory.Exists(folder)) {
			Directory.CreateDirectory(folder);
			File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
		if(File.Exists(endpoint)) {
			File.Delete(endpoint);
		}
		//Bound in a short-named (sun_path is about 104 bytes) 0700 directory of its own, then locked to 0600 and moved to the
		//endpoint: Bind creates the socket with the process umask, so bound in the
		//runner's folder it would be reachable by others until the chmod below.
		string privateFolder = Path.Combine(folder, ".h" + Guid.NewGuid().ToString("N").Substring(0, 6));
		Directory.CreateDirectory(privateFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		try {
			string bound = Path.Combine(privateFolder, "s");
			_listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
			_listener.Bind(new UnixDomainSocketEndPoint(bound));
			afterBind?.Invoke(bound);
			File.SetUnixFileMode(bound, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			File.Move(bound, endpoint);
		} finally {
			Directory.Delete(privateFolder, true);
		}
		_listener.Listen(4);
		Task.Run(() => AcceptSockets());
	}

	//Throws when the endpoint cannot be created: a startup failure, never a run
	//that proceeds without the hook.
	public static TestHookServer Start(string endpoint, Func<string, string> handler, Action<string>? afterBind = null) => new(endpoint, handler, afterBind);

	private async Task AcceptSockets()
	{
		while(!_stop.IsCancellationRequested) {
			try {
				Socket client = await _listener!.AcceptAsync(_stop.Token);
				_ = Task.Run(() => Serve(new NetworkStream(client, true)));
			} catch {
				return;
			}
		}
	}

	private async Task AcceptPipes()
	{
		while(!_stop.IsCancellationRequested) {
			try {
				NamedPipeServerStream pipe = new(_endpoint, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
				await pipe.WaitForConnectionAsync(_stop.Token);
				_ = Task.Run(() => Serve(pipe));
			} catch {
				return;
			}
		}
	}

	private void Serve(Stream stream)
	{
		using(stream) {
			try {
				using StreamReader reader = new(stream, new UTF8Encoding(false), false, 4096, true);
				using StreamWriter writer = new(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
				string? line;
				while((line = ReadBoundedLine(reader)) is not null) {
					writer.WriteLine(_handler(line));
				}
			} catch(IOException) {
			}
		}
	}

	//A line past MaxLineChars is a runner gone wrong: null ends the connection
	//rather than letting an unterminated line grow the buffer without bound.
	private static string? ReadBoundedLine(StreamReader reader)
	{
		StringBuilder line = new();
		int c;
		while((c = reader.Read()) >= 0) {
			if(c == '\n') {
				return line.ToString().TrimEnd('\r');
			}
			if(line.Length >= MaxLineChars) {
				return null;
			}
			line.Append((char)c);
		}
		return line.Length > 0 ? line.ToString() : null;
	}

	public void Dispose()
	{
		_stop.Cancel();
		_listener?.Dispose();
		if(!OperatingSystem.IsWindows() && File.Exists(_endpoint)) {
			File.Delete(_endpoint);
		}
	}
}
