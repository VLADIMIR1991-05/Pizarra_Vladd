using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pizarra
{
    static class Program
    {
        static int fails;
        static string dir;
        static StreamWriter log;

        static void Check(string name, bool ok)
        {
            log.WriteLine((ok ? "OK    " : "FALLO ") + name);
            log.Flush();
            if (!ok) fails++;
        }

        static void DrawShape(CanvasWindow c, ShapeKind k, double x1, double y1, double x2, double y2)
        {
            c.SetShape(k);
            c.BeginShape(new Point(x1, y1));
            c.UpdateShape(new Point((x1 + x2) / 2, (y1 + y2) / 2), false);
            c.UpdateShape(new Point(x2, y2), false);
            c.EndShape();
        }

        // Simula un trazo real: entra por la misma vía que el lápiz.
        static Stroke Ink(CanvasWindow c, IEnumerable<Point> pts)
        {
            var s = new Stroke(new StylusPointCollection(pts), c.Ink.DefaultDrawingAttributes.Clone());
            c.Ink.Strokes.Add(s);
            c.ProcessNewStroke(s);
            return s;
        }

        static List<Point> Wave(double x, double y, double w)
        {
            var l = new List<Point>();
            for (int i = 0; i <= 60; i++) l.Add(new Point(x + w * i / 60, y + 18 * Math.Sin(i / 6.0)));
            return l;
        }

        static List<Point> Hand(IEnumerable<Point> pts, double noise)
        {
            var r = new Random(7);
            return pts.Select(p => new Point(p.X + (r.NextDouble() - 0.5) * noise, p.Y + (r.NextDouble() - 0.5) * noise)).ToList();
        }

        static List<Point> Dense(params Point[] corners)
        {
            var l = new List<Point>();
            for (int i = 1; i < corners.Length; i++)
                for (int k = 0; k < 25; k++)
                    l.Add(corners[i - 1] + (corners[i] - corners[i - 1]) * (k / 25.0));
            l.Add(corners[corners.Length - 1]);
            return l;
        }

        static void Type(RichTextBox r, string text)
        {
            r.CaretPosition.InsertTextInRun(text);
        }

        static void Pump(CanvasWindow c)
        {
            c.UpdateLayout();
            c.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
        }

        static void Shot(CanvasWindow c, string name)
        {
            Pump(c);
            CanvasWindow.WriteImage(c.RenderSheet(), System.IO.Path.Combine(dir, name));
        }

        static void RenderBar(CanvasWindow c, string name)
        {
            c.Bar.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)c.Bar.ActualWidth, (int)c.Bar.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, c.Bar.ActualWidth, c.Bar.ActualHeight));
                dc.DrawRectangle(new VisualBrush((Visual)c.Bar.Content), null, new Rect(0, 0, c.Bar.ActualWidth, c.Bar.ActualHeight));
            }
            bmp.Render(dv);
            CanvasWindow.WriteImage(bmp, System.IO.Path.Combine(dir, name));
        }

        static void Run(CanvasWindow c)
        {
            try
            {
                Check("arranca en hojas", !c.ScreenMode && c.Sheets.Count == 1);
                Check("no atraviesa clics en hojas", !c.IsClickThrough);

                // --- Galería de lápices ---
                double y = 150;
                foreach (PenKind k in Enum.GetValues(typeof(PenKind)))
                {
                    if (k == PenKind.Desvanecer) continue;
                    c.SetPen(k);
                    c.CurrentSize = 5;
                    if (k != PenKind.Boligrafo) c.SetColor(Color.FromRgb(0x1E, 0x88, 0xE5));
                    Ink(c, Wave(60, y, 520));
                    y += 62;
                }
                c.SetColor(Color.FromRgb(0x20, 0x20, 0x20));
                c.SetTool(Tool.Highlighter);
                c.SetColor(Color.FromRgb(0xFD, 0xD8, 0x35));
                Ink(c, Wave(60, y, 520));
                c.SetColor(Color.FromRgb(0x20, 0x20, 0x20));
                Check("10 lápices + resaltador", c.Ink.Strokes.Count == 11);
                Check("neón es trazo con estilo", c.Ink.Strokes[5] is RichStroke);
                Check("punteado es trazo con estilo", c.Ink.Strokes[7] is RichStroke);
                Check("pincel afina extremos", c.Ink.Strokes[3].StylusPoints[0].PressureFactor < 0.2f);
                c.Undo();
                Check("deshacer trazo", c.Ink.Strokes.Count == 10);
                c.Redo();
                Check("rehacer mantiene estilo", c.Ink.Strokes[5] is RichStroke && c.Ink.Strokes.Count == 11);

                // --- Formas ---
                double sx = 640, sy = 150;
                int count = 0;
                foreach (ShapeKind k in Enum.GetValues(typeof(ShapeKind)))
                {
                    c.SetPen(PenKind.Boligrafo);
                    c.CurrentSize = 3;
                    c.FillShapes = k == ShapeKind.Hexagon || k == ShapeKind.Star;
                    DrawShape(c, k, sx, sy, sx + 130, sy + 100);
                    sx += 160;
                    if (++count % 5 == 0) { sx = 640; sy += 130; }
                }
                c.FillShapes = false;
                Check("13 formas", c.Ink.Strokes.Count == 24);
                c.SetPen(PenKind.Discontinuo);
                DrawShape(c, ShapeKind.Rect, 640, 560, 800, 660);
                Check("forma discontinua con estilo", c.Ink.Strokes[24] is RichStroke);
                int before = c.Cur.Undo.Count;
                DrawShape(c, ShapeKind.Line, 10, 10, 10, 10);
                Check("clic sin arrastrar no crea forma", c.Ink.Strokes.Count == 25 && c.Cur.Undo.Count == before);
                c.SetShape(ShapeKind.Rect);
                c.BeginShape(new Point(100, 100));
                c.UpdateShape(new Point(300, 150), true);
                c.EndShape();
                var pb = c.Ink.Strokes[25].GetBounds();
                Check("Mayús hace proporcional", Math.Abs(pb.Width - pb.Height) < 1);
                c.Undo();
                c.SetPen(PenKind.Boligrafo);
                Shot(c, "lapices-formas.png");

                // --- Reconocimiento de formas ---
                c.AddSheet();
                c.RecognizeShapes = true;
                c.SetPen(PenKind.Boligrafo);
                var circle = Enumerable.Range(0, 80).Select(i => new Point(300 + 100 * Math.Cos(i * 6.283 / 79), 300 + 90 * Math.Sin(i * 6.283 / 79))).ToList();
                var s1 = Hand(circle, 5);
                Ink(c, s1);
                Check("círculo reconocido (97 puntos)", c.Ink.Strokes[0].StylusPoints.Count == 97);
                Ink(c, Hand(Dense(new Point(500, 200), new Point(700, 200), new Point(700, 350), new Point(500, 350), new Point(500, 202)), 4));
                Check("rectángulo reconocido", c.Ink.Strokes[1].StylusPoints.Count == 5);
                Ink(c, Hand(Dense(new Point(800, 350), new Point(900, 180), new Point(1000, 350), new Point(802, 350)), 4));
                Check("triángulo reconocido", c.Ink.Strokes[2].StylusPoints.Count == 4);
                Ink(c, Hand(Dense(new Point(200, 500), new Point(800, 540)), 3));
                Check("línea reconocida", c.Ink.Strokes[3].StylusPoints.Count == 2);
                Ink(c, Wave(200, 650, 500));
                Check("garabato no se convierte", c.Ink.Strokes[4].StylusPoints.Count > 20);
                c.RecognizeShapes = false;
                Shot(c, "reconocer.png");

                // --- Regla ---
                c.AddSheet();
                c.ToggleGuide(GuideKind.Ruler);
                c.ToggleGuide(GuideKind.Protractor);
                c.Protractor.Tx = 300; c.Protractor.Ty = 500; c.Protractor.Angle = 0;
                c.Protractor.InvalidateVisual();
                c.Protractor.RenderTransform = c.Protractor.RenderTransform;
                Pump(c);
                Point ea, eb;
                c.Ruler.Edge(out ea, out eb);
                c.SetPen(PenKind.Boligrafo);
                var near = Hand(Dense(new Point(ea.X + 50, ea.Y - 12), new Point(ea.X + 500, ea.Y - 12)), 6);
                var st = Ink(c, near);
                bool flat = st.StylusPoints.All(p => Math.Abs(p.Y - ea.Y) < 0.5);
                Check("el trazo se ajusta a la regla", flat);
                var far = Wave(100, 700, 300);
                var sf = Ink(c, far);
                Check("trazo lejano no se ajusta", sf.StylusPoints.Any(p => Math.Abs(p.Y - 700) > 5));
                // Las guías se dibujan fuera del área exportada: captura aparte.
                {
                    var bmp = new RenderTargetBitmap((int)c.ActualWidth, (int)c.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render((Visual)c.Content);
                    CanvasWindow.WriteImage(bmp, System.IO.Path.Combine(dir, "guias.png"));
                }
                c.ToggleGuide(GuideKind.Ruler);
                c.ToggleGuide(GuideKind.Protractor);
                Check("guías se quitan", c.Ruler == null && c.Protractor == null);

                // --- Modo escritura ---
                c.AddSheet();
                var doc = c.EnterWriteMode();
                Check("modo escritura activo", c.WriteMode && c.Tool == Tool.Text);
                Check("el foco queda en el documento", Keyboard.FocusedElement == doc);
                Type(doc, "Título con ñ y acentos");
                doc.SelectAll();
                c.Format(EditingCommands.ToggleBold);
                c.CurrentSize = 34;
                doc.Selection.Select(doc.Document.ContentEnd, doc.Document.ContentEnd);
                c.Format(EditingCommands.ToggleBold);
                c.CurrentSize = 18;
                doc.CaretPosition = doc.Document.ContentEnd;
                doc.CaretPosition.InsertParagraphBreak();
                doc.CaretPosition = doc.Document.ContentEnd;
                doc.CaretPosition.InsertTextInRun("Segundo párrafo escrito con el teclado, con varias palabras para comprobar el ajuste de línea dentro de la página de texto.");
                Check("barra de formato visible", c.WriteMode);
                RenderBar(c, "barra-texto.png");
                Pump(c);
                c.Ink.Focus();
                Check("documento se conserva", c.Ink.Children.OfType<RichTextBox>().Count() == 1);
                Check("escritura en historial", c.Cur.Undo.Count >= 1);
                var doc2 = c.EnterWriteMode();
                Check("reabre el mismo documento", doc2 == doc && c.Ink.Children.OfType<RichTextBox>().Count() == 1);
                c.SetTool(Tool.Pen);
                Check("sale de escritura", !c.WriteMode);
                Shot(c, "escritura.png");

                // --- Texto y notas ---
                c.AddSheet();
                c.SetTool(Tool.Text);
                c.SetFont("Georgia");
                var t1 = c.PlaceText(new Point(120, 200));
                Type(t1, "Cuadro de texto Georgia");
                c.Ink.Focus();
                c.SetFont("Segoe UI");
                Check("texto queda", c.Ink.Children.OfType<RichTextBox>().Count() == 1);
                int u = c.Cur.Undo.Count;
                c.PlaceText(new Point(300, 650));
                c.Ink.Focus();
                Pump(c);
                Check("texto vacío se descarta", c.Ink.Children.OfType<RichTextBox>().Count() == 1 && c.Cur.Undo.Count == u);
                c.Undo();
                Check("deshacer texto", c.Ink.Children.OfType<RichTextBox>().Count() == 0);
                c.Redo();
                var t2 = c.Ink.Children.OfType<RichTextBox>().First();
                Check("rehacer conserva texto", CanvasWindow.TextOf(t2) == "Cuadro de texto Georgia");
                c.SetTool(Tool.Sticky);
                var n1 = c.PlaceSticky(new Point(500, 350));
                Type(n1, "Nota adhesiva");
                c.Ink.Focus();
                c.StickyColor = Color.FromRgb(0x90, 0xCA, 0xF9);
                var n2 = c.PlaceSticky(new Point(780, 350));
                c.Ink.Focus();
                Check("nota vacía se conserva", c.Ink.Children.OfType<RichTextBox>().Count() == 3);
                c.SetTool(Tool.Pen);
                Check("textos no bloquean el lápiz", c.Ink.Children.OfType<RichTextBox>().All(r => !r.IsHitTestVisible));
                Shot(c, "notas.png");

                // --- Imagen ---
                var src = new RenderTargetBitmap(200, 100, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(Brushes.Orange, null, new Rect(0, 0, 200, 100));
                src.Render(dv);
                string png = System.IO.Path.Combine(dir, "entrada.png");
                CanvasWindow.WriteImage(src, png);
                c.InsertImageFile(png);
                Check("imagen insertada", c.Ink.Children.OfType<Image>().Count() == 1);
                c.Undo();
                Check("deshacer imagen", c.Ink.Children.OfType<Image>().Count() == 0);
                c.Redo();
                Check("rehacer imagen", c.Ink.Children.OfType<Image>().Count() == 1);

                // --- Selección ---
                Ink(c, Wave(100, 500, 200));
                c.SetTool(Tool.Select);
                c.Ink.Select(new StrokeCollection(new[] { c.Ink.Strokes[0] }), new UIElement[] { c.Ink.Children.OfType<Image>().First() });
                Check("seleccionar trazo e imagen", c.Ink.GetSelectedStrokes().Count == 1 && c.Ink.GetSelectedElements().Count == 1);
                c.DeleteSelection();
                Check("Supr borra selección", c.Ink.Strokes.Count == 0 && c.Ink.Children.OfType<Image>().Count() == 0);
                c.Undo();
                Check("deshacer borrado", c.Ink.Strokes.Count == 1 && c.Ink.Children.OfType<Image>().Count() == 1);

                // --- Borrador parcial mantiene estilo ---
                c.SetTool(Tool.Pen);
                c.SetPen(PenKind.Neon);
                var ns = Ink(c, Dense(new Point(100, 700), new Point(800, 700)));
                c.SetPen(PenKind.Boligrafo);
                c.SetTool(Tool.Eraser);
                c.Ink.Strokes.Erase(new[] { new Point(400, 700), new Point(420, 700) }, new EllipseStylusShape(20, 20));
                c.MarkDirty(); c.Commit();
                Check("borrar neón por partes conserva estilo", c.Ink.Strokes.Count(s => s is RichStroke) >= 2);
                c.SetTool(Tool.Pen);

                // --- Tinta que se desvanece y láser ---
                c.AddSheet();
                c.SetPen(PenKind.Desvanecer);
                int undoBefore = c.Cur.Undo.Count;
                Ink(c, Wave(100, 300, 200));
                Check("desvanecer no entra en historial", c.Cur.Undo.Count == undoBefore && c.FadingCount == 1);
                c.SetTool(Tool.Laser);
                Ink(c, Wave(100, 400, 200));
                Check("láser no entra en historial", c.Cur.Undo.Count == undoBefore && c.FadingCount == 2);
                c.SetPen(PenKind.Boligrafo);
                Ink(c, Wave(100, 500, 200));
                Check("historial excluye tinta temporal", c.Cur.Last.Strokes.Count == 1);
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (c.FadingCount > 0 && DateTime.UtcNow < deadline)
                {
                    c.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                    System.Threading.Thread.Sleep(30);
                }
                Check("tinta temporal desaparece", c.FadingCount == 0 && c.Ink.Strokes.Count == 1);

                // --- Fondos ---
                foreach (Paper p in Enum.GetValues(typeof(Paper)))
                {
                    c.SetPaper(p);
                    Shot(c, "papel-" + p + ".png");
                }
                c.SetPaper(Paper.Negra);
                Check("tinta no invisible en negra", c.InkColor != Color.FromRgb(0x20, 0x20, 0x20));
                c.SetPaper(Paper.Blanca);

                // --- Zoom y desplazamiento ---
                c.ResetView();
                c.ZoomAt(new Point(200, 200), 2);
                Check("zoom", c.Cur.Zoom == 2);
                var inkPt = c.ViewToInk(new Point(200, 200));
                Check("zoom se centra en el cursor", Math.Abs(inkPt.X - 200) < 0.01 && Math.Abs(inkPt.Y - 200) < 0.01);
                c.PanBy(-500, -300);
                Check("desplazar", c.Cur.PanX < -200);
                c.PanBy(9000, 9000);
                Check("no pasa del origen", c.Cur.PanX == 0 && c.Cur.PanY == 0);
                c.SetTool(Tool.Text);
                c.PanBy(-400, -400);
                var tz = c.PlaceText(c.ViewToInk(new Point(300, 300)));
                Type(tz, "Con zoom");
                c.Ink.Focus();
                c.SetTool(Tool.Pen);
                c.ResetView();

                // --- Plantillas y gestión de hojas ---
                foreach (var tpl in new[] { "kanban", "dafo", "cornell", "semana" })
                {
                    int sc = c.Sheets.Count;
                    c.ApplyTemplate(tpl);
                    Check("plantilla " + tpl, c.Sheets.Count == sc + 1 && c.Ink.Strokes.Count > 0 && c.Ink.Children.Count > 0);
                    Shot(c, "plantilla-" + tpl + ".png");
                }
                int total = c.Sheets.Count, idx = c.SheetIndex, str = c.Ink.Strokes.Count;
                c.DuplicateSheet();
                Check("duplicar hoja", c.Sheets.Count == total + 1 && c.Ink.Strokes.Count == str);
                c.ClearCurrent();
                c.GoToSheet(idx);
                Check("duplicado independiente", c.Ink.Strokes.Count == str);
                c.GoToSheet(idx + 1);
                c.MoveSheet(-1);
                Check("mover hoja", c.SheetIndex == idx);
                c.MoveSheet(1);
                c.RemoveCurrentSheet();
                Check("eliminar hoja", c.Sheets.Count == total);

                // --- Guardar y abrir ---
                c.GoToSheet(0);
                c.SetTool(Tool.Pen);
                int s0 = c.Ink.Strokes.Count;
                int rich0 = c.Ink.Strokes.Count(s => s is RichStroke);
                string file = System.IO.Path.Combine(dir, "prueba.pizarra");
                c.WriteNotebook(file);
                c.WriteNotebook(file);
                var loaded = CanvasWindow.ReadNotebook(file);
                Check("cuaderno: nº de hojas", loaded.Count == c.Sheets.Count);
                Check("cuaderno: trazos", loaded[0].Strokes.Count == s0);
                Check("cuaderno: estilos conservados", loaded[0].Strokes.Count(s => s is RichStroke) == rich0 && rich0 > 0);
                c.OpenPath(file);
                Check("abrir restaura", c.Ink.Strokes.Count == s0 && !c.Modified && !c.ScreenMode);
                c.GoToSheet(3);
                var opened = c.Ink.Children.OfType<RichTextBox>().FirstOrDefault();
                Check("abrir: texto con ñ", opened != null && CanvasWindow.TextOf(opened).StartsWith("Título con ñ y acentos"));
                Check("abrir: tipo de documento", opened != null && ((Item)opened.Tag).Kind == ItemKind.Doc);
                c.GoToSheet(4);
                Check("abrir: notas e imagen", c.Ink.Children.OfType<Image>().Count() == 1 && c.Ink.Children.OfType<RichTextBox>().Count() == 3);
                c.GoToSheet(0);

                // Formato antiguo PZR1
                string old = System.IO.Path.Combine(dir, "antiguo.pizarra");
                using (var w = new BinaryWriter(File.Create(old)))
                {
                    w.Write("PZR1"); w.Write(1); w.Write(1); w.Write(0); w.Write(1);
                    w.Write(10.0); w.Write(20.0); w.Write(double.NaN); w.Write(double.NaN); w.Write(24.0);
                    w.Write((byte)255); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
                    w.Write("Texto antiguo ñ");
                }
                var oldList = CanvasWindow.ReadNotebook(old);
                Check("lee cuadernos de la versión anterior", oldList.Count == 1 && oldList[0].Paper == Paper.Cuadriculada && oldList[0].Items.Count == 1);

                // --- PDF ---
                string pdf = System.IO.Path.Combine(dir, "prueba.pdf");
                int cur = c.SheetIndex;
                c.ExportPdfTo(pdf);
                var bytes = File.ReadAllBytes(pdf);
                string head = System.Text.Encoding.ASCII.GetString(bytes, 0, 8), all = System.Text.Encoding.ASCII.GetString(bytes);
                Check("PDF válido", head == "%PDF-1.4" && all.TrimEnd().EndsWith("%%EOF"));
                Check("PDF con todas las hojas", all.Contains("/Count " + c.Sheets.Count + " "));
                Check("PDF no altera la hoja actual", c.SheetIndex == cur);

                // --- Modo pantalla ---
                c.SetMode(true);
                Check("pantalla: lienzo propio y encima", c.ScreenMode && c.Topmost && c.Bar.Topmost && c.Ink.Strokes.Count == 0);
                var bg = c.Ink.Background as SolidColorBrush;
                Check("pantalla: fondo casi transparente", bg != null && bg.Color.A == 1 && c.Host.Background == null);
                c.SetPen(PenKind.Neon);
                Ink(c, Wave(200, 300, 400));
                c.SetPen(PenKind.Boligrafo);
                bool mod = c.Modified;
                c.SetTool(Tool.Cursor);
                Check("puntero: clics atraviesan", c.IsClickThrough);
                RenderBar(c, "barra-pantalla.png");
                c.ToggleHotkey();
                Check("atajo vuelve a dibujar", !c.IsClickThrough && c.Tool == Tool.Pen);
                c.ToggleHotkey();
                Check("atajo pasa a puntero", c.IsClickThrough);
                c.SetTool(Tool.Spotlight);
                Check("foco no atraviesa", !c.IsClickThrough);
                c.SetTool(Tool.Magnifier);
                c.SetTool(Tool.Eyedropper);
                Check("cuentagotas lee color", c.PickPixel(new Point(5, 5)).HasValue);
                c.SetTool(Tool.Pen);
                Check("cuentagotas libera captura", c.PickPixel(new Point(5, 5)) == null);
                c.SetMode(false);
                Check("hojas: no atraviesa, no encima", !c.IsClickThrough && !c.Topmost && !c.Bar.Topmost && c.Tool != Tool.Cursor);
                RenderBar(c, "barra-hojas.png");
                c.SetMode(true);
                Check("pantalla conserva dibujo", c.Ink.Strokes.Count == 1);
                c.ClearCurrent();
                Check("borrar todo", c.Ink.Strokes.Count == 0);
                c.Undo();
                Check("deshacer borrar todo", c.Ink.Strokes.Count == 1);
                c.SetMode(false);

                // --- Captura a hoja (solo en memoria y en la hoja de prueba) ---
                int sh = c.Sheets.Count;
                c.CaptureToSheet();
                Check("captura crea hoja con imagen", c.Sheets.Count == sh + 1 && c.Ink.Children.OfType<Image>().Count() == 1);
                c.RemoveCurrentSheet();

                if (c.MonitorCount > 1)
                {
                    double l = c.Left;
                    c.NextMonitor();
                    Check("cambia de monitor", c.Left != l);
                    c.NextMonitor();
                    Check("vuelve al monitor", c.Left == l);
                }
                log.WriteLine("Fallos: " + fails);
            }
            catch (Exception ex)
            {
                log.WriteLine("EXCEPCION " + ex);
                fails++;
            }
            log.Flush();
            c.Modified = false;
            Application.Current.Shutdown(fails);
        }

        [STAThread]
        static int Main(string[] a)
        {
            dir = a[0];
            CanvasWindow.AutosaveEnabled = false;
            log = new StreamWriter(System.IO.Path.Combine(dir, "resultado.txt"));
            var app = new Application();
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                log.WriteLine("EXCEPCION UI " + e.Exception); log.Flush(); e.Handled = true; fails++;
            };
            var c = new CanvasWindow();
            // Opacidad 0: no tapa ni intercepta nada de tu escritorio durante la prueba.
            c.Opacity = 0;
            c.Bar.Opacity = 0;
            c.Loaded += delegate { c.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate { Run(c); })); };
            return app.Run(c);
        }
    }
}
