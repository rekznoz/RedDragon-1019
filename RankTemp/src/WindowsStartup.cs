using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;
using System.Xml.Linq;

namespace RankTemp
{
    internal sealed class StartupState
    {
        public bool Enabled { get; private set; }
        public bool CanUpdate { get; private set; }

        public StartupState(bool enabled, Version installedVersion, Version runningVersion)
        {
            Enabled = enabled;
            CanUpdate = enabled && (installedVersion == null || installedVersion < runningVersion);
        }
    }

    internal static class WindowsStartup
    {
        private const int TaskMissing = unchecked((int)0x80070002);
        private const int CreateOrUpdate = 6;
        private const int InteractiveToken = 3;
        private const string SlotA = "Inicio-A";
        private const string SlotB = "Inicio-B";
        private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        private static readonly Version RunningVersion = typeof(WindowsStartup).Assembly.GetName().Version;
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

        public static StartupState ReadState()
        {
            string sid = UserSid;
            object task = ReadTask(OpenTaskFolder(), sid);
            bool enabled = task != null && (bool)((dynamic)task).Enabled;
            return new StartupState(enabled, enabled ? InstalledVersion(GetExecutable(task, sid)) : null, RunningVersion);
        }

        public static void SetEnabled(bool enabled)
        {
            if (!Program.IsElevated()) throw new UnauthorizedAccessException("Abre RankTemp como administrador.");
            string sid = UserSid;
            object folder = OpenTaskFolder();
            if (enabled)
            {
                object task = ReadTask(folder, sid);
                RegisterStartup(folder, sid, GetExecutable(task, sid));
            }
            else
            {
                try { ((dynamic)folder).DeleteTask(TaskName(sid), 0); }
                catch (Exception ex) { if (ex.HResult != TaskMissing) throw; }
            }
        }

        public static void Update()
        {
            if (!Program.IsElevated()) throw new UnauthorizedAccessException("Abre RankTemp como administrador.");
            string sid = UserSid;
            object folder = OpenTaskFolder();
            object task = ReadTask(folder, sid);
            if (task == null || !(bool)((dynamic)task).Enabled)
                throw new InvalidOperationException("El inicio con Windows está desactivado.");
            string executable = GetExecutable(task, sid);
            Version installedVersion = InstalledVersion(executable);
            if (installedVersion != null && installedVersion >= RunningVersion) return;

            RegisterStartup(folder, sid, executable);
        }

        private static void RegisterStartup(object folder, string sid, string currentExecutable)
        {
            string executable = CopyToProgramFiles(sid, currentExecutable);
            ((dynamic)folder).RegisterTask(TaskName(sid), TaskXml(sid, executable), CreateOrUpdate,
                sid, null, InteractiveToken, null);
        }

        private static object ReadTask(object folder, string sid)
        {
            try { return ((dynamic)folder).GetTask(TaskName(sid)); }
            catch (Exception ex)
            {
                if (ex.HResult != TaskMissing) throw;
                return null;
            }
        }

        private static string GetExecutable(object task, string sid)
        {
            if (task == null) return null;
            XDocument definition = XDocument.Parse((string)((dynamic)task).Xml);
            string executable = (string)definition.Root.Elements(TaskNamespace + "Actions")
                .Elements(TaskNamespace + "Exec").Elements(TaskNamespace + "Command").SingleOrDefault();
            if (string.IsNullOrEmpty(executable) || !IsManagedFolder(Path.GetDirectoryName(executable), sid) ||
                !string.Equals(Path.GetFileName(executable), "RankTemp.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La tarea de RankTemp apunta a una ruta de inicio desconocida.");
            return executable;
        }

        private static Version InstalledVersion(string executable)
        {
            if (!File.Exists(executable)) return null;
            var version = FileVersionInfo.GetVersionInfo(executable);
            return new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart);
        }

        private static bool IsManagedFolder(string folder, string sid)
        {
            string userFolder = Path.Combine(InstallRoot, sid);
            return SamePath(folder, userFolder) || SamePath(folder, Path.Combine(userFolder, SlotA)) ||
                SamePath(folder, Path.Combine(userFolder, SlotB));
        }

        private static bool SamePath(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string TaskName(string sid) { return "RankTemp-" + sid; }

        private static object OpenTaskFolder()
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            scheduler.Connect();
            return scheduler.GetFolder(@"\");
        }

        internal static string TaskXml(string sid, string executable)
        {
            XNamespace ns = TaskNamespace;
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

        internal static string NextInstallFolder(string userFolder, string currentExecutable)
        {
            string first = Path.Combine(userFolder, SlotA);
            return currentExecutable != null && SamePath(Path.GetDirectoryName(currentExecutable), first)
                ? Path.Combine(userFolder, SlotB) : first;
        }

        private static string CopyToProgramFiles(string sid, string currentExecutable)
        {
            string source = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (string name in AppFiles)
                if (!File.Exists(Path.Combine(source, name)))
                    throw new FileNotFoundException("Extrae el ZIP completo de RankTemp.", name);

            string userFolder = Path.Combine(InstallRoot, sid);
            string installFolder = IsManagedFolder(source, sid) ? source : NextInstallFolder(userFolder, currentExecutable);
            var security = new DirectorySecurity();
            RestrictAccess(security, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            EnsureProtectedDirectory(InstallRoot, security);
            EnsureProtectedDirectory(userFolder, security);

            if (!SamePath(userFolder, installFolder)) EnsureProtectedDirectory(installFolder, security);

            if (!SamePath(source, installFolder))
            {
                var fileSecurity = new FileSecurity();
                RestrictAccess(fileSecurity, InheritanceFlags.None);
                foreach (string name in AppFiles)
                {
                    string target = Path.Combine(installFolder, name);
                    File.Delete(target);
                    using (var input = File.OpenRead(Path.Combine(source, name)))
                    using (var output = new FileStream(target, FileMode.CreateNew, FileSystemRights.Write,
                        FileShare.None, 4096, FileOptions.None, fileSecurity))
                    {
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                }
            }
            return Path.Combine(installFolder, "RankTemp.exe");
        }

        private static void EnsureProtectedDirectory(string path, DirectorySecurity security)
        {
            var folder = Directory.CreateDirectory(path, security);
            if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La carpeta de inicio de RankTemp no puede ser un enlace.");
            folder.SetAccessControl(security);
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
