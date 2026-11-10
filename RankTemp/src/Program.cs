using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace RankTemp
{
    internal static class Program
    {
        internal const string InstallDriverArgument = "--activar-sensores";
        internal const string ReopenArgument = "--reabrir-sensores";
        internal const string TrayArgument = "--tray";

        internal static bool IsElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool elevated = IsElevated();
            string argument = args.Length == 1 ? args[0] : null;
            bool startInTray = argument == TrayArgument;
            bool installingDriver = argument == InstallDriverArgument && elevated;
            if (installingDriver)
                InstallSensorAccess();
            else if (!startInTray && !elevated && SensorMonitor.DriverReady && TryOpenElevated())
                return;

            using (var instance = new Mutex(false, @"Local\RankTemp-" + WindowsStartup.UserSid))
            {
                bool acquired;
                try { acquired = instance.WaitOne(argument == ReopenArgument || installingDriver ? 5000 : 0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    if (!startInTray)
                        MessageBox.Show("RankTemp ya está abierto. Puedes abrir su ventana desde la bandeja.",
                            "RankTemp", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try { Application.Run(new RankTempForm(!installingDriver && !startInTray, startInTray)); }
                finally { instance.ReleaseMutex(); }
            }
        }

        private static bool TryOpenElevated()
        {
            try
            {
                var start = new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using (var process = Process.Start(start))
                    return process != null;
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223)
                    MessageBox.Show(ex.Message, "RankTemp · Permisos de sensores", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static void InstallSensorAccess()
        {
            try
            {
                int result = SensorMonitor.InstallDriver();
                if (result == 3010)
                    MessageBox.Show("Reinicia Windows para completar la activación de los sensores.",
                        "RankTemp", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else if (result != 0)
                    MessageBox.Show("El instalador del controlador devolvió el código " + result + ".",
                        "RankTemp", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "RankTemp · Activar sensores", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
