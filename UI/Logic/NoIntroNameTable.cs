using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Mesen.Logic
{
	//A ROM the No-Intro database knows: which console its dump belongs to, and
	//the game's own name as the database spells it ("Tetris (World) (Rev 1)").
	public readonly struct NoIntroRomName
	{
		public RomConsole Console { get; }

		//The No-Intro name, tags and all: the canonical spelling comes from
		//CanonicalTitle, the box-art key from this name (the downloader's
		//file-name rule, #1030).
		public string Name { get; }

		public NoIntroRomName(RomConsole console, string name)
		{
			Console = console;
			Name = name;
		}
	}

	//The `SHA1 -> No-Intro name` lookup the flat library reads a ROM through
	//(#1038, spec #1030). A match hands the tile a console and a real title; a
	//miss leaves the cleaned file name in charge, which is why every failure
	//here is a `false`, never an exception.
	//
	//The table is a generated artifact (scripts/generate_no_intro_sha1_table.py,
	//pinned by scripts/test_generate_no_intro_sha1_table.py) committed to the
	//repo and embedded as `Mesen.no_intro_sha1.tsv.gz` by UI.csproj. It is read
	//from the manifest stream, never from a repo-relative path: a published app
	//has no scripts/ tree (the rule ADR-0138 §41 set for the host allow-list).
	//
	//Keys are the SHA-1 of the ROM PAYLOAD, not of the file: for a `.nes` dump
	//the 16-byte iNES header and any 512-byte trainer are skipped and the range
	//is clamped to the header-declared PRG+CHR size (ADR-0003, ADR-0039). Every
	//console is keyed the same way, and the table holds no whole-file key at
	//all: the NES DAT's headered `.nes` rows are dropped by the generator, only
	//their headerless `.unh` twins are listed, so one payload hash decides a
	//lookup. The caller computing the hash must follow the same contract -
	//hashing the raw file silently matches nothing for headered NES dumps, and
	//this table cannot tell that apart from a ROM the database does not know.
	//
	//Host-free by construction (ADR-0123): BCL plus System.IO.Compression, so it
	//dual-compiles into UI.Tests and the lookup is unit-tested without a window.
	public sealed class NoIntroNameTable
	{
		public const int FormatVersion = 1;

		//The LogicalName both UI.csproj and UI.Tests.csproj embed the table
		//under, and the only name this reader knows.
		public const string ResourceName = "Mesen.no_intro_sha1.tsv.gz";

		private const string Magic = "#mesen-no-intro-sha1-table";
		private const int Sha1HexLength = 40;

		//A sha1 two consoles' DATs both list is kept once; the generator already
		//decided, this is only what the reader does with a hand-edited table.
		private readonly Dictionary<string, NoIntroRomName> _bySha1;

		public int Count => _bySha1.Count;

		private NoIntroNameTable(Dictionary<string, NoIntroRomName> bySha1)
		{
			_bySha1 = bySha1;
		}

		//The table THIS assembly ships, read once on first use and kept (#1039).
		//Lazy on purpose: the artifact holds 17867 rows and most sessions never open
		//the library, so nothing pays for reading it until a tile asks for a name.
		//The null is a real answer and is kept like any other - an assembly that
		//carries no table must not go looking for one again on every tile.
		//
		//This is the seam the box-art chain is wired to: a caller that answers names
		//from here is answering from the shipped artifact, and a caller that did not
		//have it would make the whole chain a no-op (no name, no request).
		private static readonly Lazy<NoIntroNameTable?> _embedded = new(static () => LoadEmbedded());

		public static NoIntroNameTable? Embedded => _embedded.Value;

		//The whole of the app's own seam (#1039): the record the SHIPPED table files
		//this SHA-1 under - the console and the database's own name - or null for a
		//dump it does not know. Named here rather than left as a lambda at the call
		//site because it is the one line the box-art chain turns on: a caller that
		//answers names from anywhere else is not answering from the artifact the app
		//ships, and a stub here makes every tile of the library art-less in silence.
		public static NoIntroRomName? ForSha1(string sha1) =>
			Embedded is { } table && table.TryLookup(sha1, out NoIntroRomName rom) ? rom : null;

		//null when the assembly carries no table, so a caller (or a dual-compiled
		//test run) gets a library of file names rather than a crash.
		public static NoIntroNameTable? LoadEmbedded(Assembly? assembly = null)
		{
			Stream? table = (assembly ?? typeof(NoIntroNameTable).Assembly).GetManifestResourceStream(ResourceName);
			if(table == null) {
				return null;
			}
			using(table) {
				return Load(table);
			}
		}

		//Reads the gzipped TSV. Throws InvalidDataException, naming the line, on
		//anything else - a wrong format version or a row the reader cannot file
		//is a broken artifact, and a library that silently answered nothing for
		//every ROM is the failure this refuses.
		public static NoIntroNameTable Load(Stream table)
		{
			Dictionary<string, NoIntroRomName> bySha1 = new Dictionary<string, NoIntroRomName>(StringComparer.Ordinal);
			using StreamReader reader = new StreamReader(new GZipStream(table, CompressionMode.Decompress), Encoding.UTF8);

			bool headerSeen = false;
			int lineNumber = 0;
			string? line;
			while((line = reader.ReadLine()) != null) {
				lineNumber++;
				if(line.Length == 0) {
					continue;
				}
				if(line[0] == '#') {
					if(!headerSeen) {
						headerSeen = true;
						ReadHeader(line, lineNumber);
					}
					continue;
				}
				if(!headerSeen) {
					throw new InvalidDataException("line " + lineNumber + ": this is not a No-Intro name table (no " + Magic + " header)");
				}
				ReadRow(bySha1, line, lineNumber);
			}
			return new NoIntroNameTable(bySha1);
		}

		private static void ReadHeader(string line, int lineNumber)
		{
			if(!line.StartsWith(Magic + "\t", StringComparison.Ordinal)) {
				throw new InvalidDataException("line " + lineNumber + ": this is not a No-Intro name table (no " + Magic + " header)");
			}
			if(!int.TryParse(line.Substring(Magic.Length + 1), out int version) || version != FormatVersion) {
				throw new InvalidDataException("line " + lineNumber + ": table format " + line.Substring(Magic.Length + 1) + ", this reader speaks " + FormatVersion);
			}
		}

		private static void ReadRow(Dictionary<string, NoIntroRomName> bySha1, string line, int lineNumber)
		{
			string[] fields = line.Split('\t');
			if(fields.Length != 3) {
				throw new InvalidDataException("line " + lineNumber + ": expected 3 tab-separated fields, got " + fields.Length);
			}
			if(!IsSha1(fields[0])) {
				throw new InvalidDataException("line " + lineNumber + ": '" + fields[0] + "' is not 40 hex digits");
			}
			if(!TryParseConsoleCode(fields[1], out RomConsole console)) {
				throw new InvalidDataException("line " + lineNumber + ": '" + fields[1] + "' names no console this app runs");
			}
			if(fields[2].Length == 0) {
				throw new InvalidDataException("line " + lineNumber + ": empty game name");
			}
			bySha1.TryAdd(fields[0].ToLowerInvariant(), new NoIntroRomName(console, fields[2]));
		}

		//The console code the table stores, mapped onto the console the app
		//talks about. `sg1000` is not folded into `sms`: they are one core and
		//two libraries (RomConsole's own rule).
		private static bool TryParseConsoleCode(string code, out RomConsole console)
		{
			switch(code.ToLowerInvariant()) {
				case "nes": console = RomConsole.Nes; return true;
				case "gb": console = RomConsole.GameBoy; return true;
				case "gbc": console = RomConsole.GameBoyColor; return true;
				case "gba": console = RomConsole.GameBoyAdvance; return true;
				case "sms": console = RomConsole.MasterSystem; return true;
				case "sg1000": console = RomConsole.Sg1000; return true;
				case "gg": console = RomConsole.GameGear; return true;
				default: console = RomConsole.Unknown; return false;
			}
		}

		private static bool IsSha1(string value)
		{
			if(value.Length != Sha1HexLength) {
				return false;
			}
			foreach(char c in value) {
				bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
				if(!hex) {
					return false;
				}
			}
			return true;
		}

		public bool TryLookup(string? sha1, out NoIntroRomName rom)
		{
			rom = default;
			if(sha1 == null) {
				return false;
			}
			string key = sha1.Trim();
			if(key.Length != Sha1HexLength) {
				return false;
			}
			return _bySha1.TryGetValue(key.ToLowerInvariant(), out rom);
		}

		//The title a tile shows for a matched ROM: the No-Intro name without its
		//tag groups, so "Pokemon - Crystal Version (USA, Europe) (Rev 1)" reads
		//as "Pokemon - Crystal Version". Only parentheses are dropped - No-Intro
		//does its tagging there, while brackets belong to the dump-file-name
		//cleaner that title-cleans a miss. A name that is nothing but tags keeps
		//its own spelling: an empty title is not a title.
		public static string CanonicalTitle(string noIntroName)
		{
			if(string.IsNullOrEmpty(noIntroName)) {
				return noIntroName ?? string.Empty;
			}

			StringBuilder kept = new StringBuilder(noIntroName.Length);
			for(int i = 0; i < noIntroName.Length; i++) {
				char c = noIntroName[i];
				if(c == '(') {
					int close = noIntroName.IndexOf(')', i + 1);
					if(close > i) {
						i = close;
						continue;
					}
				}
				kept.Append(c);
			}

			string title = CollapseSpaces(kept.ToString());
			return title.Length == 0 ? noIntroName.Trim() : title;
		}

		private static string CollapseSpaces(string text)
		{
			StringBuilder output = new StringBuilder(text.Length);
			bool pendingSpace = false;
			foreach(char c in text) {
				if(char.IsWhiteSpace(c)) {
					pendingSpace = output.Length > 0;
					continue;
				}
				if(pendingSpace) {
					output.Append(' ');
					pendingSpace = false;
				}
				output.Append(c);
			}
			return output.ToString();
		}
	}
}
