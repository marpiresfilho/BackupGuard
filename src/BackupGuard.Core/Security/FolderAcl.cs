using System.Security.AccessControl;
using System.Security.Principal;

namespace BackupGuard.Core.Security;

public static class FolderAcl
{
    /// <summary>
    /// Remove herança e deixa a pasta acessível só para SYSTEM e Administradores.
    /// Um usuário comum (ou um malware sem privilégio de admin) não lê nem altera nada.
    /// </summary>
    public static void RestrictToAdmins(string directory)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                inherit, PropagationFlags.None, AccessControlType.Allow));
        }
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new DirectoryInfo(directory).SetAccessControl(security);
    }
}
