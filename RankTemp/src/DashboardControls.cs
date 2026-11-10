using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RankTemp
{
    internal static class DashboardTheme
    {
        public static readonly Color Background = Color.FromArgb(15, 18, 23);
        public static readonly Color Surface = Color.FromArgb(24, 29, 37);
        public static readonly Color Input = Color.FromArgb(32, 39, 50);
        public static readonly Color Border = Color.FromArgb(55, 65, 79);
        public static readonly Color Text = Color.FromArgb(240, 244, 249);
        public static readonly Color Muted = Color.FromArgb(167, 181, 200);
        public static readonly Color Accent = Color.FromArgb(121, 231, 198);

        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0) return path;

            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class DashboardCard : TableLayoutPanel
    {
        public DashboardCard()
        {
            BackColor = DashboardTheme.Surface;
            DoubleBuffered = true;
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            base.OnPaintBackground(args);
            if (Width < 2 || Height < 2) return;

            args.Graphics.Clear(DashboardTheme.Background);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = DashboardTheme.RoundedPath(bounds, 14))
            using (var fill = new SolidBrush(BackColor))
            using (var border = new Pen(DashboardTheme.Border))
            {
                args.Graphics.FillPath(fill, path);
                args.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class DashboardComboBox : ComboBox
    {
        public string Placeholder { get; set; }

        public DashboardComboBox()
        {
            Dock = DockStyle.Fill;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            BackColor = DashboardTheme.Input;
            ForeColor = DashboardTheme.Text;
            Margin = new Padding(0, 3, 0, 3);
            DropDownWidth = 420;
            ItemHeight = 25;
            SelectedIndexChanged += (sender, args) => Invalidate();
            EnabledChanged += (sender, args) => Invalidate();
            GotFocus += (sender, args) => Invalidate();
            LostFocus += (sender, args) => Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs args)
        {
            bool selected = (args.State & DrawItemState.Selected) != 0;
            var background = selected ? DashboardTheme.Border : DashboardTheme.Input;
            using (var fill = new SolidBrush(background)) args.Graphics.FillRectangle(fill, args.Bounds);
            if (args.Index < 0 || args.Index >= Items.Count) return;

            var textBounds = Rectangle.Inflate(args.Bounds, -9, 0);
            TextRenderer.DrawText(args.Graphics, GetItemText(Items[args.Index]), Font, textBounds,
                DashboardTheme.Text, TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void WndProc(ref Message message)
        {
            const int Paint = 0x000F;
            const int PrintClient = 0x0318;
            if (message.Msg == PrintClient && message.WParam != IntPtr.Zero)
            {
                using (var graphics = Graphics.FromHdc(message.WParam)) DrawField(graphics);
                message.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref message);
            if (message.Msg == Paint && IsHandleCreated)
            {
                using (var graphics = Graphics.FromHwnd(Handle)) DrawField(graphics);
            }
        }

        private void DrawField(Graphics graphics)
        {
            if (Width < 2 || Height < 2) return;
            graphics.Clear(Parent == null ? DashboardTheme.Surface : Parent.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            var foreground = Enabled ? DashboardTheme.Text : DashboardTheme.Muted;
            using (var path = DashboardTheme.RoundedPath(bounds, 7))
            using (var fill = new SolidBrush(Enabled ? DashboardTheme.Input : DashboardTheme.Surface))
            using (var border = new Pen(Focused && Enabled ? DashboardTheme.Accent : DashboardTheme.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }

            var textBounds = new Rectangle(10, 0, Math.Max(1, Width - 34), Height);
            string label = SelectedItem == null ? Placeholder ?? "" : GetItemText(SelectedItem);
            TextRenderer.DrawText(graphics, label, Font, textBounds, foreground,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int arrowX = Width - 15;
            int arrowY = Height / 2 - 1;
            using (var arrow = new Pen(DashboardTheme.Muted, 1.4f))
                graphics.DrawLines(arrow, new[] { new Point(arrowX - 4, arrowY),
                    new Point(arrowX, arrowY + 4), new Point(arrowX + 4, arrowY) });
        }
    }

    internal sealed class DashboardSwitch : CheckBox
    {
        public DashboardSwitch(string text)
        {
            Text = text;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(Parent == null ? DashboardTheme.Background : Parent.BackColor);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = args.Graphics.DpiX / 96f;
            int width = (int)(32 * scale);
            int height = (int)(18 * scale);
            int y = (Height - height) / 2;
            var track = new Rectangle(1, y, width, height);
            Color trackColor = Checked && Enabled ? DashboardTheme.Accent : DashboardTheme.Border;
            using (var path = DashboardTheme.RoundedPath(track, height / 2))
            using (var fill = new SolidBrush(trackColor))
            using (var thumb = new SolidBrush(Checked && Enabled ? DashboardTheme.Background : DashboardTheme.Muted))
            {
                args.Graphics.FillPath(fill, path);
                int inset = (int)(3 * scale);
                int size = height - inset * 2;
                int x = Checked ? track.Right - size - inset : track.Left + inset;
                args.Graphics.FillEllipse(thumb, x, y + inset, size, size);
            }
            int textLeft = track.Right + (int)(9 * scale);
            var textBounds = new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft), Height);
            TextRenderer.DrawText(args.Graphics, Text, Font, textBounds, Enabled ? DashboardTheme.Text : DashboardTheme.Muted,
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(args.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1),
                    DashboardTheme.Accent, Parent == null ? DashboardTheme.Background : Parent.BackColor);
        }
    }

    internal sealed class DashboardButton : Button
    {
        private bool hovered;

        public DashboardButton()
        {
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            MouseEnter += (sender, args) => { hovered = true; Invalidate(); };
            MouseLeave += (sender, args) => { hovered = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.Clear(Parent == null ? DashboardTheme.Background : Parent.BackColor);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = Enabled
                ? (hovered ? FlatAppearance.MouseOverBackColor : BackColor)
                : DashboardTheme.Surface;
            Color foreground = Enabled ? ForeColor : DashboardTheme.Muted;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = DashboardTheme.RoundedPath(bounds, 7))
            using (var fill = new SolidBrush(background))
            using (var border = new Pen(Focused && ShowFocusCues ? DashboardTheme.Accent : DashboardTheme.Border))
            {
                args.Graphics.FillPath(fill, path);
                if (FlatAppearance.BorderSize > 0 || Focused && ShowFocusCues)
                    args.Graphics.DrawPath(border, path);
            }
            TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle, foreground,
                TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
