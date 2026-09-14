using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Mesen.Logic
{
	//Host-free half of the MEGA fetch kind (ADR-0187 §3): parsing the file
	//handle and decryption key out of a `https://mega.nz/file/<h>#<k>` URL,
	//and the AES-CTR transform the encrypted body needs. Mirrors
	//scripts/fetch_pack.py::parse_mega_url. BCL only; dual-compiled into
	//UI.Tests.
	//
	//The key lives in the URL *fragment*, which is never transmitted - not by
	//a browser and not by us. Scrub() exists because the rest of the download
	//path logs URLs, and a logged MEGA fragment is a leaked decryption key.
	public static class CommunityPackMega
	{
		public sealed record FileRef(string Handle, byte[] AesKey, byte[] Nonce);

		private static readonly Regex HandleInPath = new(
			"/file/([a-zA-Z0-9_-]+)", RegexOptions.CultureInvariant);
		private static readonly Regex KeyInText = new(
			"(mega\\.nz/file/[a-zA-Z0-9_-]+)#\\S*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

		//Null when the URL is not a usable MEGA file link; callers treat that
		//as a failed download rather than throwing.
		public static FileRef? Parse(string url)
		{
			if(string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)) {
				return null;
			}
			Match m = HandleInPath.Match(parsed.AbsolutePath);
			if(!m.Success) {
				return null;
			}
			string fragment = parsed.Fragment.StartsWith('#') ? parsed.Fragment.Substring(1) : parsed.Fragment;
			//A folder-link fragment is "<folderKey>!<handle>!<fileKey>"; only
			//the leading file key is usable here.
			int bang = fragment.IndexOf('!');
			if(bang >= 0) {
				fragment = fragment.Substring(0, bang);
			}
			byte[]? key = DecodeBase64Url(fragment);
			if(key == null || key.Length != 32) {
				return null;
			}
			//Eight big-endian words: the AES key is the first four XORed with
			//the last four, and the CTR counter starts at words 4-5 followed
			//by eight zero bytes.
			uint[] w = new uint[8];
			for(int i = 0; i < 8; i++) {
				w[i] = BinaryPrimitives.ReadUInt32BigEndian(key.AsSpan(i * 4, 4));
			}
			byte[] aesKey = new byte[16];
			for(int i = 0; i < 4; i++) {
				BinaryPrimitives.WriteUInt32BigEndian(aesKey.AsSpan(i * 4, 4), w[i] ^ w[i + 4]);
			}
			byte[] nonce = new byte[16];
			BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(0, 4), w[4]);
			BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(4, 4), w[5]);
			return new FileRef(m.Groups[1].Value, aesKey, nonce);
		}

		public static string RequestBody(string handle)
		{
			//ssl=1 is mandatory: without it MEGA answers with an http:// URL,
			//which the allow-list rejects (https only).
			return "[{\"a\":\"g\",\"g\":1,\"ssl\":1,\"p\":\"" + handle + "\"}]";
		}

		//The API answers `[{"s":<size>,...,"g":"https://..."}]`. Matched with a
		//regex rather than parsed: UI.csproj compiles with reflection-based
		//JSON disabled (AOT), and a source-generated context for one string
		//field out of a response we otherwise ignore is not worth it. Mirrors
		//scripts/fetch_pack.py::request_mega_download_url's `info["g"]`.
		public static string? ExtractDownloadUrl(string json)
		{
			if(string.IsNullOrEmpty(json)) {
				return null;
			}
			Match m = ApiDownloadUrl.Match(json);
			return m.Success ? m.Groups[1].Value : null;
		}

		private static readonly Regex ApiDownloadUrl = new(
			"\"g\"\\s*:\\s*\"(https://[^\"]+)\"", RegexOptions.CultureInvariant);

		//Strips the key out of any text that may contain a MEGA link, so a
		//log line or an error message never carries one.
		public static string Scrub(string text)
		{
			return string.IsNullOrEmpty(text) ? text : KeyInText.Replace(text, "$1#<redacted>");
		}

		//.NET has no CTR mode, so the standard construction: encrypt the
		//counter block with AES-ECB and XOR it into the stream, incrementing
		//the counter big-endian per 16-byte block. In place, over `data`.
		public static void DecryptCtr(byte[] aesKey, byte[] nonce, byte[] data)
		{
			using Aes aes = Aes.Create();
			aes.Key = aesKey;
			aes.Mode = CipherMode.ECB;
			aes.Padding = PaddingMode.None;
			using ICryptoTransform encryptor = aes.CreateEncryptor();
			byte[] counter = (byte[])nonce.Clone();
			byte[] keystream = new byte[16];
			for(int offset = 0; offset < data.Length; offset += 16) {
				encryptor.TransformBlock(counter, 0, 16, keystream, 0);
				int n = Math.Min(16, data.Length - offset);
				for(int i = 0; i < n; i++) {
					data[offset + i] ^= keystream[i];
				}
				IncrementCounter(counter);
			}
		}

		private static void IncrementCounter(byte[] counter)
		{
			for(int i = counter.Length - 1; i >= 0; i--) {
				if(++counter[i] != 0) {
					break;
				}
			}
		}

		private static byte[]? DecodeBase64Url(string value)
		{
			if(string.IsNullOrEmpty(value)) {
				return null;
			}
			string padded = value.Replace('-', '+').Replace('_', '/');
			padded += new string('=', (4 - padded.Length % 4) % 4);
			try {
				return Convert.FromBase64String(padded);
			} catch(FormatException) {
				return null;
			}
		}
	}
}
