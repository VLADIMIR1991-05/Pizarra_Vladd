// Pizarra: dibujar sobre la pantalla o en hojas independientes, y escribir con el teclado.
// C# 5 / WPF sobre .NET Framework 4.8 (incluido en Windows). Compilar con build.ps1.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pizarra
{
    enum Tool { Cursor, Pen, Highlighter, Eraser, StrokeEraser, Text, Sticky, Select, Shape, Hand, Laser, Spotlight, Magnifier, Eyedropper }
    enum PenKind { Boligrafo, Lapiz, Rotulador, Pincel, Caligrafia, Neon, Discontinuo, Punteado, Aerosol, Arcoiris, Desvanecer }
    enum ShapeKind { Line, Arrow, DoubleArrow, Rect, RoundRect, Ellipse, Triangle, RightTriangle, Diamond, Pentagon, Hexagon, Star, Axes }
    // Los valores se guardan en los cuadernos: añadir siempre al final.
    enum Paper { Blanca, Cuadriculada, Rayada, Puntos, Negra, Verde, Milimetrada, Pentagrama, Isometrica }
    enum ItemKind { Text, Sticky, Doc, Image }
    enum StrokeStyle { Plain = 0, Neon = 1, Dashed = 2, Dotted = 3, Spray = 4, Rainbow = 5 }
    enum GuideKind { Ruler, Protractor }

    // Texto, nota, documento o imagen colocados sobre la hoja.
    class Item
    {
        public ItemKind Kind;
        public double X, Y, W = double.NaN, H = double.NaN;
        public Color Bg = Colors.Transparent;
        public byte[] Data; // XAML del texto o PNG de la imagen
    }

    class Snap
    {
        public StrokeCollection Strokes = new StrokeCollection();
        public List<Item> Items = new List<Item>();
    }

    class Sheet
    {
        public StrokeCollection Strokes = new StrokeCollection();
        public List<Item> Items = new List<Item>();
        public Paper Paper = Paper.Blanca;
        public double Zoom = 1, PanX, PanY;
        public List<Snap> Undo = new List<Snap>();
        public List<Snap> Redo = new List<Snap>();
        public Snap Last = new Snap();
        public bool IsEmpty { get { return Strokes.Count == 0 && Items.Count == 0; } }
    }

    static class Native
    {
        public const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_HOTKEY = 0x0312, MOD_ALT = 1, MOD_CONTROL = 2, HOTKEY_ID = 0x5A1;
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, int flags);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        public const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    }

    class Win32Owner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; set; }
    }

    // ---- Trazos con estilo: el estilo viaja en los atributos, así sobrevive a copias y archivos ----
    class RichStroke : Stroke
    {
        public static readonly Guid StyleId = new Guid("6f2b9c50-1a3e-4c55-9a21-5d1c7e0b1101");
        public static readonly Guid FillId = new Guid("6f2b9c50-1a3e-4c55-9a21-5d1c7e0b1102");

        public RichStroke(StylusPointCollection p, DrawingAttributes da) : base(p, da) { }

        public static int Prop(DrawingAttributes da, Guid id)
        {
            return da.ContainsPropertyData(id) ? (int)da.GetPropertyData(id) : 0;
        }

        public static bool NeedsRich(DrawingAttributes da)
        {
            return Prop(da, StyleId) != 0 || Prop(da, FillId) != 0;
        }

        // Al borrar por partes o abrir un archivo, WPF devuelve trazos simples: se recuperan aquí.
        public static void Upgrade(StrokeCollection strokes)
        {
            foreach (var s in strokes.ToList())
            {
                if (s is RichStroke || !NeedsRich(s.DrawingAttributes)) continue;
                strokes.Replace(s, new StrokeCollection(new Stroke[] { new RichStroke(s.StylusPoints.Clone(), s.DrawingAttributes.Clone()) }));
            }
        }

        Geometry PathGeometry(bool closed)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure((Point)StylusPoints[0], closed, closed);
                c.PolyLineTo(StylusPoints.Skip(1).Select(p => (Point)p).ToList(), true, true);
            }
            g.Freeze();
            return g;
        }

        static Pen MakePen(Color c, double w)
        {
            return new Pen(new SolidColorBrush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        }

        protected override void DrawCore(DrawingContext dc, DrawingAttributes da)
        {
            var own = DrawingAttributes;
            var style = (StrokeStyle)Prop(own, StyleId);
            double w = Math.Max(0.5, da.Width);
            Color c = da.Color;
            if (Prop(own, FillId) != 0)
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(c.A * 0.35), c.R, c.G, c.B)), null, PathGeometry(true));
            switch (style)
            {
                case StrokeStyle.Neon:
                    var ng = PathGeometry(false);
                    dc.DrawGeometry(null, MakePen(Color.FromArgb((byte)(c.A * 0.18), c.R, c.G, c.B), w * 3.4), ng);
                    dc.DrawGeometry(null, MakePen(Color.FromArgb((byte)(c.A * 0.45), c.R, c.G, c.B), w * 1.9), ng);
                    dc.DrawGeometry(null, MakePen(Color.FromArgb(c.A, (byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2)), Math.Max(1, w * 0.7)), ng);
                    break;
                case StrokeStyle.Dashed:
                    var dp = MakePen(c, w);
                    dp.StartLineCap = dp.EndLineCap = PenLineCap.Flat;
                    dp.DashStyle = new DashStyle(new double[] { 3.2, 2.4 }, 0);
                    dc.DrawGeometry(null, dp, PathGeometry(false));
                    break;
                case StrokeStyle.Dotted:
                    var op = MakePen(c, w);
                    op.DashCap = PenLineCap.Round;
                    op.DashStyle = new DashStyle(new double[] { 0, 2.1 }, 0);
                    dc.DrawGeometry(null, op, PathGeometry(false));
                    break;
                case StrokeStyle.Spray:
                    var rnd = new Random(4711);
                    var brush = new SolidColorBrush(Color.FromArgb((byte)(c.A * 0.75), c.R, c.G, c.B));
                    brush.Freeze();
                    foreach (var p in StylusPoints)
                        for (int i = 0; i < 7; i++)
                        {
                            double a = rnd.NextDouble() * 2 * Math.PI, r = Math.Sqrt(rnd.NextDouble()) * w * 1.7, d = 0.5 + rnd.NextDouble() * 0.8;
                            dc.DrawEllipse(brush, null, new Point(p.X + r * Math.Cos(a), p.Y + r * Math.Sin(a)), d, d);
                        }
                    break;
                case StrokeStyle.Rainbow:
                    for (int i = 1; i < StylusPoints.Count; i++)
                    {
                        var hue = Hsv(i * 5 % 360, c.A);
                        dc.DrawLine(MakePen(hue, w), (Point)StylusPoints[i - 1], (Point)StylusPoints[i]);
                    }
                    break;
                default:
                    if (Prop(own, FillId) != 0) dc.DrawGeometry(null, MakePen(c, w), PathGeometry(false));
                    else base.DrawCore(dc, da);
                    break;
            }
        }

        public static Color Hsv(double h, byte alpha)
        {
            double x = 1 - Math.Abs(h / 60 % 2 - 1), r = 0, g = 0, b = 0;
            if (h < 60) { r = 1; g = x; }
            else if (h < 120) { r = x; g = 1; }
            else if (h < 180) { g = 1; b = x; }
            else if (h < 240) { g = x; b = 1; }
            else if (h < 300) { r = x; b = 1; }
            else { r = 1; b = x; }
            return Color.FromArgb(alpha, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
    }

    // ---- Tinta a forma: convierte trazos a mano en líneas, elipses, triángulos y rectángulos ----
    static class ShapeRecognizer
    {
        static double Dist(Point a, Point b) { return (a - b).Length; }

        static double DistToSegment(Point p, Point a, Point b)
        {
            Vector ab = b - a;
            double len2 = ab.LengthSquared;
            if (len2 < 1e-6) return Dist(p, a);
            double t = Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / len2));
            return Dist(p, a + ab * t);
        }

        static void Simplify(List<Point> pts, int first, int last, double eps, SortedSet<int> keep)
        {
            double max = 0; int idx = -1;
            for (int i = first + 1; i < last; i++)
            {
                double d = DistToSegment(pts[i], pts[first], pts[last]);
                if (d > max) { max = d; idx = i; }
            }
            if (idx >= 0 && max > eps)
            {
                keep.Add(idx);
                Simplify(pts, first, idx, eps, keep);
                Simplify(pts, idx, last, eps, keep);
            }
        }

        public static List<Point> Recognize(IEnumerable<Point> input)
        {
            var pts = input.ToList();
            if (pts.Count < 4) return null;
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
            if (diag < 25) return null;
            double len = 0;
            for (int i = 1; i < pts.Count; i++) len += Dist(pts[i - 1], pts[i]);
            Point a = pts[0], b = pts[pts.Count - 1];
            if (Dist(a, b) > 0.22 * len)
            {
                // Abierto: solo se acepta si es casi recto.
                return pts.Max(p => DistToSegment(p, a, b)) < 0.05 * Dist(a, b) ? new List<Point> { a, b } : null;
            }
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2, rx = Math.Max(1, (maxX - minX) / 2), ry = Math.Max(1, (maxY - minY) / 2);
            var radii = pts.Select(p => Math.Sqrt((p.X - cx) * (p.X - cx) / (rx * rx) + (p.Y - cy) * (p.Y - cy) / (ry * ry))).ToList();
            double mean = radii.Average(), dev = Math.Sqrt(radii.Sum(r => (r - mean) * (r - mean)) / radii.Count);
            if (dev < 0.085) return Shapes.Polygon(ShapeKind.Ellipse, new Point(minX, minY), new Point(maxX, maxY), 0);
            var keep = new SortedSet<int> { 0, pts.Count - 1 };
            Simplify(pts, 0, pts.Count - 1, 0.07 * diag, keep);
            var corners = keep.Select(i => pts[i]).ToList();
            // El punto final casi coincide con el inicial.
            corners.RemoveAt(corners.Count - 1);
            if (corners.Count == 3)
            {
                var t = new List<Point>(corners); t.Add(corners[0]);
                return t;
            }
            if (corners.Count == 4) return Shapes.Polygon(ShapeKind.Rect, new Point(minX, minY), new Point(maxX, maxY), 0);
            return null;
        }
    }

    static class Shapes
    {
        public static bool IsClosed(ShapeKind k)
        {
            return k != ShapeKind.Line && k != ShapeKind.Arrow && k != ShapeKind.DoubleArrow && k != ShapeKind.Axes;
        }

        static List<Point> Regular(Point a, Point b, int n, double startDeg)
        {
            double cx = (a.X + b.X) / 2, cy = (a.Y + b.Y) / 2, rx = Math.Abs(b.X - a.X) / 2, ry = Math.Abs(b.Y - a.Y) / 2;
            var list = new List<Point>();
            for (int i = 0; i <= n; i++)
            {
                double th = (startDeg + i * 360.0 / n) * Math.PI / 180;
                list.Add(new Point(cx + rx * Math.Cos(th), cy + ry * Math.Sin(th)));
            }
            return list;
        }

        static void Head(List<Point> l, Point from, Point tip, double size)
        {
            double ang = Math.Atan2(tip.Y - from.Y, tip.X - from.X), head = 12 + size * 2.5;
            l.Add(new Point(tip.X - head * Math.Cos(ang - 0.5), tip.Y - head * Math.Sin(ang - 0.5)));
            l.Add(tip);
            l.Add(new Point(tip.X - head * Math.Cos(ang + 0.5), tip.Y - head * Math.Sin(ang + 0.5)));
            l.Add(tip);
        }

        public static List<Point> Polygon(ShapeKind k, Point a, Point b, double size)
        {
            double l = Math.Min(a.X, b.X), r = Math.Max(a.X, b.X), t = Math.Min(a.Y, b.Y), d = Math.Max(a.Y, b.Y), mx = (l + r) / 2, my = (t + d) / 2;
            var p = new List<Point>();
            switch (k)
            {
                case ShapeKind.Line: p.Add(a); p.Add(b); break;
                case ShapeKind.Arrow: p.Add(a); p.Add(b); Head(p, a, b, size); break;
                case ShapeKind.DoubleArrow:
                    p.Add(b); p.Add(a); Head(p, b, a, size); p.Add(b); Head(p, a, b, size);
                    break;
                case ShapeKind.Rect:
                    p.Add(new Point(l, t)); p.Add(new Point(r, t)); p.Add(new Point(r, d)); p.Add(new Point(l, d)); p.Add(new Point(l, t));
                    break;
                case ShapeKind.RoundRect:
                    double c = Math.Min(Math.Min(r - l, d - t) * 0.25, 40);
                    foreach (var corner in new[] { new { X = r - c, Y = t + c, S = 270 }, new { X = r - c, Y = d - c, S = 0 }, new { X = l + c, Y = d - c, S = 90 }, new { X = l + c, Y = t + c, S = 180 } })
                        for (int i = 0; i <= 12; i++)
                        {
                            double th = (corner.S + i * 7.5) * Math.PI / 180;
                            p.Add(new Point(corner.X + c * Math.Cos(th), corner.Y + c * Math.Sin(th)));
                        }
                    p.Add(p[0]);
                    break;
                case ShapeKind.Ellipse: return Regular(a, b, 96, 0);
                case ShapeKind.Triangle:
                    p.Add(new Point(mx, t)); p.Add(new Point(r, d)); p.Add(new Point(l, d)); p.Add(new Point(mx, t));
                    break;
                case ShapeKind.RightTriangle:
                    p.Add(new Point(l, t)); p.Add(new Point(r, d)); p.Add(new Point(l, d)); p.Add(new Point(l, t));
                    break;
                case ShapeKind.Diamond:
                    p.Add(new Point(mx, t)); p.Add(new Point(r, my)); p.Add(new Point(mx, d)); p.Add(new Point(l, my)); p.Add(new Point(mx, t));
                    break;
                case ShapeKind.Pentagon: return Regular(a, b, 5, -90);
                case ShapeKind.Hexagon: return Regular(a, b, 6, 0);
                case ShapeKind.Star:
                    double rx = (r - l) / 2, ry = (d - t) / 2;
                    for (int i = 0; i <= 10; i++)
                    {
                        double f = i % 2 == 0 ? 1 : 0.42, th = (-90 + i * 36) * Math.PI / 180;
                        p.Add(new Point(mx + rx * f * Math.Cos(th), my + ry * f * Math.Sin(th)));
                    }
                    break;
                case ShapeKind.Axes:
                    p.Add(new Point(l, my)); p.Add(new Point(r, my)); Head(p, new Point(l, my), new Point(r, my), size);
                    p.Add(new Point(mx, my)); p.Add(new Point(mx, t)); Head(p, new Point(mx, d), new Point(mx, t), size);
                    p.Add(new Point(mx, d));
                    break;
            }
            return p;
        }

        public static string Icon(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Line: return "M3,15 L15,3";
                case ShapeKind.Arrow: return "M3,15 L15,3 M8,3 H15 V10";
                case ShapeKind.DoubleArrow: return "M3,15 L15,3 M9,3 H15 V9 M3,9 V15 H9";
                case ShapeKind.Rect: return "M2.5,4 H15.5 V14 H2.5 Z";
                case ShapeKind.RoundRect: return "M5.5,4 H12.5 A3,3 0 0 1 15.5,7 V11 A3,3 0 0 1 12.5,14 H5.5 A3,3 0 0 1 2.5,11 V7 A3,3 0 0 1 5.5,4 Z";
                case ShapeKind.Ellipse: return "M2.5,9 A6.5,6.5 0 1 1 15.5,9 A6.5,6.5 0 1 1 2.5,9 Z";
                case ShapeKind.Triangle: return "M9,3 L16,15 H2 Z";
                case ShapeKind.RightTriangle: return "M3,3 L15,15 H3 Z";
                case ShapeKind.Diamond: return "M9,2 L16,9 L9,16 L2,9 Z";
                case ShapeKind.Pentagon: return "M9,2 L16,7.5 L13.3,16 H4.7 L2,7.5 Z";
                case ShapeKind.Hexagon: return "M2,9 L5.5,3 H12.5 L16,9 L12.5,15 H5.5 Z";
                case ShapeKind.Star: return "M9,2 L11,7 L16,7.2 L12,10.5 L13.5,15.5 L9,12.5 L4.5,15.5 L6,10.5 L2,7.2 L7,7 Z";
                default: return "M2,9 H16 M13,6 L16,9 L13,12 M9,16 V2 M6,5 L9,2 L12,5";
            }
        }

        public static string Name(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Line: return "Línea (L)";
                case ShapeKind.Arrow: return "Flecha (A)";
                case ShapeKind.DoubleArrow: return "Flecha doble";
                case ShapeKind.Rect: return "Rectángulo (R)";
                case ShapeKind.RoundRect: return "Rectángulo redondeado";
                case ShapeKind.Ellipse: return "Elipse (O)";
                case ShapeKind.Triangle: return "Triángulo";
                case ShapeKind.RightTriangle: return "Triángulo rectángulo";
                case ShapeKind.Diamond: return "Rombo";
                case ShapeKind.Pentagon: return "Pentágono";
                case ShapeKind.Hexagon: return "Hexágono";
                case ShapeKind.Star: return "Estrella";
                default: return "Ejes de coordenadas";
            }
        }
    }

    // ---- Interfaz común ----
    static class Ui
    {
        static Style buttonStyle;
        public static readonly Color PanelColor = Color.FromRgb(0x20, 0x21, 0x24);

        public static Style ButtonStyle
        {
            get
            {
                if (buttonStyle == null)
                    buttonStyle = (Style)XamlReader.Parse(
@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Setter Property='Foreground' Value='#E8EAED'/>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Focusable' Value='False'/>
  <Setter Property='Width' Value='36'/>
  <Setter Property='Height' Value='36'/>
  <Setter Property='Margin' Value='1'/>
  <Setter Property='HorizontalContentAlignment' Value='Center'/>
  <Setter Property='FontFamily' Value='Segoe MDL2 Assets'/>
  <Setter Property='FontSize' Value='16'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Button'>
      <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='7'>
        <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#3C4043'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='#50545A'/></Trigger>
        <Trigger Property='Tag' Value='on'>
          <Setter TargetName='b' Property='Background' Value='#8AB4F8'/>
          <Setter Property='Foreground' Value='#14171C'/>
        </Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>");
                return buttonStyle;
            }
        }

        public static Button Glyph(string glyph, string tip, Action click)
        {
            var b = new Button { Style = ButtonStyle, Content = glyph, ToolTip = tip };
            b.Click += delegate { click(); };
            return b;
        }

        public static Button Label(string text, string tip, double width, Action click)
        {
            var b = new Button { Style = ButtonStyle, Content = text, ToolTip = tip, Width = width, FontFamily = new FontFamily("Segoe UI"), FontSize = 13 };
            b.Click += delegate { click(); };
            return b;
        }

        // Icono dibujado: usa el color del propio botón para seguir el estado activo.
        public static System.Windows.Shapes.Path IconPath(string geometry)
        {
            var p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometry),
                StrokeThickness = 1.8,
                Width = 18,
                Height = 18,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            p.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
            });
            return p;
        }

        public static Button Drawn(string geometry, string tip, Action click)
        {
            var b = new Button { Style = ButtonStyle, Content = IconPath(geometry), ToolTip = tip };
            b.Click += delegate { click(); };
            return b;
        }

        // Flechita junto a una herramienta que abre sus opciones.
        public static Button Chevron(string tip, Action click)
        {
            var b = new Button { Style = ButtonStyle, Content = "", ToolTip = tip, Width = 15, FontSize = 8, Margin = new Thickness(-1, 1, 2, 1) };
            b.Click += delegate { click(); };
            return b;
        }

        public static Border Separator()
        {
            return new Border { Width = 1, Height = 24, Margin = new Thickness(5, 0, 5, 0), Background = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68)) };
        }

        public static StackPanel Row() { return new StackPanel { Orientation = Orientation.Horizontal }; }

        public static TextBlock Caption(string text)
        {
            return new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xBD, 0xC1, 0xC6)), FontSize = 12, Margin = new Thickness(6, 6, 6, 3) };
        }

        // Ventana flotante sin foco: los textos no pierden el cursor al usarla.
        public static Popup Flyout(UIElement anchor, UIElement content, PlacementMode placement)
        {
            var p = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = placement,
                PlacementTarget = anchor,
                PopupAnimation = PopupAnimation.None,
                Child = new Border
                {
                    Background = new SolidColorBrush(PanelColor),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0x63, 0x68)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(5),
                    Child = content
                }
            };
            return p;
        }

        public static Button MenuItem(string text, bool on, Action click)
        {
            var b = new Button
            {
                Style = ButtonStyle,
                Content = new TextBlock { Text = text, Margin = new Thickness(8, 0, 12, 0) },
                Width = double.NaN,
                Height = 30,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = on ? "on" : null
            };
            b.Click += delegate { click(); };
            return b;
        }
    }

    // ---- Regla y transportador: se arrastran y giran con la rueda ----
    class MathGuide : FrameworkElement
    {
        public readonly GuideKind Kind;
        public double Angle, Tx = 240, Ty = 300;
        const double RulerW = 800, RulerH = 84, ProtR = 190;
        readonly RotateTransform rot = new RotateTransform();
        readonly TranslateTransform move = new TranslateTransform();
        Point grab;

        public MathGuide(GuideKind k)
        {
            Kind = k;
            Width = k == GuideKind.Ruler ? RulerW : ProtR * 2;
            Height = k == GuideKind.Ruler ? RulerH : ProtR;
            var g = new TransformGroup();
            g.Children.Add(rot);
            g.Children.Add(move);
            RenderTransform = g;
            Cursor = Cursors.SizeAll;
            Sync();
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { grab = e.GetPosition(Parent as IInputElement); CaptureMouse(); e.Handled = true; };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!IsMouseCaptured) return;
                var p = e.GetPosition(Parent as IInputElement);
                Tx += p.X - grab.X; Ty += p.Y - grab.Y; grab = p;
                Sync();
            };
            MouseLeftButtonUp += delegate { ReleaseMouseCapture(); };
            MouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 1 : 5;
                Angle = (Angle + (e.Delta > 0 ? step : -step) + 360) % 360;
                Sync();
                InvalidateVisual();
                e.Handled = true;
            };
        }

        void Sync()
        {
            rot.Angle = Angle;
            rot.CenterX = Width / 2;
            rot.CenterY = Height / 2;
            move.X = Tx;
            move.Y = Ty;
        }

        // Borde de dibujo de la regla, en coordenadas de la ventana.
        public void Edge(out Point a, out Point b)
        {
            a = RenderTransform.Transform(new Point(0, 0));
            b = RenderTransform.Transform(new Point(RulerW, 0));
        }

        FormattedText Label(string s, double size)
        {
            return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var fill = new SolidColorBrush(Color.FromArgb(205, 0xFF, 0xF3, 0xC4));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 1);
            if (Kind == GuideKind.Ruler)
            {
                dc.DrawRectangle(fill, pen, new Rect(0, 0, RulerW, RulerH));
                double cm = 96 / 2.54;
                for (int i = 0; i * cm / 10 <= RulerW; i++)
                {
                    double x = i * cm / 10, h = i % 10 == 0 ? 22 : i % 5 == 0 ? 15 : 8;
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
                    if (i % 10 == 0 && i > 0) dc.DrawText(Label((i / 10).ToString(), 11), new Point(x - 4, 23));
                }
                dc.DrawText(Label(Math.Round(Angle) + "°", 14), new Point(RulerW / 2 - 12, 52));
            }
            else
            {
                Point c = new Point(ProtR, ProtR);
                var g = Geometry.Parse("M0," + ProtR + " A" + ProtR + "," + ProtR + " 0 0 1 " + ProtR * 2 + "," + ProtR + " Z");
                dc.DrawGeometry(fill, pen, g);
                for (int d = 0; d <= 180; d += 1)
                {
                    double th = d * Math.PI / 180, inner = d % 10 == 0 ? ProtR - 20 : d % 5 == 0 ? ProtR - 13 : ProtR - 7;
                    dc.DrawLine(pen, new Point(c.X - ProtR * Math.Cos(th), c.Y - ProtR * Math.Sin(th)), new Point(c.X - inner * Math.Cos(th), c.Y - inner * Math.Sin(th)));
                    if (d % 10 == 0)
                    {
                        var t = Label(d.ToString(), 10);
                        double rt = ProtR - 32;
                        dc.DrawText(t, new Point(c.X - rt * Math.Cos(th) - t.Width / 2, c.Y - rt * Math.Sin(th) - t.Height / 2));
                    }
                }
                dc.DrawEllipse(Brushes.Black, null, c, 2.5, 2.5);
                dc.DrawText(Label(Math.Round(Angle) + "°", 13), new Point(c.X - 12, c.Y - 48));
            }
        }
    }

    // ---- Exportar a PDF sin dependencias: cada hoja es una imagen JPEG ----
    static class PdfWriter
    {
        public static void Write(string path, List<KeyValuePair<byte[], Size>> pages)
        {
            using (var f = File.Create(path))
            {
                var offsets = new List<long>();
                Action<string> w = s => { var b = Encoding.ASCII.GetBytes(s); f.Write(b, 0, b.Length); };
                Action<int> start = n => { while (offsets.Count < n) offsets.Add(0); offsets[n - 1] = f.Position; w(n + " 0 obj\n"); };
                w("%PDF-1.4\n");
                start(1); w("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                start(2);
                w("<< /Type /Pages /Count " + pages.Count + " /Kids [" + string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => (3 + i * 3) + " 0 R")) + "] >>\nendobj\n");
                for (int i = 0; i < pages.Count; i++)
                {
                    int pg = 3 + i * 3;
                    byte[] jpg = pages[i].Key;
                    int pw = (int)pages[i].Value.Width, ph = (int)pages[i].Value.Height;
                    string mw = (pw * 0.75).ToString(CultureInfo.InvariantCulture), mh = (ph * 0.75).ToString(CultureInfo.InvariantCulture);
                    start(pg);
                    w("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + mw + " " + mh + "] /Resources << /XObject << /I " + (pg + 2) + " 0 R >> >> /Contents " + (pg + 1) + " 0 R >>\nendobj\n");
                    string content = "q " + mw + " 0 0 " + mh + " 0 0 cm /I Do Q";
                    start(pg + 1);
                    w("<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\nendobj\n");
                    start(pg + 2);
                    w("<< /Type /XObject /Subtype /Image /Width " + pw + " /Height " + ph + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpg.Length + " >>\nstream\n");
                    f.Write(jpg, 0, jpg.Length);
                    w("\nendstream\nendobj\n");
                }
                long xref = f.Position;
                w("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var o in offsets) w(o.ToString("D10") + " 00000 n \n");
                w("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }
    }

    // ==================================================================================
    class CanvasWindow : Window
    {
        public static bool AutosaveEnabled = true;
        internal const double CanvasW = 16000, CanvasH = 12000;

        internal readonly InkCanvas Ink;
        internal readonly Grid Host;
        internal readonly ToolbarWindow Bar;
        internal readonly List<Sheet> Sheets = new List<Sheet> { new Sheet() };
        internal readonly Sheet ScreenSheet = new Sheet();
        internal int SheetIndex;
        internal Sheet Cur;
        internal bool ScreenMode, WriteMode;
        internal Tool Tool = Tool.Pen;
        internal PenKind Pen = PenKind.Boligrafo;
        internal ShapeKind ShapeType = ShapeKind.Rect;
        internal bool FillShapes, RecognizeShapes;
        internal Color InkColor = Color.FromRgb(0x20, 0x20, 0x20);
        internal Color StickyColor = Color.FromRgb(0xFF, 0xF1, 0x76);
        internal double InkOpacity = 1;
        internal double PenSize = 3, HighlightSize = 24, EraserSize = 30, FontPt = 22;
        internal string FontName = "Segoe UI";
        internal string FilePath;
        internal bool Modified;
        internal int MonitorIndex;
        internal MathGuide Ruler, Protractor;

        readonly Grid root;
        readonly Canvas inkHost, guideLayer, effectLayer;
        readonly StackPanel pill;
        readonly TextBlock pageLabel, zoomLabel;
        readonly Button prevBtn, nextBtn, paperBtn, sheetMenuBtn;
        readonly System.Windows.Shapes.Path spot;
        readonly Ellipse lens;
        readonly ImageBrush lensBrush;
        readonly DispatcherTimer fadeTimer, autosaveTimer;
        readonly List<Fading> fading = new List<Fading>();
        readonly string autosavePath;

        bool dirty, loading, suspend, shaping, panning;
        Tool lastDrawTool = Tool.Pen;
        Point shapeStart, panStart;
        double panX0, panY0;
        Stroke shapeStroke;
        IntPtr hwnd;
        RichTextBox activeText;
        System.Drawing.Bitmap shot;
        double spotRadius = 150, lensRadius = 130, lensZoom = 2;

        class Fading { public Stroke S; public StrokeCollection Owner; public DateTime Start; public double Life; public byte Alpha; }

        public CanvasWindow()
        {
            Title = "Pizarra";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            Width = 800; Height = 600;
            WindowStartupLocation = WindowStartupLocation.Manual;
            autosavePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pizarra", "autoguardado.pizarra");

            Ink = new InkCanvas { Focusable = true, FocusVisualStyle = null, Width = CanvasW, Height = CanvasH, Background = Brushes.Transparent };
            inkHost = new Canvas();
            inkHost.Children.Add(Ink);
            Host = new Grid { ClipToBounds = true };
            Host.Children.Add(inkHost);

            guideLayer = new Canvas();
            effectLayer = new Canvas { IsHitTestVisible = false };
            spot = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(175, 0, 0, 0)), Visibility = Visibility.Collapsed };
            lensBrush = new ImageBrush { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            lens = new Ellipse { Fill = lensBrush, Stroke = new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xF8)), StrokeThickness = 3, Visibility = Visibility.Collapsed };
            effectLayer.Children.Add(spot);
            effectLayer.Children.Add(lens);

            prevBtn = Ui.Glyph("", "Hoja anterior (RePág)", delegate { GoToSheet(SheetIndex - 1); });
            nextBtn = Ui.Glyph("", "Hoja siguiente (AvPág)", delegate { GoToSheet(SheetIndex + 1); });
            pageLabel = new TextBlock { Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, MinWidth = 62, TextAlignment = TextAlignment.Center };
            zoomLabel = new TextBlock { Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center };
            paperBtn = Ui.Glyph("", "Tipo de hoja", ShowPaperMenu);
            sheetMenuBtn = Ui.Glyph("", "Más opciones de hoja: duplicar, mover, plantillas", ShowSheetMenu);
            pill = Ui.Row();
            pill.Children.Add(prevBtn);
            pill.Children.Add(pageLabel);
            pill.Children.Add(nextBtn);
            pill.Children.Add(Ui.Separator());
            pill.Children.Add(Ui.Glyph("", "Hoja nueva (Ctrl+N)", AddSheet));
            pill.Children.Add(Ui.Glyph("", "Eliminar esta hoja", DeleteSheet));
            pill.Children.Add(paperBtn);
            pill.Children.Add(sheetMenuBtn);
            pill.Children.Add(Ui.Separator());
            pill.Children.Add(Ui.Glyph("", "Alejar (Ctrl+rueda)", delegate { ZoomBy(1 / 1.25); }));
            pill.Children.Add(zoomLabel);
            pill.Children.Add(Ui.Glyph("", "Acercar (Ctrl+rueda)", delegate { ZoomBy(1.25); }));
            pill.Children.Add(Ui.Glyph("", "Volver al inicio de la hoja, 100 %", ResetView));

            root = new Grid();
            root.Children.Add(Host);
            root.Children.Add(guideLayer);
            root.Children.Add(effectLayer);
            root.Children.Add(new Border
            {
                Child = pill,
                Background = new SolidColorBrush(Ui.PanelColor),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            });
            guideLayer.IsHitTestVisible = true;
            guideLayer.Background = null;
            Content = root;

            Ink.StrokeCollected += delegate(object s, InkCanvasStrokeCollectedEventArgs e) { ProcessNewStroke(e.Stroke); };
            Ink.SelectionMoved += delegate { dirty = true; Commit(); };
            Ink.SelectionResized += delegate { dirty = true; Commit(); };
            Ink.PreviewMouseLeftButtonDown += OnInkDown;
            Ink.PreviewMouseMove += OnInkMove;
            Ink.PreviewMouseLeftButtonUp += OnInkUp;
            Ink.PreviewMouseDown += OnInkMiddleDown;
            Ink.PreviewMouseUp += OnInkMiddleUp;
            Ink.PreviewStylusUp += delegate { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Commit)); };
            PreviewMouseWheel += OnWheel;
            PreviewKeyDown += OnKey;
            Closing += OnClosing;

            Cur = Sheets[0];
            SetStrokes(Cur.Strokes);
            Bar = new ToolbarWindow(this);

            fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            fadeTimer.Tick += delegate { TickFading(); };
            autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            autosaveTimer.Tick += delegate { Autosave(); };

            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd).AddHook(WndProc);
                Native.RegisterHotKey(hwnd, Native.HOTKEY_ID, Native.MOD_CONTROL | Native.MOD_ALT, KeyInterop.VirtualKeyFromKey(Key.D));
                PlaceOnMonitor();
            };
            Closed += delegate
            {
                Native.UnregisterHotKey(hwnd, Native.HOTKEY_ID);
                fadeTimer.Stop();
                autosaveTimer.Stop();
                DropShot();
            };
            Loaded += delegate
            {
                Bar.Owner = this;
                Bar.Show();
                UpdateCanvasLook();
                ApplyTool();
                PlaceToolbar();
                Activate();
                Ink.Focus();
                if (AutosaveEnabled)
                {
                    OfferRecovery();
                    autosaveTimer.Start();
                }
            };
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && w.ToInt32() == Native.HOTKEY_ID)
            {
                ToggleHotkey();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // Ctrl+Alt+D: alterna entre dibujar y usar el equipo con normalidad.
        internal void ToggleHotkey()
        {
            if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Activate(); return; }
            if (!ScreenMode) { WindowState = WindowState.Minimized; return; }
            SetTool(Tool == Tool.Cursor ? lastDrawTool : Tool.Cursor);
        }

        // ---- Monitores ----
        internal int MonitorCount { get { return System.Windows.Forms.Screen.AllScreens.Length; } }

        void PlaceOnMonitor()
        {
            var all = System.Windows.Forms.Screen.AllScreens;
            if (MonitorIndex >= all.Length) MonitorIndex = 0;
            var b = all[MonitorIndex].Bounds;
            Native.SetWindowPos(hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height, 0x4 | 0x10);
        }

        internal void NextMonitor()
        {
            MonitorIndex++;
            PlaceOnMonitor();
            UpdateLayout();
            PlaceToolbar();
        }

        internal void PlaceToolbar()
        {
            Bar.UpdateLayout();
            Bar.Left = Left + Math.Max(0, (ActualWidth - Bar.ActualWidth) / 2);
            Bar.Top = Top + 4;
        }

        // ---- Herramientas ----
        internal void SetTool(Tool t)
        {
            if (t == Tool.Cursor && !ScreenMode) t = Tool.Pen;
            FinishText();
            Commit();
            Tool = t;
            WriteMode = false;
            if (t == Tool.Pen || t == Tool.Highlighter || t == Tool.Eraser || t == Tool.StrokeEraser || t == Tool.Text || t == Tool.Sticky || t == Tool.Select || t == Tool.Shape || t == Tool.Hand || t == Tool.Laser)
                lastDrawTool = t;
            DropShot();
            if (t == Tool.Magnifier || t == Tool.Eyedropper) TakeShot();
            ApplyTool();
            if (t != Tool.Cursor) { Activate(); Ink.Focus(); }
        }

        internal void SetPen(PenKind k)
        {
            Pen = k;
            SetTool(Tool.Pen);
        }

        internal void SetShape(ShapeKind k)
        {
            ShapeType = k;
            SetTool(Tool.Shape);
        }

        static StrokeStyle StyleOf(PenKind k)
        {
            switch (k)
            {
                case PenKind.Neon: return StrokeStyle.Neon;
                case PenKind.Discontinuo: return StrokeStyle.Dashed;
                case PenKind.Punteado: return StrokeStyle.Dotted;
                case PenKind.Aerosol: return StrokeStyle.Spray;
                case PenKind.Arcoiris: return StrokeStyle.Rainbow;
                default: return StrokeStyle.Plain;
            }
        }

        Color WithOpacity(double extra)
        {
            return Color.FromArgb((byte)(255 * InkOpacity * extra), InkColor.R, InkColor.G, InkColor.B);
        }

        internal DrawingAttributes PenAttributes()
        {
            var da = new DrawingAttributes { Color = WithOpacity(1), FitToCurve = true, Width = PenSize, Height = PenSize };
            switch (Pen)
            {
                case PenKind.Lapiz:
                    da.Color = WithOpacity(0.7);
                    da.Width = da.Height = Math.Max(1, PenSize * 0.8);
                    break;
                case PenKind.Rotulador:
                    da.Width = da.Height = PenSize * 2.2;
                    da.IgnorePressure = true;
                    break;
                case PenKind.Pincel:
                    da.Width = da.Height = PenSize * 3;
                    break;
                case PenKind.Caligrafia:
                    da.StylusTip = StylusTip.Rectangle;
                    da.Width = Math.Max(1, PenSize * 0.4);
                    da.Height = PenSize * 2.6;
                    da.StylusTipTransform = new RotateTransform(35).Value;
                    da.IgnorePressure = true;
                    break;
                case PenKind.Boligrafo:
                case PenKind.Desvanecer:
                    break;
                default:
                    da.IgnorePressure = true;
                    da.FitToCurve = false;
                    da.AddPropertyData(RichStroke.StyleId, (int)StyleOf(Pen));
                    break;
            }
            return da;
        }

        internal void ApplyTool()
        {
            SetClickThrough(Tool == Tool.Cursor);
            Ink.EditingMode = InkCanvasEditingMode.None;
            if (Tool == Tool.Highlighter)
            {
                Ink.DefaultDrawingAttributes = new DrawingAttributes
                {
                    Color = InkColor,
                    IsHighlighter = true,
                    StylusTip = StylusTip.Rectangle,
                    Height = HighlightSize,
                    Width = Math.Max(2, HighlightSize / 3),
                    IgnorePressure = true,
                    FitToCurve = true
                };
            }
            else if (Tool == Tool.Laser)
            {
                var da = new DrawingAttributes { Color = Color.FromRgb(0xFF, 0x1E, 0x2D), Width = 5, Height = 5, IgnorePressure = true };
                da.AddPropertyData(RichStroke.StyleId, (int)StrokeStyle.Neon);
                Ink.DefaultDrawingAttributes = da;
            }
            else Ink.DefaultDrawingAttributes = PenAttributes();
            Ink.UseCustomCursor = false;
            switch (Tool)
            {
                case Tool.Pen:
                case Tool.Highlighter:
                case Tool.Laser:
                    Ink.EditingMode = InkCanvasEditingMode.Ink;
                    break;
                case Tool.Eraser:
                    Ink.EraserShape = new EllipseStylusShape(EraserSize, EraserSize);
                    Ink.EditingMode = InkCanvasEditingMode.EraseByPoint;
                    break;
                case Tool.StrokeEraser:
                    Ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
                    break;
                case Tool.Select:
                    Ink.EditingMode = InkCanvasEditingMode.Select;
                    break;
                case Tool.Text:
                case Tool.Sticky:
                    Ink.UseCustomCursor = true;
                    Ink.Cursor = Cursors.IBeam;
                    break;
                case Tool.Hand:
                    Ink.UseCustomCursor = true;
                    Ink.Cursor = Cursors.SizeAll;
                    break;
                case Tool.Spotlight:
                case Tool.Magnifier:
                    Ink.UseCustomCursor = true;
                    Ink.Cursor = Cursors.None;
                    break;
                case Tool.Cursor:
                    break;
                default:
                    Ink.UseCustomCursor = true;
                    Ink.Cursor = Cursors.Cross;
                    break;
            }
            bool editable = Tool == Tool.Text || Tool == Tool.Sticky;
            foreach (var rt in Ink.Children.OfType<RichTextBox>()) rt.IsHitTestVisible = editable;
            spot.Visibility = Tool == Tool.Spotlight ? Visibility.Visible : Visibility.Collapsed;
            lens.Visibility = Tool == Tool.Magnifier ? Visibility.Visible : Visibility.Collapsed;
            UpdateEffects(Mouse.GetPosition(root));
            Bar.Refresh();
        }

        void SetClickThrough(bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            int s = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            s = on ? (s | Native.WS_EX_TRANSPARENT) : (s & ~Native.WS_EX_TRANSPARENT);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, s);
        }

        internal bool IsClickThrough
        {
            get { return (Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0; }
        }

        internal void SetColor(Color c)
        {
            InkColor = c;
            if (activeText != null)
            {
                activeText.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(c));
                dirty = true;
            }
            ApplyTool();
        }

        internal double CurrentSize
        {
            get
            {
                switch (Tool)
                {
                    case Tool.Highlighter: return HighlightSize;
                    case Tool.Eraser: return EraserSize;
                    case Tool.Text:
                    case Tool.Sticky: return FontPt;
                    default: return PenSize;
                }
            }
            set
            {
                switch (Tool)
                {
                    case Tool.Highlighter: HighlightSize = value; break;
                    case Tool.Eraser: EraserSize = value; break;
                    case Tool.Text:
                    case Tool.Sticky:
                        FontPt = value;
                        if (activeText != null)
                        {
                            activeText.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, value);
                            dirty = true;
                        }
                        break;
                    default: PenSize = value; break;
                }
                ApplyTool();
            }
        }

        // ---- Formato del texto: se aplica a la selección o a lo que se escriba a continuación ----
        internal void Format(RoutedUICommand cmd)
        {
            if (activeText == null) return;
            cmd.Execute(null, activeText);
            dirty = true;
        }

        internal void ToggleStrike()
        {
            if (activeText == null) return;
            var sel = activeText.Selection;
            var cur = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool has = cur != null && cur.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, has ? new TextDecorationCollection() : TextDecorations.Strikethrough);
            dirty = true;
        }

        internal void SetFont(string name)
        {
            FontName = name;
            if (activeText != null)
            {
                activeText.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(name));
                dirty = true;
            }
            Bar.Refresh();
        }

        internal void SetTextHighlight(Color? c)
        {
            if (activeText == null) return;
            activeText.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, c.HasValue ? new SolidColorBrush(c.Value) : null);
            dirty = true;
        }

        // ---- Modo pantalla / hojas / escritura ----
        internal void SetMode(bool screen)
        {
            FinishText();
            Commit();
            StoreCurrent();
            EnterMode(screen);
        }

        void EnterMode(bool screen)
        {
            ScreenMode = screen;
            WriteMode = false;
            Topmost = screen;
            Bar.Topmost = screen;
            if (!screen && Tool == Tool.Cursor) Tool = lastDrawTool;
            LoadSheet(screen ? ScreenSheet : Sheets[SheetIndex]);
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            UpdateCanvasLook();
            ApplyTool();
            Activate();
            Ink.Focus();
        }

        // Modo escritura: una página de texto lista para teclear, como un procesador de textos.
        internal RichTextBox EnterWriteMode()
        {
            if (ScreenMode) SetMode(false);
            SetTool(Tool.Text);
            WriteMode = true;
            var doc = Ink.Children.OfType<RichTextBox>().FirstOrDefault(r => ((Item)r.Tag).Kind == ItemKind.Doc);
            if (doc == null)
            {
                double w = Math.Min(900, Math.Max(400, Host.ActualWidth - 80));
                Point p = ViewToInk(new Point((Host.ActualWidth - w) / 2, 110));
                doc = (RichTextBox)AddElement(new Item { Kind = ItemKind.Doc, X = p.X, Y = p.Y, W = w });
                doc.UpdateLayout();
            }
            doc.Focus();
            Keyboard.Focus(doc);
            doc.CaretPosition = doc.Document.ContentEnd;
            Bar.Refresh();
            return doc;
        }

        void UpdateCanvasLook()
        {
            // Alfa 1: invisible, pero sigue recibiendo el ratón para dibujar sobre otras ventanas.
            Ink.Background = ScreenMode ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
            ApplyView();
            ((Border)pill.Parent).Visibility = ScreenMode ? Visibility.Collapsed : Visibility.Visible;
            pageLabel.Text = "Hoja " + (SheetIndex + 1) + "/" + Sheets.Count;
            prevBtn.IsEnabled = SheetIndex > 0;
            nextBtn.IsEnabled = SheetIndex < Sheets.Count - 1;
            string name = FilePath == null ? "" : " - " + System.IO.Path.GetFileNameWithoutExtension(FilePath);
            Title = "Pizarra" + name;
            if (Bar != null) Bar.Refresh();
        }

        internal static bool IsDark(Paper p) { return p == Paper.Negra || p == Paper.Verde; }

        internal static Brush PaperBrush(Paper p)
        {
            Color bg = p == Paper.Negra ? Color.FromRgb(0x1F, 0x24, 0x28) : p == Paper.Verde ? Color.FromRgb(0x1E, 0x4D, 0x3B) : Colors.White;
            if (p == Paper.Blanca || p == Paper.Negra || p == Paper.Verde) return new SolidColorBrush(bg);
            double w = 30, h = 30;
            if (p == Paper.Rayada) w = h = 38;
            if (p == Paper.Milimetrada) w = h = 50;
            if (p == Paper.Pentagrama) { w = 40; h = 150; }
            if (p == Paper.Isometrica) { w = 34.64; h = 40; }
            var line = new Pen(new SolidColorBrush(Color.FromRgb(0xC5, 0xD3, 0xE6)), 1);
            var fine = new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0xEA, 0xF3)), 0.6);
            var dark = new Pen(new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), 1);
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null, new RectangleGeometry(new Rect(0, 0, w, h))));
            var inv = CultureInfo.InvariantCulture;
            switch (p)
            {
                case Paper.Cuadriculada:
                    group.Children.Add(new GeometryDrawing(null, line, Geometry.Parse("M0,0 H30 M0,0 V30")));
                    break;
                case Paper.Rayada:
                    group.Children.Add(new GeometryDrawing(null, line, Geometry.Parse("M0,37 H38")));
                    break;
                case Paper.Puntos:
                    group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC6)), null, new EllipseGeometry(new Point(15, 15), 1.3, 1.3)));
                    break;
                case Paper.Milimetrada:
                    var sb = new StringBuilder();
                    for (int i = 1; i < 10; i++) sb.Append("M" + i * 5 + ",0 V50 M0," + i * 5 + " H50 ");
                    group.Children.Add(new GeometryDrawing(null, fine, Geometry.Parse(sb.ToString())));
                    group.Children.Add(new GeometryDrawing(null, line, Geometry.Parse("M0,0 H50 M0,0 V50")));
                    break;
                case Paper.Pentagrama:
                    group.Children.Add(new GeometryDrawing(null, dark, Geometry.Parse("M0,40 H40 M0,52 H40 M0,64 H40 M0,76 H40 M0,88 H40")));
                    break;
                case Paper.Isometrica:
                    group.Children.Add(new GeometryDrawing(null, line, Geometry.Parse(string.Format(inv, "M0,0 V{1} M{0},0 V{1} M0,0 L{0},20 M0,20 L{0},40 M0,20 L{0},0 M0,40 L{0},20", w, h))));
                    break;
            }
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, w, h),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, w, h),
                ViewportUnits = BrushMappingMode.Absolute
            };
            return brush;
        }

        internal void SetPaper(Paper p)
        {
            Cur.Paper = p;
            Modified = true;
            // Evita tinta invisible al cambiar entre hoja clara y oscura.
            if (IsDark(p) && InkColor == Color.FromRgb(0x20, 0x20, 0x20)) InkColor = Colors.White;
            else if (!IsDark(p) && InkColor == Colors.White) InkColor = Color.FromRgb(0x20, 0x20, 0x20);
            UpdateCanvasLook();
            ApplyTool();
        }

        void ShowPaperMenu()
        {
            var panel = new StackPanel();
            Popup pop = null;
            string[] names = { "Blanca", "Cuadriculada", "Rayada", "Puntos", "Negra", "Verde de pizarra", "Milimetrada", "Pentagrama", "Isométrica" };
            foreach (Paper p in Enum.GetValues(typeof(Paper)))
            {
                var pp = p;
                panel.Children.Add(Ui.MenuItem(names[(int)p], Cur.Paper == p, delegate { pop.IsOpen = false; SetPaper(pp); }));
            }
            pop = Ui.Flyout(paperBtn, panel, PlacementMode.Top);
            pop.IsOpen = true;
        }

        void ShowSheetMenu()
        {
            var panel = new StackPanel();
            Popup pop = null;
            Action<string, Action> add = (t, a) => panel.Children.Add(Ui.MenuItem(t, false, delegate { pop.IsOpen = false; a(); }));
            add("Duplicar hoja", DuplicateSheet);
            add("Mover hoja a la izquierda", delegate { MoveSheet(-1); });
            add("Mover hoja a la derecha", delegate { MoveSheet(1); });
            panel.Children.Add(Ui.Caption("Plantillas (hoja nueva)"));
            add("Kanban: Pendiente / En curso / Hecho", delegate { ApplyTemplate("kanban"); });
            add("DAFO", delegate { ApplyTemplate("dafo"); });
            add("Notas Cornell", delegate { ApplyTemplate("cornell"); });
            add("Plan semanal", delegate { ApplyTemplate("semana"); });
            pop = Ui.Flyout(sheetMenuBtn, panel, PlacementMode.Top);
            pop.IsOpen = true;
        }

        // ---- Vista: zoom y desplazamiento en una hoja muy amplia ----
        void ApplyView()
        {
            Matrix m = new Matrix(Cur.Zoom, 0, 0, Cur.Zoom, Cur.PanX, Cur.PanY);
            Ink.RenderTransform = new MatrixTransform(m);
            Brush paper = ScreenMode ? null : PaperBrush(Cur.Paper);
            if (paper is DrawingBrush) paper.Transform = new MatrixTransform(m);
            Host.Background = paper;
            zoomLabel.Text = Math.Round(Cur.Zoom * 100) + " %";
        }

        internal Point ViewToInk(Point v)
        {
            return new Point((v.X - Cur.PanX) / Cur.Zoom, (v.Y - Cur.PanY) / Cur.Zoom);
        }

        internal void ZoomAt(Point view, double factor)
        {
            if (ScreenMode) return;
            double z = Math.Max(0.25, Math.Min(4, Cur.Zoom * factor));
            Point p = ViewToInk(view);
            Cur.Zoom = z;
            Cur.PanX = view.X - p.X * z;
            Cur.PanY = view.Y - p.Y * z;
            ClampPan();
            ApplyView();
        }

        internal void ZoomBy(double f) { ZoomAt(new Point(Host.ActualWidth / 2, Host.ActualHeight / 2), f); }

        internal void ResetView()
        {
            Cur.Zoom = 1; Cur.PanX = 0; Cur.PanY = 0;
            ApplyView();
        }

        internal void PanBy(double dx, double dy)
        {
            if (ScreenMode) return;
            Cur.PanX += dx; Cur.PanY += dy;
            ClampPan();
            ApplyView();
        }

        void ClampPan()
        {
            Cur.PanX = Math.Min(0, Math.Max(Cur.PanX, Host.ActualWidth - CanvasW * Cur.Zoom));
            Cur.PanY = Math.Min(0, Math.Max(Cur.PanY, Host.ActualHeight - CanvasH * Cur.Zoom));
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.OriginalSource is MathGuide) return;
            if (Tool == Tool.Spotlight)
            {
                spotRadius = Math.Max(50, Math.Min(500, spotRadius + (e.Delta > 0 ? 15 : -15)));
                UpdateEffects(e.GetPosition(root));
                e.Handled = true;
            }
            else if (Tool == Tool.Magnifier)
            {
                lensZoom = Math.Max(1.25, Math.Min(6, lensZoom + (e.Delta > 0 ? 0.25 : -0.25)));
                UpdateEffects(e.GetPosition(root));
                e.Handled = true;
            }
            else if (ScreenMode) return;
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ZoomAt(e.GetPosition(Host), e.Delta > 0 ? 1.15 : 1 / 1.15);
                e.Handled = true;
            }
            else
            {
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                PanBy(shift ? e.Delta / 2.0 : 0, shift ? 0 : e.Delta / 2.0);
                e.Handled = true;
            }
        }

        // ---- Hojas ----
        internal void AddSheet()
        {
            if (ScreenMode) return;
            bool write = WriteMode;
            FinishText();
            Commit();
            StoreCurrent();
            Sheets.Insert(SheetIndex + 1, new Sheet { Paper = Cur.Paper });
            SheetIndex++;
            Modified = true;
            LoadSheet(Sheets[SheetIndex]);
            UpdateCanvasLook();
            if (write) EnterWriteMode();
        }

        internal void GoToSheet(int i)
        {
            if (ScreenMode || i < 0 || i >= Sheets.Count || i == SheetIndex) return;
            FinishText();
            Commit();
            StoreCurrent();
            SheetIndex = i;
            LoadSheet(Sheets[i]);
            UpdateCanvasLook();
        }

        void DeleteSheet()
        {
            FinishText();
            Commit();
            StoreCurrent();
            if (!Cur.IsEmpty && MessageBox.Show(this, "¿Eliminar esta hoja? No se puede deshacer.", "Pizarra", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            RemoveCurrentSheet();
        }

        internal void RemoveCurrentSheet()
        {
            Sheets.RemoveAt(SheetIndex);
            if (Sheets.Count == 0) Sheets.Add(new Sheet());
            if (SheetIndex >= Sheets.Count) SheetIndex = Sheets.Count - 1;
            Modified = true;
            LoadSheet(Sheets[SheetIndex]);
            UpdateCanvasLook();
        }

        internal void DuplicateSheet()
        {
            if (ScreenMode) return;
            FinishText();
            Commit();
            StoreCurrent();
            var copy = new Sheet { Paper = Cur.Paper, Strokes = Cur.Strokes.Clone(), Items = new List<Item>(Cur.Items), Zoom = Cur.Zoom, PanX = Cur.PanX, PanY = Cur.PanY };
            copy.Last = new Snap { Strokes = copy.Strokes.Clone(), Items = copy.Items };
            Sheets.Insert(SheetIndex + 1, copy);
            SheetIndex++;
            Modified = true;
            LoadSheet(copy);
            UpdateCanvasLook();
        }

        internal void MoveSheet(int delta)
        {
            int to = SheetIndex + delta;
            if (ScreenMode || to < 0 || to >= Sheets.Count) return;
            var s = Sheets[SheetIndex];
            Sheets.RemoveAt(SheetIndex);
            Sheets.Insert(to, s);
            SheetIndex = to;
            Modified = true;
            UpdateCanvasLook();
        }

        // ---- Plantillas ----
        internal void ApplyTemplate(string name)
        {
            if (ScreenMode) SetMode(false);
            AddSheet();
            double w = Math.Max(600, Host.ActualWidth), h = Math.Max(400, Host.ActualHeight), top = 120, bottom = h - 90, left = 40, right = w - 40;
            Action<double, double, double, double> line = (x1, y1, x2, y2) =>
            {
                var da = new DrawingAttributes { Color = Color.FromRgb(0x60, 0x70, 0x80), Width = 2, Height = 2, IgnorePressure = true };
                Ink.Strokes.Add(new Stroke(new StylusPointCollection(new[] { new Point(x1, y1), new Point(x2, y2) }), da));
            };
            Action<double, double, string> title = (x, y, t) =>
            {
                var c = IsDark(Cur.Paper) ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20);
                AddElement(new Item { Kind = ItemKind.Text, X = x, Y = y, W = 420, Data = PlainXaml(t, 22, c, true) });
            };
            Action<string[]> columns = names =>
            {
                double cw = (right - left) / names.Length;
                line(left, top + 50, right, top + 50);
                for (int i = 0; i < names.Length; i++)
                {
                    if (i > 0) line(left + i * cw, top, left + i * cw, bottom);
                    title(left + i * cw + 8, top + 6, names[i]);
                }
            };
            switch (name)
            {
                case "kanban": columns(new[] { "Pendiente", "En curso", "Hecho" }); break;
                case "semana": columns(new[] { "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom" }); break;
                case "dafo":
                    double mx = (left + right) / 2, my = (top + bottom) / 2;
                    line(mx, top, mx, bottom);
                    line(left, my, right, my);
                    title(left + 8, top + 6, "Debilidades");
                    title(mx + 8, top + 6, "Amenazas");
                    title(left + 8, my + 6, "Fortalezas");
                    title(mx + 8, my + 6, "Oportunidades");
                    break;
                case "cornell":
                    double cue = left + (right - left) * 0.28, sum = bottom - (bottom - top) * 0.2;
                    line(cue, top, cue, sum);
                    line(left, sum, right, sum);
                    line(left, top + 50, right, top + 50);
                    title(left + 8, top + 6, "Ideas clave");
                    title(cue + 8, top + 6, "Notas");
                    title(left + 8, sum + 6, "Resumen");
                    break;
            }
            dirty = true;
            Commit();
        }

        void SetStrokes(StrokeCollection s)
        {
            Ink.Strokes.StrokesChanged -= OnStrokesChanged;
            Ink.Strokes = s;
            s.StrokesChanged += OnStrokesChanged;
        }

        void OnStrokesChanged(object sender, StrokeCollectionChangedEventArgs e)
        {
            if (!loading && !suspend) dirty = true;
        }

        void StoreCurrent()
        {
            Cur.Strokes = Ink.Strokes;
            Cur.Items = ReadItems();
        }

        void LoadSheet(Sheet s)
        {
            Cur = s;
            ShowContent(s.Strokes, s.Items);
            ApplyView();
        }

        void ShowContent(StrokeCollection strokes, List<Item> items)
        {
            loading = true;
            activeText = null;
            Ink.Children.Clear();
            SetStrokes(strokes);
            foreach (var t in items) AddElement(t);
            loading = false;
            dirty = false;
        }

        // ---- Deshacer: guarda una copia del estado tras cada cambio ----
        internal void MarkDirty() { dirty = true; }

        internal void Commit()
        {
            if (!dirty || loading) return;
            suspend = true;
            RichStroke.Upgrade(Ink.Strokes);
            suspend = false;
            dirty = false;
            Cur.Undo.Add(Cur.Last);
            if (Cur.Undo.Count > 40) Cur.Undo.RemoveAt(0);
            Cur.Redo.Clear();
            Cur.Last = Capture();
            StoreCurrent();
            if (Cur != ScreenSheet) Modified = true;
            Bar.Refresh();
        }

        Snap Capture()
        {
            var keep = new StrokeCollection();
            foreach (var s in Ink.Strokes)
                if (!fading.Any(f => f.S == s)) keep.Add(s.Clone());
            return new Snap { Strokes = keep, Items = ReadItems() };
        }

        internal void Undo()
        {
            FinishText();
            Commit();
            if (Cur.Undo.Count == 0) return;
            Cur.Redo.Add(Cur.Last);
            Cur.Last = Cur.Undo[Cur.Undo.Count - 1];
            Cur.Undo.RemoveAt(Cur.Undo.Count - 1);
            RestoreLast();
        }

        internal void Redo()
        {
            FinishText();
            Commit();
            if (Cur.Redo.Count == 0) return;
            Cur.Undo.Add(Cur.Last);
            Cur.Last = Cur.Redo[Cur.Redo.Count - 1];
            Cur.Redo.RemoveAt(Cur.Redo.Count - 1);
            RestoreLast();
        }

        void RestoreLast()
        {
            ShowContent(Cur.Last.Strokes.Clone(), Cur.Last.Items);
            StoreCurrent();
            ApplyTool();
            if (Cur != ScreenSheet) Modified = true;
        }

        internal void ClearCurrent()
        {
            FinishText();
            Commit();
            if (Ink.Strokes.Count == 0 && Ink.Children.Count == 0) return;
            Ink.EditingMode = InkCanvasEditingMode.None;
            Ink.Strokes.Clear();
            Ink.Children.Clear();
            ApplyTool();
            dirty = true;
            Commit();
        }

        // ---- Texto, notas e imágenes ----
        internal static byte[] PlainXaml(string text, double size, Color color, bool bold)
        {
            var doc = new FlowDocument(new Paragraph(new Run(text)) { Margin = new Thickness(0) })
            {
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                Foreground = new SolidColorBrush(color),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
            };
            using (var ms = new MemoryStream())
            {
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(ms, DataFormats.Xaml);
                return ms.ToArray();
            }
        }

        static bool IsBlank(RichTextBox r)
        {
            return new TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.Trim().Length == 0;
        }

        internal static string TextOf(RichTextBox r)
        {
            return new TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.Trim();
        }

        List<Item> ReadItems()
        {
            var list = new List<Item>();
            foreach (FrameworkElement el in Ink.Children.OfType<FrameworkElement>())
            {
                var meta = el.Tag as Item;
                if (meta == null) continue;
                var it = new Item { Kind = meta.Kind, Bg = meta.Bg, X = InkCanvas.GetLeft(el), Y = InkCanvas.GetTop(el), W = el.Width, H = el.Height, Data = meta.Data };
                var rt = el as RichTextBox;
                if (rt != null)
                {
                    if (it.Kind != ItemKind.Sticky && IsBlank(rt)) continue;
                    using (var ms = new MemoryStream())
                    {
                        new TextRange(rt.Document.ContentStart, rt.Document.ContentEnd).Save(ms, DataFormats.Xaml);
                        it.Data = ms.ToArray();
                    }
                }
                list.Add(it);
            }
            return list;
        }

        internal FrameworkElement AddElement(Item t)
        {
            FrameworkElement el;
            if (t.Kind == ItemKind.Image) el = MakeImage(t);
            else el = MakeRich(t);
            el.Tag = t;
            InkCanvas.SetLeft(el, t.X);
            InkCanvas.SetTop(el, t.Y);
            Ink.Children.Add(el);
            return el;
        }

        static Image MakeImage(Item t)
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(t.Data))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            return new Image { Source = bmp, Stretch = Stretch.Fill, Width = t.W, Height = t.H, IsHitTestVisible = false };
        }

        RichTextBox MakeRich(Item t)
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };
            var rt = new RichTextBox
            {
                Document = doc,
                Background = t.Kind == ItemKind.Sticky ? (Brush)new SolidColorBrush(t.Bg) : Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = t.Kind == ItemKind.Sticky ? new Thickness(10) : t.Kind == ItemKind.Doc ? new Thickness(20) : new Thickness(2),
                AcceptsReturn = true,
                AcceptsTab = true,
                Width = t.W,
                Height = t.H,
                IsHitTestVisible = Tool == Tool.Text || Tool == Tool.Sticky,
                FocusVisualStyle = null,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                CaretBrush = new SolidColorBrush(IsDark(Cur.Paper) ? Colors.White : Color.FromRgb(0x20, 0x20, 0x20))
            };
            var pstyle = new Style(typeof(Paragraph));
            pstyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0, 0, 0, 2)));
            rt.Resources.Add(typeof(Paragraph), pstyle);
            if (t.Kind == ItemKind.Sticky)
                rt.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.35 };
            if (t.Kind == ItemKind.Doc) rt.MinHeight = 400;
            if (t.Data != null)
            {
                using (var ms = new MemoryStream(t.Data))
                    new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.Xaml);
            }
            else
            {
                doc.FontFamily = new FontFamily(FontName);
                doc.FontSize = FontPt;
                doc.Foreground = new SolidColorBrush(t.Kind == ItemKind.Sticky ? Color.FromRgb(0x20, 0x20, 0x20) : InkColor);
            }
            rt.TextChanged += delegate { if (!loading) dirty = true; };
            rt.GotKeyboardFocus += delegate { activeText = rt; };
            rt.LostKeyboardFocus += OnTextLostFocus;
            return rt;
        }

        void OnTextLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (loading) return;
            var rt = (RichTextBox)sender;
            if (activeText == rt) activeText = null;
            if (((Item)rt.Tag).Kind != ItemKind.Sticky && IsBlank(rt)) Ink.Children.Remove(rt);
            Commit();
        }

        internal RichTextBox PlaceText(Point p)
        {
            double maxW = Math.Max(160, (Host.ActualWidth - Cur.PanX) / Cur.Zoom - p.X - 12);
            var rt = (RichTextBox)AddElement(new Item { Kind = ItemKind.Text, X = p.X, Y = p.Y - FontPt * 0.75, W = Math.Min(520, maxW) });
            rt.UpdateLayout();
            rt.Focus();
            Keyboard.Focus(rt);
            return rt;
        }

        internal RichTextBox PlaceSticky(Point p)
        {
            var rt = (RichTextBox)AddElement(new Item { Kind = ItemKind.Sticky, X = p.X - 110, Y = p.Y - 30, W = 220, H = 220, Bg = StickyColor });
            dirty = true;
            rt.UpdateLayout();
            rt.Focus();
            Keyboard.Focus(rt);
            return rt;
        }

        void FinishText()
        {
            if (activeText != null) Ink.Focus();
        }

        void EraseTextAt(Point p)
        {
            foreach (var rt in Ink.Children.OfType<RichTextBox>().ToList())
            {
                var kind = ((Item)rt.Tag).Kind;
                if (kind == ItemKind.Doc) continue;
                var local = new Point(p.X - InkCanvas.GetLeft(rt), p.Y - InkCanvas.GetTop(rt));
                bool hit;
                if (kind == ItemKind.Sticky) hit = new Rect(0, 0, rt.ActualWidth, rt.ActualHeight).Contains(local);
                else hit = rt.GetPositionFromPoint(local, false) != null;
                if (hit) { Ink.Children.Remove(rt); dirty = true; }
            }
        }

        internal void InsertImageBytes(byte[] png, double pxW, double pxH)
        {
            double maxW = Host.ActualWidth * 0.7 / Cur.Zoom, maxH = Host.ActualHeight * 0.7 / Cur.Zoom;
            double f = Math.Min(1, Math.Min(maxW / pxW, maxH / pxH));
            double w = pxW * f, h = pxH * f;
            Point c = ViewToInk(new Point(Host.ActualWidth / 2, Host.ActualHeight / 2));
            AddElement(new Item { Kind = ItemKind.Image, X = c.X - w / 2, Y = c.Y - h / 2, W = w, H = h, Data = png });
            dirty = true;
            Commit();
        }

        static byte[] Encode(BitmapSource b)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(b));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        internal void InsertImageFile(string path)
        {
            var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = dec.Frames[0];
            InsertImageBytes(Encode(frame), frame.PixelWidth, frame.PixelHeight);
        }

        internal void PickImage()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
            if (d.ShowDialog(this) != true) return;
            try { InsertImageFile(d.FileName); SetTool(Tool.Select); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir la imagen:\n" + ex.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        void PasteFromClipboard()
        {
            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null) InsertImageBytes(Encode(img), img.PixelWidth, img.PixelHeight);
            }
            else if (Clipboard.ContainsText())
            {
                Point c = ViewToInk(new Point(Host.ActualWidth * 0.25, Host.ActualHeight * 0.35));
                AddElement(new Item { Kind = ItemKind.Text, X = c.X, Y = c.Y, W = 520, Data = PlainXaml(Clipboard.GetText(), FontPt, InkColor, false) });
                dirty = true;
                Commit();
            }
            else if (Ink.CanPaste())
            {
                Ink.Paste(ViewToInk(new Point(Host.ActualWidth * 0.3, Host.ActualHeight * 0.3)));
                dirty = true;
                Commit();
            }
        }

        // ---- Trazos nuevos: efectos al terminar de dibujar ----
        internal void ProcessNewStroke(Stroke st)
        {
            if (Tool == Tool.Laser || (Tool == Tool.Pen && Pen == PenKind.Desvanecer))
            {
                suspend = true;
                Stroke live = st;
                if (RichStroke.NeedsRich(st.DrawingAttributes))
                {
                    live = new RichStroke(st.StylusPoints.Clone(), st.DrawingAttributes.Clone());
                    Ink.Strokes.Replace(st, new StrokeCollection(new[] { live }));
                }
                suspend = false;
                dirty = false;
                fading.Add(new Fading { S = live, Owner = Ink.Strokes, Start = DateTime.UtcNow, Life = Tool == Tool.Laser ? 1.2 : 4, Alpha = live.DrawingAttributes.Color.A });
                fadeTimer.Start();
                return;
            }
            suspend = true;
            Stroke cur = st;
            if (Tool == Tool.Pen)
            {
                if (Ruler != null) cur = SnapToRuler(cur);
                if (RecognizeShapes && Pen != PenKind.Aerosol)
                {
                    var rec = ShapeRecognizer.Recognize(cur.StylusPoints.Select(p => (Point)p));
                    if (rec != null)
                    {
                        var da = cur.DrawingAttributes.Clone();
                        da.FitToCurve = false;
                        da.IgnorePressure = true;
                        Stroke ns = RichStroke.NeedsRich(da) ? new RichStroke(new StylusPointCollection(rec), da) : new Stroke(new StylusPointCollection(rec), da);
                        Ink.Strokes.Replace(cur, new StrokeCollection(new[] { ns }));
                        cur = ns;
                    }
                }
                if (Pen == PenKind.Pincel && cur.StylusPoints.All(p => Math.Abs(p.PressureFactor - 0.5f) < 0.001f))
                {
                    // Con ratón no hay presión: se afinan los extremos para imitar un pincel.
                    var pts = new StylusPointCollection();
                    int n = cur.StylusPoints.Count;
                    for (int i = 0; i < n; i++)
                    {
                        double t = n > 1 ? (double)i / (n - 1) : 0.5;
                        pts.Add(new StylusPoint(cur.StylusPoints[i].X, cur.StylusPoints[i].Y, (float)(0.12 + 0.6 * Math.Sin(Math.PI * t))));
                    }
                    cur.StylusPoints = pts;
                }
            }
            suspend = false;
            dirty = true;
            Commit();
        }

        Stroke SnapToRuler(Stroke st)
        {
            Point va, vb;
            Ruler.Edge(out va, out vb);
            Point a = ViewToInk(va), b = ViewToInk(vb);
            Vector dir = b - a;
            double len = dir.Length;
            if (len < 1) return st;
            dir /= len;
            double maxDist = 0, tol = 34 / Cur.Zoom;
            foreach (var sp in st.StylusPoints)
            {
                Vector v = (Point)sp - a;
                double along = v.X * dir.X + v.Y * dir.Y, off = Math.Abs(v.X * dir.Y - v.Y * dir.X);
                if (along < -tol || along > len + tol) return st;
                maxDist = Math.Max(maxDist, off);
            }
            if (maxDist > tol) return st;
            var pts = new StylusPointCollection();
            foreach (var sp in st.StylusPoints)
            {
                Vector v = (Point)sp - a;
                double along = v.X * dir.X + v.Y * dir.Y;
                Point p = a + dir * along;
                pts.Add(new StylusPoint(p.X, p.Y, sp.PressureFactor));
            }
            st.StylusPoints = pts;
            return st;
        }

        void TickFading()
        {
            suspend = true;
            var now = DateTime.UtcNow;
            foreach (var f in fading.ToList())
            {
                double t = (now - f.Start).TotalSeconds / f.Life;
                if (t >= 1)
                {
                    if (f.Owner.Contains(f.S)) f.Owner.Remove(f.S);
                    fading.Remove(f);
                }
                else if (t > 0.5)
                {
                    var c = f.S.DrawingAttributes.Color;
                    f.S.DrawingAttributes.Color = Color.FromArgb((byte)(f.Alpha * (1 - t) * 2), c.R, c.G, c.B);
                }
            }
            suspend = false;
            if (fading.Count == 0) fadeTimer.Stop();
        }

        internal int FadingCount { get { return fading.Count; } }

        // ---- Regla y transportador ----
        internal void ToggleGuide(GuideKind k)
        {
            var existing = k == GuideKind.Ruler ? Ruler : Protractor;
            if (existing != null)
            {
                guideLayer.Children.Remove(existing);
                if (k == GuideKind.Ruler) Ruler = null; else Protractor = null;
            }
            else
            {
                var g = new MathGuide(k);
                guideLayer.Children.Add(g);
                if (k == GuideKind.Ruler) Ruler = g; else Protractor = g;
            }
            Bar.Refresh();
        }

        // ---- Foco, lupa y cuentagotas ----
        void TakeShot()
        {
            shot = CaptureScreen();
            var h = shot.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                lensBrush.ImageSource = src;
            }
            finally { Native.DeleteObject(h); }
        }

        void DropShot()
        {
            lensBrush.ImageSource = null;
            if (shot != null) { shot.Dispose(); shot = null; }
        }

        double DeviceScale
        {
            get
            {
                var src = PresentationSource.FromVisual(this);
                return src != null ? src.CompositionTarget.TransformToDevice.M11 : 1;
            }
        }

        void UpdateEffects(Point p)
        {
            if (Tool == Tool.Spotlight)
            {
                var hole = new EllipseGeometry(p, spotRadius, spotRadius);
                var all = new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                spot.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, hole);
            }
            else if (Tool == Tool.Magnifier && shot != null)
            {
                lens.Width = lens.Height = lensRadius * 2;
                Canvas.SetLeft(lens, p.X - lensRadius);
                Canvas.SetTop(lens, p.Y - lensRadius);
                double s = DeviceScale, size = lensRadius * 2 * s / lensZoom;
                lensBrush.Viewbox = new Rect(p.X * s - size / 2, p.Y * s - size / 2, size, size);
            }
        }

        internal Color? PickPixel(Point p)
        {
            if (shot == null) return null;
            double s = DeviceScale;
            int x = (int)Math.Max(0, Math.Min(shot.Width - 1, p.X * s)), y = (int)Math.Max(0, Math.Min(shot.Height - 1, p.Y * s));
            var c = shot.GetPixel(x, y);
            return Color.FromRgb(c.R, c.G, c.B);
        }

        // ---- Ratón sobre el lienzo ----
        static bool Inside<T>(object source) where T : DependencyObject
        {
            var d = source as DependencyObject;
            while (d != null && !(d is T)) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d != null;
        }

        void OnInkDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(Ink);
            switch (Tool)
            {
                case Tool.Text:
                case Tool.Sticky:
                    if (Inside<RichTextBox>(e.OriginalSource)) return; // clic dentro: editar
                    e.Handled = true;
                    if (Tool == Tool.Text) PlaceText(p); else PlaceSticky(p);
                    break;
                case Tool.Shape:
                    e.Handled = true;
                    BeginShape(p);
                    Ink.CaptureMouse();
                    break;
                case Tool.Hand:
                    e.Handled = true;
                    StartPan(e);
                    break;
                case Tool.Eyedropper:
                    e.Handled = true;
                    var col = PickPixel(e.GetPosition(root));
                    if (col.HasValue) InkColor = col.Value;
                    SetTool(lastDrawTool);
                    break;
                case Tool.Eraser:
                case Tool.StrokeEraser:
                    EraseTextAt(p);
                    break;
            }
        }

        void StartPan(MouseEventArgs e)
        {
            panning = true;
            panStart = e.GetPosition(root);
            panX0 = Cur.PanX;
            panY0 = Cur.PanY;
            Ink.CaptureMouse();
        }

        void OnInkMiddleDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && !ScreenMode) { StartPan(e); e.Handled = true; }
        }

        void OnInkMiddleUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && panning) { panning = false; Ink.ReleaseMouseCapture(); e.Handled = true; }
        }

        void OnInkMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(Ink);
            if (panning)
            {
                var v = e.GetPosition(root);
                Cur.PanX = panX0 + v.X - panStart.X;
                Cur.PanY = panY0 + v.Y - panStart.Y;
                ClampPan();
                ApplyView();
            }
            else if (shaping) UpdateShape(p, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            else if ((Tool == Tool.Eraser || Tool == Tool.StrokeEraser) && e.LeftButton == MouseButtonState.Pressed) EraseTextAt(p);
            else if (Tool == Tool.Spotlight || Tool == Tool.Magnifier) UpdateEffects(e.GetPosition(root));
        }

        void OnInkUp(object sender, MouseButtonEventArgs e)
        {
            if (panning)
            {
                panning = false;
                Ink.ReleaseMouseCapture();
                e.Handled = true;
            }
            if (shaping)
            {
                Ink.ReleaseMouseCapture();
                EndShape();
                e.Handled = true;
            }
            // El borrador termina después del evento: se registra al quedar libre el despachador.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Commit));
        }

        internal void BeginShape(Point p)
        {
            shaping = true;
            shapeStart = p;
            shapeStroke = null;
        }

        internal void UpdateShape(Point p, bool proportional)
        {
            if (proportional)
            {
                double d = Math.Max(Math.Abs(p.X - shapeStart.X), Math.Abs(p.Y - shapeStart.Y));
                if (ShapeType == ShapeKind.Line || ShapeType == ShapeKind.Arrow || ShapeType == ShapeKind.DoubleArrow)
                {
                    // Línea: ajusta a múltiplos de 45 grados.
                    double ang = Math.Round(Math.Atan2(p.Y - shapeStart.Y, p.X - shapeStart.X) / (Math.PI / 4)) * Math.PI / 4;
                    double len = (p - shapeStart).Length;
                    p = new Point(shapeStart.X + len * Math.Cos(ang), shapeStart.Y + len * Math.Sin(ang));
                }
                else p = new Point(shapeStart.X + Math.Sign(p.X - shapeStart.X) * d, shapeStart.Y + Math.Sign(p.Y - shapeStart.Y) * d);
            }
            var pts = new StylusPointCollection(Shapes.Polygon(ShapeType, shapeStart, p, PenSize));
            suspend = true;
            if (shapeStroke == null)
            {
                var da = new DrawingAttributes { Color = WithOpacity(1), Width = PenSize, Height = PenSize, IgnorePressure = true, FitToCurve = false };
                var style = StyleOf(Pen);
                if (style != StrokeStyle.Plain && style != StrokeStyle.Spray) da.AddPropertyData(RichStroke.StyleId, (int)style);
                if (FillShapes && Shapes.IsClosed(ShapeType)) da.AddPropertyData(RichStroke.FillId, 1);
                shapeStroke = RichStroke.NeedsRich(da) ? new RichStroke(pts, da) : new Stroke(pts, da);
                Ink.Strokes.Add(shapeStroke);
            }
            else shapeStroke.StylusPoints = pts;
            suspend = false;
        }

        internal void EndShape()
        {
            shaping = false;
            if (shapeStroke == null) return;
            var b = shapeStroke.GetBounds();
            if (b.Width < PenSize + 3 && b.Height < PenSize + 3)
            {
                suspend = true;
                Ink.Strokes.Remove(shapeStroke);
                suspend = false;
            }
            else
            {
                dirty = true;
                Commit();
            }
            shapeStroke = null;
        }

        // ---- Teclado ----
        void OnKey(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool typing = Keyboard.FocusedElement is RichTextBox;
            bool handled = true;
            if (ctrl && e.Key == Key.S) Save(shift);
            else if (ctrl && e.Key == Key.O) Open();
            else if (ctrl && e.Key == Key.N) AddSheet();
            else if (ctrl && !typing && e.Key == Key.Z) Undo();
            else if (ctrl && !typing && e.Key == Key.Y) Redo();
            else if (ctrl && !typing && e.Key == Key.V) PasteFromClipboard();
            else if (ctrl && !typing && e.Key == Key.C && Ink.EditingMode == InkCanvasEditingMode.Select) Ink.CopySelection();
            else if (e.Key == Key.Escape)
            {
                if (typing) FinishText();
                else if (Tool == Tool.Spotlight || Tool == Tool.Magnifier || Tool == Tool.Eyedropper || Tool == Tool.Laser) SetTool(lastDrawTool == Tool.Laser ? Tool.Pen : lastDrawTool);
                else if (ScreenMode) SetTool(Tool.Cursor);
            }
            else if (typing || ctrl) handled = false;
            else if (e.Key == Key.Delete) DeleteSelection();
            else if (e.Key == Key.PageDown) GoToSheet(SheetIndex + 1);
            else if (e.Key == Key.PageUp) GoToSheet(SheetIndex - 1);
            else if (e.Key == Key.P) SetTool(Tool.Pen);
            else if (e.Key == Key.H) SetTool(Tool.Highlighter);
            else if (e.Key == Key.E) SetTool(Tool.Eraser);
            else if (e.Key == Key.T) SetTool(Tool.Text);
            else if (e.Key == Key.W) EnterWriteMode();
            else if (e.Key == Key.N) SetTool(Tool.Sticky);
            else if (e.Key == Key.V) SetTool(Tool.Select);
            else if (e.Key == Key.M) SetTool(Tool.Hand);
            else if (e.Key == Key.K) SetTool(Tool.Laser);
            else if (e.Key == Key.L) SetShape(ShapeKind.Line);
            else if (e.Key == Key.A) SetShape(ShapeKind.Arrow);
            else if (e.Key == Key.R) SetShape(ShapeKind.Rect);
            else if (e.Key == Key.O) SetShape(ShapeKind.Ellipse);
            else if (e.Key == Key.OemOpenBrackets) CurrentSize = Math.Max(1, CurrentSize - 1);
            else if (e.Key == Key.OemCloseBrackets) CurrentSize = CurrentSize + 1;
            else if (e.Key >= Key.D1 && e.Key <= Key.D9) SetColor(ToolbarWindow.Palette[e.Key - Key.D1]);
            else if (e.Key == Key.D0) SetColor(ToolbarWindow.Palette[9]);
            else handled = false;
            if (handled) e.Handled = true;
        }

        internal void DeleteSelection()
        {
            if (Ink.EditingMode != InkCanvasEditingMode.Select) return;
            var strokes = Ink.GetSelectedStrokes();
            var elements = Ink.GetSelectedElements().ToList();
            if (strokes.Count == 0 && elements.Count == 0) return;
            Ink.Select(new StrokeCollection());
            Ink.Strokes.Remove(strokes);
            foreach (var el in elements) Ink.Children.Remove(el);
            dirty = true;
            Commit();
        }

        // ---- Guardar y abrir ----
        internal bool Save(bool saveAs)
        {
            FinishText();
            Commit();
            StoreCurrent();
            string path = FilePath;
            if (path == null || saveAs)
            {
                var d = new Microsoft.Win32.SaveFileDialog { Filter = "Cuaderno de Pizarra (*.pizarra)|*.pizarra", FileName = "Cuaderno", DefaultExt = ".pizarra" };
                if (d.ShowDialog(this) != true) return false;
                path = d.FileName;
            }
            try
            {
                WriteNotebook(path);
                FilePath = path;
                Modified = false;
                UpdateCanvasLook();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar:\n" + ex.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        internal void WriteNotebook(string path)
        {
            // Primero a un temporal: un fallo a medias no estropea el cuaderno existente.
            string tmp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write("PZR2");
                w.Write(Sheets.Count);
                foreach (var s in Sheets)
                {
                    w.Write((int)s.Paper);
                    w.Write(s.Zoom); w.Write(s.PanX); w.Write(s.PanY);
                    byte[] isf = new byte[0];
                    if (s.Strokes.Count > 0)
                        using (var ms = new MemoryStream()) { s.Strokes.Save(ms, true); isf = ms.ToArray(); }
                    w.Write(isf.Length);
                    w.Write(isf);
                    w.Write(s.Items.Count);
                    foreach (var t in s.Items)
                    {
                        w.Write((int)t.Kind);
                        w.Write(t.X); w.Write(t.Y); w.Write(t.W); w.Write(t.H);
                        w.Write(t.Bg.A); w.Write(t.Bg.R); w.Write(t.Bg.G); w.Write(t.Bg.B);
                        w.Write(t.Data.Length);
                        w.Write(t.Data);
                    }
                }
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        internal static List<Sheet> ReadNotebook(string path)
        {
            var list = new List<Sheet>();
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                string magic = r.ReadString();
                if (magic != "PZR1" && magic != "PZR2") throw new InvalidDataException("El archivo no es un cuaderno de Pizarra.");
                bool v2 = magic == "PZR2";
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var s = new Sheet { Paper = (Paper)r.ReadInt32() };
                    if (v2) { s.Zoom = r.ReadDouble(); s.PanX = r.ReadDouble(); s.PanY = r.ReadDouble(); }
                    int len = r.ReadInt32();
                    if (len > 0)
                        using (var ms = new MemoryStream(r.ReadBytes(len))) s.Strokes = new StrokeCollection(ms);
                    RichStroke.Upgrade(s.Strokes);
                    int tc = r.ReadInt32();
                    for (int j = 0; j < tc; j++)
                    {
                        if (v2)
                        {
                            var t = new Item { Kind = (ItemKind)r.ReadInt32(), X = r.ReadDouble(), Y = r.ReadDouble(), W = r.ReadDouble(), H = r.ReadDouble() };
                            t.Bg = Color.FromArgb(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                            t.Data = r.ReadBytes(r.ReadInt32());
                            s.Items.Add(t);
                        }
                        else
                        {
                            // Formato de la primera versión: texto plano.
                            double x = r.ReadDouble(), y = r.ReadDouble(), w = r.ReadDouble(), h = r.ReadDouble(), size = r.ReadDouble();
                            var c = Color.FromArgb(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                            string text = r.ReadString();
                            s.Items.Add(new Item { Kind = ItemKind.Text, X = x, Y = y, W = double.IsNaN(w) ? 520 : w, H = h, Data = PlainXaml(text, size, c, false) });
                        }
                    }
                    s.Last = new Snap { Strokes = s.Strokes.Clone(), Items = s.Items };
                    list.Add(s);
                }
            }
            if (list.Count == 0) list.Add(new Sheet());
            return list;
        }

        internal void Open()
        {
            if (!ConfirmDiscard()) return;
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Cuaderno de Pizarra (*.pizarra)|*.pizarra" };
            if (d.ShowDialog(this) != true) return;
            OpenPath(d.FileName);
        }

        internal bool OpenPath(string path)
        {
            try
            {
                var list = ReadNotebook(path);
                FinishText();
                Commit();
                StoreCurrent();
                Sheets.Clear();
                Sheets.AddRange(list);
                SheetIndex = 0;
                FilePath = path;
                EnterMode(false);
                Modified = false;
                UpdateCanvasLook();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo abrir:\n" + ex.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        bool ConfirmDiscard()
        {
            FinishText();
            Commit();
            StoreCurrent();
            if (!Modified) return true;
            var r = MessageBox.Show(this, "¿Guardar los cambios de tus hojas?", "Pizarra", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return false;
            if (r == MessageBoxResult.Yes) return Save(false);
            return true;
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            try { if (AutosaveEnabled && File.Exists(autosavePath)) File.Delete(autosavePath); }
            catch (IOException) { }
        }

        // ---- Autoguardado: protege frente a cierres inesperados ----
        void Autosave()
        {
            if (!Modified) return;
            try
            {
                if (!ScreenMode) StoreCurrent();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(autosavePath));
                WriteNotebook(autosavePath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void OfferRecovery()
        {
            if (!File.Exists(autosavePath)) return;
            var r = MessageBox.Show(this, "Pizarra no se cerró bien la última vez. ¿Recuperar el trabajo autoguardado?", "Pizarra", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes && OpenPath(autosavePath))
            {
                FilePath = null;
                Modified = true;
                UpdateCanvasLook();
            }
        }

        // ---- Exportar ----
        internal BitmapSource RenderSheet()
        {
            double sx = DeviceScale;
            var bmp = new RenderTargetBitmap((int)(Host.ActualWidth * sx), (int)(Host.ActualHeight * sx), 96 * sx, 96 * sx, PixelFormats.Pbgra32);
            bmp.Render(Host);
            return bmp;
        }

        // Ajusta temporalmente la vista para que quepa todo el contenido de la hoja.
        BitmapSource RenderFit()
        {
            double z = Cur.Zoom, px = Cur.PanX, py = Cur.PanY;
            Rect b = Rect.Empty;
            if (Ink.Strokes.Count > 0) b = Ink.Strokes.GetBounds();
            foreach (var el in Ink.Children.OfType<FrameworkElement>())
                b.Union(new Rect(InkCanvas.GetLeft(el), InkCanvas.GetTop(el), el.ActualWidth, el.ActualHeight));
            Cur.Zoom = 1; Cur.PanX = 0; Cur.PanY = 0;
            if (!b.IsEmpty && (b.Right > Host.ActualWidth || b.Bottom > Host.ActualHeight))
            {
                double f = Math.Min(Host.ActualWidth / (b.Right + 20), Host.ActualHeight / (b.Bottom + 20));
                Cur.Zoom = Math.Min(1, f);
            }
            ApplyView();
            UpdateLayout();
            var bmp = RenderSheet();
            Cur.Zoom = z; Cur.PanX = px; Cur.PanY = py;
            ApplyView();
            return bmp;
        }

        internal static void WriteImage(BitmapSource bmp, string path)
        {
            bool jpg = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
            BitmapEncoder enc = jpg ? (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(jpg ? new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0) : bmp));
            using (var f = File.Create(path)) enc.Save(f);
        }

        // Con la herramienta de selección activa, InkCanvas dibujaría los tiradores en la imagen.
        void ClearSelectionForExport()
        {
            FinishText();
            Commit();
            Ink.EditingMode = InkCanvasEditingMode.None;
            ApplyTool();
        }

        internal void SaveImage()
        {
            ClearSelectionForExport();
            System.Drawing.Bitmap screen = null;
            BitmapSource page = null;
            if (ScreenMode) screen = CaptureScreen();
            else
            {
                UpdateLayout();
                page = RenderSheet();
            }
            try
            {
                var d = new Microsoft.Win32.SaveFileDialog { Filter = "Imagen PNG (*.png)|*.png|Imagen JPEG (*.jpg)|*.jpg", FileName = ScreenMode ? "Pantalla" : "Hoja " + (SheetIndex + 1), DefaultExt = ".png" };
                if (d.ShowDialog(this) != true) return;
                if (screen != null)
                    screen.Save(d.FileName, d.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? System.Drawing.Imaging.ImageFormat.Png : System.Drawing.Imaging.ImageFormat.Jpeg);
                else WriteImage(page, d.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar la imagen:\n" + ex.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (screen != null) screen.Dispose();
            }
        }

        internal void ExportPdfTo(string path)
        {
            ClearSelectionForExport();
            StoreCurrent();
            bool wasScreen = ScreenMode;
            if (wasScreen) SetMode(false);
            int keep = SheetIndex;
            var pages = new List<KeyValuePair<byte[], Size>>();
            for (int i = 0; i < Sheets.Count; i++)
            {
                SheetIndex = i;
                LoadSheet(Sheets[i]);
                UpdateCanvasLook();
                UpdateLayout();
                var bmp = RenderFit();
                var enc = new JpegBitmapEncoder { QualityLevel = 90 };
                enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0)));
                using (var ms = new MemoryStream())
                {
                    enc.Save(ms);
                    pages.Add(new KeyValuePair<byte[], Size>(ms.ToArray(), new Size(bmp.PixelWidth, bmp.PixelHeight)));
                }
            }
            SheetIndex = keep;
            LoadSheet(Sheets[keep]);
            UpdateCanvasLook();
            PdfWriter.Write(path, pages);
            if (wasScreen) SetMode(true);
        }

        internal void ExportPdf()
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Documento PDF (*.pdf)|*.pdf", FileName = FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : "Hojas", DefaultExt = ".pdf" };
            if (d.ShowDialog(this) != true) return;
            try { ExportPdfTo(d.FileName); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo crear el PDF:\n" + ex.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // Toma lo que hay debajo de Pizarra y lo pega en una hoja nueva para anotarlo.
        internal void CaptureToSheet()
        {
            FinishText();
            Commit();
            double op = Opacity;
            Opacity = 0;
            System.Drawing.Bitmap bmp = null;
            try
            {
                bmp = CaptureScreen();
            }
            finally { Opacity = op; }
            using (bmp)
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                if (ScreenMode) SetMode(false);
                AddSheet();
                double s = DeviceScale;
                InsertImageBytes(ms.ToArray(), bmp.Width / s, bmp.Height / s);
            }
        }

        // Captura lo que se ve en el monitor (ventanas + dibujo), sin la barra de herramientas.
        internal System.Drawing.Bitmap CaptureScreen()
        {
            bool barWasVisible = Bar.IsVisible;
            if (barWasVisible) Bar.Hide();
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
            Thread.Sleep(150);
            var b = System.Windows.Forms.Screen.AllScreens[MonitorIndex].Bounds;
            var bmp = new System.Drawing.Bitmap(b.Width, b.Height);
            // BitBlt directo: CopyFromScreen rechaza SRCCOPY|CAPTUREBLT, necesario para incluir el dibujo.
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                IntPtr screen = Native.GetDC(IntPtr.Zero), dest = g.GetHdc();
                Native.BitBlt(dest, 0, 0, b.Width, b.Height, screen, b.X, b.Y, Native.SRCCOPY | Native.CAPTUREBLT);
                g.ReleaseHdc(dest);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
            if (barWasVisible) Bar.Show();
            return bmp;
        }

        internal void PickCustomColor()
        {
            using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(InkColor.R, InkColor.G, InkColor.B) })
                if (d.ShowDialog(new Win32Owner { Handle = hwnd }) == System.Windows.Forms.DialogResult.OK)
                    SetColor(Color.FromRgb(d.Color.R, d.Color.G, d.Color.B));
        }

        internal void ShowHelp()
        {
            MessageBox.Show(this,
                "Herramientas\n" +
                "P lápiz · H resaltador · E borrador · T texto · W modo escritura · N nota · V seleccionar\n" +
                "M mano · K láser · L línea · A flecha · R rectángulo · O elipse\n" +
                "1-9 y 0 colores · [ y ] grosor\n\n" +
                "Texto (al escribir)\n" +
                "Ctrl+B negrita · Ctrl+I cursiva · Ctrl+U subrayado · Esc terminar\n\n" +
                "Hojas\n" +
                "Ctrl+N nueva · RePág / AvPág cambiar · rueda desplazar · Mayús+rueda horizontal\n" +
                "Ctrl+rueda zoom · botón central arrastrar\n\n" +
                "General\n" +
                "Ctrl+Z deshacer · Ctrl+Y rehacer · Ctrl+S guardar · Ctrl+Mayús+S guardar como · Ctrl+O abrir\n" +
                "Ctrl+V pegar imagen o texto · Supr borrar selección · Mayús al dibujar formas: proporcional\n" +
                "Esc o Ctrl+Alt+D: puntero (usar el equipo sin borrar el dibujo)\n\n" +
                "Regla y transportador: arrastrar para mover, rueda para girar (Mayús: de 1 en 1 grado).",
                "Pizarra: atajos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ==================================================================================
    class ToolbarWindow : Window
    {
        readonly CanvasWindow c;
        readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
        readonly Dictionary<Color, Button> swatches = new Dictionary<Color, Button>();
        readonly Button screenBtn, sheetsBtn, writeBtn, undoBtn, redoBtn, monitorBtn, miniPen, miniCursor, shapeBtn, guideBtn, presentBtn, colorBtn, fontBtn, penChevron;
        readonly Slider slider;
        readonly TextBlock sizeLabel;
        readonly FrameworkElement full, mini, textRow;
        readonly Ellipse colorDot;
        bool refreshing;
        internal readonly Border RootPanel;

        internal static readonly Color[] Palette =
        {
            Color.FromRgb(0x20,0x20,0x20), Colors.White, Color.FromRgb(0xE5,0x39,0x35), Color.FromRgb(0xFB,0x8C,0x00), Color.FromRgb(0xFD,0xD8,0x35),
            Color.FromRgb(0x43,0xA0,0x47), Color.FromRgb(0x00,0xAC,0xC1), Color.FromRgb(0x1E,0x88,0xE5), Color.FromRgb(0x8E,0x24,0xAA), Color.FromRgb(0xEC,0x40,0x7A)
        };

        static readonly string[] ExtendedColors =
        {
            "000000","434343","666666","999999","B7B7B7","D9D9D9","EFEFEF","FFFFFF",
            "B71C1C","E53935","EF5350","FF8A80","F8BBD0","EC407A","AD1457","880E4F",
            "E65100","FB8C00","FFB74D","FFE0B2","F57F17","FDD835","FFF176","FFF9C4",
            "1B5E20","43A047","81C784","C8E6C9","33691E","7CB342","AED581","DCEDC8",
            "004D40","00897B","4DB6AC","00ACC1","80DEEA","01579B","1E88E5","90CAF9",
            "1A237E","3949AB","9FA8DA","4A148C","8E24AA","CE93D8","4E342E","8D6E63"
        };

        static readonly string[] Fonts = { "Segoe UI", "Arial", "Calibri", "Cambria", "Georgia", "Times New Roman", "Verdana", "Tahoma", "Consolas", "Courier New", "Comic Sans MS", "Segoe Print", "Segoe Script", "Ink Free", "Impact" };

        static readonly string[] PenNames = { "Bolígrafo", "Lápiz", "Rotulador", "Pincel", "Caligrafía", "Neón", "Discontinuo", "Punteado", "Aerosol", "Arcoíris", "Tinta que se desvanece" };

        static Color Hex(string s)
        {
            return Color.FromRgb(Convert.ToByte(s.Substring(0, 2), 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16));
        }

        public ToolbarWindow(CanvasWindow canvas)
        {
            c = canvas;
            Title = "Herramientas de Pizarra";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SourceInitialized += delegate
            {
                // No roba el foco: se puede seguir escribiendo o usando otra app al pulsar la barra.
                var h = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(h, Native.GWL_EXSTYLE, Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            };

            // Fila 1: modo y herramientas
            var row1 = Ui.Row();
            row1.Children.Add(Grip());
            screenBtn = Ui.Label("Pantalla", "Dibujar encima de la pantalla", 62, delegate { c.SetMode(true); });
            sheetsBtn = Ui.Label("Hojas", "Dibujar en hojas independientes", 52, delegate { c.SetMode(false); });
            writeBtn = Ui.Label("Escribir", "Modo escritura: teclea en una página de texto (W)", 58, delegate { c.EnterWriteMode(); });
            row1.Children.Add(screenBtn);
            row1.Children.Add(sheetsBtn);
            row1.Children.Add(writeBtn);
            row1.Children.Add(Ui.Separator());
            AddTool(row1, Tool.Cursor, Ui.Glyph("", "Puntero: usar el equipo sin borrar el dibujo (Esc, o Ctrl+Alt+D)", delegate { c.SetTool(Tool.Cursor); }));
            AddTool(row1, Tool.Pen, Ui.Glyph("", "Lápiz (P)", delegate { c.SetTool(Tool.Pen); }));
            penChevron = Ui.Chevron("Tipos de lápiz", ShowPenMenu);
            row1.Children.Add(penChevron);
            AddTool(row1, Tool.Highlighter, Ui.Glyph("", "Resaltador (H)", delegate { c.SetTool(Tool.Highlighter); }));
            AddTool(row1, Tool.Eraser, Ui.Glyph("", "Borrador (E)", delegate { c.SetTool(Tool.Eraser); }));
            AddTool(row1, Tool.StrokeEraser, Ui.Drawn("M3,15 L15,3 M3,3 L15,15", "Borrar trazo completo", delegate { c.SetTool(Tool.StrokeEraser); }));
            AddTool(row1, Tool.Text, Ui.Glyph("", "Cuadro de texto (T): haz clic donde quieras escribir", delegate { c.SetTool(Tool.Text); }));
            AddTool(row1, Tool.Sticky, Ui.Glyph("", "Nota adhesiva (N)", delegate { c.SetTool(Tool.Sticky); }));
            row1.Children.Add(Ui.Chevron("Color de la nota", ShowStickyMenu));
            row1.Children.Add(Ui.Glyph("", "Insertar imagen (también Ctrl+V)", delegate { c.PickImage(); }));
            AddTool(row1, Tool.Select, Ui.Glyph("", "Seleccionar, mover y redimensionar (V). Supr elimina", delegate { c.SetTool(Tool.Select); }));
            AddTool(row1, Tool.Hand, Ui.Glyph("", "Mano: mover la vista de la hoja (M)", delegate { c.SetTool(Tool.Hand); }));
            shapeBtn = Ui.Drawn(Shapes.Icon(ShapeKind.Rect), "Dibujar forma", delegate { c.SetTool(Tool.Shape); });
            AddTool(row1, Tool.Shape, shapeBtn);
            row1.Children.Add(Ui.Chevron("Elegir forma", ShowShapeMenu));
            guideBtn = Ui.Drawn("M2,12 L12,2 L16,6 L6,16 Z M5,9 L7,11 M8,6 L10,8 M11,3 L13,5", "Regla y transportador", ShowGuideMenu);
            row1.Children.Add(guideBtn);
            presentBtn = Ui.Drawn("M9,2 A7,7 0 1 1 9,16 A7,7 0 1 1 9,2 Z M9,6.5 A2.5,2.5 0 1 1 9,11.5 A2.5,2.5 0 1 1 9,6.5 Z", "Presentar: láser, foco y lupa", ShowPresentMenu);
            row1.Children.Add(presentBtn);
            row1.Children.Add(Ui.Separator());
            row1.Children.Add(Ui.Glyph("", "Compactar la barra", delegate { SetCompact(true); }));
            row1.Children.Add(Ui.Glyph("", "Minimizar", delegate { c.WindowState = WindowState.Minimized; }));
            row1.Children.Add(Ui.Glyph("", "Cerrar Pizarra", delegate { c.Close(); }));

            // Fila 2: color, grosor y acciones
            var row2 = Ui.Row();
            row2.Margin = new Thickness(18, 2, 0, 0);
            foreach (var col in Palette)
            {
                var cc = col;
                var dot = new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(cc), Stroke = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)), StrokeThickness = 1 };
                var b = new Button { Style = Ui.ButtonStyle, Content = dot, Width = 28, Height = 28, Margin = new Thickness(1, 4, 1, 4) };
                b.Click += delegate { c.SetColor(cc); };
                swatches[cc] = b;
                row2.Children.Add(b);
            }
            colorDot = new Ellipse { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 2 };
            colorBtn = new Button { Style = Ui.ButtonStyle, Content = colorDot, Width = 30, ToolTip = "Más colores, color personalizado, cuentagotas y opacidad" };
            colorBtn.Click += delegate { ShowColorMenu(); };
            row2.Children.Add(colorBtn);
            row2.Children.Add(Ui.Separator());
            slider = new Slider { Width = 96, Minimum = 1, Maximum = 40, Focusable = false, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Grosor o tamaño de letra" };
            slider.ValueChanged += delegate
            {
                if (refreshing) return;
                sizeLabel.Text = ((int)slider.Value).ToString();
                c.CurrentSize = Math.Round(slider.Value);
            };
            sizeLabel = new TextBlock { Foreground = Brushes.White, FontSize = 13, Width = 26, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row2.Children.Add(slider);
            row2.Children.Add(sizeLabel);
            row2.Children.Add(Ui.Separator());
            undoBtn = Ui.Glyph("", "Deshacer (Ctrl+Z)", delegate { c.Undo(); });
            redoBtn = Ui.Glyph("", "Rehacer (Ctrl+Y)", delegate { c.Redo(); });
            row2.Children.Add(undoBtn);
            row2.Children.Add(redoBtn);
            row2.Children.Add(Ui.Glyph("", "Borrar todo lo de esta hoja o pantalla (se puede deshacer)", delegate { c.ClearCurrent(); }));
            row2.Children.Add(Ui.Separator());
            row2.Children.Add(Ui.Glyph("", "Abrir cuaderno (Ctrl+O)", delegate { c.Open(); }));
            row2.Children.Add(Ui.Glyph("", "Guardar cuaderno con todas las hojas (Ctrl+S; Ctrl+Mayús+S: guardar como)", delegate { c.Save(false); }));
            Button exportBtn = null;
            exportBtn = Ui.Glyph("", "Exportar: imagen, PDF o captura", delegate { ShowExportMenu(exportBtn); });
            row2.Children.Add(exportBtn);
            monitorBtn = Ui.Glyph("", "Pasar al otro monitor", delegate { c.NextMonitor(); });
            row2.Children.Add(monitorBtn);
            row2.Children.Add(Ui.Glyph("", "Ayuda y atajos de teclado", delegate { c.ShowHelp(); }));

            // Fila 3: formato de texto
            var row3 = Ui.Row();
            row3.Margin = new Thickness(18, 2, 0, 0);
            fontBtn = Ui.Label("Segoe UI", "Tipo de letra", 130, ShowFontMenu);
            row3.Children.Add(fontBtn);
            row3.Children.Add(Ui.Separator());
            row3.Children.Add(TextStyleBtn("N", "Negrita (Ctrl+B)", FontWeights.Bold, FontStyles.Normal, delegate { c.Format(EditingCommands.ToggleBold); }));
            row3.Children.Add(TextStyleBtn("K", "Cursiva (Ctrl+I)", FontWeights.Normal, FontStyles.Italic, delegate { c.Format(EditingCommands.ToggleItalic); }));
            var under = TextStyleBtn("S", "Subrayado (Ctrl+U)", FontWeights.Normal, FontStyles.Normal, delegate { c.Format(EditingCommands.ToggleUnderline); });
            ((TextBlock)under.Content).TextDecorations = TextDecorations.Underline;
            row3.Children.Add(under);
            var strike = TextStyleBtn("T", "Tachado", FontWeights.Normal, FontStyles.Normal, c.ToggleStrike);
            ((TextBlock)strike.Content).TextDecorations = TextDecorations.Strikethrough;
            row3.Children.Add(strike);
            Button markBtn = null;
            markBtn = Ui.Glyph("", "Resaltar el texto seleccionado", delegate { ShowTextHighlightMenu(markBtn); });
            row3.Children.Add(markBtn);
            row3.Children.Add(Ui.Separator());
            row3.Children.Add(Ui.Glyph("", "Alinear a la izquierda", delegate { c.Format(EditingCommands.AlignLeft); }));
            row3.Children.Add(Ui.Glyph("", "Centrar", delegate { c.Format(EditingCommands.AlignCenter); }));
            row3.Children.Add(Ui.Glyph("", "Alinear a la derecha", delegate { c.Format(EditingCommands.AlignRight); }));
            row3.Children.Add(Ui.Glyph("", "Lista con viñetas", delegate { c.Format(EditingCommands.ToggleBullets); }));
            row3.Children.Add(Ui.Label("1.", "Lista numerada", 36, delegate { c.Format(EditingCommands.ToggleNumbering); }));
            row3.Children.Add(Ui.Glyph("", "Aumentar sangría", delegate { c.Format(EditingCommands.IncreaseIndentation); }));
            row3.Children.Add(Ui.Glyph("", "Reducir sangría", delegate { c.Format(EditingCommands.DecreaseIndentation); }));
            textRow = row3;

            var fullPanel = new StackPanel();
            fullPanel.Children.Add(row1);
            fullPanel.Children.Add(row2);
            fullPanel.Children.Add(row3);
            full = fullPanel;

            // Vista compacta
            var miniRow = Ui.Row();
            miniRow.Children.Add(Grip());
            miniCursor = Ui.Glyph("", "Puntero", delegate { c.SetTool(Tool.Cursor); });
            miniPen = Ui.Glyph("", "Lápiz", delegate { c.SetTool(Tool.Pen); });
            miniRow.Children.Add(miniCursor);
            miniRow.Children.Add(miniPen);
            miniRow.Children.Add(Ui.Glyph("", "Mostrar la barra completa", delegate { SetCompact(false); }));
            miniRow.Visibility = Visibility.Collapsed;
            mini = miniRow;

            var host = new Grid();
            host.Children.Add(full);
            host.Children.Add(mini);
            RootPanel = new Border
            {
                Child = host,
                Background = new SolidColorBrush(Ui.PanelColor),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6),
                Margin = new Thickness(12),
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 }
            };
            Content = RootPanel;
        }

        static Button TextStyleBtn(string text, string tip, FontWeight weight, FontStyle style, Action click)
        {
            var b = new Button
            {
                Style = Ui.ButtonStyle,
                ToolTip = tip,
                Content = new TextBlock { Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = 15, FontWeight = weight, FontStyle = style }
            };
            b.Click += delegate { click(); };
            return b;
        }

        // ---- Menús ----
        void OpenList(UIElement anchor, Action<StackPanel, Action> fill)
        {
            var panel = new StackPanel();
            Popup pop = null;
            fill(panel, delegate { pop.IsOpen = false; });
            pop = Ui.Flyout(anchor, panel, PlacementMode.Bottom);
            pop.IsOpen = true;
        }

        void ShowPenMenu()
        {
            OpenList(penChevron, (panel, close) =>
            {
                panel.Children.Add(Ui.Caption("Tipo de lápiz"));
                foreach (PenKind k in Enum.GetValues(typeof(PenKind)))
                {
                    var kk = k;
                    panel.Children.Add(Ui.MenuItem(PenNames[(int)k], c.Pen == k, delegate { close(); c.SetPen(kk); }));
                }
                panel.Children.Add(new Border { Height = 1, Margin = new Thickness(4), Background = Brushes.Gray });
                panel.Children.Add(Ui.MenuItem("Convertir dibujos en formas", c.RecognizeShapes, delegate { close(); c.RecognizeShapes = !c.RecognizeShapes; c.SetTool(Tool.Pen); }));
            });
        }

        void ShowShapeMenu()
        {
            OpenList(shapeBtn, (panel, close) =>
            {
                var wrap = new WrapPanel { Width = 190 };
                foreach (ShapeKind k in Enum.GetValues(typeof(ShapeKind)))
                {
                    var kk = k;
                    var b = Ui.Drawn(Shapes.Icon(k), Shapes.Name(k), delegate { close(); c.SetShape(kk); });
                    b.Tag = c.ShapeType == k ? "on" : null;
                    wrap.Children.Add(b);
                }
                panel.Children.Add(wrap);
                panel.Children.Add(Ui.MenuItem("Rellenar formas", c.FillShapes, delegate { close(); c.FillShapes = !c.FillShapes; c.SetTool(Tool.Shape); }));
                panel.Children.Add(Ui.Caption("Las líneas usan el tipo de lápiz:\nneón, discontinuo, punteado o arcoíris."));
            });
        }

        void ShowStickyMenu()
        {
            OpenList(toolButtons[Tool.Sticky], (panel, close) =>
            {
                panel.Children.Add(Ui.Caption("Color de la nota"));
                var row = Ui.Row();
                foreach (var hex in new[] { "FFF176", "FFCC80", "F48FB1", "CE93D8", "90CAF9", "A5D6A7", "E0E0E0" })
                {
                    var col = Hex(hex);
                    var b = new Button { Style = Ui.ButtonStyle, Width = 30, Height = 30, Content = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(col) } };
                    b.Click += delegate { close(); c.StickyColor = col; c.SetTool(Tool.Sticky); };
                    row.Children.Add(b);
                }
                panel.Children.Add(row);
            });
        }

        void ShowGuideMenu()
        {
            OpenList(guideBtn, (panel, close) =>
            {
                panel.Children.Add(Ui.MenuItem("Regla (el lápiz se ajusta a su borde)", c.Ruler != null, delegate { close(); c.ToggleGuide(GuideKind.Ruler); }));
                panel.Children.Add(Ui.MenuItem("Transportador", c.Protractor != null, delegate { close(); c.ToggleGuide(GuideKind.Protractor); }));
                panel.Children.Add(Ui.Caption("Arrastra para mover; usa la rueda para girar."));
            });
        }

        void ShowPresentMenu()
        {
            OpenList(presentBtn, (panel, close) =>
            {
                panel.Children.Add(Ui.MenuItem("Puntero láser (K)", c.Tool == Tool.Laser, delegate { close(); c.SetTool(Tool.Laser); }));
                panel.Children.Add(Ui.MenuItem("Foco: oscurece todo salvo el ratón", c.Tool == Tool.Spotlight, delegate { close(); c.SetTool(Tool.Spotlight); }));
                panel.Children.Add(Ui.MenuItem("Lupa", c.Tool == Tool.Magnifier, delegate { close(); c.SetTool(Tool.Magnifier); }));
                panel.Children.Add(Ui.Caption("La rueda ajusta el foco y la lupa. Esc para salir."));
            });
        }

        void ShowColorMenu()
        {
            OpenList(colorBtn, (panel, close) =>
            {
                var grid = new UniformGrid { Columns = 8, Width = 8 * 26 };
                foreach (var hex in ExtendedColors)
                {
                    var col = Hex(hex);
                    var b = new Button { Style = Ui.ButtonStyle, Width = 24, Height = 24, Content = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Background = new SolidColorBrush(col) } };
                    b.Click += delegate { close(); c.SetColor(col); };
                    grid.Children.Add(b);
                }
                panel.Children.Add(grid);
                panel.Children.Add(Ui.MenuItem("Personalizado…", false, delegate { close(); c.PickCustomColor(); }));
                panel.Children.Add(Ui.MenuItem("Cuentagotas: tomar color de la pantalla", false, delegate { close(); c.SetTool(Tool.Eyedropper); }));
                panel.Children.Add(Ui.Caption("Opacidad del lápiz"));
                var op = new Slider { Minimum = 0.1, Maximum = 1, Value = c.InkOpacity, Focusable = false, Margin = new Thickness(6, 0, 6, 6) };
                op.ValueChanged += delegate { c.InkOpacity = op.Value; c.ApplyTool(); };
                panel.Children.Add(op);
            });
        }

        void ShowExportMenu(UIElement anchor)
        {
            OpenList(anchor, (panel, close) =>
            {
                panel.Children.Add(Ui.MenuItem("Guardar imagen de lo que ves (PNG o JPEG)", false, delegate { close(); c.SaveImage(); }));
                panel.Children.Add(Ui.MenuItem("Exportar todas las hojas a PDF", false, delegate { close(); c.ExportPdf(); }));
                panel.Children.Add(Ui.MenuItem("Capturar la pantalla en una hoja nueva", false, delegate { close(); c.CaptureToSheet(); }));
            });
        }

        void ShowFontMenu()
        {
            OpenList(fontBtn, (panel, close) =>
            {
                foreach (var name in Fonts)
                {
                    var n = name;
                    var item = Ui.MenuItem(name, c.FontName == name, delegate { close(); c.SetFont(n); });
                    ((TextBlock)item.Content).FontFamily = new FontFamily(name);
                    panel.Children.Add(item);
                }
            });
        }

        void ShowTextHighlightMenu(UIElement anchor)
        {
            OpenList(anchor, (panel, close) =>
            {
                var row = Ui.Row();
                foreach (var hex in new[] { "FFF176", "A5D6A7", "90CAF9", "F48FB1", "FFCC80" })
                {
                    var col = Hex(hex);
                    var b = new Button { Style = Ui.ButtonStyle, Width = 30, Height = 30, Content = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(col) } };
                    b.Click += delegate { close(); c.SetTextHighlight(col); };
                    row.Children.Add(b);
                }
                panel.Children.Add(row);
                panel.Children.Add(Ui.MenuItem("Quitar resaltado", false, delegate { close(); c.SetTextHighlight(null); }));
            });
        }

        void SetCompact(bool compact)
        {
            full.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            mini.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        // Arrastre manual: DragMove no pinta bien con WS_EX_NOACTIVATE.
        FrameworkElement Grip()
        {
            var g = new Border { Width = 18, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "Arrastra para mover la barra" };
            g.Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Point start = new Point();
            g.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { start = e.GetPosition(g); g.CaptureMouse(); e.Handled = true; };
            g.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!g.IsMouseCaptured) return;
                var p = e.GetPosition(g);
                Left += p.X - start.X;
                Top += p.Y - start.Y;
            };
            g.MouseLeftButtonUp += delegate { g.ReleaseMouseCapture(); };
            return g;
        }

        void AddTool(Panel row, Tool t, Button b)
        {
            toolButtons[t] = b;
            row.Children.Add(b);
        }

        internal void Refresh()
        {
            if (c.Bar == null) return;
            refreshing = true;
            foreach (var kv in toolButtons) kv.Value.Tag = kv.Key == c.Tool ? "on" : null;
            foreach (var kv in swatches) kv.Value.Tag = kv.Key == c.InkColor ? "on" : null;
            screenBtn.Tag = c.ScreenMode ? "on" : null;
            sheetsBtn.Tag = !c.ScreenMode && !c.WriteMode ? "on" : null;
            writeBtn.Tag = c.WriteMode ? "on" : null;
            toolButtons[Tool.Cursor].Visibility = c.ScreenMode ? Visibility.Visible : Visibility.Collapsed;
            toolButtons[Tool.Hand].Visibility = c.ScreenMode ? Visibility.Collapsed : Visibility.Visible;
            miniCursor.Visibility = toolButtons[Tool.Cursor].Visibility;
            miniCursor.Tag = c.Tool == Tool.Cursor ? "on" : null;
            miniPen.Tag = c.Tool == Tool.Pen ? "on" : null;
            undoBtn.IsEnabled = c.Cur.Undo.Count > 0;
            redoBtn.IsEnabled = c.Cur.Redo.Count > 0;
            monitorBtn.Visibility = c.MonitorCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            shapeBtn.Content = Ui.IconPath(Shapes.Icon(c.ShapeType));
            shapeBtn.ToolTip = Shapes.Name(c.ShapeType);
            toolButtons[Tool.Pen].ToolTip = PenNames[(int)c.Pen] + " (P)";
            guideBtn.Tag = c.Ruler != null || c.Protractor != null ? "on" : null;
            presentBtn.Tag = c.Tool == Tool.Laser || c.Tool == Tool.Spotlight || c.Tool == Tool.Magnifier ? "on" : null;
            colorDot.Fill = new SolidColorBrush(c.InkColor);
            fontBtn.Content = c.FontName;
            textRow.Visibility = c.Tool == Tool.Text || c.Tool == Tool.Sticky ? Visibility.Visible : Visibility.Collapsed;

            double min = 1, max = 40;
            bool hasSize = true;
            switch (c.Tool)
            {
                case Tool.Highlighter: min = 8; max = 60; break;
                case Tool.Eraser: min = 8; max = 120; break;
                case Tool.Text:
                case Tool.Sticky: min = 8; max = 96; break;
                case Tool.Pen:
                case Tool.Shape: break;
                default: hasSize = false; break;
            }
            slider.Minimum = min;
            slider.Maximum = max;
            slider.Value = c.CurrentSize;
            slider.IsEnabled = hasSize;
            sizeLabel.Text = hasSize ? ((int)c.CurrentSize).ToString() : "";
            refreshing = false;
        }
    }

#if !SELFTEST
    static class Program
    {
        [STAThread]
        static void Main()
        {
            var app = new Application();
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                MessageBox.Show("Ocurrió un error:\n" + e.Exception.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            app.Run(new CanvasWindow());
        }
    }
#endif
}
