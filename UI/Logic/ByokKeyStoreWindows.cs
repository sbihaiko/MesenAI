using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mesen.Logic;

//F14.20 (ADR-0247 Decision 3): the Windows store - a generic credential in the
//user's Credential Manager, target "MesenAI BYOK <vendor>", through advapi32's
//CredReadW / CredWriteW / CredDeleteW. The blob is the key's UTF-8 bytes, kept
//with CRED_PERSIST_LOCAL_MACHINE (this computer, not roamed). Every string
//crosses as an HGlobal pointer so the CREDENTIAL struct stays blittable (AOT).
//No field holds a key; the unmanaged copies are zeroed before they are freed.
public sealed class WindowsCredentialByokKeyStore : IByokKeyStore
{
	private const uint CRED_TYPE_GENERIC = 1;
	private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
	private const int ERROR_NOT_FOUND = 1168;
	private const string UserName = "MesenAI";

	public string? UnsupportedReason => null;

	public string? Read(ByokVendor vendor)
	{
		IntPtr target = Marshal.StringToHGlobalUni(vendor.ServiceName);
		try {
			if(!CredReadW(target, CRED_TYPE_GENERIC, 0, out IntPtr credential)) {
				int error = Marshal.GetLastPInvokeError();
				if(error == ERROR_NOT_FOUND) {
					return null;
				}
				throw new ByokKeyStoreException("read", vendor, error);
			}
			try {
				CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credential);
				byte[] bytes = new byte[cred.CredentialBlobSize];
				Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
				try {
					return Encoding.UTF8.GetString(bytes);
				} finally {
					Array.Clear(bytes);
				}
			} finally {
				CredFree(credential);
			}
		} finally {
			Marshal.FreeHGlobal(target);
		}
	}

	public void Write(ByokVendor vendor, string key)
	{
		ArgumentException.ThrowIfNullOrEmpty(key);
		byte[] bytes = Encoding.UTF8.GetBytes(key);
		IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
		IntPtr target = Marshal.StringToHGlobalUni(vendor.ServiceName);
		IntPtr user = Marshal.StringToHGlobalUni(UserName);
		IntPtr comment = Marshal.StringToHGlobalUni(vendor.DisplayName + " key (MesenAI)");
		try {
			Marshal.Copy(bytes, 0, blob, bytes.Length);
			CREDENTIAL cred = new() {
				Type = CRED_TYPE_GENERIC,
				TargetName = target,
				Comment = comment,
				CredentialBlobSize = (uint)bytes.Length,
				CredentialBlob = blob,
				Persist = CRED_PERSIST_LOCAL_MACHINE,
				UserName = user,
			};
			if(!CredWriteW(ref cred, 0)) {
				throw new ByokKeyStoreException("store", vendor, Marshal.GetLastPInvokeError());
			}
		} finally {
			Array.Clear(bytes);
			Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
			Marshal.FreeHGlobal(blob);
			Marshal.FreeHGlobal(target);
			Marshal.FreeHGlobal(user);
			Marshal.FreeHGlobal(comment);
		}
	}

	public bool Remove(ByokVendor vendor)
	{
		IntPtr target = Marshal.StringToHGlobalUni(vendor.ServiceName);
		try {
			if(CredDeleteW(target, CRED_TYPE_GENERIC, 0)) {
				return true;
			}
			int error = Marshal.GetLastPInvokeError();
			if(error == ERROR_NOT_FOUND) {
				return false;
			}
			throw new ByokKeyStoreException("remove", vendor, error);
		} finally {
			Marshal.FreeHGlobal(target);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct CREDENTIAL
	{
		public uint Flags;
		public uint Type;
		public IntPtr TargetName;
		public IntPtr Comment;
		public uint LastWrittenLow;
		public uint LastWrittenHigh;
		public uint CredentialBlobSize;
		public IntPtr CredentialBlob;
		public uint Persist;
		public uint AttributeCount;
		public IntPtr Attributes;
		public IntPtr TargetAlias;
		public IntPtr UserName;
	}

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CredReadW(IntPtr target, uint type, uint flags, out IntPtr credential);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CredDeleteW(IntPtr target, uint type, uint flags);

	[DllImport("advapi32.dll")]
	private static extern void CredFree(IntPtr buffer);
}
