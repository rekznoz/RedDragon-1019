using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Theme = RankTemp.DashboardTheme;

namespace RankTemp
{
    internal sealed class RankTempForm : Form
    {
        private const string SettingsPath = @"Software\RankTemp";

        private readonly RankTempDevice device = new RankTempDevice();
        private readonly SensorMonitor sensors = new SensorMonitor();
        private readonly Timer timer = new Timer { Interval = 1000 };
        private readonly Stopwatch reconnectDelay = new Stopwatch();
        private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ComboBox ports = new DashboardComboBox
        {
            AccessibleName = "Puerto del Redragon 1019", Placeholder = "Seleccionar puerto"
        };
        private readonly Button refresh = NewButton("Buscar puertos", false);
        private readonly Button connect = NewButton("Conectar", true);
        private readonly CheckBox startup = new DashboardSwitch("Iniciar RankTemp con Windows");
        private readonly Label connectionStatus = NewLabel("●  Sin conexión", 10, true, Theme.Muted);
        private readonly Label sensorStatus = NewLabel("Buscando sensores…", 10);
        private readonly LinkLabel sensorAccess = new LinkLabel { LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text,
            VisitedLinkColor = Theme.Accent, Dock = DockStyle.Fill };
        private readonly LinkLabel startupUpdate = new LinkLabel
        {
            Text = "Actualizar copia de inicio", LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text,
            VisitedLinkColor = Theme.Accent, Dock = DockStyle.Fill, Visible = false
        };
        private readonly SensorRow[] rows =
        {
            new SensorRow("Cpu", "Temperatura de CPU", "°C", Theme.Accent),
            new SensorRow("Fan", "Ventilador de CPU", "RPM", Color.FromArgb(158, 196, 255)),
            new SensorRow("Pump", "Bomba", "RPM", Color.FromArgb(207, 185, 255))
        };
        private readonly bool elevated;
        private readonly bool offerActivation;
        private bool startHidden;
        private bool started;
        private bool connected;
        private bool synchronizing;
        private bool busy;
        private bool connectionPending;
        private bool closeWhenIdle;
        private bool activationOffered;
        private bool restoringWindow;
        private bool startupSettingLoaded;
        private string rememberedPort;
        private SensorReading[] temperatures = Array.Empty<SensorReading>();
        private SensorReading[] fans = Array.Empty<SensorReading>();
        private string sensorError;

        public RankTempForm(bool offerActivation, bool startInTray)
        {
            this.offerActivation = offerActivation;
            startHidden = startInTray;
            Text = "RankTemp · Redragon 1019";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(856, 512);
            MinimumSize = Size;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 10);
            elevated = Program.IsElevated();

            trayMenu.Items.Add("Abrir RankTemp", null, (sender, args) => RestoreWindow());
            trayMenu.Items.Add("Salir", null, (sender, args) => Close());
            trayIcon.Icon = Icon;
            trayIcon.Text = "RankTemp · Redragon 1019";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.MouseDoubleClick += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left) RestoreWindow();
            };
            Resize += (sender, args) =>
            {
                if (!restoringWindow && WindowState == FormWindowState.Minimized) Hide();
            };

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 16, 28, 16), ColumnCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            Add(layout, NewLabel("RankTemp", 29, true), 53);
            Add(layout, NewLabel("Redragon 1019", 11, false, Theme.Muted), 29);

            var connection = new DashboardCard
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2,
                Padding = new Padding(15, 9, 15, 11), Margin = new Padding(3, 7, 3, 0)
            };
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            connection.Controls.Add(ports, 0, 1);
            connection.Controls.Add(refresh, 1, 1);
            connection.Controls.Add(connect, 2, 1);
            connection.Controls.Add(connectionStatus, 0, 0);
            connection.SetColumnSpan(connectionStatus, 3);
            Add(layout, connection, 94);

            Add(layout, NewLabel("Lecturas de la pantalla", 14, true), 50);
            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = rows.Length, RowCount = 1, Margin = Padding.Empty };
            for (int column = 0; column < rows.Length; column++)
            {
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / rows.Length));
                cards.Controls.Add(rows[column].Panel, column, 0);
            }
            Add(layout, cards, 188);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
                Margin = new Padding(3, 13, 3, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 267));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sensorStatus.ForeColor = Theme.Muted;
            footer.Controls.Add(sensorStatus, 0, 0);
            footer.Controls.Add(startup, 1, 0);
            sensorAccess.LinkClicked += (sender, args) => ActivateSensors();
            footer.Controls.Add(sensorAccess, 0, 1);
            startupUpdate.LinkClicked += (sender, args) => UpdateStartupCopy();
            footer.Controls.Add(startupUpdate, 1, 1);
            Add(layout, footer, 64);
            UpdateSensorAccess(null);
            startup.Enabled = false;
            startup.Click += (sender, args) => ChangeStartupSetting();

            LoadSelections();
            refresh.Click += (sender, args) => RefreshPorts();
            ports.SelectedIndexChanged += (sender, args) => UpdateButtons();
            connect.Click += async (sender, args) => await ChangeConnectionAsync();
            timer.Tick += async (sender, args) => await PollAsync();
            VisibleChanged += (sender, args) => LoadStartupSetting();
            Shown += async (sender, args) => await StartAsync();
            FormClosing += CloseWhenReady;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            if (SystemInformation.HighContrast) return;
            const int useDarkMode = 20;
            const int captionColor = 35;
            const int captionTextColor = 36;
            int dark = 1;
            int caption = ColorTranslator.ToWin32(Theme.Background);
            int text = ColorTranslator.ToWin32(Theme.Text);
            DwmSetWindowAttribute(Handle, useDarkMode, ref dark, sizeof(int));
            DwmSetWindowAttribute(Handle, captionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(Handle, captionTextColor, ref text, sizeof(int));
        }

        protected override void SetVisibleCore(bool value)
        {
            if (value && startHidden)
            {
                startHidden = false;
                if (!IsHandleCreated) CreateHandle();
                BeginInvoke(new Action(async () => await StartAsync()));
                return;
            }
            base.SetVisibleCore(value);
        }

        private async Task StartAsync()
        {
            if (started || closeWhenIdle) return;
            started = true;
            trayIcon.Visible = true;
            if (offerActivation && !activationOffered && SensorMonitor.HasBundledDriver && !SensorMonitor.DriverReady)
            {
                activationOffered = true;
                ActivateSensors();
                if (closeWhenIdle) return;
            }
            RefreshPorts();
            timer.Start();
            await PollAsync();
        }

        private void LoadStartupSetting()
        {
            if (!Visible || startupSettingLoaded) return;
            startupSettingLoaded = true;
            try { ShowStartupState(WindowsStartup.ReadState()); }
            catch (Exception) { startup.Enabled = false; }
        }

        private void ShowStartupState(StartupState state)
        {
            startup.Checked = state.Enabled;
            startup.Enabled = elevated;
            startupUpdate.Visible = elevated && state.CanUpdate;
        }

        private void ChangeStartupSetting()
        {
            bool enable = startup.Checked;
            if (enable && MessageBox.Show(this,
                "RankTemp guardará una copia en Archivos de programa y la abrirá en la bandeja con permisos de administrador al iniciar sesión. ¿Quieres activarlo?",
                "Iniciar RankTemp con Windows", MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                startup.Checked = false;
                return;
            }

            try
            {
                WindowsStartup.SetEnabled(enable);
                SaveSelections();
                startupUpdate.Visible = false;
            }
            catch (Exception ex)
            {
                startup.Checked = !enable;
                MessageBox.Show(this, ex.Message, "RankTemp · Inicio con Windows", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void UpdateStartupCopy()
        {
            try
            {
                UseWaitCursor = true;
                WindowsStartup.Update();
                SaveSelections();
                startupUpdate.Visible = false;
                MessageBox.Show(this, "La nueva versión se abrirá la próxima vez que inicies sesión. Se han conservado el puerto y los sensores.",
                    "RankTemp · Actualización", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "RankTemp · Actualización", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { UseWaitCursor = false; }
        }

        private void RestoreWindow()
        {
            if (closeWhenIdle) return;
            restoringWindow = true;
            try
            {
                RefreshReadings(true);
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            }
            finally { restoringWindow = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trayIcon.Dispose();
                trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool RefreshPorts()
        {
            var previous = ports.SelectedItem as PortChoice;
            ports.Items.Clear();
            try
            {
                var choices = RankTempDevice.ListPorts();
                ports.Items.AddRange(choices);
                var selected = previous == null ? null : choices.FirstOrDefault(choice => choice.Name == previous.Name);
                var candidates = choices.Where(choice => choice.IsCh340).ToArray();
                ports.SelectedItem = candidates.FirstOrDefault(IsRememberedPort) ?? selected
                    ?? (candidates.Length == 1 ? candidates[0] : null);
                SetStatus(candidates.Length == 1 ? "CH340 detectado · " + candidates[0].Name :
                    choices.Length == 0 ? "Sin puertos serie disponibles" : "Selecciona el puerto del 1019.", false);
            }
            catch (Exception ex)
            {
                SetStatus("No se pudieron leer los puertos · " + ex.Message, false);
                return false;
            }
            finally { UpdateButtons(); }
            return true;
        }

        private bool IsRememberedPort(PortChoice choice)
        {
            return choice != null && choice.IsCh340 &&
                string.Equals(choice.Name, rememberedPort, StringComparison.OrdinalIgnoreCase);
        }

        private async Task ChangeConnectionAsync()
        {
            if (connectionPending || closeWhenIdle) return;
            var choice = ports.SelectedItem as PortChoice;
            bool disconnecting = connected || rememberedPort != null;
            if (!disconnecting && choice == null) return;
            connectionPending = true;
            connect.Enabled = refresh.Enabled = ports.Enabled = false;
            while (busy && !closeWhenIdle) await Task.Delay(50);
            if (closeWhenIdle) return;
            busy = true;
            SetStatus(disconnecting ? "Desconectando…" : "Comprobando el 1019 · " + choice.Name, false);
            try
            {
                if (disconnecting)
                {
                    rememberedPort = null;
                    await Task.Run(() => device.Dispose());
                    connected = false;
                    SetStatus("Sin conexión", false);
                }
                else
                {
                    await ConnectAsync(choice);
                }
            }
            catch (Exception ex)
            {
                connected = false;
                SetStatus("No se pudo abrir la conexión · " + ex.Message, false);
            }
            finally
            {
                connectionPending = false;
                if (!closeWhenIdle) UpdateButtons();
                FinishWork();
            }
        }

        private async Task ConnectAsync(PortChoice choice)
        {
            await Task.Run(() => device.Connect(choice.Name));
            connected = true;
            synchronizing = false;
            rememberedPort = choice.IsCh340 ? choice.Name : null;
            SetStatus("1019 conectado · " + choice.Name, true);
            if (!connectionPending && !closeWhenIdle) UpdateButtons();
        }

        private async Task ReconnectAsync()
        {
            if (!RefreshPorts()) return;
            var choice = ports.SelectedItem as PortChoice;
            if (!IsRememberedPort(choice))
            {
                SetStatus("Esperando al 1019 · " + rememberedPort, false);
                return;
            }

            SetStatus("Reconectando al 1019 · " + choice.Name, false);
            try { await ConnectAsync(choice); }
            catch (Exception ex) { SetStatus("Reconexión pendiente · " + ex.Message, false); }
        }

        private async Task PollAsync()
        {
            if (busy || connectionPending || closeWhenIdle) return;
            busy = true;
            try
            {
                if (!connected && rememberedPort != null &&
                    (!reconnectDelay.IsRunning || reconnectDelay.ElapsedMilliseconds >= 3000))
                {
                    await ReconnectAsync();
                    reconnectDelay.Restart();
                }
                if (closeWhenIdle || connectionPending) return;

                SensorReading[] readings;
                try
                {
                    readings = await Task.Run(() => sensors.Read());
                    sensorError = null;
                }
                catch (Exception ex)
                {
                    readings = Array.Empty<SensorReading>();
                    sensorError = ex.Message;
                }
                if (closeWhenIdle || connectionPending) return;
                temperatures = readings.Where(reading => reading.IsTemperature).ToArray();
                fans = readings.Where(reading => !reading.IsTemperature).ToArray();
                RefreshReadings(Visible);
                if (connected)
                {
                    float? temperature = rows[0].DisplayValue;
                    float? fan = rows[1].DisplayValue;
                    float? pump = rows[2].DisplayValue;
                    try
                    {
                        await Task.Run(() => device.Send(temperature, pump, fan));
                        if (!synchronizing && !closeWhenIdle)
                        {
                            synchronizing = true;
                            SetStatus("1019 conectado · " + ((PortChoice)ports.SelectedItem).Name + " · Sincronizando", true);
                        }
                    }
                    catch (Exception ex)
                    {
                        connected = false;
                        reconnectDelay.Restart();
                        SetStatus("Sincronización interrumpida · " + ex.Message, false);
                        if (!connectionPending && !closeWhenIdle) UpdateButtons();
                    }
                }
            }
            catch (Exception ex) { sensorStatus.Text = "No se pudieron leer los sensores · " + ex.Message; }
            finally { FinishWork(); }
        }

        private void RefreshReadings(bool render)
        {
            rows[0].Refresh(temperatures, render);
            rows[1].Refresh(fans, render);
            rows[2].Refresh(fans, render);
            if (!render) return;

            UpdateSensorAccess(temperatures.Any(reading => reading.Value.HasValue) ||
                fans.Any(reading => reading.Value.HasValue));
            if (sensorError != null) sensorStatus.Text = "No se pudieron leer los sensores · " + sensorError;
        }

        private void SetStatus(string message, bool active)
        {
            connectionStatus.Text = "●  " + message;
            connectionStatus.ForeColor = active ? Theme.Accent : Theme.Muted;
        }

        private void UpdateButtons()
        {
            bool reconnecting = !connected && rememberedPort != null;
            ports.Enabled = !connected && !reconnecting;
            refresh.Enabled = ports.Enabled;
            connect.Enabled = connected || reconnecting || ports.SelectedItem != null;
            connect.Text = connected ? "Desconectar" : reconnecting ? "Cancelar" : "Conectar";
            bool primary = !connected && !reconnecting && connect.Enabled;
            connect.BackColor = primary ? Theme.Accent : Theme.Input;
            connect.ForeColor = primary ? Theme.Background : Theme.Text;
            connect.FlatAppearance.BorderSize = primary ? 0 : 1;
            connect.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(162, 244, 220) : Theme.Border;
        }

        private void FinishWork()
        {
            busy = false;
            if (closeWhenIdle && !IsDisposed) Close();
        }

        private void CloseWhenReady(object sender, FormClosingEventArgs args)
        {
            timer.Stop();
            closeWhenIdle = true;
            if (busy)
            {
                if (args.CloseReason == CloseReason.WindowsShutDown) SaveSelections();
                else args.Cancel = true;
                return;
            }

            timer.Dispose();
            try { device.Dispose(); }
            finally
            {
                try { sensors.Dispose(); }
                finally { SaveSelections(); }
            }
        }

        private void LoadSelections()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsPath))
                {
                    if (key == null) return;
                    activationOffered = Equals(key.GetValue("ActivationOffered"), 1);
                    rememberedPort = key.GetValue("RememberedPort") as string;
                    foreach (var row in rows)
                    {
                        row.SensorId = key.GetValue(row.Key + "Sensor") as string;
                        row.ShowOnDisplay.Checked = !Equals(key.GetValue(row.Key + "Show"), 0);
                    }
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void SaveSelections()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsPath))
                {
                    if (activationOffered) key.SetValue("ActivationOffered", 1);
                    if (rememberedPort == null) key.DeleteValue("RememberedPort", false);
                    else key.SetValue("RememberedPort", rememberedPort);
                    foreach (var row in rows)
                    {
                        if (row.SensorId != null) key.SetValue(row.Key + "Sensor", row.SensorId);
                        key.SetValue(row.Key + "Show", row.ShowOnDisplay.Checked ? 1 : 0);
                    }
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void UpdateSensorAccess(bool? readingsAvailable)
        {
            bool driverReady = SensorMonitor.DriverReady;
            sensorAccess.Visible = driverReady ? !elevated : SensorMonitor.HasBundledDriver;
            sensorAccess.Text = driverReady ? "Abrir RankTemp como administrador" : "Activar sensores";
            if (!driverReady)
                sensorStatus.Text = SensorMonitor.HasBundledDriver
                    ? "RankTemp incluye el controlador de Windows para leer los sensores."
                    : "Algunas lecturas requieren acceso a los sensores de la placa.";
            else if (!elevated)
                sensorStatus.Text = "Algunos sensores requieren permisos de administrador.";
            else if (readingsAvailable.HasValue)
                sensorStatus.Text = readingsAvailable.Value
                    ? "Lecturas de la placa actualizadas."
                    : "La placa no ha devuelto lecturas de temperatura ni de RPM.";
        }

        private void ActivateSensors()
        {
            if (closeWhenIdle) return;
            bool needsDriver = !SensorMonitor.DriverReady;
            if (needsDriver && !SensorMonitor.HasBundledDriver) return;
            if (needsDriver && MessageBox.Show(this,
                    "¿Quieres instalar el controlador PawnIO incluido para leer la temperatura y las RPM? RankTemp volverá a abrirse al terminar.",
                    "Activar sensores", MessageBoxButtons.OKCancel, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return;

            try
            {
                SaveSelections();
                var start = new ProcessStartInfo(Application.ExecutablePath,
                    needsDriver ? Program.InstallDriverArgument : Program.ReopenArgument)
                {
                    UseShellExecute = !elevated,
                    Verb = elevated ? "" : "runas"
                };
                using (Process.Start(start)) { }
                Close();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223) MessageBox.Show(this, ex.Message, "RankTemp · Activar sensores");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "RankTemp · Activar sensores"); }
        }

        private static void Add(TableLayoutPanel panel, Control control, int height)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            panel.Controls.Add(control, 0, row);
        }

        private static Label NewLabel(string text, int size, bool bold = false, Color? color = null)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Fill, AutoEllipsis = true,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? Theme.Text, TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button NewButton(string title, bool primary)
        {
            var button = new DashboardButton
            {
                Text = title, Dock = DockStyle.Fill,
                BackColor = primary ? Theme.Accent : Theme.Input,
                ForeColor = primary ? Theme.Background : Theme.Text,
                Margin = new Padding(9, 3, 0, 3)
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(162, 244, 220) : Theme.Border;
            return button;
        }

        private sealed class SensorRow
        {
            private readonly ComboBox selector = new DashboardComboBox();
            private readonly Label readout;
            private bool refreshing;
            private float? reading;

            public string Key { get; private set; }
            public string SensorId { get; set; }
            public TableLayoutPanel Panel { get; private set; }
            public CheckBox ShowOnDisplay { get; private set; }
            public float? DisplayValue { get { return ShowOnDisplay.Checked ? reading : null; } }

            public SensorRow(string key, string title, string unit, Color accent)
            {
                Key = key;
                selector.AccessibleName = "Sensor de " + title.ToLowerInvariant();
                Panel = new DashboardCard
                {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                    Padding = new Padding(15, 11, 15, 11), Margin = new Padding(3, 0, 5, 0)
                };
                Panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                Panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
                Panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
                Panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                Panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                Panel.Controls.Add(NewLabel(title, 11, true), 0, 0);

                var value = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
                readout = NewLabel("—", 34, true, accent);
                readout.Dock = DockStyle.None;
                readout.AutoSize = true;
                readout.Margin = Padding.Empty;
                var unitLabel = NewLabel(unit, 11, true, Theme.Muted);
                unitLabel.Dock = DockStyle.None;
                unitLabel.AutoSize = true;
                unitLabel.Margin = new Padding(2, 29, 0, 0);
                value.Controls.Add(readout);
                value.Controls.Add(unitLabel);
                Panel.Controls.Add(value, 0, 1);
                Panel.Controls.Add(selector, 0, 2);
                ShowOnDisplay = new DashboardSwitch("Mostrar en pantalla") { Checked = true };
                Panel.Controls.Add(ShowOnDisplay, 0, 3);
                selector.Items.Add("Sin asignar");
                selector.SelectedIndex = 0;
                selector.SelectedIndexChanged += (sender, args) =>
                {
                    if (refreshing) return;
                    var selected = selector.SelectedItem as SensorReading;
                    SensorId = selected == null ? "" : selected.Id;
                };
            }

            public void Refresh(SensorReading[] choices, bool render)
            {
                if (SensorId == null)
                {
                    var available = choices.Where(item => item.Value.HasValue).ToArray();
                    var named = available.Where(item => MatchName(item.Name)).ToArray();
                    if (Key == "Cpu")
                    {
                        var best = named.FirstOrDefault() ?? available.FirstOrDefault();
                        if (best != null) SensorId = best.Id;
                    }
                    else if (named.Length == 1) SensorId = named[0].Id;
                }

                int index = Array.FindIndex(choices, item => item.Id == SensorId);
                reading = index < 0 ? null : choices[index].Value;
                if (!render) return;

                bool rebuild = selector.Items.Count != choices.Length + 1;
                for (int item = 0; !rebuild && item < choices.Length; item++)
                {
                    var previous = (SensorReading)selector.Items[item + 1];
                    rebuild = previous.Id != choices[item].Id || previous.Name != choices[item].Name;
                }
                int selectedIndex = index + 1;
                if (rebuild || selector.SelectedIndex != selectedIndex)
                {
                    refreshing = true;
                    selector.BeginUpdate();
                    try
                    {
                        if (rebuild)
                        {
                            selector.Items.Clear();
                            selector.Items.Add("Sin asignar");
                            selector.Items.AddRange(choices);
                        }
                        selector.SelectedIndex = selectedIndex;
                    }
                    finally { selector.EndUpdate(); refreshing = false; }
                }
                readout.Text = reading.HasValue ? reading.Value.ToString(Key == "Cpu" ? "0.0" : "0") : "—";
            }

            private bool MatchName(string name)
            {
                name = name.Split(new[] { " · " }, 2, StringSplitOptions.None)[0];
                if (Key == "Cpu") return Contains(name, "CPU Package") || Contains(name, "Tctl");
                return Contains(name, Key == "Pump" ? "Pump" : "CPU");
            }

            private static bool Contains(string name, string part)
            {
                return name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
