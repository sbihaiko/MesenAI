using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Byok
{
	//F14.20 / ADR-0247 Decision 3: one custody interface, one entry per vendor.
	public class ByokKeyStoreTests
	{
		[Fact]
		public void OpenRouter_is_the_variable_jev_client_reads()
		{
			//scripts/jev_client.py: API_KEY_ENV = "OPENROUTER_API_KEY"
			Assert.Equal("OPENROUTER_API_KEY", ByokVendor.OpenRouter.EnvironmentVariable);
			Assert.Equal("openrouter", ByokVendor.OpenRouter.Id);
			Assert.Equal("MesenAI BYOK openrouter", ByokVendor.OpenRouter.ServiceName);
		}

		[Theory]
		[InlineData("OpenRouter", "OPENROUTER_API_KEY")]
		[InlineData("open router", "OPENROUTER_API_KEY")]
		[InlineData("", "OPENROUTER_API_KEY")]
		[InlineData("openrouter", "lower_case")]
		[InlineData("openrouter", "HAS SPACE")]
		public void A_vendor_id_or_variable_outside_the_pattern_is_refused(string id, string variable)
		{
			Assert.Throws<ArgumentException>(() => new ByokVendor(id, "x", variable));
		}

		[Fact]
		public void The_fake_reads_writes_and_removes_per_vendor()
		{
			InMemoryByokKeyStore store = new();
			ByokVendor other = new("gemini", "Gemini", "GEMINI_API_KEY");
			Assert.Null(store.Read(ByokVendor.OpenRouter));
			store.Write(ByokVendor.OpenRouter, "k1");
			store.Write(other, "k2");
			Assert.Equal("k1", store.Read(ByokVendor.OpenRouter));
			Assert.True(store.Remove(ByokVendor.OpenRouter));
			Assert.False(store.Remove(ByokVendor.OpenRouter));
			Assert.Null(store.Read(ByokVendor.OpenRouter));
			Assert.Equal("k2", store.Read(other));
		}

		[Fact]
		public void The_OS_selection_is_the_platform_store_and_Linux_says_why_it_is_unsupported()
		{
			IByokKeyStore store = ByokKeyStores.ForCurrentOS();
			if(OperatingSystem.IsMacOS()) {
				Assert.IsType<MacKeychainByokKeyStore>(store);
				Assert.Null(store.UnsupportedReason);
			} else if(OperatingSystem.IsWindows()) {
				Assert.IsType<WindowsCredentialByokKeyStore>(store);
				Assert.Null(store.UnsupportedReason);
			} else if(OperatingSystem.IsLinux()) {
				Assert.Equal(ByokKeyStores.LinuxUnsupportedReason, store.UnsupportedReason);
				Assert.Null(store.Read(ByokVendor.OpenRouter));
				Assert.Throws<PlatformNotSupportedException>(() => store.Write(ByokVendor.OpenRouter, "k"));
			}
		}

		[Fact]
		public void A_store_failure_names_the_status_and_never_a_key()
		{
			ByokKeyStoreException ex = new("store", ByokVendor.OpenRouter, -25308);
			Assert.Contains("-25308", ex.Message);
			Assert.Contains("OpenRouter", ex.Message);
		}

		//The real OS stores, round-tripped under a throwaway vendor id. Opt-in
		//(MESENAI_BYOK_LIVE=1): a credential-store write from a test is a change to
		//the developer's login keychain / credential vault, even when it is removed
		//again at the end, and CI has no keychain to unlock.
		[Fact]
		public void The_platform_store_round_trips_a_key_when_asked_to()
		{
			if(Environment.GetEnvironmentVariable("MESENAI_BYOK_LIVE") != "1") {
				return;
			}
			IByokKeyStore store = ByokKeyStores.ForCurrentOS();
			if(store.UnsupportedReason != null) {
				return;
			}
			ByokVendor vendor = new("test-" + Guid.NewGuid().ToString("N").Substring(0, 12), "Test", "MESENAI_TEST_KEY");
			try {
				Assert.Null(store.Read(vendor));
				store.Write(vendor, "first-value");
				Assert.Equal("first-value", store.Read(vendor));
				store.Write(vendor, "second-value-é");
				Assert.Equal("second-value-é", store.Read(vendor));
				//#681: stored as the bare key the child uses.
				store.Write(vendor, " third-value\n");
				Assert.Equal("third-value", store.Read(vendor));
				Assert.True(store.Remove(vendor));
				Assert.Null(store.Read(vendor));
				Assert.False(store.Remove(vendor));
			} finally {
				store.Remove(vendor);
			}
		}
	}
}
