using System;
using System.Collections.Generic;

namespace Mesen.Logic.TestHook;

//The hook's only switch (the GUI test hook ADR, PR #1202, item 5): a command-line
//argument. Nothing in a config file, a setting or the environment reads here, so
//a released binary cannot be listening by accident. No --test-hook, no options,
//and nothing downstream is ever created.
public sealed record TestHookOptions(string Endpoint, string Token)
{
	public const string Flag = "--test-hook";
	public const string TokenFlag = "--test-hook-token";

	//Null when the flag is absent. The flag with no endpoint is an error, not a run
	//that quietly goes on without the hook: the adapter is told "unavailable". The
	//same for a missing or empty token, since every request has to carry one.
	public static TestHookOptions? Parse(IReadOnlyList<string> args)
	{
		string? endpoint = null;
		string token = "";
		for(int i = 0; i < args.Count; i++) {
			string arg = args[i];
			if(arg == Flag || arg.StartsWith(Flag + "=", StringComparison.Ordinal) && !arg.StartsWith(TokenFlag, StringComparison.Ordinal)) {
				endpoint = ValueOf(args, ref i, Flag);
			} else if(arg == TokenFlag || arg.StartsWith(TokenFlag + "=", StringComparison.Ordinal)) {
				token = ValueOf(args, ref i, TokenFlag);
			}
		}
		if(endpoint is null) {
			return null;
		}
		if(endpoint.Length == 0) {
			throw new ArgumentException(Flag + " needs an endpoint: " + Flag + "=<socket path or pipe name>");
		}
		if(token.Length == 0) {
			throw new ArgumentException(Flag + " needs a token: " + TokenFlag + "=<secret every request carries>");
		}
		return new TestHookOptions(endpoint, token);
	}

	//The command line the emulator itself reads: the hook's two flags, and the
	//value after either in its space form, are gone. Left in, ConfigManager.ProcessSwitch
	//rejects them and the OSD would show the token in an "invalid argument" message.
	public static string[] WithoutHookArgs(IReadOnlyList<string> args)
	{
		List<string> rest = new();
		for(int i = 0; i < args.Count; i++) {
			string arg = args[i];
			if(IsHookFlag(arg, Flag) || IsHookFlag(arg, TokenFlag)) {
				if(arg.IndexOf('=') < 0) {
					i++;
				}
				continue;
			}
			rest.Add(arg);
		}
		return rest.ToArray();
	}

	private static bool IsHookFlag(string arg, string flag) => arg == flag || arg.StartsWith(flag + "=", StringComparison.Ordinal);

	private static string ValueOf(IReadOnlyList<string> args, ref int i, string flag)
	{
		string arg = args[i];
		if(arg.Length > flag.Length) {
			return arg.Substring(flag.Length + 1);
		}
		return i + 1 < args.Count ? args[++i] : "";
	}
}
