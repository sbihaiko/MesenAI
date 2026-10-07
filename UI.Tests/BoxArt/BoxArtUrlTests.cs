using System;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.BoxArt
{
	//The two rules that decide WHICH file is asked for: the collection's own file
	//name rule, and the repository each console's art is published in.
	public class BoxArtUrlTests
	{
		//The eleven characters the collection's file names cannot carry, each
		//replaced by `_`. One case per character, so a character dropped from the
		//table fails a test that names it.
		[Theory]
		[InlineData("&")]
		[InlineData("*")]
		[InlineData("/")]
		[InlineData(":")]
		[InlineData("`")]
		[InlineData("<")]
		[InlineData(">")]
		[InlineData("?")]
		[InlineData("\\")]
		[InlineData("|")]
		[InlineData("\"")]
		public void Each_replaced_character_becomes_an_underscore(string replaced)
		{
			Assert.Equal("Zelda_II", BoxArtUrl.Sanitize($"Zelda{replaced}II"));
		}

		[Fact]
		public void All_eleven_replaced_characters_at_once()
		{
			//A&B*C/D:E`F<G>H?I\J|K"L - twelve letters, eleven separators.
			Assert.Equal("A_B_C_D_E_F_G_H_I_J_K_L", BoxArtUrl.Sanitize("A&B*C/D:E`F<G>H?I\\J|K\"L"));
		}

		[Theory]
		//Everything the rule does not name is kept: spaces, brackets, punctuation
		//and accented letters are all part of a real No-Intro name.
		[InlineData("Super Mario Bros. 3 (USA)", "Super Mario Bros. 3 (USA)")]
		[InlineData("Tetris (World) (Rev 1)", "Tetris (World) (Rev 1)")]
		[InlineData("Aladdin (USA, Europe) [!]", "Aladdin (USA, Europe) [!]")]
		[InlineData("Pokemon - FireRed Version (USA)", "Pokemon - FireRed Version (USA)")]
		public void Nothing_else_is_touched(string name, string sanitized)
		{
			Assert.Equal(sanitized, BoxArtUrl.Sanitize(name));
		}

		[Fact]
		public void The_url_is_the_console_repository_and_the_sanitised_name()
		{
			//Checked against the collection itself on 2026-10-07: this URL answers
			//206 with the picture. The class encodes every character a URL path may
			//not carry raw (the space, and the punctuation a No-Intro name keeps),
			//which the host decodes back to the file the collection actually holds.
			Assert.Equal(
				"https://raw.githubusercontent.com/Nintendo_-_Nintendo_Entertainment_System/master/Named_Boxarts/Super%20Mario%20Bros.%203%20%28USA%29.png",
				BoxArtUrl.Boxarts(BoxArtConsole.Nes, "Super Mario Bros. 3 (USA)")!.AbsoluteUri
			);
		}

		[Theory]
		[InlineData(BoxArtConsole.Nes, "Nintendo_-_Nintendo_Entertainment_System")]
		[InlineData(BoxArtConsole.GameBoy, "Nintendo_-_Game_Boy")]
		[InlineData(BoxArtConsole.GameBoyColor, "Nintendo_-_Game_Boy_Color")]
		[InlineData(BoxArtConsole.GameBoyAdvance, "Nintendo_-_Game_Boy_Advance")]
		[InlineData(BoxArtConsole.MasterSystem, "Sega_-_Master_System_-_Mark_III")]
		[InlineData(BoxArtConsole.Sg1000, "Sega_-_SG-1000")]
		[InlineData(BoxArtConsole.GameGear, "Sega_-_Game_Gear")]
		public void One_repository_per_console(BoxArtConsole console, string repository)
		{
			Uri? url = BoxArtUrl.Titles(console, "Tetris (World) (Rev 1)");

			Assert.NotNull(url);
			Assert.StartsWith($"https://raw.githubusercontent.com/{repository}/master/Named_Titles/", url!.AbsoluteUri);
			//HTTPS only, and never a bare name: an http:// hop would be refused by
			//the host allow-list anyway (ADR-0138 §41).
			Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
			Assert.Equal(repository, url.AbsolutePath.Split('/')[1]);
		}

		[Theory]
		[InlineData(BoxArtConsole.Unknown, "Tetris (World) (Rev 1)")]
		[InlineData(BoxArtConsole.Nes, "")]
		public void No_name_or_no_repository_means_no_url(BoxArtConsole console, string name)
		{
			Assert.Null(BoxArtUrl.Boxarts(console, name));
			Assert.Null(BoxArtUrl.Titles(console, name));
		}

		[Fact]
		public void A_slash_in_a_name_cannot_escape_the_repository_path()
		{
			Uri? url = BoxArtUrl.Boxarts(BoxArtConsole.GameGear, "Sonic/../Secret (World)");

			Assert.NotNull(url);
			//The slash is gone before the URL is built, so the name cannot climb out
			//of the collection's folder: the path has exactly the four segments the
			//builder wrote.
			Assert.Equal("/Sega_-_Game_Gear/master/Named_Boxarts/Sonic_.._Secret%20%28World%29.png", url!.AbsolutePath);
		}
	}
}
