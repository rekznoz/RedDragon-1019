using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace RankTemp
{
    internal sealed class SensorReading
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public bool IsTemperature { get; private set; }
        public float? Value { get; private set; }

        public SensorReading(string id, string name, bool isTemperature, float? value)
        {
            Id = id;
            Name = name;
            IsTemperature = isTemperature;
            Value = value;
        }

        public override string ToString() { return Name; }
    }

    internal sealed class SensorMonitor : IDisposable
    {
        private Computer computer;
        private const string SetupHash = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
        private static readonly string SetupPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "controlador", "PawnIO_setup.exe");

        public static bool DriverReady
        {
            get { return PawnIo.IsInstalled && PawnIo.Version.Major >= 2; }
        }

        public static bool HasBundledDriver { get { return File.Exists(SetupPath); } }

        public static int InstallDriver()
        {
            using (var installer = File.OpenRead(SetupPath))
            using (var sha256 = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha256.ComputeHash(installer)).Replace("-", "");
                if (hash != SetupHash) throw new IOException("El controlador incluido no coincide con el original.");
                using (var setup = Process.Start(new ProcessStartInfo(SetupPath, "-install -silent")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    if (setup == null) throw new IOException("No se pudo iniciar la instalación del controlador.");
                    setup.WaitForExit();
                    return setup.ExitCode;
                }
            }
        }

        public SensorReading[] Read()
        {
            try
            {
                if (computer == null)
                {
                    computer = new Computer { IsCpuEnabled = true, IsMotherboardEnabled = true };
                    computer.Open();
                }

                var readings = new List<SensorReading>();
                foreach (IHardware hardware in computer.Hardware) ReadHardware(hardware, readings);
                return readings.OrderBy(sensor => sensor.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(sensor => sensor.Id, StringComparer.Ordinal).ToArray();
            }
            catch
            {
                try { Dispose(); }
                catch (Exception) { /* Keep the original sensor error. */ }
                throw;
            }
        }

        private static void ReadHardware(IHardware hardware, List<SensorReading> readings)
        {
            hardware.Update();
            foreach (ISensor sensor in hardware.Sensors)
            {
                bool isCpuTemperature = hardware.HardwareType == HardwareType.Cpu &&
                    sensor.SensorType == SensorType.Temperature;
                if (!isCpuTemperature && sensor.SensorType != SensorType.Fan) continue;
                readings.Add(new SensorReading(sensor.Identifier.ToString(), sensor.Name + " · " + hardware.Name,
                    isCpuTemperature, Normalize(sensor.Value, isCpuTemperature)));
            }

            foreach (IHardware child in hardware.SubHardware) ReadHardware(child, readings);
        }

        internal static float? Normalize(float? value, bool isTemperature)
        {
            if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value) ||
                value.Value < 0 || (isTemperature && value.Value == 0))
                return null;
            return value;
        }

        public void Dispose()
        {
            var instance = computer;
            computer = null;
            if (instance != null) instance.Close();
        }
    }
}
