using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Mesen.Config;
using Mesen.Logic;
using Xunit;

namespace Mesen.HeadlessTests;

//F14.20 / ADR-0247 Consequences: "every BYOK slice ships a test that the key
//never reaches the sinks the app writes", settings.json first. The other sinks
//are host-free and covered in UI.Tests/Byok/ByokJobLauncherTests; this one needs
//the app's own Configuration type and its source-generated serializer, which only
//this project can reach. A job is run with the key in a store first, so the test
//covers the whole path a W-R8 job takes, not an untouched config.
public class ByokSettingsSerializationTests
{
	private const string Key = "sk-or-v1-HEADLESS-fedcba9876543210-not-in-settings";

	[Fact]
	public async Task A_job_run_with_a_stored_key_leaves_settings_json_without_it()
	{
		InMemoryByokKeyStore store = new();
		store.Write(ByokVendor.OpenRouter, Key);
		(string file, List<string> args) = OperatingSystem.IsWindows()
			? ("cmd.exe", new List<string> { "/d", "/c", "exit 0" })
			: ("/bin/sh", new List<string> { "-c", "exit 0" });
		using(ByokJob job = ByokJobLauncher.Start(store, ByokVendor.OpenRouter, file, args, null, null)) {
			Assert.Equal(0, await job.WaitForExitAsync());
		}

		Configuration config = Configuration.CreateConfig();
		string json = JsonSerializer.Serialize(config, typeof(Configuration), MesenSerializerContext.Default);

		Assert.False(string.IsNullOrEmpty(json));
		Assert.DoesNotContain(Key, json);
		Assert.DoesNotContain(ByokVendor.OpenRouter.EnvironmentVariable, json);
	}
}
