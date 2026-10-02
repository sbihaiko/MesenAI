using System.Collections.Generic;
using System.Linq;
using Mesen.Interop;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Look
{
	//ADR-0246 (P.13, PRD Part B §13 W-P10): Settings › Look names the three
	//layers - Art, Pixels, Screen - and says where each choice's result goes.
	//These pin the rules host-free; UI.HeadlessTests only checks the wiring.
	public class LookLayersTests
	{
		private static readonly NamedLookEntry Crt = new("crt-tv", "CRT TV", "/home/Shaders/Looks/crt/crt-geom.slangp");
		private static readonly NamedLookEntry Lcd = new("handheld-lcd", "Handheld LCD", "/home/Shaders/Looks/handheld/zfast-lcd.slangp");

		private static LookInput Input(string filter = "None", string shader = "", ConsoleType? console = ConsoleType.Nes, bool packArt = false,
			ShaderAvailability shaders = ShaderAvailability.Available, IReadOnlyList<string>? recent = null)
		{
			return new LookInput(filter, shader, console, packArt, packArt ? "Contra 80s" : "", shaders, new[] { Crt, Lcd }, recent ?? new string[0]);
		}

		[Theory]
		[InlineData("None", LookFilterLayer.None)]
		[InlineData("HQ4x", LookFilterLayer.Pixels)]
		[InlineData("xBRZ6x", LookFilterLayer.Pixels)]
		[InlineData("_2xSai", LookFilterLayer.Pixels)]
		[InlineData("Prescale10x", LookFilterLayer.Pixels)]
		[InlineData("NtscBlargg", LookFilterLayer.Screen)]
		[InlineData("NtscBisqwit", LookFilterLayer.Screen)]
		[InlineData("LcdGrid", LookFilterLayer.Screen)]
		public void Each_filter_belongs_to_one_layer(string filter, LookFilterLayer layer)
		{
			Assert.Equal(layer, LookLayers.LayerOf(filter));
		}

		[Fact]
		public void Pixels_offers_the_short_list_in_order()
		{
			LookView view = LookLayers.Build(Input());
			Assert.Equal(new[] { PixelsItemKind.Sharp, PixelsItemKind.SmoothHq4x, PixelsItemKind.SmoothXbrz4x, PixelsItemKind.MoreInOptions },
				view.PixelsItems.Select(i => i.Kind));
			Assert.Equal(PixelsItemKind.Sharp, view.PixelsSelected.Kind);
			Assert.True(view.PixelsEnabled);
			Assert.Equal(LookReason.None, view.PixelsReason);
		}

		[Theory]
		[InlineData(ConsoleType.Nes)]
		[InlineData(ConsoleType.Gameboy)]
		[InlineData(ConsoleType.Sms)]
		public void Pixels_is_disabled_with_its_reason_while_a_pack_draws_the_art(ConsoleType console)
		{
			LookView view = LookLayers.Build(Input(console: console, packArt: true));
			Assert.False(view.PixelsEnabled);
			Assert.Equal(LookReason.PackDrawsArt, view.PixelsReason);
			Assert.True(view.HasArt);
			Assert.Equal("Contra 80s", view.PackName);
		}

		[Fact]
		public void Look_never_overrides_a_scale_filter_set_in_Options_over_a_pack()
		{
			LookInput input = Input(filter: "HQ4x", packArt: true);
			LookView view = LookLayers.Build(input);
			Assert.Equal(PixelsItemKind.SmoothHq4x, view.PixelsSelected.Kind);
			foreach(PixelsItem item in view.PixelsItems) {
				Assert.Equal(("HQ4x", ""), LookLayers.ApplyPixels(input, item));
			}
		}

		[Fact]
		public void A_value_set_in_Options_that_is_not_in_the_list_shows_as_the_current_item()
		{
			LookInput input = Input(filter: "Scale3x");
			LookView view = LookLayers.Build(input);
			Assert.Equal(PixelsItemKind.Current, view.PixelsSelected.Kind);
			Assert.Equal("Scale3x", view.PixelsSelected.Filter);
			Assert.Contains(view.PixelsSelected, view.PixelsItems);
			//Re-picking it (what a ComboBox does on open/close) changes nothing.
			Assert.Equal(("Scale3x", ""), LookLayers.ApplyPixels(input, view.PixelsSelected));
			//"More in Options…" never writes either.
			Assert.Equal(("Scale3x", ""), LookLayers.ApplyPixels(input, view.PixelsItems.Single(i => i.Kind == PixelsItemKind.MoreInOptions)));
		}

		[Fact]
		public void Picking_a_smooth_item_sets_the_filter_and_Sharp_clears_only_a_scale_filter()
		{
			LookInput none = Input();
			LookView view = LookLayers.Build(none);
			Assert.Equal(("HQ4x", ""), LookLayers.ApplyPixels(none, view.PixelsItems.Single(i => i.Kind == PixelsItemKind.SmoothHq4x)));
			Assert.Equal(("xBRZ4x", ""), LookLayers.ApplyPixels(none, view.PixelsItems.Single(i => i.Kind == PixelsItemKind.SmoothXbrz4x)));

			LookInput hq = Input(filter: "HQ4x");
			Assert.Equal(("None", ""), LookLayers.ApplyPixels(hq, LookLayers.Build(hq).PixelsItems[0]));

			//The NTSC filter lives in the same config field, but it is a Screen
			//choice: Pixels shows Sharp and re-picking Sharp leaves it alone.
			LookInput ntsc = Input(filter: "NtscBlargg");
			LookView ntscView = LookLayers.Build(ntsc);
			Assert.Equal(PixelsItemKind.Sharp, ntscView.PixelsSelected.Kind);
			Assert.Equal(("NtscBlargg", ""), LookLayers.ApplyPixels(ntsc, ntscView.PixelsSelected));
		}

		[Fact]
		public void Every_choice_carries_where_its_result_goes()
		{
			LookView view = LookLayers.Build(Input(recent: new[] { "/x/crt-royale.slangp" }));
			Assert.All(view.PixelsItems, i => Assert.Equal(LookCapture.Captured, i.Capture));
			foreach(ScreenItem item in view.ScreenItems) {
				LookCapture expected = item.Kind switch {
					ScreenItemKind.None => LookCapture.None,
					ScreenItemKind.Ntsc => LookCapture.Captured,
					_ => LookCapture.DisplayOnly
				};
				Assert.Equal(expected, item.Capture);
			}
		}

		[Fact]
		public void The_mark_follows_the_selection()
		{
			Assert.Equal(LookCapture.Captured, LookLayers.Build(Input()).PixelsCapture);
			Assert.Equal(LookCapture.None, LookLayers.Build(Input()).ScreenCapture);
			Assert.Equal(LookCapture.Captured, LookLayers.Build(Input(filter: "NtscBlargg")).ScreenCapture);
			Assert.Equal(LookCapture.Captured, LookLayers.Build(Input(filter: "LcdGrid")).ScreenCapture);
			Assert.Equal(LookCapture.DisplayOnly, LookLayers.Build(Input(shader: Crt.PresetPath)).ScreenCapture);
			//Options can set an NTSC filter and a shader at once: both marks apply.
			LookView mixed = LookLayers.Build(Input(filter: "NtscBlargg", shader: "/x/crt-royale.slangp"));
			Assert.Equal(LookCapture.Mixed, mixed.ScreenCapture);
			Assert.Equal(ScreenItemKind.Current, mixed.ScreenSelected.Kind);
		}

		[Fact]
		public void Screen_offers_none_ntsc_the_named_looks_recent_files_and_choose()
		{
			LookView view = LookLayers.Build(Input(recent: new[] { "/x/crt-royale.slangp", Crt.PresetPath, "/x/crt-royale.slangp" }));
			Assert.Equal(new[] { ScreenItemKind.None, ScreenItemKind.Ntsc, ScreenItemKind.NamedLook, ScreenItemKind.NamedLook, ScreenItemKind.ShaderFile, ScreenItemKind.ChooseFile },
				view.ScreenItems.Select(i => i.Kind));
			Assert.Equal(new[] { "CRT TV", "Handheld LCD", "crt-royale" }, view.ScreenItems.Where(i => i.Label != "").Select(i => i.Label));
			Assert.Equal(ScreenItemKind.None, view.ScreenSelected.Kind);
			Assert.False(view.CanAdjust);
		}

		[Fact]
		public void A_named_look_is_recognized_as_the_current_shader_and_can_be_adjusted()
		{
			LookView view = LookLayers.Build(Input(shader: Lcd.PresetPath));
			Assert.Equal(ScreenItemKind.NamedLook, view.ScreenSelected.Kind);
			Assert.Equal("Handheld LCD", view.ScreenSelected.Label);
			Assert.True(view.CanAdjust);
		}

		[Fact]
		public void A_shader_file_set_elsewhere_shows_as_a_file_item_even_if_not_recent()
		{
			LookView view = LookLayers.Build(Input(shader: "/x/other.slangp"));
			Assert.Equal(ScreenItemKind.ShaderFile, view.ScreenSelected.Kind);
			Assert.Equal("other", view.ScreenSelected.Label);
			Assert.Contains(view.ScreenSelected, view.ScreenItems);
		}

		[Fact]
		public void Ntsc_is_for_NES_only_and_disabled_elsewhere_with_its_reason()
		{
			ScreenItem nes = LookLayers.Build(Input(console: ConsoleType.Nes)).ScreenItems.Single(i => i.Kind == ScreenItemKind.Ntsc);
			Assert.True(nes.IsEnabled);
			ScreenItem noGame = LookLayers.Build(Input(console: null)).ScreenItems.Single(i => i.Kind == ScreenItemKind.Ntsc);
			Assert.True(noGame.IsEnabled);
			foreach(ConsoleType other in new[] { ConsoleType.Gameboy, ConsoleType.Sms, ConsoleType.Gba }) {
				ScreenItem item = LookLayers.Build(Input(console: other)).ScreenItems.Single(i => i.Kind == ScreenItemKind.Ntsc);
				Assert.False(item.IsEnabled);
				Assert.Equal(LookReason.NtscNesOnly, item.Reason);
			}
		}

		[Fact]
		public void Ntsc_says_it_is_not_applied_while_a_pack_draws_the_art()
		{
			LookView view = LookLayers.Build(Input(filter: "NtscBlargg", packArt: true));
			Assert.Equal(ScreenItemKind.Ntsc, view.ScreenSelected.Kind);
			Assert.Equal(LookReason.NtscNotAppliedUnderPack, view.ScreenNote);
			Assert.Equal(LookReason.NtscNotAppliedUnderPack, view.ScreenItems.Single(i => i.Kind == ScreenItemKind.Ntsc).Reason);
			//Without a pack the item reads plainly.
			Assert.Equal(LookReason.None, LookLayers.Build(Input(filter: "NtscBlargg")).ScreenNote);
		}

		[Fact]
		public void Shaders_stay_allowed_over_pack_art()
		{
			LookView view = LookLayers.Build(Input(shader: Crt.PresetPath, packArt: true));
			Assert.All(view.ScreenItems.Where(i => i.Kind == ScreenItemKind.NamedLook), i => Assert.True(i.IsEnabled));
			Assert.Equal(LookReason.None, view.ScreenNote);
		}

		[Theory]
		[InlineData(ShaderAvailability.NotInBuild, LookReason.ShadersNotInBuild)]
		[InlineData(ShaderAvailability.NeedsMetalRenderer, LookReason.ShadersNeedMetalRenderer)]
		public void Unavailable_shaders_are_shown_disabled_with_their_reason(ShaderAvailability availability, LookReason reason)
		{
			LookView view = LookLayers.Build(Input(shaders: availability, recent: new[] { "/x/a.slangp" }));
			foreach(ScreenItem item in view.ScreenItems.Where(i => i.Kind is ScreenItemKind.NamedLook or ScreenItemKind.ShaderFile or ScreenItemKind.ChooseFile)) {
				Assert.False(item.IsEnabled);
				Assert.Equal(reason, item.Reason);
			}
			Assert.Equal(2, view.ScreenItems.Count(i => i.Kind == ScreenItemKind.NamedLook));
			Assert.True(view.ScreenItems.Single(i => i.Kind == ScreenItemKind.None).IsEnabled);

			LookView withShader = LookLayers.Build(Input(shader: Crt.PresetPath, shaders: availability));
			Assert.Equal(reason, withShader.ScreenNote);
			Assert.False(withShader.CanAdjust);
		}

		[Theory]
		[InlineData(true, false, false, ShaderAvailability.Available)]
		[InlineData(true, true, false, ShaderAvailability.Available)]
		[InlineData(false, false, false, ShaderAvailability.NotInBuild)]
		[InlineData(false, true, false, ShaderAvailability.NotInBuild)]
		[InlineData(true, true, true, ShaderAvailability.NeedsMetalRenderer)]
		[InlineData(false, true, true, ShaderAvailability.NeedsMetalRenderer)]
		[InlineData(true, false, true, ShaderAvailability.Available)]
		public void Shader_availability_names_why(bool core, bool macOs, bool software, ShaderAvailability expected)
		{
			Assert.Equal(expected, LookLayers.ResolveShaderAvailability(core, macOs, software));
		}

		[Fact]
		public void Screen_choices_write_one_effect()
		{
			LookInput input = Input(filter: "HQ4x", shader: "");
			LookView view = LookLayers.Build(input);
			ScreenItem ntsc = view.ScreenItems.Single(i => i.Kind == ScreenItemKind.Ntsc);
			ScreenItem crt = view.ScreenItems.First(i => i.Kind == ScreenItemKind.NamedLook);
			Assert.Equal(("NtscBlargg", ""), LookLayers.ApplyScreen(input, ntsc));
			//A shader keeps a Pixels filter (it is another layer)...
			Assert.Equal(("HQ4x", Crt.PresetPath), LookLayers.ApplyScreen(input, crt));

			//...but replaces the NTSC filter, and None clears both Screen effects.
			LookInput tv = Input(filter: "NtscBlargg");
			LookView tvView = LookLayers.Build(tv);
			Assert.Equal(("None", Crt.PresetPath), LookLayers.ApplyScreen(tv, tvView.ScreenItems.First(i => i.Kind == ScreenItemKind.NamedLook)));
			LookInput both = Input(filter: "LcdGrid", shader: Crt.PresetPath);
			Assert.Equal(("None", ""), LookLayers.ApplyScreen(both, LookLayers.Build(both).ScreenItems.Single(i => i.Kind == ScreenItemKind.None)));
			//None keeps a Pixels filter.
			LookInput hqShader = Input(filter: "HQ4x", shader: Crt.PresetPath);
			Assert.Equal(("HQ4x", ""), LookLayers.ApplyScreen(hqShader, LookLayers.Build(hqShader).ScreenItems[0]));
		}

		[Fact]
		public void Re_picking_the_current_or_a_disabled_screen_item_changes_nothing()
		{
			LookInput mixed = Input(filter: "NtscBisqwit", shader: "/x/a.slangp");
			LookView view = LookLayers.Build(mixed);
			Assert.Equal(("NtscBisqwit", "/x/a.slangp"), LookLayers.ApplyScreen(mixed, view.ScreenSelected));

			LookInput gb = Input(console: ConsoleType.Gameboy);
			ScreenItem ntsc = LookLayers.Build(gb).ScreenItems.Single(i => i.Kind == ScreenItemKind.Ntsc);
			Assert.Equal(("None", ""), LookLayers.ApplyScreen(gb, ntsc));

			LookInput noShaders = Input(shaders: ShaderAvailability.NotInBuild);
			ScreenItem look = LookLayers.Build(noShaders).ScreenItems.First(i => i.Kind == ScreenItemKind.NamedLook);
			Assert.Equal(("None", ""), LookLayers.ApplyScreen(noShaders, look));
		}

		[Fact]
		public void A_chosen_shader_file_replaces_only_the_screen_layer()
		{
			Assert.Equal(("HQ4x", "/x/b.slangp"), LookLayers.ApplyShaderFile(Input(filter: "HQ4x"), "/x/b.slangp"));
			Assert.Equal(("None", "/x/b.slangp"), LookLayers.ApplyShaderFile(Input(filter: "NtscBlargg"), "/x/b.slangp"));
			Assert.Equal(("None", ""), LookLayers.ApplyShaderFile(Input(shaders: ShaderAvailability.NotInBuild), "/x/b.slangp"));
		}

		[Fact]
		public void Hold_to_Compare_needs_something_besides_the_art()
		{
			Assert.False(LookLayers.Build(Input()).CanCompare);
			Assert.Equal(LookReason.NothingToCompare, LookLayers.Build(Input()).CompareReason);
			Assert.False(LookLayers.Build(Input(packArt: true)).CanCompare);
			Assert.True(LookLayers.Build(Input(filter: "HQ4x")).CanCompare);
			Assert.True(LookLayers.Build(Input(filter: "LcdGrid")).CanCompare);
			Assert.True(LookLayers.Build(Input(shader: Crt.PresetPath)).CanCompare);
			Assert.True(LookLayers.Build(Input(filter: "NtscBlargg")).CanCompare);
			//NTSC under a pack is not applied, so there is nothing to compare...
			Assert.False(LookLayers.Build(Input(filter: "NtscBlargg", packArt: true)).CanCompare);
			//...a scale filter Options set over the pack does run, so there is.
			Assert.True(LookLayers.Build(Input(filter: "HQ4x", packArt: true)).CanCompare);
			//A shader that cannot run is nothing to compare either.
			Assert.False(LookLayers.Build(Input(shader: Crt.PresetPath, shaders: ShaderAvailability.NotInBuild)).CanCompare);
		}

		[Fact]
		public void Without_a_pack_the_art_row_says_so()
		{
			LookView view = LookLayers.Build(Input());
			Assert.False(view.HasArt);
			Assert.Equal("", view.PackName);
		}
	}
}
