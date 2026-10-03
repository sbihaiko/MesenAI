using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mesen.Logic;

//F14.20 (ADR-0247 Decision 3): the macOS store - a generic-password item in the
//user's login Keychain, service "MesenAI BYOK <vendor>", through the Security
//framework's SecItem API called directly. Not the `security` CLI: that would put
//the key on a command line, which ADR-0242 Decision 4 forbids.
//
//Every call builds its CoreFoundation query, runs it and releases it; the key's
//bytes are cleared from the managed buffer after use. No field holds a key.
public sealed class MacKeychainByokKeyStore : IByokKeyStore
{
	private const string SecurityLib = "/System/Library/Frameworks/Security.framework/Security";
	private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
	private const uint kCFStringEncodingUTF8 = 0x08000100;
	private const int errSecSuccess = 0;
	private const int errSecItemNotFound = -25300;
	private const int errSecDuplicateItem = -25299;
	private const string Account = "MesenAI";

	public string? UnsupportedReason => null;

	public string? Read(ByokVendor vendor)
	{
		using CfScope cf = new();
		IntPtr query = cf.Dictionary(
			(Sec.Class, Sec.ClassGenericPassword),
			(Sec.AttrService, cf.String(vendor.ServiceName)),
			(Sec.AttrAccount, cf.String(Account)),
			(Sec.ReturnData, Sec.BooleanTrue),
			(Sec.MatchLimit, Sec.MatchLimitOne));
		int status = SecItemCopyMatching(query, out IntPtr data);
		if(status == errSecItemNotFound) {
			return null;
		} else if(status != errSecSuccess) {
			throw new ByokKeyStoreException("read", vendor, status);
		}
		try {
			long length = CFDataGetLength(data);
			byte[] bytes = new byte[length];
			CFDataGetBytes(data, new CFRange { Location = 0, Length = length }, bytes);
			try {
				return Encoding.UTF8.GetString(bytes);
			} finally {
				Array.Clear(bytes);
			}
		} finally {
			CFRelease(data);
		}
	}

	public void Write(ByokVendor vendor, string key)
	{
		key = ByokKey.Normalize(key);
		byte[] bytes = Encoding.UTF8.GetBytes(key);
		try {
			using CfScope cf = new();
			IntPtr value = cf.Data(bytes);
			IntPtr service = cf.String(vendor.ServiceName);
			IntPtr account = cf.String(Account);
			IntPtr add = cf.Dictionary(
				(Sec.Class, Sec.ClassGenericPassword),
				(Sec.AttrService, service),
				(Sec.AttrAccount, account),
				(Sec.AttrLabel, cf.String(vendor.DisplayName + " key (MesenAI)")),
				(Sec.ValueData, value));
			int status = SecItemAdd(add, IntPtr.Zero);
			if(status == errSecDuplicateItem) {
				IntPtr query = cf.Dictionary(
					(Sec.Class, Sec.ClassGenericPassword),
					(Sec.AttrService, service),
					(Sec.AttrAccount, account));
				IntPtr update = cf.Dictionary((Sec.ValueData, value));
				status = SecItemUpdate(query, update);
			}
			if(status != errSecSuccess) {
				throw new ByokKeyStoreException("store", vendor, status);
			}
		} finally {
			Array.Clear(bytes);
		}
	}

	public bool Remove(ByokVendor vendor)
	{
		using CfScope cf = new();
		IntPtr query = cf.Dictionary(
			(Sec.Class, Sec.ClassGenericPassword),
			(Sec.AttrService, cf.String(vendor.ServiceName)),
			(Sec.AttrAccount, cf.String(Account)));
		int status = SecItemDelete(query);
		if(status == errSecItemNotFound) {
			return false;
		} else if(status != errSecSuccess) {
			throw new ByokKeyStoreException("remove", vendor, status);
		}
		return true;
	}

	//The framework's constant CFStringRefs/CFBooleanRef, read once from their
	//exported symbols (they are data, not functions, so no DllImport reaches them).
	private static class Sec
	{
		public static readonly IntPtr Class = Constant(SecurityLib, "kSecClass");
		public static readonly IntPtr ClassGenericPassword = Constant(SecurityLib, "kSecClassGenericPassword");
		public static readonly IntPtr AttrService = Constant(SecurityLib, "kSecAttrService");
		public static readonly IntPtr AttrAccount = Constant(SecurityLib, "kSecAttrAccount");
		public static readonly IntPtr AttrLabel = Constant(SecurityLib, "kSecAttrLabel");
		public static readonly IntPtr ValueData = Constant(SecurityLib, "kSecValueData");
		public static readonly IntPtr ReturnData = Constant(SecurityLib, "kSecReturnData");
		public static readonly IntPtr MatchLimit = Constant(SecurityLib, "kSecMatchLimit");
		public static readonly IntPtr MatchLimitOne = Constant(SecurityLib, "kSecMatchLimitOne");
		public static readonly IntPtr BooleanTrue = Constant(CoreFoundationLib, "kCFBooleanTrue");
		//Addresses of the callback structs themselves, as CFDictionaryCreate takes them
		public static readonly IntPtr KeyCallBacks = Export(CoreFoundationLib, "kCFTypeDictionaryKeyCallBacks");
		public static readonly IntPtr ValueCallBacks = Export(CoreFoundationLib, "kCFTypeDictionaryValueCallBacks");

		private static IntPtr Export(string library, string symbol)
		{
			return NativeLibrary.GetExport(NativeLibrary.Load(library), symbol);
		}

		private static IntPtr Constant(string library, string symbol)
		{
			return Marshal.ReadIntPtr(Export(library, symbol));
		}
	}

	//Owns every CF object one call creates and releases them together.
	private sealed class CfScope : IDisposable
	{
		private readonly System.Collections.Generic.List<IntPtr> _owned = new();

		private IntPtr Own(IntPtr cf)
		{
			if(cf == IntPtr.Zero) {
				throw new InvalidOperationException("CoreFoundation could not allocate an object.");
			}
			_owned.Add(cf);
			return cf;
		}

		public IntPtr String(string text)
		{
			byte[] utf8 = Encoding.UTF8.GetBytes(text);
			return Own(CFStringCreateWithBytes(IntPtr.Zero, utf8, utf8.Length, kCFStringEncodingUTF8, false));
		}

		public IntPtr Data(byte[] bytes)
		{
			return Own(CFDataCreate(IntPtr.Zero, bytes, bytes.Length));
		}

		public IntPtr Dictionary(params (IntPtr key, IntPtr value)[] pairs)
		{
			IntPtr[] keys = new IntPtr[pairs.Length];
			IntPtr[] values = new IntPtr[pairs.Length];
			for(int i = 0; i < pairs.Length; i++) {
				keys[i] = pairs[i].key;
				values[i] = pairs[i].value;
			}
			return Own(CFDictionaryCreate(IntPtr.Zero, keys, values, pairs.Length, Sec.KeyCallBacks, Sec.ValueCallBacks));
		}

		public void Dispose()
		{
			foreach(IntPtr cf in _owned) {
				CFRelease(cf);
			}
			_owned.Clear();
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CFRange
	{
		public long Location;
		public long Length;
	}

	[DllImport(SecurityLib)]
	private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

	[DllImport(SecurityLib)]
	private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

	[DllImport(SecurityLib)]
	private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

	[DllImport(SecurityLib)]
	private static extern int SecItemDelete(IntPtr query);

	[DllImport(CoreFoundationLib)]
	private static extern IntPtr CFStringCreateWithBytes(IntPtr alloc, byte[] bytes, long numBytes, uint encoding, [MarshalAs(UnmanagedType.U1)] bool isExternalRepresentation);

	[DllImport(CoreFoundationLib)]
	private static extern IntPtr CFDataCreate(IntPtr alloc, byte[] bytes, long length);

	[DllImport(CoreFoundationLib)]
	private static extern long CFDataGetLength(IntPtr data);

	[DllImport(CoreFoundationLib)]
	private static extern void CFDataGetBytes(IntPtr data, CFRange range, byte[] buffer);

	[DllImport(CoreFoundationLib)]
	private static extern IntPtr CFDictionaryCreate(IntPtr alloc, IntPtr[] keys, IntPtr[] values, long numValues, IntPtr keyCallBacks, IntPtr valueCallBacks);

	[DllImport(CoreFoundationLib)]
	private static extern void CFRelease(IntPtr cf);
}
