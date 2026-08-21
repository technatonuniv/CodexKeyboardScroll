using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexKeyboardScroll
{
    internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Selection = Color.FromArgb(224, 237, 255);
        private static readonly Color Accent = Color.FromArgb(37, 99, 235);
        private static readonly Color Border = Color.FromArgb(214, 224, 237);
        private static readonly Color Separator = Color.FromArgb(226, 232, 240);

        internal ModernMenuRenderer() : base(new ModernColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled)
            {
                base.OnRenderMenuItemBackground(e);
                return;
            }

            Rectangle bounds = new Rectangle(4, 2, e.Item.Width - 8, e.Item.Height - 4);
            using (GraphicsPath path = RoundedRectangle(bounds, 7))
            using (var brush = new SolidBrush(Selection))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle box = new Rectangle(e.ImageRectangle.Left + 2, e.ImageRectangle.Top + 2, 18, 18);
            using (GraphicsPath path = RoundedRectangle(box, 5))
            using (var brush = new SolidBrush(Accent))
            using (var pen = new Pen(Color.White, 2.2f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawLines(pen, new[]
                {
                    new Point(box.Left + 4, box.Top + 9),
                    new Point(box.Left + 8, box.Top + 13),
                    new Point(box.Left + 15, box.Top + 5)
                });
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Separator))
            {
                e.Graphics.DrawLine(pen, 14, y, e.Item.Width - 14, y);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var pen = new Pen(Border))
            {
                e.Graphics.DrawRectangle(pen, bounds);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class ModernColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(248, 250, 252); } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Selection; } }
            public override Color MenuItemSelected { get { return Selection; } }
            public override Color CheckBackground { get { return Selection; } }
            public override Color CheckSelectedBackground { get { return Selection; } }
            public override Color CheckPressedBackground { get { return Selection; } }
        }
    }
}
