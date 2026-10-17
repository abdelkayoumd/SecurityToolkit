using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace SecurityToolkit.UI
{
    /// <summary>The small icons in front of tree nodes, one per kind of thing a node can be.</summary>
    internal static class TreeIcons
    {
        public const int User = 0;
        public const int Team = 1;
        /// <summary>A security role assigned straight to the user.</summary>
        public const int DirectRole = 2;
        /// <summary>A security role the user gets from a team.</summary>
        public const int InheritedRole = 3;
        /// <summary>The "Direct roles" heading.</summary>
        public const int DirectRolesGroup = 4;
        /// <summary>The "Teams" heading.</summary>
        public const int TeamsGroup = 5;
        /// <summary>A business unit.</summary>
        public const int BusinessUnit = 6;

        private const int Size = 18;
        private const string UserGlyph = "";
        private const string TeamGlyph = "";
        private const string RoleGlyph = "";

        private static readonly Color DirectColor = Theme.Success;
        private static readonly Color InheritedColor = Color.FromArgb(202, 80, 16);

        /// <summary>A new image list; the caller owns it. Image indexes are the constants above.</summary>
        public static ImageList Create()
        {
            var list = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(Size, Size) };
            list.Images.Add(Draw(UserGlyph, Theme.Navy));
            list.Images.Add(Draw(TeamGlyph, Theme.Accent));
            list.Images.Add(Draw(RoleGlyph, DirectColor));
            list.Images.Add(Draw(RoleGlyph, InheritedColor));
            list.Images.Add(Draw(RoleGlyph, Theme.TextSecondary));
            list.Images.Add(Draw(TeamGlyph, Theme.TextSecondary));
            list.Images.Add(Draw(Glyphs.Business, Theme.Navy));
            return list;
        }

        private static Bitmap Draw(string glyph, Color color)
        {
            var bitmap = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, Theme.FontIcon, brush, new RectangleF(0, 0, Size, Size), format);
            }

            return bitmap;
        }
    }
}
