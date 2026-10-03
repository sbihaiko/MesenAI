using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Byok
{
	//F14.20 / ADR-0247 Consequences: every BYOK slice ships a test that the key
	//never reaches the sinks the app writes - the child's argv, logs (here: the
	//child's output as the app receives it), runs/ sidecars (the harness's own
	//files: scripts/test_jev_client.py test_no_key_leak), settings.json
	//(UI.HeadlessTests/ByokSettingsSerializationTests) and the text of
	//MesenMsgBox.ShowException (message + stack trace, and the ToString() it logs).
	public class ByokJobLauncherTests
	{
		private const string Key = "sk-or-v1-TEST-0123456789abcdef-never-leaks";

		private static InMemoryByokKeyStore StoreWithKey()
		{
			InMemoryByokKeyStore store = new();
			store.Write(ByokVendor.OpenRouter, Key);
			return store;
		}

		private static (string file, List<string> args) Shell(string script)
		{
			if(OperatingSystem.IsWindows()) {
				return ("cmd.exe", new List<string> { "/d", "/c", script });
			}
			return ("/bin/sh", new List<string> { "-c", script });
		}

		[Fact]
		public void StartInfo_carries_the_key_in_the_environment_only()
		{
			ProcessStartInfo info = ByokJobLauncher.BuildStartInfo("python3", new[] { "scripts/jev_harness.py", "--game", "megaman2" }, null, ByokVendor.OpenRouter, Key);

			Assert.Equal(Key, info.Environment["OPENROUTER_API_KEY"]);
			Assert.DoesNotContain(info.ArgumentList, a => a.Contains(Key));
			Assert.DoesNotContain(Key, info.Arguments);
			Assert.DoesNotContain(Key, info.FileName);
			Assert.False(info.UseShellExecute);
		}

		[Fact]
		public void A_key_on_the_command_line_is_refused_and_the_refusal_does_not_quote_it()
		{
			ByokLaunchException ex = Assert.Throws<ByokLaunchException>(() =>
				ByokJobLauncher.BuildStartInfo("python3", new[] { "--key", Key }, null, ByokVendor.OpenRouter, Key));
			Assert.DoesNotContain(Key, ex.ToString());
			Assert.DoesNotContain(Key, ex.Message + Environment.NewLine + ex.StackTrace);
		}

		[Fact]
		public async Task The_child_reads_the_key_from_its_environment()
		{
			//The child prints the variable's length, not the variable
			(string file, List<string> args) = OperatingSystem.IsWindows()
				? Shell("echo %OPENROUTER_API_KEY:~0,9%")
				: Shell("printf '%s\\n' \"${#OPENROUTER_API_KEY}\"");
			List<string> lines = new();
			using ByokJob job = ByokJobLauncher.Start(StoreWithKey(), ByokVendor.OpenRouter, file, args, null, line => { lock(lines) { lines.Add(line); } });
			int code = await job.WaitForExitAsync();

			Assert.Equal(0, code);
			string expected = OperatingSystem.IsWindows() ? Key.Substring(0, 9) : Key.Length.ToString();
			Assert.Contains(expected, lines);
		}

		[Fact]
		public async Task A_child_that_prints_the_key_is_redacted_before_the_app_sees_the_line()
		{
			(string file, List<string> args) = OperatingSystem.IsWindows()
				? Shell("echo before %OPENROUTER_API_KEY% after")
				: Shell("printf 'before %s after\\n' \"$OPENROUTER_API_KEY\" >&2; printf '%s\\n' \"$OPENROUTER_API_KEY\"");
			List<string> lines = new();
			using ByokJob job = ByokJobLauncher.Start(StoreWithKey(), ByokVendor.OpenRouter, file, args, null, line => { lock(lines) { lines.Add(line); } });
			await job.WaitForExitAsync();

			Assert.NotEmpty(lines);
			Assert.DoesNotContain(lines, l => l.Contains(Key));
			Assert.Contains(lines, l => l.Contains(ByokJobLauncher.Redacted));
		}

		[Fact]
		public async Task The_started_process_no_longer_holds_the_key_in_its_StartInfo()
		{
			(string file, List<string> args) = Shell("exit 0");
			using ByokJob job = ByokJobLauncher.Start(StoreWithKey(), ByokVendor.OpenRouter, file, args, null, null);
			await job.WaitForExitAsync();

			//Process.StartInfo is the object the launcher built; after the start it
			//is the one long-lived place the key could stay
			Assert.False(job.Process.StartInfo.Environment.ContainsKey("OPENROUTER_API_KEY"));
			Assert.DoesNotContain(job.Process.StartInfo.ArgumentList, a => a.Contains(Key));
		}

		[Fact]
		public async Task The_key_is_read_from_the_store_when_the_job_starts_and_only_then()
		{
			InMemoryByokKeyStore store = StoreWithKey();
			(string file, List<string> args) = Shell("exit 0");
			Assert.Equal(0, store.ReadCount);
			using(ByokJob job = ByokJobLauncher.Start(store, ByokVendor.OpenRouter, file, args, null, null)) {
				Assert.Equal(1, store.ReadCount);
				await job.WaitForExitAsync();
			}
			Assert.Equal(1, store.ReadCount);
		}

		[Fact]
		public void No_stored_key_is_a_named_refusal_and_no_child_starts()
		{
			ByokKeyMissingException ex = Assert.Throws<ByokKeyMissingException>(() =>
				ByokJobLauncher.Start(new InMemoryByokKeyStore(), ByokVendor.OpenRouter, "/bin/sh", new[] { "-c", "exit 0" }, null, null));
			Assert.Contains("OpenRouter", ex.Message);
		}

		[Fact]
		public void A_child_that_cannot_start_reports_a_text_without_the_key()
		{
			string missing = Path.Combine(Path.GetTempPath(), "mesenai-byok-no-such-tool-" + Guid.NewGuid().ToString("N"));
			ByokLaunchException ex = Assert.Throws<ByokLaunchException>(() =>
				ByokJobLauncher.Start(StoreWithKey(), ByokVendor.OpenRouter, missing, new[] { "--x" }, null, null));

			//MesenMsgBox.ShowException logs ex.ToString() and shows Message + StackTrace
			Assert.DoesNotContain(Key, ex.ToString());
			Assert.DoesNotContain(Key, ex.Message + Environment.NewLine + ex.StackTrace);
			Assert.Null(ex.InnerException);
		}

		[Fact]
		public void Redact_replaces_every_occurrence_and_leaves_other_text()
		{
			Assert.Equal("a [redacted] b [redacted]", ByokJobLauncher.Redact("a " + Key + " b " + Key, Key));
			Assert.Equal("nothing here", ByokJobLauncher.Redact("nothing here", Key));
		}

		[Fact]
		public void No_custody_type_keeps_a_key_in_a_static_or_instance_field()
		{
			//"The key is read from the credential store only when a job starts and is
			//not kept in a long-lived field" (ADR-0247). The fake is the one store
			//that holds keys by construction; ByokJob holds only the redactor while
			//its child runs, and drops it on exit (checked above via the output test).
			const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
			AssertNoSecretField(typeof(ByokJobLauncher).GetFields(all));
			AssertNoSecretField(typeof(MacKeychainByokKeyStore).GetFields(all));
			AssertNoSecretField(typeof(WindowsCredentialByokKeyStore).GetFields(all));
			AssertNoSecretField(typeof(UnsupportedByokKeyStore).GetFields(all));
		}

		private static void AssertNoSecretField(FieldInfo[] fields)
		{
			//The one string an unsupported store keeps is its reason sentence
			Assert.Empty(fields.Where(f => !f.IsLiteral && f.Name != "<UnsupportedReason>k__BackingField" && (f.FieldType == typeof(string) || f.FieldType == typeof(byte[]) || f.FieldType == typeof(char[]))).Select(f => f.DeclaringType + "." + f.Name));
		}
	}
}
