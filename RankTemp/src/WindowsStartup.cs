using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using System.Xml.Linq;

namespace RankTemp
{
    internal static class WindowsStartup
    {
        private const int TaskMissing = unchecked((int)0x80070002);
        private const int CreateOrUpdate = 6;
        private const int InteractiveToken = 3;
        private static readonly string InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RankTemp");

        private static readonly string[] AppFiles =
        {
            "RankTemp.exe", "LibreHardwareMonitorLib.dll", "BlackSharp.Core.dll",
            "DiskInfoToolkit.dll", "HidSharp.dll", "RAMSPDToolkit-NDD.dll",
            "System.Buffers.dll", "System.Memory.dll", "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll"
        };

        public static string UserSid
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return identity.User.Value;
            }
        }

        public static bool IsEnabled()
        {
            dynamic folder = OpenTaskFolder();
            try { return (bool)folder.GetTask(TaskName(UserSid)).Enabled; }
            catch (Exception ex)
            {
                if (ex.HResult != TaskMissing) throw;
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (!Program.IsElevated()) throw new UnauthorizedAccessException("Abre RankTemp como administrador.");
            string sid = UserSid;
            dynamic folder = OpenTaskFolder();
            if (enabled)
            {
                string executable = CopyToProgramFiles(sid);
                folder.RegisterTask(TaskName(sid), TaskXml(sid, executable), CreateOrUpdate,
                    sid, null, InteractiveToken, null);
            }
            else
            {
                try { folder.DeleteTask(TaskName(sid), 0); }
                catch (Exception ex) { if (ex.HResult != TaskMissing) throw; }
            }
        }

        private static string TaskName(string sid) { return "RankTemp-" + sid; }

        private static dynamic OpenTaskFolder()
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            scheduler.Connect();
            return scheduler.GetFolder(@"\");
        }

        internal static string TaskXml(string sid, string executable)
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            return new XElement(ns + "Task", new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Inicia RankTemp en la bandeja al iniciar sesión.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger", new XElement(ns + "UserId", sid))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal", new XAttribute("id", "User"),
                        new XElement(ns + "UserId", sid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "HighestAvailable"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", false),
                    new XElement(ns + "StopIfGoingOnBatteries", false),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions", new XAttribute("Context", "User"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", executable),
                        new XElement(ns + "Arguments", Program.TrayArgument),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(executable)))))
                .ToString();
        }

        private static string CopyToProgramFiles(string sid)
        {
            string source = Path.GetDirectoryName(Application.ExecutablePath);
            string installFolder = Path.Combine(InstallRoot, sid);
            string executable = Path.Combine(installFolder, "RankTemp.exe");
            var security = new DirectorySecurity();
            RestrictAccess(security, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);

            var root = Directory.CreateDirectory(InstallRoot, security);
            if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La carpeta de inicio de RankTemp no puede ser un enlace.");
            root.SetAccessControl(security);

            var folder = Directory.CreateDirectory(installFolder, security);
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La carpeta de inicio de RankTemp no puede ser un enlace.");
            folder.SetAccessControl(security);

            if (!string.Equals(source, installFolder, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string name in AppFiles)
                    if (!File.Exists(Path.Combine(source, name)))
                        throw new FileNotFoundException("Extrae el ZIP completo de RankTemp.", name);

                var fileSecurity = new FileSecurity();
                RestrictAccess(fileSecurity, InheritanceFlags.None);
                foreach (string name in AppFiles)
                {
                    string target = Path.Combine(installFolder, name);
                    File.Delete(target);
                    using (var input = File.OpenRead(Path.Combine(source, name)))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileSystemRights.Write,
                        FileShare.None, 4096, FileOptions.None, fileSecurity))
                        input.CopyTo(output);
                }
            }
            return executable;
        }

        private static void RestrictAccess(FileSystemSecurity security, InheritanceFlags inheritance)
        {
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            security.SetOwner(administrators);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        }
    }
}
