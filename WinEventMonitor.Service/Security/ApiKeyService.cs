using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace WinEventMonitor.Service.Security;

public class ApiKeyService
{
    private readonly string _keyFilePath;
    private string? _cachedKey;

    public ApiKeyService(IConfiguration config)
    {
        _keyFilePath = config["EventMonitor:ApiKeyPath"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
               "WinEventMonitor", "api.key");
    }

    public string GetOrCreateKey()
    {
        if (_cachedKey is not null) return _cachedKey;

        var dir = Path.GetDirectoryName(_keyFilePath)!;
        Directory.CreateDirectory(dir);

        if (!File.Exists(_keyFilePath))
        {
            var newKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            File.WriteAllText(_keyFilePath, newKey);
            RestrictFilePermissions(_keyFilePath);
        }

        _cachedKey = File.ReadAllText(_keyFilePath).Trim();
        return _cachedKey;
    }

    public bool Validate(string? provided)
    {
        if (string.IsNullOrEmpty(provided)) return false;
        var expected = GetOrCreateKey();
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided.PadRight(expected.Length).Substring(0, expected.Length));
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes)
               && provided.Length == expected.Length;
    }

    private static void RestrictFilePermissions(string path)
    {
        try
        {
            var fi  = new FileInfo(path);
            var acl = fi.GetAccessControl();

            // Eliminar herencia pero añadir primero reglas explícitas para que
            // el proceso (admin) y SYSTEM sigan teniendo acceso completo.
            var currentUser = WindowsIdentity.GetCurrent().User!;
            var system      = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);

            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            acl.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, AccessControlType.Allow));

            fi.SetAccessControl(acl);
        }
        catch
        {
            // En entornos sin soporte de ACL (p.ej. FAT32) se ignora
        }
    }
}
