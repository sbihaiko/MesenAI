using System;
using System.Collections.Generic;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.CommunityPacks
{
	//ADR-0211: a declared <supportedRom> that contradicts the loaded ROM refuses
	//the install instead of being overwritten by it. Covers all four verdicts,
	//the header/body hash distinction (the trap the ADR names), and the two
	//real packs issue #314 examined and cleared as correct behaviour.
	public class SupportedRomGuardTests
	{
		//Hashes taken from the real artifacts, so a regression here is a
		//regression against the packs on disk and not against invented input.
		private const string ContraDeclared = "C9EA66BB7CB30AD5343F1721B1D4D3219859319B";
		private const string BombermanWholeFile = "12531701E633D2196C9A15F944B101A8205248E4";
		private const string BombermanNoIntro = "D2BF7BD570430902114F1E3393F1FEB8B1C76E4D";

		private static LegacyHdPackInstall.SupportedRomDeclaration Read(params string[] lines)
		{
			return LegacyHdPackInstall.ReadSupportedRom(lines);
		}

		[Fact]
		public void PackDeclaringAnotherRomIsRefused()
		{
			//Issue #314 itself: the Contra 80s pack extracted under Bomberman.
			LegacyHdPackInstall.SupportedRomDeclaration declaration = Read("<supportedRom>" + ContraDeclared);
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Contradicts,
				LegacyHdPackInstall.DecideSupportedRom(declaration, BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void PackDeclaringNothingInstallsUnchanged()
		{
			//Four catalogued packs (Castlevania, Mega Man, SMB, Zelda II) carry
			//no <supportedRom> at all - ADR-0145's optimism must still apply.
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.NotDeclared,
				LegacyHdPackInstall.DecideSupportedRom(Read("<scale>4", "<ver>100"), BombermanNoIntro, BombermanWholeFile));
		}

		[Theory]
		[InlineData("")]
		[InlineData("   ")]
		[InlineData("not-a-hash")]
		[InlineData("C9EA66BB7CB30AD5")]                          //too short
		[InlineData("ZZEA66BB7CB30AD5343F1721B1D4D3219859319B")]  //40 chars, not hex
		public void UnparseableDeclarationNeverRefuses(string value)
		{
			//A malformed line is no evidence, so it can never cost a user their
			//install - the failure mode of this guard has to be permissive.
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.NotDeclared,
				LegacyHdPackInstall.DecideSupportedRom(Read("<supportedRom>" + value), BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void PackDeclaringTheWholeFileHashMatches()
		{
			//HdPackBuilder writes RomFile.GetSha1Hash() - the whole file,
			//header included. Every bootstrap pack on disk is this form.
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Matches,
				LegacyHdPackInstall.DecideSupportedRom(
					Read("<supportedRom>" + BombermanWholeFile), BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void PackDeclaringTheNoIntroBodyHashAlsoMatches()
		{
			//The trap this ADR names: these are two different hashes of the
			//*same* ROM. Comparing across the forms would refuse every correct
			//install; refusing the body form outright would be stricter than
			//the loader, which already tries both for <patch> (NesConsole).
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Matches,
				LegacyHdPackInstall.DecideSupportedRom(
					Read("<supportedRom>" + BombermanNoIntro), BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void ComparisonIsCaseInsensitive()
		{
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Matches,
				LegacyHdPackInstall.DecideSupportedRom(
					Read("<supportedRom>" + BombermanWholeFile.ToLowerInvariant()), BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void PackDeclaringItsOwnPatchTargetInstalls()
		{
			//Zelda Remastered: <supportedRom> and <patch> both name
			//DAB79C84…, the hash of the ROM *after* ZeldaHD.ips (ADR-0198 §2).
			//It matches no unpatched dump and is not a contradiction. Issue
			//#314 examined this pack and cleared it; refusing it here would
			//turn a correct install into a regression.
			const string patched = "DAB79C84934F9AA5DB4E7DAD390E5D0C12443FA2";
			LegacyHdPackInstall.SupportedRomDeclaration declaration = Read(
				"<supportedRom>" + patched,
				"<patch>ZeldaHD.ips," + patched);
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.PatchTarget,
				LegacyHdPackInstall.DecideSupportedRom(
					declaration, "B6643CE5CD43F14915466407FFA1F89C1CDFE76F", "3CDFA4F28B04BF39479FD431E66680F420FCE64B"));
		}

		[Fact]
		public void APatchLineForAnotherRomDoesNotExcuseTheDeclaration()
		{
			//The patch exemption is narrow on purpose: it covers a pack that
			//declares *its own* patched hash, not any pack that happens to ship
			//a patch. Otherwise #314's pack could be excused by one <patch>
			//line for an unrelated ROM.
			LegacyHdPackInstall.SupportedRomDeclaration declaration = Read(
				"<supportedRom>" + ContraDeclared,
				"<patch>something.ips,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Contradicts,
				LegacyHdPackInstall.DecideSupportedRom(declaration, BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void ADifferentRevisionOfTheSameGameIsRefused()
		{
			//Pac-Man: the pack targets the 1993 Namco release, the local dump is
			//the 1984 one. ADR-0211 accepts this as the intended trade - the
			//author stated which dump they targeted. Asserted so the day it
			//becomes too strict, this test is what gets edited, with an ADR
			//amendment beside it.
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Contradicts,
				LegacyHdPackInstall.DecideSupportedRom(
					Read("<supportedRom>E7D818E128593109B5C480497A050FACC0744F1B"),
					"A34E68372082513209A795786C8EEA493CC2CD14",
					"727176933C25DE055E7DAA92E8B943F67CAE4D9B"));
		}

		[Fact]
		public void FirstSupportedRomLineWins()
		{
			//A second declaration cannot relax the first - otherwise appending
			//one line to a pack would be enough to get past the guard.
			LegacyHdPackInstall.SupportedRomDeclaration declaration = Read(
				"<supportedRom>" + ContraDeclared,
				"<supportedRom>" + BombermanWholeFile);
			Assert.Equal(ContraDeclared, declaration.Declared);
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.Contradicts,
				LegacyHdPackInstall.DecideSupportedRom(declaration, BombermanNoIntro, BombermanWholeFile));
		}

		[Fact]
		public void DeclarationIsReadFromRealHiresPreamble()
		{
			//Whitespace and CR-terminated lines are what actual hires.txt files
			//look like; the reader trims rather than requiring a clean file.
			List<string> lines = new() {
				"<ver>100",
				"<scale>4",
				"  <supportedRom>" + BombermanWholeFile + "  ",
				"<overscan>0,0,0,0"
			};
			LegacyHdPackInstall.SupportedRomDeclaration declaration = LegacyHdPackInstall.ReadSupportedRom(lines);
			Assert.Equal(BombermanWholeFile, declaration.Declared);
			Assert.Empty(declaration.PatchTargets);
		}

		[Fact]
		public void DefaultDeclarationIsEmptyAndNeverRefuses()
		{
			//The coordinator constructs this when hires.txt cannot be read.
			LegacyHdPackInstall.SupportedRomDeclaration declaration = new();
			Assert.Equal("", declaration.Declared);
			Assert.Equal(
				LegacyHdPackInstall.SupportedRomVerdict.NotDeclared,
				LegacyHdPackInstall.DecideSupportedRom(declaration, BombermanNoIntro, BombermanWholeFile));
		}
	}
}
