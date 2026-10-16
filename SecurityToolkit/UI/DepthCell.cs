using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>
    /// Draws a privilege depth the way the Dynamics 365 security role editor does: a small icon and the depth in
    /// words. None is a red "no entry" sign, User a grey person, Business Unit blue people, Parent: Child Business
    /// Units red people, and Organization a green org chart.
    /// </summary>
    internal static class DepthCell
    {
        private const int IconSize = 18;
        private const int Padding = 6;
        private const int Gap = 5;

        private static readonly Color NoneColor = Color.FromArgb(164, 38, 44);
        private static readonly Color UserColor = Color.FromArgb(96, 94, 92);
        private static readonly Color BusinessUnitColor = Color.FromArgb(0, 120, 212);
        private static readonly Color ParentChildColor = Color.FromArgb(192, 57, 43);
        private static readonly Color OrganizationColor = Color.FromArgb(16, 124, 16);

        private enum Icon { None, User, BusinessUnit, ParentChild, Organization }

        /// <summary>The icon for the depth words used in the grid, or false for any other text.</summary>
        private static bool TryIcon(string text, out Icon icon)
        {
            switch (text)
            {
                case "None": icon = Icon.None; return true;
                case "User": icon = Icon.User; return true;
                case "Business Unit": icon = Icon.BusinessUnit; return true;
                case "Parent: Child Business Units": icon = Icon.ParentChild; return true;
                case "Organization": icon = Icon.Organization; return true;
                default: icon = Icon.None; return false;
            }
        }

        /// <summary>True when <paramref name="text"/> is one of the depth words this draws.</summary>
        public static bool Handles(string text)
        {
            return TryIcon(text, out _);
        }

        /// <summary>A <see cref="FilterableGrid.CellRenderer"/>: leaves cells that are not a depth to the grid.</summary>
        public static bool Paint(Graphics g, Rectangle bounds, string text, bool selected)
        {
            if (!TryIcon(text, out var icon))
            {
                return false;
            }

            var iconBounds = new Rectangle(bounds.Left + Padding, bounds.Top + (bounds.Height - IconSize) / 2, IconSize, IconSize);
            DrawIcon(g, iconBounds, icon);

            var textBounds = new Rectangle(iconBounds.Right + Gap, bounds.Top, Math.Max(0, bounds.Right - iconBounds.Right - Gap - 4), bounds.Height);
            TextRenderer.DrawText(g, text, Theme.FontRegular, textBounds, icon == Icon.None ? Theme.TextSecondary : Theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return true;
        }

        private static void DrawIcon(Graphics g, Rectangle r, Icon icon)
        {
            var previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (icon)
            {
                case Icon.None:
                    using (var pen = new Pen(NoneColor, 1.8f))
                    {
                        g.DrawEllipse(pen, r.Left + 1.5f, r.Top + 1.5f, IconSize - 3, IconSize - 3);
                        g.DrawLine(pen, r.Left + 4.5f, r.Bottom - 4.5f, r.Right - 4.5f, r.Top + 4.5f);
                    }

                    break;
                case Icon.User:
                    Glyph(g, r, "", UserColor);
                    break;
                case Icon.BusinessUnit:
                    Glyph(g, r, "", BusinessUnitColor);
                    break;
                case Icon.ParentChild:
                    Glyph(g, r, "", ParentChildColor);
                    break;
                case Icon.Organization:
                    DrawOrganizationChart(g, r);
                    break;
            }

            g.SmoothingMode = previous;
        }

        private static void Glyph(Graphics g, Rectangle r, string glyph, Color color)
        {
            TextRenderer.DrawText(g, glyph, Theme.FontIcon, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        /// <summary>One box above two, joined by lines: the organization chart.</summary>
        private static void DrawOrganizationChart(Graphics g, Rectangle r)
        {
            using (var pen = new Pen(OrganizationColor, 1.4f))
            using (var brush = new SolidBrush(OrganizationColor))
            {
                var midX = r.Left + r.Width / 2f;
                var leftX = r.Left + 4f;
                var rightX = r.Right - 4f;
                g.DrawLine(pen, midX, r.Top + 6f, midX, r.Top + 9f);
                g.DrawLine(pen, leftX, r.Top + 9f, rightX, r.Top + 9f);
                g.DrawLine(pen, leftX, r.Top + 9f, leftX, r.Top + 12f);
                g.DrawLine(pen, rightX, r.Top + 9f, rightX, r.Top + 12f);
                g.FillRectangle(brush, midX - 3.5f, r.Top + 1.5f, 7f, 5f);
                g.FillRectangle(brush, leftX - 3.5f, r.Top + 12f, 7f, 5f);
                g.FillRectangle(brush, rightX - 3.5f, r.Top + 12f, 7f, 5f);
            }
        }
    }
}
