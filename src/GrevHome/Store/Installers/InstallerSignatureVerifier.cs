using System.Diagnostics;
using System.Text;

namespace GrevHome.Store.Installers;

/// <summary>Fail-closed Windows Authenticode and exact publisher check before execution.</summary>
public static class InstallerSignatureVerifier
{
    public static async Task VerifyAsync(string path, string publisher, CancellationToken cancellationToken)
    {
        var script = """
            $ErrorActionPreference = 'Stop'
            $signature = Get-AuthenticodeSignature -LiteralPath $env:GREV_INSTALLER_VERIFY_PATH
            if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
                throw 'Installer does not have a valid trusted Authenticode signature.'
            }
            $name = $signature.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
            if ($name -cne $env:GREV_INSTALLER_VERIFY_PUBLISHER) {
                throw 'Installer publisher does not match the expected publisher.'
            }
            """;
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        start.Environment["GREV_INSTALLER_VERIFY_PATH"] = Path.GetFullPath(path);
        start.Environment["GREV_INSTALLER_VERIFY_PUBLISHER"] = publisher;
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("Could not start installer signature verification.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        await output;
        var detail = await error;
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Installer verification failed. Nothing was installed. {detail}");
    }
}
