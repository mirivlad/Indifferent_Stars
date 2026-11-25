using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

internal sealed class PlayerProfileKey : IDisposable
{
    private readonly ECDsa _key;
    public string PublicKey { get; }

    private PlayerProfileKey(ECDsa key)
    {
        _key = key;
        PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static PlayerProfileKey Open(string host, int port)
    {
        var directory = Godot.ProjectSettings.GlobalizePath("user://profiles");
        Directory.CreateDirectory(directory);
        var endpoint = FormattableString.Invariant($"{host.Trim().ToLowerInvariant()}:{port}");
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
        var path = Path.Combine(directory, name + ".key");
        if (!File.Exists(path)) Create(path);
        var key = ECDsa.Create();
        try
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                key.ImportPkcs8PrivateKey(bytes, out var read);
                if (read != bytes.Length || key.KeySize != 256)
                    throw new CryptographicException("Invalid profile key.");
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            return new PlayerProfileKey(key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public string Prove(string challenge) =>
        challenge + "." + Convert.ToBase64String(_key.SignData(Encoding.ASCII.GetBytes(challenge), HashAlgorithmName.SHA256));

    private static void Create(string path)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = key.ExportPkcs8PrivateKey();
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var output = new FileStream(temp, options))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            try { File.Move(temp, path); }
            catch (IOException) when (File.Exists(path)) { }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            File.Delete(temp);
        }
    }

    public void Dispose() => _key.Dispose();
}
