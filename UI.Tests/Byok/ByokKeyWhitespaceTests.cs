using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Byok
{
	//#681 (3): a key pasted with surrounding whitespace. scripts/jev_client.py
	//strips the variable (load_api_key), so the child works with the bare key;
	//the app must store and redact that same bare key, or a line where the
	//child prints it reaches the app unredacted.
	public class ByokKeyWhitespaceTests
	{
		private const string Key = "sk-or-v1-TEST-0123456789abcdef-never-leaks";

		//An OS entry that holds the padded key anyway: written before this
		//rule, or by hand in Keychain Access / Credential Manager.
		private sealed class PaddedEntryStore : IByokKeyStore
		{
			public string? UnsupportedReason => null;
			public string? Read(ByokVendor vendor) => "  " + Key + " \n";
			public void Write(ByokVendor vendor, string key) => throw new NotSupportedException();
			public bool Remove(ByokVendor vendor) => false;
		}

		public static IEnumerable<object[]> Stores()
		{
			InMemoryByokKeyStore pasted = new();
			pasted.Write(ByokVendor.OpenRouter, "  " + Key + " \n");
			yield return new object[] { "pasted", pasted };
			yield return new object[] { "padded OS entry", new PaddedEntryStore() };
		}

		[Theory]
		[MemberData(nameof(Stores))]
		public async Task A_key_with_whitespace_is_redacted_where_the_child_prints_it_stripped(string source, IByokKeyStore store)
		{
			if(OperatingSystem.IsWindows()) {
				return;
			}
			//The child strips the variable as load_api_key does, then prints it.
			List<string> args = new() { "-c", "k=$(printf '%s' \"$OPENROUTER_API_KEY\" | tr -d ' \\t\\r\\n'); printf 'key=%s\\n' \"$k\"" };
			List<string> lines = new();
			using ByokJob job = ByokJobLauncher.Start(store, ByokVendor.OpenRouter, "/bin/sh", args, null, line => { lock(lines) { lines.Add(line); } });
			Assert.Equal(0, await job.WaitForExitAsync());

			Assert.True(lines.Contains("key=" + ByokJobLauncher.Redacted), source);
			Assert.DoesNotContain(lines, l => l.Contains(Key));
		}

		[Theory]
		[InlineData("")]
		[InlineData(" \t\n")]
		public void An_empty_or_blank_key_is_refused(string pasted)
		{
			Assert.Throws<ArgumentException>(() => new InMemoryByokKeyStore().Write(ByokVendor.OpenRouter, pasted));
		}

		[Theory]
		[InlineData("  " + Key + " \n")]
		[InlineData("\t" + Key + "\r\n")]
		[InlineData(Key)]
		public void The_store_keeps_the_key_without_surrounding_whitespace(string pasted)
		{
			InMemoryByokKeyStore store = new();
			store.Write(ByokVendor.OpenRouter, pasted);
			Assert.Equal(Key, store.Read(ByokVendor.OpenRouter));
		}
	}
}
