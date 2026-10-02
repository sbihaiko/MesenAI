using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Mesen.Logic;

//ADR-0247 Decision 3 / ADR-0242 Q1 (F14.20): the one custody interface for a
//user's own model key ("bring your own key"). The key the user entered lives in
//the OS credential store - macOS Keychain, Windows Credential Manager - one
//entry per vendor, and is read only when a job starts (ByokJobLauncher). It
//never reaches settings.json, logs, runs/ sidecars, a command line or the text
//of MesenMsgBox.ShowException; ByokJobLauncher hands it to the child through
//the environment alone. Nothing here keeps the key in a field: every store
//reads from the OS on each call.
public interface IByokKeyStore
{
	//null when this store can hold a key on this computer; otherwise the
	//sentence a disabled control shows (PRD Part B §13.3 rule: a disabled
	//control says why).
	string? UnsupportedReason { get; }

	//The stored key, or null when none is stored.
	string? Read(ByokVendor vendor);

	//Stores (or replaces) the vendor's key.
	void Write(ByokVendor vendor, string key);

	//Removes the vendor's key. Returns false when there was none.
	bool Remove(ByokVendor vendor);
}

//A vendor whose key the user may store: its entry name in the credential store
//and the one environment variable its script reads the key from.
public sealed class ByokVendor
{
	private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,31}$");
	private static readonly Regex EnvPattern = new("^[A-Z][A-Z0-9_]{0,63}$");

	//ADR-0242: the AI recorder's vendor; scripts/jev_client.py reads this
	//variable (API_KEY_ENV).
	public static readonly ByokVendor OpenRouter = new("openrouter", "OpenRouter", "OPENROUTER_API_KEY");

	public string Id { get; }
	public string DisplayName { get; }
	public string EnvironmentVariable { get; }

	public ByokVendor(string id, string displayName, string environmentVariable)
	{
		if(!IdPattern.IsMatch(id)) {
			throw new ArgumentException("A vendor id is 1-32 lowercase letters, digits or '-'.", nameof(id));
		}
		if(!EnvPattern.IsMatch(environmentVariable)) {
			throw new ArgumentException("An environment variable name is uppercase letters, digits or '_'.", nameof(environmentVariable));
		}
		Id = id;
		DisplayName = displayName;
		EnvironmentVariable = environmentVariable;
	}

	//The credential store's service (macOS) / target name (Windows) prefix.
	public const string ServicePrefix = "MesenAI BYOK";

	public string ServiceName => ServicePrefix + " " + Id;
}

//A store failure. Its message carries the OS status code only - never the key,
//because this text can reach MesenMsgBox.ShowException and the log.
public sealed class ByokKeyStoreException : Exception
{
	public int Status { get; }

	public ByokKeyStoreException(string operation, ByokVendor vendor, int status)
		: base($"Could not {operation} the {vendor.DisplayName} key in the system credential store (status {status}).")
	{
		Status = status;
	}
}

public static class ByokKeyStores
{
	//Linux: libsecret's store/lookup calls are variadic (secret_password_store_sync)
	//or take a GHashTable built with glib's hash function pointers
	//(secret_password_storev_sync), and neither can be exercised on the machines
	//this slice is built and tested on. Rather than ship an untested P/Invoke
	//that handles a secret, the store says it is unsupported, with this reason.
	public const string LinuxUnsupportedReason = "Storing a key on Linux needs libsecret, which this build does not use yet.";
	public const string OtherUnsupportedReason = "This system has no credential store this build can use.";

	//The store for this OS. Pure selection; nothing is read until a call.
	public static IByokKeyStore ForCurrentOS()
	{
		if(OperatingSystem.IsMacOS()) {
			return new MacKeychainByokKeyStore();
		} else if(OperatingSystem.IsWindows()) {
			return new WindowsCredentialByokKeyStore();
		} else if(OperatingSystem.IsLinux()) {
			return new UnsupportedByokKeyStore(LinuxUnsupportedReason);
		}
		return new UnsupportedByokKeyStore(OtherUnsupportedReason);
	}
}

//A store that holds nothing and says why (Linux, for now).
public sealed class UnsupportedByokKeyStore : IByokKeyStore
{
	public string? UnsupportedReason { get; }

	public UnsupportedByokKeyStore(string reason)
	{
		UnsupportedReason = reason;
	}

	public string? Read(ByokVendor vendor) => null;

	public void Write(ByokVendor vendor, string key)
	{
		throw new PlatformNotSupportedException(UnsupportedReason);
	}

	public bool Remove(ByokVendor vendor) => false;
}

//The test double (UI.Tests and UI.HeadlessTests). It is the one store that
//holds keys in a field, by construction: it stands in for the OS store.
public sealed class InMemoryByokKeyStore : IByokKeyStore
{
	private readonly Dictionary<string, string> _keys = new();

	public string? UnsupportedReason => null;
	public int ReadCount { get; private set; }

	public string? Read(ByokVendor vendor)
	{
		ReadCount++;
		return _keys.TryGetValue(vendor.Id, out string? key) ? key : null;
	}

	public void Write(ByokVendor vendor, string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		_keys[vendor.Id] = key;
	}

	public bool Remove(ByokVendor vendor) => _keys.Remove(vendor.Id);
}
