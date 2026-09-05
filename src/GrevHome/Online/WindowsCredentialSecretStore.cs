using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GrevHome.Online;

/// <summary>
/// Stores Grev.dad device secrets in Windows Credential Manager instead of profile JSON.
/// Secrets are scoped to the current Windows user and never written to Grev Home's profile tree.
/// </summary>
internal sealed class WindowsCredentialSecretStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int MaximumCredentialBlobBytes = 5 * 512;

    public void Write(string grevId, string slot, string secret)
    {
        var targetName = BuildTargetName(grevId, slot);
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("A non-empty secret is required.", nameof(secret));
        }

        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length > MaximumCredentialBlobBytes)
        {
            throw new InvalidOperationException("The Grev.dad credential is too large for Windows Credential Manager.");
        }

        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "GrevHome"
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows Credential Manager did not save the Grev.dad device credential.");
            }
        }
        finally
        {
            if (bytes.Length > 0)
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
            Marshal.FreeCoTaskMem(blob);
        }
    }

    public string? Read(string grevId, string slot)
    {
        var targetName = BuildTargetName(grevId, slot);
        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) // ERROR_NOT_FOUND
            {
                return null;
            }

            throw new Win32Exception(error, "Windows Credential Manager could not read the Grev.dad device credential.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Delete(string grevId, string slot)
    {
        var targetName = BuildTargetName(grevId, slot);
        if (CredDelete(targetName, CredTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != 1168)
        {
            throw new Win32Exception(error, "Windows Credential Manager could not remove the Grev.dad device credential.");
        }
    }

    private static string BuildTargetName(string grevId, string slot)
    {
        if (string.IsNullOrWhiteSpace(grevId) || grevId.Length > 58 ||
            grevId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Invalid GrevID.", nameof(grevId));
        }

        if (string.IsNullOrWhiteSpace(slot) || slot.Length > 24 ||
            slot.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Invalid credential slot.", nameof(slot));
        }

        return $"GrevHome/grev.dad/{grevId}/{slot}";
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPointer);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
