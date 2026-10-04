using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Play
{
	//ADR-0256 Decision 6 ("Segue o controle na mão"): W-P4's footer names the
	//control the player is actually holding - Esc from a keyboard, the pad's own
	//back button from a pad (B on an Xbox pad, ○ on a DualShock) - and names no
	//control at all when the pad's family cannot be told, because a guess would
	//send the player looking for a button their pad may not have.
	public class PlayResumeHintTests
	{
		[Fact]
		public void The_keyboard_resumes_with_Esc()
		{
			Assert.Equal(new PlayResumeHint("OverlayResumeHint", "Esc"), PlayMenuHint.ResumeHint(PlayInputDevice.Keyboard, null));
		}

		//The keyboard is the keyboard whatever a pad says: a family beside it is
		//not read.
		[Fact]
		public void The_keyboard_stays_Esc_beside_a_pad_family()
		{
			Assert.Equal(new PlayResumeHint("OverlayResumeHint", "Esc"), PlayMenuHint.ResumeHint(PlayInputDevice.Keyboard, PadFamily.Xbox));
		}

		[Fact]
		public void An_Xbox_pad_resumes_with_its_B()
		{
			Assert.Equal(new PlayResumeHint("OverlayResumeHint", "B"), PlayMenuHint.ResumeHint(PlayInputDevice.Controller, PadFamily.Xbox));
		}

		[Fact]
		public void A_Ps4_pad_resumes_with_its_circle()
		{
			Assert.Equal(new PlayResumeHint("OverlayResumeHint", "○"), PlayMenuHint.ResumeHint(PlayInputDevice.Controller, PadFamily.Ps4));
		}

		//Decision 4's reason on a second surface: a family the app cannot tell
		//names nothing, so the footer falls back to a line that claims no button.
		[Fact]
		public void A_pad_whose_family_cannot_be_told_names_no_control()
		{
			PlayResumeHint hint = PlayMenuHint.ResumeHint(PlayInputDevice.Controller, null);
			Assert.Equal("OverlayResumeHintNeutral", hint.Message);
			Assert.Equal("", hint.Param);
		}

		//Only the unresolved family gets the neutral line: every answer that names
		//a control goes through the format.
		[Fact]
		public void Only_the_unresolved_family_shows_the_neutral_line()
		{
			PlayResumeHint[] named = {
				PlayMenuHint.ResumeHint(PlayInputDevice.Keyboard, null),
				PlayMenuHint.ResumeHint(PlayInputDevice.Controller, PadFamily.Xbox),
				PlayMenuHint.ResumeHint(PlayInputDevice.Controller, PadFamily.Ps4)
			};
			Assert.All(named, hint => {
				Assert.Equal("OverlayResumeHint", hint.Message);
				Assert.False(string.IsNullOrEmpty(hint.Param));
			});
			Assert.Equal(3, new HashSet<string>(Array.ConvertAll(named, hint => hint.Param)).Count);
		}

		//The footer's copy is a format that takes the control's name - the rule
		//names a control, it does not spell the sentence - and the neutral line is
		//the only one without a placeholder.
		[Fact]
		public void The_footer_format_takes_the_control_name()
		{
			Dictionary<string, string> texts = ResourceTexts();
			Assert.Contains("{0}", texts["OverlayResumeHint"], StringComparison.Ordinal);
			Assert.Equal("B to resume", string.Format(texts["OverlayResumeHint"], "B"));
			Assert.DoesNotContain("{0}", texts["OverlayResumeHintNeutral"]);
			Assert.False(string.IsNullOrWhiteSpace(texts["OverlayResumeHintNeutral"]));
		}

		//The old fixed line is gone: leaving it behind would be copy nothing reads.
		[Fact]
		public void The_fixed_Esc_line_is_no_longer_a_resource()
		{
			Assert.DoesNotContain("lblOverlayEscHint", ResourceTexts().Keys);
		}

		private static Dictionary<string, string> ResourceTexts()
		{
			XDocument doc = XDocument.Load(Path.Combine(FindRepoRoot(), "UI", "Localization", "resources.en.xml"));
			Dictionary<string, string> texts = new();
			foreach(XElement node in doc.Descendants().Where(n => n.Name.LocalName is "Message" or "Control")) {
				string? id = node.Attribute("ID")?.Value;
				if(id != null) {
					texts.TryAdd(id, node.Value);
				}
			}
			return texts;
		}

		private static string FindRepoRoot()
		{
			DirectoryInfo? dir = new(AppContext.BaseDirectory);
			while(dir != null && !File.Exists(Path.Combine(dir.FullName, "Mesen.sln"))) {
				dir = dir.Parent;
			}
			return dir?.FullName ?? throw new InvalidOperationException("Could not locate repo root (Mesen.sln) from " + AppContext.BaseDirectory);
		}
	}
}
