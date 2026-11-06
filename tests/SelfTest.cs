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

        // Mide el temblor: suma de cambios bruscos entre puntos vecinos.
        static double Rough(List<Point> p)
        {
            double sum = 0;
            for (int i = 1; i < p.Count - 1; i++)
                sum += (new Point((p[i - 1].X + p[i + 1].X) / 2, (p[i - 1].Y + p[i + 1].Y) / 2) - p[i]).Length;
            return sum;
        }

        // Palabra HOLA escrita con trazos separados, como en mayúsculas de imprenta.
        static IEnumerable<List<Point>> HolaLetters()
        {
            double x = 300, y = 300, h = 90, w = 60;
            // H
            yield return Dense(new Point(x, y), new Point(x, y + h));
            yield return Dense(new Point(x + w, y), new Point(x + w, y + h));
            yield return Dense(new Point(x, y + h / 2), new Point(x + w, y + h / 2));
            // O
            x += 90;
            yield return Enumerable.Range(0, 60).Select(i => new Point(x + w / 2 + w / 2 * Math.Sin(i * 6.283 / 59), y + h / 2 - h / 2 * Math.Cos(i * 6.283 / 59))).ToList();
            // L
            x += 90;
            yield return Dense(new Point(x, y), new Point(x, y + h), new Point(x + w, y + h));
            // A
            x += 85;
            yield return Dense(new Point(x, y + h), new Point(x + w / 2, y));
            yield return Dense(new Point(x + w / 2, y), new Point(x + w, y + h));
            yield return Dense(new Point(x + w * 0.2, y + h * 0.62), new Point(x + w * 0.8, y + h * 0.62));
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

        // Interfaz completa tal como se compone en pantalla: lienzo, paneles y barra.
        static void ShotUi(CanvasWindow c, string name)
        {
            Pump(c);
            c.Bar.UpdateLayout();
            var main = (FrameworkElement)c.Content;
            var bar = (FrameworkElement)c.Bar.Content;
            double w = main.ActualWidth, h = main.ActualHeight, bw = c.Bar.ActualWidth, bh = c.Bar.ActualHeight;
            var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(main), null, new Rect(0, 0, w, h));
                if (c.Bar.IsVisible) dc.DrawRectangle(new VisualBrush(bar), null, new Rect((w - bw) / 2, 4, bw, bh));
            }
            bmp.Render(dv);
            CanvasWindow.WriteImage(bmp, System.IO.Path.Combine(dir, name));
        }

        static void ShotPopup(CanvasWindow c, string name)
        {
            var el = (FrameworkElement)c.Bar.LastPop.Child;
            el.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)el.ActualWidth + 28, (int)el.ActualHeight + 28, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(CanvasWindow.PaperColor(Paper.Cyber)), null, new Rect(0, 0, bmp.Width, bmp.Height));
                dc.DrawRectangle(new VisualBrush(el), null, new Rect(14, 14, el.ActualWidth, el.ActualHeight));
            }
            bmp.Render(dv);
            CanvasWindow.WriteImage(bmp, System.IO.Path.Combine(dir, name));
            c.Bar.LastPop.IsOpen = false;
        }

        // Pulsación real: recorre el mismo camino que una tecla del usuario.
        static void Press(CanvasWindow c, Key k)
        {
            c.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(c), 0, k) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Pump(c);
        }

        static void RunPro(CanvasWindow c)
        {
            // --- Tema y hoja de muestra ---
            c.GoToSheet(0);
            c.AddSheet();
            c.SetPaper(Paper.Cyber);
            c.SetColor(Colors.White);
            Check("hoja cyber es oscura", CanvasWindow.IsDark(Paper.Cyber) && CanvasWindow.IsDark(Paper.Plano));
            Check("hoja nueva por defecto cyber", new Sheet().Paper == Paper.Cyber);
            c.SetPen(PenKind.Plasma);
            c.PenSize = 8;
            c.ApplyTool();
            var plasma = Ink(c, Wave(160, 240, 620));
            Check("plasma es trazo enriquecido", c.Ink.Strokes.Last() is RichStroke);
            c.SetPen(PenKind.Neon);
            c.SetColor(ToolbarWindow.Palette[6]);
            c.PenSize = 5;
            c.ApplyTool();
            Ink(c, Wave(160, 340, 620));
            c.SetColor(ToolbarWindow.Palette[8]);
            DrawShape(c, ShapeKind.Hexagon, 840, 200, 1080, 420);
            c.SetColor(ToolbarWindow.Palette[5]);
            DrawShape(c, ShapeKind.Arrow, 200, 480, 700, 480);
            c.SetTool(Tool.Text);
            c.SetColor(Colors.White);
            var label = c.PlaceText(new Point(200, 520));
            Type(label, "Pizarra pro con ñ & <acentos>");
            c.Ink.Focus();
            c.SetPen(PenKind.Boligrafo);
            c.PenSize = 3;
            c.ApplyTool();
            Pump(c);
            ShotUi(c, "ui-principal.png");
            RenderBar(c, "ui-barra.png");
            int demo = c.SheetIndex, demoStrokes = c.Ink.Strokes.Count;

            // --- Menús con el tema aplicado ---
            c.Bar.ShowPenMenu();
            ShotPopup(c, "ui-menu-lapices.png");
            c.Bar.ShowProMenu();
            ShotPopup(c, "ui-menu-pro.png");

            // --- Paleta de comandos ---
            var cmds = c.BuildCommands();
            Check("paleta: comandos sin nombre repetido", cmds.Select(x => x.Name).Distinct().Count() == cmds.Count && cmds.Count > 60);
            Check("paleta: ignora acentos", CanvasWindow.FilterCommands(cmds, "region")[0].Name == "Capturar una región de la pantalla");
            Check("paleta: varias palabras", CanvasWindow.FilterCommands(cmds, "lapiz plasma").Single().Name == "Lápiz: Plasma");
            Check("paleta: primero lo que empieza igual", CanvasWindow.FilterCommands(cmds, "hoja")[0].Name.StartsWith("Hoja"));
            Check("paleta: sin resultados", CanvasWindow.FilterCommands(cmds, "zzzz").Count == 0);
            c.ShowPalette();
            Pump(c);
            ShotUi(c, "ui-paleta.png");
            Check("paleta: abierta", c.PaletteOpen);
            Press(c, Key.Down);
            Check("paleta: flecha mueve", c.PaletteIndex == 1);
            Press(c, Key.Up);
            Press(c, Key.Up);
            Check("paleta: flecha da la vuelta", c.PaletteIndex == 8);
            c.PaletteBox.Text = "minimapa";
            Pump(c);
            bool mapOn = c.MinimapOn;
            Press(c, Key.Enter);
            Check("paleta: Enter ejecuta y cierra", c.MinimapOn != mapOn && !c.PaletteOpen);
            c.ToggleMinimap();
            c.ShowPalette();
            Press(c, Key.Escape);
            Check("paleta: Esc cierra sin ejecutar", !c.PaletteOpen && c.MinimapOn == mapOn);

            // --- Menú radial ---
            c.ShowRadial(new Point(5, 5));
            Pump(c);
            Check("radial: abierto", c.RadialOpen);
            Press(c, Key.Escape);
            Check("radial: Esc cierra", !c.RadialOpen);
            c.ShowRadial(new Point(640, 330));
            Pump(c);
            ShotUi(c, "ui-radial.png");
            c.RadialPick(1);
            Check("radial: elige herramienta y cierra", c.Tool == Tool.Highlighter && !c.RadialOpen);
            c.SetTool(Tool.Pen);

            // --- Vista y minimapa ---
            Rect content = c.ContentBounds();
            Rect dom = Minimap.Domain(content, c.ViewRect());
            Check("minimapa: abarca contenido y vista", dom.Contains(content) && dom.Contains(c.ViewRect()));
            Check("minimapa: visible con contenido", c.Map.Visibility == Visibility.Visible);
            c.CenterViewOn(new Point(3000, 2000));
            Check("centrar vista", Math.Abs(c.ViewToInk(new Point(c.Host.ActualWidth / 2, c.Host.ActualHeight / 2)).X - 3000) < 1);
            c.FitContent();
            Check("ajustar: todo el contenido a la vista", c.ViewRect().Contains(content));
            c.ResetView();
            c.Map.UpdateView();

            // --- Favoritos ---
            var fmt = CanvasWindow.FormatPreset(new PenPreset { Pen = PenKind.Gotica, Ink = Color.FromRgb(1, 2, 3), Size = 7.5, Trace = TraceMode.Curva, Opacity = 0.5 });
            var back = CanvasWindow.ParsePreset(fmt);
            Check("favorito: ida y vuelta", back != null && back.Pen == PenKind.Gotica && back.Ink == Color.FromRgb(1, 2, 3) && back.Size == 7.5 && back.Trace == TraceMode.Curva && back.Opacity == 0.5);
            Check("favorito: archivo dañado se ignora", CanvasWindow.ParsePreset("9999;1;2;3;4;0;1") == null && CanvasWindow.ParsePreset("basura") == null && CanvasWindow.ParsePreset("0;1;2;3;9000;0;1") == null);
            c.ApplyPreset(1);
            Check("favorito: aplica lápiz", c.Pen == PenKind.Neon && c.Tool == Tool.Pen && c.PenSize == 5);
            c.SetColor(ToolbarWindow.Palette[9]);
            c.StorePreset(4);
            Check("favorito: guarda el lápiz actual", c.Presets[4].Pen == PenKind.Neon && c.Presets[4].Ink == ToolbarWindow.Palette[9]);
            c.SetPen(PenKind.Boligrafo);

            // --- Selección ---
            c.SelectAll();
            int selected = c.Ink.GetSelectedStrokes().Count;
            Check("seleccionar todo", selected == demoStrokes && c.Ink.GetSelectedElements().Count == 1);
            int texts = c.Ink.Children.Count;
            c.DuplicateSelection();
            Check("duplicar selección", c.Ink.Strokes.Count == demoStrokes * 2 && c.Ink.Children.Count == texts * 2);
            Check("duplicado desplazado", c.Ink.GetSelectedStrokes().Count == demoStrokes);
            c.Undo();
            Check("deshacer duplicado", c.Ink.Strokes.Count == demoStrokes && c.Ink.Children.Count == texts);
            c.SelectAll();
            c.AlignSelection("izquierda");
            var lefts = c.Ink.GetSelectedStrokes().Select(s => s.GetBounds().Left).ToList();
            Check("alinear a la izquierda", lefts.Max() - lefts.Min() < 0.01);
            c.Undo();
            c.SelectAll();
            c.AlignSelection("abajo");
            var bottoms = c.Ink.GetSelectedStrokes().Select(s => s.GetBounds().Bottom).ToList();
            Check("alinear abajo", bottoms.Max() - bottoms.Min() < 0.01);
            c.Undo();
            c.SetTool(Tool.Pen);
            Check("selección no altera tras deshacer", c.Ink.Strokes.Count == demoStrokes);

            // --- Miniaturas ---
            c.ToggleSheetPanel();
            Check("miniaturas: todas generadas", c.SheetPanelOpen && c.Sheets.All(s => s.Thumb != null));
            Check("miniaturas: no cambian la hoja", c.SheetIndex == demo && c.Ink.Strokes.Count == demoStrokes && c.Cur.Undo.Count > 0);
            Pump(c);
            ShotUi(c, "ui-miniaturas.png");
            Ink(c, Wave(160, 620, 300));
            Check("miniatura se invalida al editar", c.Cur.Thumb == null);
            c.Undo();
            c.ToggleSheetPanel();
            Press(c, Key.F2);
            Check("F2 abre miniaturas", c.SheetPanelOpen);
            Press(c, Key.F2);
            Check("F2 cierra miniaturas", !c.SheetPanelOpen);

            // --- Presentación ---
            int undoCount = c.Cur.Undo.Count;
            Press(c, Key.F5);
            Check("presentación: oculta la barra", c.Presenting && !c.Bar.IsVisible && c.Map.Visibility != Visibility.Visible);
            Pump(c);
            ShotUi(c, "ui-presentacion.png");
            int at = c.SheetIndex;
            Press(c, Key.Right);
            Check("presentación: flecha avanza", c.SheetIndex == at + 1);
            Press(c, Key.Left);
            Check("presentación: flecha retrocede", c.SheetIndex == at);
            Press(c, Key.Escape);
            Check("presentación: Esc restaura", !c.Presenting && c.Bar.IsVisible && c.Map.Visibility == Visibility.Visible);

            // --- Repetición ---
            int before = c.Ink.Strokes.Count;
            bool modBefore = c.Modified;
            c.ReplayBegin();
            Check("repetición: abierta", c.Replaying && c.ReplayStrokeCount == 0);
            c.ReplayAdvance(1);
            Check("repetición: avanza por puntos", c.ReplayStrokeCount == 1);
            Pump(c);
            int guard = 0;
            while (c.ReplayAdvance(25) && guard++ < 100000) { }
            Check("repetición: dibuja todos los trazos", c.ReplayStrokeCount == before && c.ReplayDone == c.ReplayTotal);
            Pump(c);
            ShotUi(c, "ui-repeticion.png");
            c.StopReplay();
            Check("repetición: no toca la hoja", !c.Replaying && c.Ink.Strokes.Count == before && c.Cur.Undo.Count == undoCount && c.Modified == modBefore);

            // --- SVG ---
            string svg = c.BuildSvg();
            File.WriteAllText(System.IO.Path.Combine(dir, "prueba.svg"), svg, new System.Text.UTF8Encoding(false));
            var xml = new System.Xml.XmlDocument();
            xml.LoadXml(svg);
            var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("s", "http://www.w3.org/2000/svg");
            Check("SVG válido con todos los trazos", xml.SelectNodes("//s:path", ns).Count >= before);
            var textNode = xml.SelectSingleNode("//s:text", ns);
            Check("SVG: texto escapado con ñ", textNode != null && textNode.InnerText == "Pizarra pro con ñ & <acentos>");
            Check("SVG: decimales con punto", !System.Text.RegularExpressions.Regex.IsMatch(xml.DocumentElement.GetAttribute("viewBox"), @"\d,\d"));
            Check("SVG: fondo de la hoja", xml.SelectSingleNode("//s:rect", ns).Attributes["fill"].Value == "#0A0F1E");

            // --- Región (con un mapa de bits de prueba) ---
            int sheetsBefore = c.Sheets.Count;
            using (var bmp = new System.Drawing.Bitmap(800, 600))
                c.CompleteRegion(bmp, new Rect(20, 30, 200, 100), false);
            var cropped = c.Ink.Children.OfType<Image>().SingleOrDefault();
            Check("región: hoja nueva con el recorte", c.Sheets.Count == sheetsBefore + 1 && cropped != null && Math.Abs(cropped.Width - 200) < 2 && Math.Abs(cropped.Height - 100) < 2);
            svg = c.BuildSvg();
            Check("SVG: incluye imágenes", svg.Contains("<image ") && svg.Contains("base64,"));
            c.RemoveCurrentSheet();
            using (var bmp = new System.Drawing.Bitmap(50, 50))
            {
                c.CompleteRegion(bmp, new Rect(100, 100, 40, 40), false);
                Check("región fuera de la imagen no hace nada", c.Sheets.Count == sheetsBefore);
            }

            // --- Audio ---
            var st = new float[4];
            AudioMath.Decode(BitConverter.GetBytes(0.5f).Concat(BitConverter.GetBytes(-0.25f)).Concat(BitConverter.GetBytes(0.1f)).Concat(BitConverter.GetBytes(0.9f)).ToArray(), 2, 2, 4, true, st);
            Check("audio: decodifica coma flotante", st[0] == 0.5f && st[1] == -0.25f && st[3] == 0.9f);
            AudioMath.Decode(BitConverter.GetBytes((short)16384), 1, 1, 2, false, st);
            Check("audio: mono de 16 bits a estéreo", st[0] == 0.5f && st[1] == 0.5f);
            AudioMath.Decode(new byte[] { 0, 0, 64, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 }, 1, 6, 3, false, st);
            Check("audio: 24 bits de varios canales", Math.Abs(st[0] - 0.5f) < 0.001f);
            Check("audio: recorta sin desbordar", AudioMath.ToPcm16(2f) == 32767 && AudioMath.ToPcm16(-2f) == -32767 && AudioMath.ToPcm16(0f) == 0);
            // Remuestreo por paquetes: la duración se conserva y no hay saltos entre paquetes
            var rs = new Resampler(44100);
            var outBuf = new float[0];
            var sine = new List<float>();
            for (int packet = 0; packet < 100; packet++)
            {
                var block = new float[441 * 2];
                for (int i = 0; i < 441; i++)
                    block[i * 2] = block[i * 2 + 1] = (float)Math.Sin(2 * Math.PI * 440 * (packet * 441 + i) / 44100.0);
                int made = rs.Process(block, 441, ref outBuf);
                for (int i = 0; i < made; i++) sine.Add(outBuf[i * 2]);
            }
            Check("audio: 44,1 kHz dura lo mismo a 48 kHz", Math.Abs(sine.Count - 48000) < 3);
            double maxJump = 0;
            for (int i = 1; i < sine.Count; i++) maxJump = Math.Max(maxJump, Math.Abs(sine[i] - sine[i - 1]));
            Check("audio: sin chasquidos entre paquetes", maxJump < 2 * Math.PI * 440 / 48000 * 1.1);
            var same = new Resampler(48000);
            var sameOut = new float[0];
            int total48 = same.Process(new float[] { 1, 1, 2, 2, 3, 3 }, 3, ref sameOut);
            total48 += same.Process(new float[] { 4, 4, 5, 5 }, 2, ref sameOut);
            Check("audio: 48 kHz no cambia de duración", total48 == 4 && sameOut[0] == 3 && sameOut[2] == 4);
            // Línea de tiempo
            var track = new AudioTrack();
            var one = new float[] { 1, 1, 1, 1 };
            track.Put(0, one, 2);
            track.Put(4800, one, 2);
            var mixed = new float[4802 * 2];
            track.MixInto(0, 4802, mixed);
            Check("audio: un silencio conserva su duración", mixed[0] == 1 && mixed[2] == 1 && mixed[4] == 0 && mixed[4799 * 2] == 0 && mixed[4800 * 2] == 1 && mixed[4801 * 2] == 1);
            var late = new AudioTrack();
            late.Put(0, new float[20000], 10000);
            late.Put(100, one, 2);
            Check("audio: paquete muy atrasado se descarta", late.End == 10000);
            late.Put(9990, one, 2);
            Check("audio: pequeño desfase queda contiguo", late.End == 10002);
            var t2 = new AudioTrack();
            t2.Put(100000, one, 2);
            var far = new float[2 * 2];
            t2.MixInto(50, 2, far);
            Check("audio: leer antes de que llegue da silencio", far[0] == 0);
            var t3 = new AudioTrack();
            // Diez minutos de paquetes de 10 ms, leídos con 150 ms de margen como en la grabación
            var packet480 = new float[960];
            for (int i = 0; i < 960; i++) packet480[i] = 0.5f;
            var read = new float[960];
            long readAt = 0;
            double sum = 0;
            for (int k = 0; k < 60000; k++)
            {
                t3.Put(k * 480L, packet480, 480);
                if (k * 480L - 7200 >= readAt)
                {
                    Array.Clear(read, 0, 960);
                    t3.MixInto(readAt, 480, read);
                    sum += read[0];
                    readAt += 480;
                }
            }
            Check("audio: la pista no crece sin límite", t3.Capacity <= 96000 && t3.End == 60000 * 480L);
            Check("audio: lectura continua sin huecos", Math.Abs(sum - 0.5 * (readAt / 480)) < 0.001);

            // --- Vídeo con sonido (micrófono y equipo; el archivo se borra enseguida) ---
            string clipA = System.IO.Path.Combine(dir, "prueba-audio.mp4");
            var recA = new ScreenRecorder(clipA, new System.Drawing.Rectangle(0, 0, 320, 240), true, true);
            recA.Start();
            System.Threading.Thread.Sleep(1500);
            recA.Stop();
            log.WriteLine("INFO audio: HasAudio=" + recA.HasAudio + " avisos=[" + recA.Notes + "]");
            Check("vídeo con sonido: sin error", recA.Error == null);
            Check("vídeo con sonido: archivo MP4", File.ReadAllBytes(clipA).Length > 500);
            File.Delete(clipA);
            c.SetRecordSound(false, true);
            Check("sonido: etiqueta", c.RecordSoundLabel == "Solo audio del equipo");
            c.SetRecordSound(false, false);
            Check("sonido: sin sonido", c.RecordSoundLabel == "Sin sonido");
            c.SetRecordSound(true, true);
            c.Bar.ShowRecMenu();
            ShotPopup(c, "ui-menu-sonido.png");

            // --- Vídeo ---
            Check("vídeo: medidas pares", ScreenRecorder.OutputSize(1921, 1081) == new Size(1920, 1080));
            Check("vídeo: limita a 4K", ScreenRecorder.OutputSize(5120, 1440) == new Size(3840, 1080));
            string clip = System.IO.Path.Combine(dir, "prueba.mp4");
            var rec = new ScreenRecorder(clip, new System.Drawing.Rectangle(0, 0, 321, 241));
            rec.Start();
            Check("vídeo: arranca", rec.Error == null);
            System.Threading.Thread.Sleep(1200);
            rec.Stop();
            Check("vídeo: fotogramas a su ritmo", rec.Error == null && rec.Frames >= 20 && rec.Frames <= 42);
            var mp4 = File.ReadAllBytes(clip);
            Check("vídeo: archivo MP4", mp4.Length > 500 && System.Text.Encoding.ASCII.GetString(mp4, 4, 4) == "ftyp");
            File.Delete(clip);
            var bad = new ScreenRecorder(System.IO.Path.Combine(dir, "no-existe", "x.mp4"), new System.Drawing.Rectangle(0, 0, 320, 240));
            bad.Start();
            Check("vídeo: ruta inválida da error sin bloquear", bad.Error != null);
            bad.Stop();
            c.ToggleRecording();
            Check("vídeo: cuenta atrás", c.CountingDown && !c.Recording);
            Pump(c);
            ShotUi(c, "ui-cuenta-atras.png");
            c.ToggleRecording();
            Check("vídeo: cancelar cuenta atrás", !c.CountingDown && !c.Recording);
            string clip2 = System.IO.Path.Combine(dir, "sub", "prueba2.mp4");
            Check("vídeo: empieza creando la carpeta", c.BeginRecording(clip2) && c.Recording);
            Check("vídeo: excluye la barra", c.BarDisplayAffinity == 0x11);
            c.Bar.UpdateRec();
            RenderBar(c, "ui-barra-grabando.png");
            System.Threading.Thread.Sleep(600);
            var saved = c.EndRecording();
            Check("vídeo: termina y restaura la barra", !c.Recording && c.BarDisplayAffinity == 0 && saved != null && File.Exists(saved.Value.Path) && saved.Value.Time.TotalSeconds > 0.4);
            Check("vídeo: terminar dos veces no falla", c.EndRecording() == null);
            File.Delete(clip2);

            // --- Ayuda ---
            Press(c, Key.F1);
            Check("ayuda: abierta", c.HelpOpen);
            Pump(c);
            ShotUi(c, "ui-ayuda.png");
            Press(c, Key.Escape);
            Check("ayuda: Esc cierra", !c.HelpOpen);

            // --- Modo pantalla ---
            c.SetColor(Colors.White);
            c.SetMode(true);
            Check("pantalla: color visible", c.InkColor == ToolbarWindow.Palette[2]);
            Check("pantalla: sin paneles de hoja", c.Map.Visibility != Visibility.Visible);
            c.SetMode(false);
            Check("hojas: minimapa de vuelta", c.Map.Visibility == Visibility.Visible);
            c.GoToSheet(0);
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
                    if (k >= PenKind.Desvanecer) continue;
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
                Check("9 lápices + resaltador", c.Ink.Strokes.Count == 10);
                Check("neón es trazo con estilo", c.Ink.Strokes[4] is RichStroke);
                Check("punteado es trazo con estilo", c.Ink.Strokes[6] is RichStroke);
                Check("pincel afina extremos", c.Ink.Strokes[3].StylusPoints[0].PressureFactor < 0.2f);
                c.Undo();
                Check("deshacer trazo", c.Ink.Strokes.Count == 9);
                c.Redo();
                Check("rehacer mantiene estilo", c.Ink.Strokes[4] is RichStroke && c.Ink.Strokes.Count == 10);

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
                Check("13 formas", c.Ink.Strokes.Count == 23);
                c.SetPen(PenKind.Discontinuo);
                DrawShape(c, ShapeKind.Rect, 640, 560, 800, 660);
                Check("forma discontinua con estilo", c.Ink.Strokes[23] is RichStroke);
                int before = c.Cur.Undo.Count;
                DrawShape(c, ShapeKind.Line, 10, 10, 10, 10);
                Check("clic sin arrastrar no crea forma", c.Ink.Strokes.Count == 24 && c.Cur.Undo.Count == before);
                c.SetShape(ShapeKind.Rect);
                c.BeginShape(new Point(100, 100));
                c.UpdateShape(new Point(300, 150), true);
                c.EndShape();
                var pb = c.Ink.Strokes[24].GetBounds();
                Check("Mayús hace proporcional", Math.Abs(pb.Width - pb.Height) < 1);
                c.Undo();
                c.SetPen(PenKind.Boligrafo);
                Shot(c, "lapices-formas.png");

                // --- Reconocimiento de formas ---
                c.AddSheet();
                c.SetTrace(TraceMode.Geometria);
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
                Ink(c, Hand(Dense(new Point(1100, 200), new Point(1180, 280), new Point(1100, 360), new Point(1020, 280), new Point(1100, 202)), 3));
                Check("rombo reconocido", c.Ink.Strokes[4].StylusPoints.Count == 5 && Math.Abs(c.Ink.Strokes[4].StylusPoints[0].X - 1100) < 8);
                var penta = Enumerable.Range(0, 6).Select(i => new Point(1300 + 90 * Math.Cos((-90 + i * 72) * Math.PI / 180), 280 + 90 * Math.Sin((-90 + i * 72) * Math.PI / 180))).ToArray();
                Ink(c, Hand(Dense(penta), 3));
                Check("pentágono reconocido", c.Ink.Strokes[5].StylusPoints.Count == 6);
                var star = Enumerable.Range(0, 6).Select(i => new Point(1300 + 90 * Math.Cos((-90 + i * 144) * Math.PI / 180), 560 + 90 * Math.Sin((-90 + i * 144) * Math.PI / 180))).ToArray();
                Ink(c, Hand(Dense(star), 3));
                Check("estrella reconocida", c.Ink.Strokes[6].StylusPoints.Count == 11);
                Ink(c, Hand(Dense(new Point(900, 700), new Point(1200, 500), new Point(1140, 505), new Point(1200, 500), new Point(1180, 560)), 2));
                Check("flecha de un trazo reconocida", c.Ink.Strokes[7].StylusPoints.Count >= 5);
                c.SetTrace(TraceMode.Libre);
                Ink(c, Wave(200, 650, 500));
                Check("modo libre no altera el trazo", c.Ink.Strokes[8].StylusPoints.Count == 61);
                Shot(c, "reconocer.png");

                // --- Estilos de escritura ---
                c.AddSheet();
                c.SetPaper(Paper.Pauta);
                c.CurrentSize = 5;
                double wy = 150;
                foreach (var pk in new[] { PenKind.Caligrafia, PenKind.Gotica, PenKind.Inglesa, PenKind.Estilografica, PenKind.Firma, PenKind.Cepillo })
                {
                    c.SetPen(pk);
                    var loops = new List<Point>();
                    for (int i = 0; i <= 300; i++)
                    {
                        double t = i / 300.0;
                        // Distancia variable entre puntos: imita aceleraciones del pulso.
                        double warp = t + 0.03 * Math.Sin(t * 40);
                        loops.Add(new Point(70 + 650 * warp + 26 * Math.Sin(warp * 30), wy + 30 * Math.Cos(warp * 30)));
                    }
                    var sk = Ink(c, Hand(loops, 1.5));
                    if (pk == PenKind.Inglesa)
                    {
                        float pmin = sk.StylusPoints.Min(p => p.PressureFactor), pmax = sk.StylusPoints.Max(p => p.PressureFactor);
                        Check("letra inglesa: fino y grueso", pmax - pmin > 0.3f);
                    }
                    if (pk == PenKind.Firma)
                        Check("firma: extremos afinados", sk.StylusPoints[0].PressureFactor < 0.2f);
                    wy += 100;
                }
                c.SetPen(PenKind.Caligrafia);
                c.NibAngle = 80;
                c.ApplyTool();
                Check("ángulo de plumilla", Math.Abs(c.Ink.DefaultDrawingAttributes.StylusTipTransform.M12 - Math.Sin(80 * Math.PI / 180)) < 0.01);
                Ink(c, Wave(800, 200, 600));
                c.NibAngle = 35;
                c.SetPen(PenKind.Boligrafo);
                // Suavizado
                c.SetTrace(TraceMode.Suavizado);
                var shaky = Hand(Wave(800, 400, 600), 14);
                var ss = Ink(c, shaky);
                double before2 = Rough(shaky), after2 = Rough(ss.StylusPoints.Select(p => (Point)p).ToList());
                Check("suavizado reduce el temblor", after2 < before2 * 0.5);
                c.SetTrace(TraceMode.Recto);
                var rs = Ink(c, Hand(Dense(new Point(800, 500), new Point(1400, 503)), 8));
                Check("línea recta horizontal", rs != null && c.Ink.Strokes[c.Ink.Strokes.Count - 1].StylusPoints.Count == 2 && Math.Abs(c.Ink.Strokes[c.Ink.Strokes.Count - 1].StylusPoints[0].Y - c.Ink.Strokes[c.Ink.Strokes.Count - 1].StylusPoints[1].Y) < 0.5);
                c.SetTrace(TraceMode.Ortogonal);
                Ink(c, Hand(Dense(new Point(800, 600), new Point(1000, 620), new Point(1020, 760), new Point(1300, 770)), 3));
                var os = c.Ink.Strokes[c.Ink.Strokes.Count - 1].StylusPoints;
                bool orth = true;
                for (int i = 1; i < os.Count; i++) if (Math.Abs(os[i].X - os[i - 1].X) > 0.01 && Math.Abs(os[i].Y - os[i - 1].Y) > 0.01) orth = false;
                Check("ortogonal a mano: tramos rectos", orth && os.Count == 4);
                c.SetTrace(TraceMode.Libre);
                Shot(c, "estilos.png");

                // --- Punto a punto ---
                c.AddSheet();
                c.SetPaper(Paper.Cuadriculada);
                c.SetTrace(TraceMode.Polilinea);
                Check("polilínea no usa tinta libre", c.Ink.EditingMode == InkCanvasEditingMode.None);
                c.PolyClick(new Point(100, 200), false, 1);
                c.PolyMove(new Point(300, 150), false);
                c.PolyClick(new Point(300, 150), false, 1);
                c.PolyClick(new Point(500, 300), false, 1);
                c.PolyMove(new Point(700, 300), false);
                Check("el tramo elástico sigue al ratón", c.Ink.Strokes[0].StylusPoints.Count == 4);
                Check("polilínea aún sin historial", c.Cur.Undo.Count == 0);
                c.PolyClick(new Point(500, 300), false, 2);
                Check("doble clic termina y quita el elástico", !c.PolyActive && c.Ink.Strokes[0].StylusPoints.Count == 3 && c.Cur.Undo.Count == 1);
                c.PolyClick(new Point(100, 400), false, 1);
                c.PolyClick(new Point(300, 400), false, 1);
                c.PolyClick(new Point(200, 550), false, 1);
                c.PolyClick(new Point(104, 402), false, 1);
                var closed = c.Ink.Strokes[1].StylusPoints;
                Check("clic junto al inicio cierra", !c.PolyActive && closed[0].X == closed[closed.Count - 1].X && closed.Count == 4);
                c.PolyClick(new Point(600, 500), false, 1);
                c.PolyClick(new Point(800, 500), false, 1);
                c.PolyBack(new Point(800, 500));
                Check("retroceso quita un vértice", c.PolyActive);
                c.PolyCancel();
                Check("Esc cancela sin rastro", !c.PolyActive && c.Ink.Strokes.Count == 2);
                c.PolyClick(new Point(600, 200), false, 1);
                c.PolyClick(new Point(800, 300), true, 1);
                c.PolyFinish();
                var diag = c.Ink.Strokes[2].StylusPoints;
                Check("Mayús ajusta a 45 grados", Math.Abs(Math.Abs(diag[1].X - diag[0].X) - Math.Abs(diag[1].Y - diag[0].Y)) < 0.01);
                c.SetTrace(TraceMode.PoliOrtogonal);
                c.PolyClick(new Point(900, 200), false, 1);
                c.PolyClick(new Point(1100, 240), false, 1);
                c.PolyClick(new Point(1130, 400), false, 1);
                c.PolyClick(new Point(1300, 410), false, 1);
                c.PolyFinish();
                var po = c.Ink.Strokes[3].StylusPoints;
                Check("polilínea ortogonal", po[1].Y == po[0].Y && po[2].X == po[1].X && po[3].Y == po[2].Y);
                c.SetTrace(TraceMode.Curva);
                c.PolyClick(new Point(900, 600), false, 1);
                c.PolyClick(new Point(1050, 480), false, 1);
                c.PolyClick(new Point(1200, 650), false, 1);
                c.PolyClick(new Point(1400, 500), false, 1);
                c.PolyFinish();
                var cu = c.Ink.Strokes[4].StylusPoints;
                Check("curva con puntos intermedios", cu.Count > 30);
                Check("curva pasa por los puntos", cu.Any(p => Math.Abs(p.X - 1050) < 0.01 && Math.Abs(p.Y - 480) < 0.01));
                c.SetTrace(TraceMode.Polilinea);
                c.SnapGrid = true;
                c.PolyClick(new Point(103, 652), false, 1);
                c.PolyClick(new Point(287, 702), false, 1);
                c.PolyFinish();
                var gs = c.Ink.Strokes[5].StylusPoints;
                Check("ajuste a la rejilla", gs[0].X == 90 && gs[0].Y == 660 && gs[1].X == 300 && gs[1].Y == 690);
                c.SnapGrid = false;
                c.SetTrace(TraceMode.Polilinea);
                c.PolyClick(new Point(500, 700), false, 1);
                c.PolyClick(new Point(700, 700), false, 1);
                c.SetTool(Tool.Eraser);
                Check("cambiar de herramienta termina la polilínea", !c.PolyActive && c.Ink.Strokes.Count == 7);
                c.SetTrace(TraceMode.Libre);
                Shot(c, "polilineas.png");

                // --- Convertir lo ya dibujado ---
                c.AddSheet();
                c.SetPen(PenKind.Boligrafo);
                Ink(c, Hand(Dense(new Point(100, 200), new Point(400, 200), new Point(400, 400), new Point(100, 400), new Point(100, 202)), 5));
                Check("en libre sigue a mano", c.Ink.Strokes[0].StylusPoints.Count > 50);
                Check("convertir a formas", c.ConvertSelectionToShapes() == 1 && c.Ink.Strokes[0].StylusPoints.Count == 5);
                c.Undo();
                Check("deshacer conversión", c.Ink.Strokes[0].StylusPoints.Count > 50);
                c.ClearCurrent();

                // --- Letras a texto: se usa el reconocedor real de Windows ---
                Check("reconocedor de Windows disponible", Handwriting.Available);
                var hola = new StrokeCollection();
                c.SetTrace(TraceMode.Libre);
                foreach (var letter in HolaLetters())
                {
                    var ls = new Stroke(new StylusPointCollection(letter), c.Ink.DefaultDrawingAttributes.Clone());
                    c.Ink.Strokes.Add(ls);
                    hola.Add(ls);
                }
                c.MarkDirty(); c.Commit();
                var task = c.RecognizeWords(hola);
                var limit = DateTime.UtcNow.AddSeconds(25);
                while (!task.IsCompleted && DateTime.UtcNow < limit)
                {
                    c.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
                    System.Threading.Thread.Sleep(20);
                }
                Check("el reconocedor responde", task.IsCompleted && !task.IsFaulted && task.Result != null && task.Result.Count > 0);
                if (task.IsCompleted && !task.IsFaulted && task.Result != null && task.Result.Count > 0)
                {
                    var found = string.Join(" | ", task.Result.Select(w => string.Join(",", w.Take(5))));
                    log.WriteLine("INFO  Reconocido: " + found);
                    Check("identifica la palabra HOLA", task.Result.Any(w => w.Any(x => x.Equals("HOLA", StringComparison.OrdinalIgnoreCase))));
                    c.ShowCorrection(hola, task.Result);
                    Pump(c);
                    Check("panel de corrección abierto", c.CorrectionOpen);
                    {
                        var bmp = new RenderTargetBitmap((int)c.ActualWidth, (int)c.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bmp.Render((Visual)c.Content);
                        CanvasWindow.WriteImage(bmp, System.IO.Path.Combine(dir, "correccion.png"));
                    }
                    c.SetCorrectionText("Hola corregido");
                    c.AcceptCorrection();
                    Check("aceptar sustituye trazos por texto", !c.CorrectionOpen && c.Ink.Strokes.Count == 0 && c.Ink.Children.OfType<RichTextBox>().Count() == 1);
                    Check("el texto es el corregido", CanvasWindow.TextOf(c.Ink.Children.OfType<RichTextBox>().First()) == "Hola corregido");
                    Shot(c, "letras.png");
                    c.Undo();
                    Check("deshacer recupera lo escrito a mano", c.Ink.Strokes.Count == 8 && c.Ink.Children.Count == 0);
                    var again = new StrokeCollection(c.Ink.Strokes);
                    c.ShowCorrection(again, task.Result);
                    c.CloseCorrection();
                    Check("dejar a mano conserva trazos", c.Ink.Strokes.Count == 8 && !c.CorrectionOpen);
                }
                var alts = CanvasWindow.Alternatives(new List<List<string>> { new List<string> { "hola", "bola" }, new List<string> { "mundo", "munda" } });
                Check("alternativas de frase", alts.Contains("bola mundo") && alts.Contains("hola munda"));

                // Revisión ortográfica activa al escribir
                c.AddSheet();
                c.SetTool(Tool.Text);
                var sp = c.PlaceText(new Point(200, 200));
                Type(sp, "palabrra ");
                Pump(c);
                Check("ortografía activa al editar", sp.SpellCheck.IsEnabled);
                c.Ink.Focus();
                Check("ortografía apagada al terminar", !sp.SpellCheck.IsEnabled);
                c.SetTool(Tool.Pen);

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
                c.GoToSheet(c.Sheets.FindIndex(s => s.Items.Any(i => i.Kind == ItemKind.Doc)));
                var opened = c.Ink.Children.OfType<RichTextBox>().FirstOrDefault();
                Check("abrir: texto con ñ", opened != null && CanvasWindow.TextOf(opened).StartsWith("Título con ñ y acentos"));
                Check("abrir: tipo de documento", opened != null && ((Item)opened.Tag).Kind == ItemKind.Doc);
                c.GoToSheet(c.Sheets.FindIndex(s => s.Items.Any(i => i.Kind == ItemKind.Image)));
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

                RunPro(c);

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
            Ui.Install(app);
            Ui.Motion = false;
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
