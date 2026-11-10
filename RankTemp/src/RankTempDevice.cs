using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using Microsoft.Win32;

namespace RankTemp
{
    internal sealed class PortChoice
    {
        public string Name { get; private set; }
        public bool IsCh340 { get; private set; }

        public PortChoice(string name, bool isCh340)
        {
            Name = name;
            IsCh340 = isCh340;
        }

        public override string ToString()
        {
            return IsCh340 ? Name + " · USB-SERIAL CH340" : Name;
        }
    }

    internal sealed class RankTempDevice : IDisposable
    {
        private SerialPort port;

        public static PortChoice[] ListPorts()
        {
            var ch340Ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1A86&PID_7523"))
            {
                if (usb != null)
                {
                    foreach (string instance in usb.GetSubKeyNames())
                    {
                        using (var parameters = usb.OpenSubKey(instance + @"\Device Parameters"))
                        {
                            var name = parameters == null ? null : parameters.GetValue("PortName") as string;
                            if (!string.IsNullOrEmpty(name)) ch340Ports.Add(name);
                        }
                    }
                }
            }

            return SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new PortChoice(name, ch340Ports.Contains(name)))
                .OrderByDescending(choice => choice.IsCh340).ThenBy(choice => choice.Name)
                .ToArray();
        }

        public void Connect(string name)
        {
            Dispose();
            var candidate = new SerialPort(name, 115200, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 2000,
                WriteTimeout = 2000
            };

            try
            {
                candidate.Open();
                Exchange(candidate, new byte[] { 0x02, 0x01, 0x00 }, 0x31);
                port = candidate;
            }
            catch
            {
                candidate.Dispose();
                throw;
            }
        }

        public void Send(float? cpuTemperature, float? pumpRpm, float? cpuFanRpm)
        {
            try { Exchange(port, Encode(cpuTemperature, pumpRpm, cpuFanRpm), 0x21); }
            catch
            {
                try { Dispose(); }
                catch (Exception) { /* Keep the original serial error. */ }
                throw;
            }
        }

        internal static byte[] Encode(float? cpuTemperature, float? pumpRpm, float? cpuFanRpm)
        {
            var frame = new byte[] { 0x01, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            PutWord(frame, 2, cpuTemperature, 10);
            PutWord(frame, 4, pumpRpm, 1);
            PutWord(frame, 6, cpuFanRpm, 1);
            return frame;
        }

        private static void PutWord(byte[] frame, int offset, float? value, int scale)
        {
            if (!value.HasValue) return;
            double word = Math.Round((double)value.Value * scale, MidpointRounding.AwayFromZero);
            if (double.IsNaN(word) || word < 0 || word >= ushort.MaxValue) return;
            frame[offset] = (byte)((int)word >> 8);
            frame[offset + 1] = (byte)((int)word & 0xFF);
        }

        private static void Exchange(SerialPort port, byte[] message, int expected)
        {
            port.Write(message, 0, message.Length);
            int response = port.ReadByte();
            if (response != expected)
                throw new IOException(string.Format("Respuesta {0:X2}. Se esperaba {1:X2}.", response, expected));
        }

        public void Dispose()
        {
            var connection = port;
            port = null;
            if (connection != null) connection.Dispose();
        }
    }
}
