using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CalcStudioEPM.Resources
{
    /// <summary>
    /// Creates small toolbar icons (16x16) using GDI+ drawing.
    /// All icons use muted colors that look good on both light and dark toolbars.
    /// </summary>
    public static class IconHelper
    {
        /// <summary>
        /// Returns a copy of <paramref name="source"/> with every colour blended toward
        /// white (alpha untouched), so the dark/muted light-theme icons stay readable on
        /// Notepad++'s dark-mode toolbar. Muted colours get 55%; strongly saturated
        /// accents (e.g. the red "+") only 30%, so they keep their colour.
        /// </summary>
        public static Bitmap ForDarkMode(Bitmap source)
        {
            var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                Color c = source.GetPixel(x, y);
                int pct = c.GetSaturation() > 0.75f ? 30 : 55;
                bmp.SetPixel(x, y, Color.FromArgb(c.A,
                    c.R + (255 - c.R) * pct / 100,
                    c.G + (255 - c.G) * pct / 100,
                    c.B + (255 - c.B) * pct / 100));
            }
            return bmp;
        }

        /// <summary>
        /// The "Standard icons" toolbar bitmap (hToolbarBmp): a 32-bit DIB of size² pixels
        /// whose colours are pre-blended onto a matte — the toolbar's background — with the
        /// alpha kept.
        ///
        /// Notepad++ adds this bitmap to its toolbar as is. Some setups honour the alpha
        /// channel, others drop it and draw every pixel opaque. A grey (192,192,192) key
        /// colour then showed as a grey square behind each icon; blended onto the toolbar's
        /// own background the icon looks right either way. Since that background changes
        /// with dark mode, <see cref="SetMatte"/> re-blends the pixels in place — Notepad++
        /// keeps using the same handle whenever it rebuilds its toolbar.
        /// </summary>
        public sealed class ToolbarBitmap
        {
            private readonly Bitmap _image;   // the icon at toolbar size, kept for re-blending
            private readonly IntPtr _bits;

            /// <summary>GDI handle; never destroyed — Notepad++ keeps it for its lifetime.</summary>
            public IntPtr Handle { get; }

            public ToolbarBitmap(Bitmap source, int size, Color matte)
            {
                _image = Resize(source, size);
                var bmi = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                    biWidth = size, biHeight = -size,   // top-down
                    biPlanes = 1, biBitCount = 32,
                };
                Handle = CreateDIBSection(IntPtr.Zero, ref bmi, 0 /* DIB_RGB_COLORS */, out _bits, IntPtr.Zero, 0);
                if (Handle == IntPtr.Zero)
                    Handle = _image.GetHbitmap(matte);   // opaque fallback, same look (not re-blendable)
                else
                    SetMatte(matte);
            }

            public void SetMatte(Color matte)
            {
                if (_bits == IntPtr.Zero) return;
                int w = _image.Width, h = _image.Height;
                var pixels = new byte[w * 4 * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = _image.GetPixel(x, y);
                    int a = c.A;
                    int i = (y * w + x) * 4;
                    pixels[i]     = (byte)((c.B * a + matte.B * (255 - a)) / 255);
                    pixels[i + 1] = (byte)((c.G * a + matte.G * (255 - a)) / 255);
                    pixels[i + 2] = (byte)((c.R * a + matte.R * (255 - a)) / 255);
                    pixels[i + 3] = (byte)a;
                }
                GdiFlush();
                Marshal.Copy(pixels, 0, _bits, pixels.Length);
            }
        }

        private static Bitmap Resize(Bitmap source, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = size % source.Width == 0 ? InterpolationMode.NearestNeighbor
                                                                : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY);   // no faded fringe along the edges
                    g.DrawImage(source, new Rectangle(0, 0, size, size),
                                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            return bmp;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage,
                                                      out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern bool GdiFlush();

        /// <summary>
        /// Gear icon — Configure EPM Connection button.
        /// A solid round body with 8 rectangular teeth and a small punched-out center.
        /// (The hole must stay well inside the body — when it nearly matched the body's
        /// radius it ate the whole body and left only a ring of loose teeth.)
        /// </summary>
        public static Bitmap CreateGearIcon(int size = 16)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode   = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float c      = size / 2f;
                float body   = size * 0.34f;   // body radius
                float tip    = size * 0.49f;   // teeth reach this radius
                float toothW = size * 0.21f;
                float hole   = size * 0.14f;

                using (var brush = new SolidBrush(Color.FromArgb(90, 90, 90)))
                using (var gear  = new GraphicsPath(FillMode.Winding))
                {
                    gear.AddEllipse(c - body, c - body, body * 2f, body * 2f);

                    // 8 teeth at 45° intervals, each overlapping the body by a pixel
                    for (int i = 0; i < 8; i++)
                    {
                        using (var tooth = new GraphicsPath())
                        using (var m = new Matrix())
                        {
                            tooth.AddRectangle(new RectangleF(c - toothW / 2f, c - tip, toothW, tip - body + 1f));
                            m.RotateAt(i * 45f, new PointF(c, c));
                            tooth.Transform(m);
                            gear.AddPath(tooth, false);
                        }
                    }
                    g.FillPath(brush, gear);
                }

                // Punch out the center hole
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clearBrush = new SolidBrush(Color.Transparent))
                    g.FillEllipse(clearBrush, c - hole, c - hole, hole * 2f, hole * 2f);
            }
            return bmp;
        }

        /// <summary>
        /// Download arrow icon — Pull Calculations button.
        /// Draws a downward arrow over a horizontal tray line.
        /// </summary>
        public static Bitmap CreateDownloadIcon(int size = 16)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var arrowColor = Color.FromArgb(0, 100, 180);
                using (var pen   = new Pen(arrowColor, 2f))
                using (var brush = new SolidBrush(arrowColor))
                {
                    int m = size / 8;

                    // Vertical shaft
                    g.DrawLine(pen, size / 2, m, size / 2, size - 3 * m);

                    // Arrowhead (filled triangle pointing down)
                    var pts = new[]
                    {
                        new Point(size / 2 - 3, size - 4 * m),
                        new Point(size / 2,     size - 2 * m),
                        new Point(size / 2 + 3, size - 4 * m),
                    };
                    g.FillPolygon(brush, pts);

                    // Tray (horizontal bottom line)
                    g.DrawLine(pen, m, size - m, size - m, size - m);
                }
            }
            return bmp;
        }

        /// <summary>
        /// Pink folder with a red "+" badge — Open as Workspace button.
        /// Classic folder (tab, back, lighter open front flap) drawn pixel-crisp on a
        /// 16×16 grid; the plus sits bottom-right with a white halo so it stays
        /// readable where it overlaps the folder.
        /// </summary>
        public static Bitmap CreateWorkspaceIcon()
        {
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode   = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;

                using (var outline = new SolidBrush(Color.FromArgb(170, 50, 105)))   // deep pink
                using (var back    = new SolidBrush(Color.FromArgb(230, 110, 165)))  // folder pink
                using (var front   = new SolidBrush(Color.FromArgb(246, 160, 200)))  // lighter flap
                using (var halo    = new SolidBrush(Color.White))
                using (var plus    = new SolidBrush(Color.FromArgb(215, 25, 30)))    // red
                {
                    // ── Tab ──────────────────────────────────────────────
                    g.FillRectangle(outline, 0, 1, 7, 3);
                    g.FillRectangle(back,    1, 2, 5, 2);

                    // ── Folder back ──────────────────────────────────────
                    g.FillRectangle(outline, 0, 3, 15, 11);
                    g.FillRectangle(back,    1, 4, 13, 9);

                    // ── Open front flap ──────────────────────────────────
                    g.FillRectangle(outline, 1, 6, 13, 1);
                    g.FillRectangle(front,   1, 7, 13, 6);

                    // ── Red plus, bottom-right, with white halo ──────────
                    // 7×7 plus, 3 px thick, centred on (12,12)
                    g.FillRectangle(halo, 8, 10, 8, 5);    // horizontal halo
                    g.FillRectangle(halo, 10, 8, 5, 8);    // vertical halo
                    g.FillRectangle(plus, 9, 11, 7, 3);    // horizontal bar
                    g.FillRectangle(plus, 11, 9, 3, 7);    // vertical bar
                }
            }
            return bmp;
        }

        /// <summary>
        /// Network-topology deploy icon — Deploy Active Tab button.
        /// Draws a source node at top, a downward arrow in the centre, a horizontal
        /// distribution bar, and three target nodes at the bottom — matching the
        /// standard "publish to infrastructure" icon shape.
        /// </summary>
        public static Bitmap CreateDeployIcon(int size = 16)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                // Pixel-crisp rendering — anti-aliasing blurs small icons
                g.SmoothingMode    = SmoothingMode.None;
                g.PixelOffsetMode  = System.Drawing.Drawing2D.PixelOffsetMode.None;

                var color = Color.FromArgb(80, 110, 160); // slate blue
                using (var brush = new SolidBrush(color))
                {
                    // ── Top source node (centred, 10 × 4 px) ─────────────
                    g.FillRectangle(brush, 3, 0, 10, 4);

                    // ── Arrow shaft (2 px wide) ───────────────────────────
                    g.FillRectangle(brush, 7, 4, 2, 3);

                    // ── Arrow head (filled downward triangle) ─────────────
                    g.FillPolygon(brush, new[]
                    {
                        new Point(4,  7),
                        new Point(12, 7),
                        new Point(8,  11),
                    });

                    // ── Horizontal distribution bar ───────────────────────
                    g.FillRectangle(brush, 1, 11, 14, 1);

                    // ── Three bottom target nodes (4 × 4 px each) ─────────
                    g.FillRectangle(brush,  0, 12, 4, 4);   // left
                    g.FillRectangle(brush,  6, 12, 4, 4);   // centre
                    g.FillRectangle(brush, 12, 12, 4, 4);   // right
                }
            }
            return bmp;
        }

        /// <summary>
        /// "{x}" icon — Variables button. Curly braces (the Calc script prompt syntax)
        /// around a bold x, drawn anti-aliased on the 16×16 grid.
        /// </summary>
        public static Bitmap CreateVariablesIcon()
        {
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode   = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var brace = new Pen(Color.FromArgb(120, 70, 175), 1.6f))
                using (var cross = new SolidBrush(Color.FromArgb(200, 90, 20)))
                {
                    brace.StartCap = brace.EndCap = LineCap.Round;

                    // Left brace "{"
                    using (var p = new GraphicsPath())
                    {
                        p.AddBezier(5.0f, 1.5f, 3.2f, 1.5f, 3.2f, 2.5f, 3.2f, 4.0f);
                        p.AddLine  (3.2f, 4.0f, 3.2f, 6.2f);
                        p.AddBezier(3.2f, 6.2f, 3.2f, 7.5f, 2.2f, 8.0f, 1.0f, 8.0f);
                        p.AddBezier(1.0f, 8.0f, 2.2f, 8.0f, 3.2f, 8.5f, 3.2f, 9.8f);
                        p.AddLine  (3.2f, 9.8f, 3.2f, 12.0f);
                        p.AddBezier(3.2f, 12.0f, 3.2f, 13.5f, 3.2f, 14.5f, 5.0f, 14.5f);
                        g.DrawPath(brace, p);

                        // Right brace "}" — the same path mirrored around x = 8
                        using (var m = new Matrix(-1, 0, 0, 1, 16, 0))
                        {
                            p.Transform(m);
                            g.DrawPath(brace, p);
                        }
                    }

                    // Pixel-crisp "x": two 2-px-thick diagonals over rows 5..10
                    g.SmoothingMode = SmoothingMode.None;
                    for (int r = 0; r < 6; r++)
                    {
                        g.FillRectangle(cross, 5 + r, 5 + r, 1, 1);
                        g.FillRectangle(cross, 10 - r, 5 + r, 1, 1);
                        if (r < 5)
                        {
                            g.FillRectangle(cross, 6 + r, 5 + r, 1, 1);
                            g.FillRectangle(cross, 9 - r, 5 + r, 1, 1);
                        }
                    }
                }
            }
            return bmp;
        }

        /// <summary>
        /// A small outline tree — Dimension Navigator: a parent box at the top-left with
        /// two children connected by elbow lines, in the Variables icon's purple and orange.
        /// </summary>
        public static Bitmap CreateHierarchyIcon()
        {
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                using (var parent = new SolidBrush(Color.FromArgb(120, 70, 175)))
                using (var child  = new SolidBrush(Color.FromArgb(200, 90, 20)))
                using (var line   = new Pen(Color.FromArgb(110, 110, 110), 1f))
                {
                    // Elbow connectors from the parent down to each child
                    g.DrawLine(line, 3, 6, 3, 12);
                    g.DrawLine(line, 3, 8, 8, 8);
                    g.DrawLine(line, 3, 12, 8, 12);

                    g.FillRectangle(parent, 1, 1, 6, 5);      // parent
                    g.FillRectangle(child,  8, 6, 7, 4);      // child 1
                    g.FillRectangle(child,  8, 11, 7, 4);     // child 2
                }
            }
            return bmp;
        }

        /// <summary>
        /// Green play triangle with a blue cloud badge (bottom-right) — Execute Deployed
        /// Calculation button: "run what is in the cloud". The cloud gets a white halo so
        /// it reads as a separate shape where it overlaps the triangle.
        /// </summary>
        public static Bitmap CreateCloudPlayIcon()
        {
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode   = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // ── Play triangle ────────────────────────────────────────
                // Saturated enough that the dark-mode variant keeps its colour
                using (var green = new SolidBrush(Color.FromArgb(20, 160, 50)))
                    g.FillPolygon(green, new[]
                    {
                        new PointF(1.0f, 0.5f), new PointF(12.5f, 7.0f), new PointF(1.0f, 13.5f),
                    });

                // ── Cloud: small left puff, big middle puff, right puff, flat base ──
                void Cloud(Brush b, float grow)
                {
                    g.FillEllipse(b, 5.0f - grow, 10.6f - grow, 4.2f + 2 * grow, 4.2f + 2 * grow);  // left puff
                    g.FillEllipse(b, 7.4f - grow,  7.8f - grow, 5.2f + 2 * grow, 5.2f + 2 * grow);  // middle puff
                    g.FillEllipse(b, 11.0f - grow, 10.2f - grow, 4.6f + 2 * grow, 4.6f + 2 * grow); // right puff
                    g.FillRectangle(b, 7.1f - grow, 12.0f - grow, 6.2f + 2 * grow, 2.8f + 2 * grow); // flat base
                }
                using (var halo = new SolidBrush(Color.White))
                    Cloud(halo, 1.2f);
                using (var blue = new SolidBrush(Color.FromArgb(40, 120, 215)))
                    Cloud(blue, 0f);
            }
            return bmp;
        }

        /// <summary>
        /// Green circle with a white tick — Validate Groovy Script button. The green is
        /// saturated enough to keep its colour in the dark-mode variant.
        /// </summary>
        public static Bitmap CreateValidateIcon()
        {
            var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode   = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var green = new SolidBrush(Color.FromArgb(15, 160, 55)))
                    g.FillEllipse(green, 0.5f, 0.5f, 15f, 15f);

                using (var tick = new Pen(Color.White, 2.1f))
                {
                    tick.StartCap = tick.EndCap = LineCap.Round;
                    tick.LineJoin = LineJoin.Round;
                    g.DrawLines(tick, new[]
                    {
                        new PointF(4.3f, 8.3f), new PointF(6.9f, 10.8f), new PointF(11.7f, 5.5f),
                    });
                }
            }
            return bmp;
        }

        /// <summary>
        /// Play triangle icon — Execute Calculation button.
        /// Draws a right-pointing filled green triangle.
        /// </summary>
        public static Bitmap CreatePlayIcon(int size = 16)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var brush = new SolidBrush(Color.FromArgb(0, 150, 60)))
                {
                    int m = size / 5;
                    var pts = new[]
                    {
                        new Point(m,          m),
                        new Point(size - m,   size / 2),
                        new Point(m,          size - m),
                    };
                    g.FillPolygon(brush, pts);
                }
            }
            return bmp;
        }
    }
}
