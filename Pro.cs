// Funciones pro de Pizarra: paleta de comandos, menú radial, miniaturas, minimapa, presentación,
// repetición del dibujo, captura de región, exportación SVG, favoritos y operaciones de selección.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pizarra
{
    // Lápiz guardado en uno de los cinco favoritos.
    class PenPreset
    {
        public PenKind Pen;
        public Color Ink;
        public double Size;
        public TraceMode Trace;
        public double Opacity = 1;
    }

    // Acción que aparece en la paleta de comandos.
    class Cmd
    {
        public string Name, Keys;
        public Action Run;
        public Cmd(string name, string keys, Action run) { Name = name; Keys = keys; Run = run; }
    }

    // Vista general de la hoja: muestra el contenido y la zona visible; se navega con clic o arrastre.
    class Minimap : FrameworkElement
    {
        const double W = 190, H = 124, Inset = 8;
        readonly CanvasWindow c;
        readonly DrawingGroup content = new DrawingGroup();
        Rect domain = new Rect(0, 0, 1, 1);
        bool dragging;

        public Minimap(CanvasWindow canvas)
        {
            c = canvas;
            Width = W;
            Height = H;
            Cursor = Cursors.Hand;
            ToolTip = "Minimapa: haz clic o arrastra para moverte por la hoja";
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { dragging = true; CaptureMouse(); Navigate(e.GetPosition(this)); e.Handled = true; };
            MouseMove += delegate(object s, MouseEventArgs e) { if (dragging) Navigate(e.GetPosition(this)); };
            MouseLeftButtonUp += delegate { dragging = false; ReleaseMouseCapture(); Refresh(); };
        }

        // El área mostrada es el contenido más la vista, con margen; no cambia durante un arrastre.
        internal static Rect Domain(Rect contentBounds, Rect view)
        {
            Rect d = view;
            if (!contentBounds.IsEmpty) d.Union(contentBounds);
            d.Inflate(Math.Max(40, d.Width * 0.08), Math.Max(40, d.Height * 0.08));
            return d;
        }

        double Scale { get { return Math.Min((W - 2 * Inset) / domain.Width, (H - 2 * Inset) / domain.Height); } }
        double OffX { get { return (W - domain.Width * Scale) / 2 - domain.X * Scale; } }
        double OffY { get { return (H - domain.Height * Scale) / 2 - domain.Y * Scale; } }

        internal Point ToInk(Point p) { return new Point((p.X - OffX) / Scale, (p.Y - OffY) / Scale); }

        void Navigate(Point p) { c.CenterViewOn(ToInk(p)); }

        internal void Refresh()
        {
            using (var dc = content.Open())
            {
                c.Ink.Strokes.Draw(dc);
                var box = new SolidColorBrush(Color.FromArgb(0x66, 0x22, 0xD3, 0xEE));
                foreach (var el in c.Ink.Children.OfType<FrameworkElement>())
                    dc.DrawRectangle(box, null, new Rect(InkCanvas.GetLeft(el), InkCanvas.GetTop(el), el.ActualWidth, el.ActualHeight));
            }
            UpdateView();
        }

        internal void UpdateView()
        {
            if (!dragging) domain = Domain(c.ContentBounds(), c.ViewRect());
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRoundedRectangle(Ui.Glass, new Pen(Ui.Edge, 1), new Rect(0, 0, W, H), 12, 12);
            dc.PushClip(new RectangleGeometry(new Rect(1, 1, W - 2, H - 2), 11, 11));
            dc.PushTransform(new MatrixTransform(Scale, 0, 0, Scale, OffX, OffY));
            dc.DrawDrawing(content);
            dc.Pop();
            Rect v = c.ViewRect();
            var box = new Rect(v.X * Scale + OffX, v.Y * Scale + OffY, v.Width * Scale, v.Height * Scale);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x1E, 0x22, 0xD3, 0xEE)), new Pen(new SolidColorBrush(Ui.Cyan), 1.2), box, 2, 2);
            dc.Pop();
        }
    }

    partial class CanvasWindow
    {
        internal readonly PenPreset[] Presets =
        {
            new PenPreset { Pen = PenKind.Boligrafo, Ink = Colors.White, Size = 3 },
            new PenPreset { Pen = PenKind.Neon, Ink = Color.FromRgb(0x22, 0xD3, 0xEE), Size = 5 },
            new PenPreset { Pen = PenKind.Plasma, Ink = Color.FromRgb(0xF4, 0x72, 0xB6), Size = 6 },
            new PenPreset { Pen = PenKind.Caligrafia, Ink = Color.FromRgb(0xFF, 0xE1, 0x4D), Size = 4 },
            new PenPreset { Pen = PenKind.Boligrafo, Ink = Color.FromRgb(0x39, 0xFF, 0x88), Size = 3, Trace = TraceMode.Geometria }
        };

        internal bool Presenting, SheetPanelOpen, MinimapOn = true;
        internal Minimap Map;
        internal int ReplayTotal, ReplayDone;
        internal string LastToast;

        Grid overlay;
        Border hud, sheetPanel, toast, presentHud, replayBar;
        TextBlock hudText, toastText, presentText, replayText;
        Ellipse hudDot;
        StackPanel thumbList;
        Rectangle vignette, replayProgress;
        DispatcherTimer thumbTimer, presentTimer, replayTimer;
        DateTime presentStart;
        bool thumbing, chromeFaded, paletteOpen, radialOpen, helpOpen, regionOpen;
        Grid paletteRoot, radialRoot, helpRoot, regionRoot, replayRoot;

        // ---- Construcción ----
        void InitPro()
        {
            vignette = new Rectangle
            {
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x70, 0, 0, 0)) { RadiusX = 0.75, RadiusY = 0.75 }
            };
            root.Children.Insert(1, vignette);

            hudDot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
            hudText = new TextBlock { Foreground = new SolidColorBrush(Ui.Text), FontFamily = Ui.Font, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var hudRow = Ui.Row();
            hudRow.Children.Add(hudDot);
            hudRow.Children.Add(hudText);
            hud = Ui.Card(hudRow, 12, 8);
            hud.IsHitTestVisible = false;
            hud.HorizontalAlignment = HorizontalAlignment.Left;
            hud.VerticalAlignment = VerticalAlignment.Bottom;
            hud.Margin = new Thickness(16);
            root.Children.Add(hud);

            Map = new Minimap(this)
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(16),
                Effect = new DropShadowEffect { Color = Ui.Cyan, BlurRadius = 20, ShadowDepth = 0, Opacity = 0.25 }
            };
            root.Children.Add(Map);

            thumbList = new StackPanel();
            var addTile = Ui.Glyph("", "Hoja nueva (Ctrl+N)", AddSheet);
            addTile.HorizontalAlignment = HorizontalAlignment.Center;
            var column = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(addTile, Dock.Bottom);
            column.Children.Add(addTile);
            column.Children.Add(new ScrollViewer { Content = thumbList, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false });
            sheetPanel = Ui.Card(column, 14, 8);
            sheetPanel.Width = 190;
            sheetPanel.HorizontalAlignment = HorizontalAlignment.Left;
            sheetPanel.VerticalAlignment = VerticalAlignment.Center;
            sheetPanel.Margin = new Thickness(16, 120, 0, 90);
            sheetPanel.Visibility = Visibility.Collapsed;
            root.Children.Add(sheetPanel);

            toastText = new TextBlock { Foreground = new SolidColorBrush(Ui.Text), FontFamily = Ui.Font, FontSize = 13, Margin = new Thickness(6, 0, 6, 0) };
            toast = Ui.Card(toastText, 12, 9);
            toast.IsHitTestVisible = false;
            toast.HorizontalAlignment = HorizontalAlignment.Center;
            toast.VerticalAlignment = VerticalAlignment.Bottom;
            toast.Margin = new Thickness(0, 0, 0, 84);
            toast.Opacity = 0;
            root.Children.Add(toast);

            presentText = new TextBlock { Foreground = new SolidColorBrush(Ui.Cyan), FontFamily = Ui.Font, FontSize = 13 };
            presentHud = Ui.Card(presentText, 12, 8);
            presentHud.IsHitTestVisible = false;
            presentHud.HorizontalAlignment = HorizontalAlignment.Right;
            presentHud.VerticalAlignment = VerticalAlignment.Top;
            presentHud.Margin = new Thickness(0, 16, 16, 0);
            presentHud.Visibility = Visibility.Collapsed;
            root.Children.Add(presentHud);

            overlay = new Grid();
            root.Children.Add(overlay);

            thumbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            thumbTimer.Tick += delegate { ThumbTick(); };
            presentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            presentTimer.Tick += delegate { UpdatePresentHud(); };
            replayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            replayTimer.Tick += delegate { if (!ReplayAdvance(Math.Max(1, ReplayTotal / 360))) StopReplay(); };

            Ink.PreviewMouseLeftButtonDown += delegate { if (IsDrawingTool) SetChromeFaded(true); };
            Ink.PreviewMouseLeftButtonUp += delegate { SetChromeFaded(false); };
            Ink.PreviewMouseMove += delegate(object s, MouseEventArgs e) { if (chromeFaded && e.LeftButton == MouseButtonState.Released) SetChromeFaded(false); };
            Closed += delegate { thumbTimer.Stop(); presentTimer.Stop(); replayTimer.Stop(); EndRecording(); };
            LoadPresets();
        }

        bool IsDrawingTool
        {
            get { return Tool == Tool.Pen || Tool == Tool.Highlighter || Tool == Tool.Shape || Tool == Tool.Eraser || Tool == Tool.StrokeEraser || Tool == Tool.Laser; }
        }

        // La interfaz se atenúa al dibujar para no tapar el trazo.
        void SetChromeFaded(bool faded)
        {
            if (faded == chromeFaded) return;
            chromeFaded = faded;
            double to = faded ? 0.25 : 1;
            int ms = faded ? 180 : 260;
            Ui.FadeTo(Bar.RootPanel, to, ms);
            Ui.FadeTo((Border)pill.Parent, to, ms);
            Ui.FadeTo(hud, to, ms);
            Ui.FadeTo(Map, to, ms);
        }

        internal static Color PaperColor(Paper p)
        {
            switch (p)
            {
                case Paper.Negra: return Color.FromRgb(0x1F, 0x24, 0x28);
                case Paper.Verde: return Color.FromRgb(0x1E, 0x4D, 0x3B);
                case Paper.Cyber: return Color.FromRgb(0x0A, 0x0F, 0x1E);
                case Paper.Plano: return Color.FromRgb(0x0B, 0x3A, 0x6E);
                default: return Colors.White;
            }
        }

        // ---- Avisos ----
        internal void Toast(string text)
        {
            LastToast = text;
            toastText.Text = text;
            if (!Ui.Motion) return;
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
            a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2800))));
            toast.BeginAnimation(UIElement.OpacityProperty, a);
        }

        // ---- Estado de la interfaz ----
        internal void ProStatus()
        {
            if (hud == null) return;
            hudDot.Fill = new SolidColorBrush(InkColor);
            hudDot.Effect = new DropShadowEffect { Color = InkColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };
            string what;
            switch (Tool)
            {
                case Tool.Pen: what = ToolbarWindow.PenNames[(int)Pen]; break;
                case Tool.Highlighter: what = "Resaltador"; break;
                case Tool.Eraser: what = "Borrador"; break;
                case Tool.StrokeEraser: what = "Borrar trazo"; break;
                case Tool.Text: what = "Texto"; break;
                case Tool.Sticky: what = "Nota"; break;
                case Tool.Select: what = "Selección"; break;
                case Tool.Shape: what = Shapes.Name(ShapeType); break;
                case Tool.Hand: what = "Mano"; break;
                case Tool.Laser: what = "Láser"; break;
                case Tool.Spotlight: what = "Foco"; break;
                case Tool.Magnifier: what = "Lupa"; break;
                case Tool.Eyedropper: what = "Cuentagotas"; break;
                default: what = "Puntero"; break;
            }
            hudText.Text = what + "  ·  " + Ink.Strokes.Count + (Ink.Strokes.Count == 1 ? " trazo" : " trazos");
        }

        void ProViewChanged()
        {
            if (Map != null && Map.Visibility == Visibility.Visible) Map.UpdateView();
        }

        void ProContentChanged(bool edited)
        {
            if (Map == null) return;
            if (edited)
            {
                Cur.Thumb = null;
                if (SheetPanelOpen) thumbTimer.Start();
            }
            ProChromeChanged();
            if (Map.Visibility == Visibility.Visible) Map.Refresh();
        }

        void ProChromeChanged()
        {
            if (Map == null) return;
            bool chrome = !ScreenMode && !Presenting;
            hud.Visibility = chrome ? Visibility.Visible : Visibility.Collapsed;
            bool wasVisible = Map.Visibility == Visibility.Visible;
            Map.Visibility = chrome && MinimapOn && !ContentBounds().IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (!wasVisible && Map.Visibility == Visibility.Visible) Map.Refresh();
            sheetPanel.Visibility = chrome && SheetPanelOpen ? Visibility.Visible : Visibility.Collapsed;
            vignette.Visibility = !ScreenMode && (Cur.Paper == Paper.Cyber || Cur.Paper == Paper.Plano) ? Visibility.Visible : Visibility.Collapsed;
            if (SheetPanelOpen && !thumbing) RebuildThumbList();
            ProStatus();
        }

        bool ProWheelBlocked()
        {
            return sheetPanel.IsMouseOver || paletteOpen || helpOpen || regionOpen || radialOpen;
        }

        // ---- Vista ----
        internal Rect ContentBounds()
        {
            Rect b = Rect.Empty;
            if (Ink.Strokes.Count > 0) b = Ink.Strokes.GetBounds();
            foreach (var el in Ink.Children.OfType<FrameworkElement>())
                b.Union(new Rect(InkCanvas.GetLeft(el), InkCanvas.GetTop(el), el.ActualWidth, el.ActualHeight));
            return b;
        }

        internal Rect ViewRect()
        {
            return new Rect(-Cur.PanX / Cur.Zoom, -Cur.PanY / Cur.Zoom, Host.ActualWidth / Cur.Zoom, Host.ActualHeight / Cur.Zoom);
        }

        internal void CenterViewOn(Point ink)
        {
            if (ScreenMode) return;
            Cur.PanX = Host.ActualWidth / 2 - ink.X * Cur.Zoom;
            Cur.PanY = Host.ActualHeight / 2 - ink.Y * Cur.Zoom;
            ClampPan();
            ApplyView();
        }

        internal void FitContent()
        {
            if (ScreenMode) return;
            Rect b = ContentBounds();
            if (b.IsEmpty) { ResetView(); return; }
            b.Inflate(60, 60);
            Cur.Zoom = Math.Max(0.25, Math.Min(2, Math.Min(Host.ActualWidth / b.Width, Host.ActualHeight / b.Height)));
            CenterViewOn(new Point(b.X + b.Width / 2, b.Y + b.Height / 2));
        }

        internal void ToggleMinimap()
        {
            MinimapOn = !MinimapOn;
            ProChromeChanged();
        }

        // ---- Miniaturas ----
        internal void ToggleSheetPanel()
        {
            if (ScreenMode) { Toast("Las miniaturas están disponibles en el modo Hojas"); return; }
            SheetPanelOpen = !SheetPanelOpen;
            ProChromeChanged();
            if (SheetPanelOpen)
            {
                EnsureThumbs();
                Ui.SlideIn(sheetPanel, 12, 220);
            }
            else thumbTimer.Stop();
        }

        BitmapSource RenderThumb()
        {
            var full = RenderFit();
            double f = 164.0 / full.PixelWidth;
            var small = new WriteableBitmap(new TransformedBitmap(full, new ScaleTransform(f, f)));
            small.Freeze();
            return small;
        }

        internal void EnsureThumbs()
        {
            if (ScreenMode || thumbing) return;
            thumbing = true;
            try
            {
                FinishText();
                Commit();
                StoreCurrent();
                UpdateLayout();
                int keep = SheetIndex;
                bool moved = false;
                for (int i = 0; i < Sheets.Count; i++)
                {
                    if (Sheets[i].Thumb != null) continue;
                    if (i != SheetIndex)
                    {
                        SheetIndex = i;
                        LoadSheet(Sheets[i]);
                        UpdateLayout();
                        moved = true;
                    }
                    Sheets[i].Thumb = RenderThumb();
                }
                if (moved)
                {
                    SheetIndex = keep;
                    LoadSheet(Sheets[keep]);
                    UpdateCanvasLook();
                }
            }
            finally { thumbing = false; }
            RebuildThumbList();
        }

        void ThumbTick()
        {
            // No se toca la vista mientras se dibuja o escribe: se reintenta en el siguiente ciclo.
            if (Mouse.LeftButton == MouseButtonState.Pressed || shaping || PolyActive || activeText != null) return;
            thumbTimer.Stop();
            if (SheetPanelOpen && !ScreenMode) EnsureThumbs();
        }

        void RebuildThumbList()
        {
            thumbList.Children.Clear();
            for (int i = 0; i < Sheets.Count; i++)
            {
                int index = i;
                bool current = i == SheetIndex;
                var grid = new Grid();
                grid.Children.Add(new Image { Source = Sheets[i].Thumb, Stretch = Stretch.Uniform, Height = 96 });
                grid.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x05, 0x0A, 0x16)),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Child = new TextBlock { Text = (i + 1).ToString(), Foreground = new SolidColorBrush(current ? Ui.Cyan : Ui.Text), FontFamily = Ui.Font, FontSize = 11 }
                });
                var tile = new Border
                {
                    Child = grid,
                    MinHeight = 60,
                    Margin = new Thickness(0, 0, 0, 7),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(current ? 2 : 1),
                    BorderBrush = current ? Ui.Accent : new SolidColorBrush(Ui.Line),
                    Background = new SolidColorBrush(PaperColor(Sheets[i].Paper)),
                    ClipToBounds = true,
                    Cursor = Cursors.Hand,
                    ToolTip = "Ir a la hoja " + (i + 1)
                };
                tile.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { GoToSheet(index); e.Handled = true; };
                thumbList.Children.Add(tile);
            }
            if (SheetPanelOpen && Sheets.Any(s => s.Thumb == null)) thumbTimer.Start();
        }

        // ---- Modo presentación ----
        internal void EnterPresent()
        {
            if (Presenting) return;
            if (ScreenMode) SetMode(false);
            FinishText();
            Commit();
            Presenting = true;
            presentStart = DateTime.UtcNow;
            Bar.Hide();
            presentHud.Visibility = Visibility.Visible;
            UpdatePresentHud();
            presentTimer.Start();
            UpdateCanvasLook();
            Activate();
            Ink.Focus();
            Toast("Presentación: flechas para cambiar de hoja, K láser, Esc para salir");
        }

        internal void ExitPresent()
        {
            if (!Presenting) return;
            Presenting = false;
            presentTimer.Stop();
            presentHud.Visibility = Visibility.Collapsed;
            Bar.Show();
            UpdateCanvasLook();
            Activate();
            Ink.Focus();
        }

        void UpdatePresentHud()
        {
            var t = DateTime.UtcNow - presentStart;
            presentText.Text = "Hoja " + (SheetIndex + 1) + " de " + Sheets.Count + "   ·   " + ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
        }

        internal void PresentStep(int delta)
        {
            GoToSheet(SheetIndex + delta);
            UpdatePresentHud();
        }

        // ---- Repetición: se anima una copia, sin tocar la hoja ni el historial ----
        StrokeCollection replayStrokes;
        List<Stroke> replaySource;
        int replayStroke, replayPoint;
        Stroke replayLive;

        internal bool Replaying { get { return replayRoot != null; } }

        internal void StartReplay()
        {
            ReplayBegin();
            if (Replaying) replayTimer.Start();
        }

        internal void ReplayBegin()
        {
            if (Replaying) return;
            FinishText();
            Commit();
            replaySource = Ink.Strokes.ToList();
            if (replaySource.Count == 0) { Toast("No hay trazos que repetir en esta hoja"); return; }
            ReplayTotal = replaySource.Sum(s => s.StylusPoints.Count);
            ReplayDone = 0;
            replayStroke = 0;
            replayPoint = 0;
            replayLive = null;
            replayStrokes = new StrokeCollection();
            Matrix m = new Matrix(Cur.Zoom, 0, 0, Cur.Zoom, Cur.PanX, Cur.PanY);
            var presenter = new InkPresenter { Strokes = replayStrokes, Width = CanvasW, Height = CanvasH, RenderTransform = new MatrixTransform(m) };
            Brush paper = ScreenMode ? new SolidColorBrush(Color.FromArgb(0xE6, 0x0A, 0x0F, 0x1E)) : PaperBrush(Cur.Paper);
            if (paper is DrawingBrush) paper.Transform = new MatrixTransform(m);
            var stage = new Canvas();
            stage.Children.Add(presenter);
            replayRoot = new Grid { Background = paper, ClipToBounds = true, Cursor = Cursors.Hand };
            replayRoot.Children.Add(stage);
            replayProgress = new Rectangle { Height = 3, Fill = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Left, RadiusX = 1.5, RadiusY = 1.5 };
            replayText = new TextBlock { Foreground = new SolidColorBrush(Ui.Text), FontFamily = Ui.Font, FontSize = 12, Text = "Repitiendo el dibujo. Clic o Esc para terminar", Margin = new Thickness(0, 0, 0, 6) };
            var box = new StackPanel { Width = 320 };
            box.Children.Add(replayText);
            box.Children.Add(replayProgress);
            replayBar = Ui.Card(box, 12, 10);
            replayBar.HorizontalAlignment = HorizontalAlignment.Center;
            replayBar.VerticalAlignment = VerticalAlignment.Bottom;
            replayBar.Margin = new Thickness(0, 0, 0, 24);
            replayRoot.Children.Add(replayBar);
            replayRoot.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { StopReplay(); e.Handled = true; };
            overlay.Children.Add(replayRoot);
        }

        // Dibuja los siguientes puntos; devuelve false al terminar.
        internal bool ReplayAdvance(int points)
        {
            if (!Replaying) return false;
            while (points > 0 && replayStroke < replaySource.Count)
            {
                var src = replaySource[replayStroke];
                if (replayLive == null)
                {
                    var first = new StylusPointCollection(src.StylusPoints.Take(1));
                    var da = src.DrawingAttributes.Clone();
                    replayLive = src is RichStroke ? new RichStroke(first, da) : new Stroke(first, da);
                    replayStrokes.Add(replayLive);
                    replayPoint = 1;
                    points--;
                    ReplayDone++;
                }
                while (points > 0 && replayPoint < src.StylusPoints.Count)
                {
                    replayLive.StylusPoints.Add(src.StylusPoints[replayPoint++]);
                    points--;
                    ReplayDone++;
                }
                if (replayPoint >= src.StylusPoints.Count)
                {
                    replayStroke++;
                    replayLive = null;
                }
            }
            replayProgress.Width = 320.0 * ReplayDone / Math.Max(1, ReplayTotal);
            return replayStroke < replaySource.Count;
        }

        internal int ReplayStrokeCount { get { return replayStrokes == null ? 0 : replayStrokes.Count; } }

        internal void StopReplay()
        {
            replayTimer.Stop();
            if (replayRoot == null) return;
            overlay.Children.Remove(replayRoot);
            replayRoot = null;
            replayStrokes = null;
            Ink.Focus();
        }

        // ---- Captura de una región: congela la pantalla y se recorta arrastrando ----
        internal void StartRegion()
        {
            if (regionOpen) return;
            FinishText();
            Commit();
            if (Tool == Tool.Cursor) SetTool(lastDrawTool);
            double op = Opacity;
            if (!ScreenMode) Opacity = 0; // en pantalla el dibujo forma parte de la captura
            System.Drawing.Bitmap bmp;
            try { bmp = CaptureScreen(); }
            finally { Opacity = op; }
            BitmapSource frozen;
            var hb = bmp.GetHbitmap();
            try
            {
                frozen = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                frozen.Freeze();
            }
            finally { Native.DeleteObject(hb); }

            var dim = new System.Windows.Shapes.Path { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0x05, 0x0A, 0x16)), IsHitTestVisible = false };
            var band = new Rectangle { Stroke = new SolidColorBrush(Ui.Cyan), StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed, Effect = new DropShadowEffect { Color = Ui.Cyan, BlurRadius = 12, ShadowDepth = 0 } };
            var size = new TextBlock { Foreground = new SolidColorBrush(Ui.Cyan), FontFamily = Ui.Font, FontSize = 12, Text = "Arrastra para elegir la región. Esc cancela", Margin = new Thickness(4) };
            var label = Ui.Card(size, 8, 2);
            label.IsHitTestVisible = false;
            var marks = new Canvas { IsHitTestVisible = false };
            marks.Children.Add(band);
            marks.Children.Add(label);
            Canvas.SetLeft(label, 20);
            Canvas.SetTop(label, 20);
            regionRoot = new Grid { Cursor = Cursors.Cross, Background = Brushes.Black };
            regionRoot.Children.Add(new Image { Source = frozen, Stretch = Stretch.Fill, IsHitTestVisible = false });
            regionRoot.Children.Add(dim);
            regionRoot.Children.Add(marks);
            overlay.Children.Add(regionRoot);
            regionRoot.UpdateLayout();
            Action<Rect> paint = r =>
            {
                var all = new RectangleGeometry(new Rect(0, 0, regionRoot.ActualWidth, regionRoot.ActualHeight));
                dim.Data = r.IsEmpty ? (Geometry)all : new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(r));
            };
            paint(Rect.Empty);
            regionOpen = true;
            Point start = new Point();
            Rect sel = Rect.Empty;
            bool down = false;
            regionRoot.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                start = e.GetPosition(regionRoot);
                down = true;
                regionRoot.CaptureMouse();
                band.Visibility = Visibility.Visible;
            };
            regionRoot.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!down) return;
                sel = new Rect(start, e.GetPosition(regionRoot));
                paint(sel);
                band.Width = sel.Width;
                band.Height = sel.Height;
                Canvas.SetLeft(band, sel.X);
                Canvas.SetTop(band, sel.Y);
                Canvas.SetLeft(label, sel.X);
                Canvas.SetTop(label, Math.Max(4, sel.Y - 34));
                double ds = DeviceScale;
                size.Text = (int)(sel.Width * ds) + " × " + (int)(sel.Height * ds) + " px";
            };
            regionRoot.MouseLeftButtonUp += delegate
            {
                if (!down) return;
                down = false;
                regionRoot.ReleaseMouseCapture();
                CloseRegion();
                using (bmp)
                    if (sel.Width >= 4 && sel.Height >= 4) CompleteRegion(bmp, sel, true);
            };
            regionCancel = delegate { bmp.Dispose(); };
            Activate();
            Focus();
        }

        Action regionCancel;

        void CloseRegion()
        {
            regionOpen = false;
            overlay.Children.Remove(regionRoot);
            regionRoot = null;
            regionCancel = null;
            Ink.Focus();
        }

        // Recorta la captura, la copia al portapapeles y la pega en una hoja nueva.
        internal void CompleteRegion(System.Drawing.Bitmap bmp, Rect view, bool clipboard)
        {
            double s = DeviceScale;
            var area = System.Drawing.Rectangle.Intersect(
                new System.Drawing.Rectangle((int)(view.X * s), (int)(view.Y * s), (int)(view.Width * s), (int)(view.Height * s)),
                new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
            if (area.Width < 1 || area.Height < 1) return;
            using (var crop = bmp.Clone(area, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var ms = new MemoryStream())
            {
                crop.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                if (clipboard)
                {
                    try { System.Windows.Forms.Clipboard.SetImage(crop); }
                    catch (System.Runtime.InteropServices.ExternalException) { }
                }
                if (ScreenMode) SetMode(false);
                AddSheet();
                InsertImageBytes(ms.ToArray(), area.Width / s, area.Height / s);
            }
            Toast(clipboard ? "Región copiada al portapapeles y pegada en una hoja nueva" : "Región pegada en una hoja nueva");
        }

        // ---- SVG ----
        static string Num(double v) { return Math.Round(v, 2).ToString(CultureInfo.InvariantCulture); }
        static string Css(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        static string Esc(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }

        internal string BuildSvg()
        {
            FinishText();
            Commit();
            Rect b = ContentBounds();
            b = b.IsEmpty ? ViewRect() : Rect.Inflate(b, 24, 24);
            var sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"" + Num(b.Width) + "\" height=\"" + Num(b.Height) + "\" viewBox=\"" + Num(b.X) + " " + Num(b.Y) + " " + Num(b.Width) + " " + Num(b.Height) + "\">\n");
            if (!ScreenMode)
                sb.Append("<rect x=\"" + Num(b.X) + "\" y=\"" + Num(b.Y) + "\" width=\"" + Num(b.Width) + "\" height=\"" + Num(b.Height) + "\" fill=\"" + Css(PaperColor(Cur.Paper)) + "\"/>\n");
            foreach (var el in Ink.Children.OfType<FrameworkElement>()) AppendItem(sb, el);
            foreach (var s in Ink.Strokes) AppendStroke(sb, s);
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        static void AppendStroke(StringBuilder sb, Stroke s)
        {
            var da = s.DrawingAttributes;
            var style = (StrokeStyle)RichStroke.Prop(da, RichStroke.StyleId);
            bool fill = RichStroke.Prop(da, RichStroke.FillId) != 0;
            string opacity = Num(da.Color.A / 255.0 * (da.IsHighlighter ? 0.5 : 1));
            if (style == StrokeStyle.Plain && !fill)
            {
                // Contorno real del trazo: conserva presión y forma de la plumilla.
                string d = s.GetGeometry().GetFlattenedPathGeometry(0.15, ToleranceType.Absolute).ToString(CultureInfo.InvariantCulture);
                if (d.StartsWith("F0") || d.StartsWith("F1")) d = d.Substring(2);
                if (d.Length > 0) sb.Append("<path d=\"" + d + "\" fill=\"" + Css(da.Color) + "\" fill-opacity=\"" + opacity + "\"/>\n");
                return;
            }
            var path = new StringBuilder();
            for (int i = 0; i < s.StylusPoints.Count; i++)
                path.Append((i == 0 ? "M" : "L") + Num(s.StylusPoints[i].X) + " " + Num(s.StylusPoints[i].Y) + " ");
            string color = style == StrokeStyle.Plasma ? "#22D3EE" : Css(da.Color), dash = "";
            if (style == StrokeStyle.Dashed) dash = " stroke-dasharray=\"" + Num(da.Width * 3.2) + " " + Num(da.Width * 2.4) + "\"";
            if (style == StrokeStyle.Dotted) dash = " stroke-dasharray=\"0 " + Num(da.Width * 2.1) + "\"";
            string fillAttr = fill ? "fill=\"" + color + "\" fill-opacity=\"0.35\"" : "fill=\"none\"";
            // Neón y plasma: un halo ancho y translúcido debajo del trazo.
            if (style == StrokeStyle.Neon || style == StrokeStyle.Plasma)
                sb.Append("<path d=\"" + path.ToString().TrimEnd() + "\" fill=\"none\" stroke=\"" + color + "\" stroke-opacity=\"0.3\" stroke-width=\"" + Num(da.Width * 2.6) + "\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>\n");
            sb.Append("<path d=\"" + path.ToString().TrimEnd() + "\" " + fillAttr + " stroke=\"" + color + "\" stroke-opacity=\"" + opacity + "\" stroke-width=\"" + Num(da.Width) + "\" stroke-linecap=\"round\" stroke-linejoin=\"round\"" + dash + "/>\n");
        }

        void AppendItem(StringBuilder sb, FrameworkElement el)
        {
            var meta = el.Tag as Item;
            if (meta == null) return;
            double x = InkCanvas.GetLeft(el), y = InkCanvas.GetTop(el);
            if (meta.Kind == ItemKind.Image)
            {
                sb.Append("<image x=\"" + Num(x) + "\" y=\"" + Num(y) + "\" width=\"" + Num(el.Width) + "\" height=\"" + Num(el.Height) + "\" xlink:href=\"data:image/png;base64," + Convert.ToBase64String(meta.Data) + "\"/>\n");
                return;
            }
            var rt = el as RichTextBox;
            if (rt == null) return;
            if (meta.Kind == ItemKind.Sticky)
                sb.Append("<rect x=\"" + Num(x) + "\" y=\"" + Num(y) + "\" width=\"" + Num(el.ActualWidth) + "\" height=\"" + Num(el.ActualHeight) + "\" fill=\"" + Css(meta.Bg) + "\"/>\n");
            double px = x + rt.Padding.Left, py = y + rt.Padding.Top;
            foreach (var para in rt.Document.Blocks.OfType<Paragraph>())
            {
                var range = new TextRange(para.ContentStart, para.ContentEnd);
                object sizeValue = range.GetPropertyValue(TextElement.FontSizeProperty);
                double size = sizeValue is double ? (double)sizeValue : FontPt;
                var brush = range.GetPropertyValue(TextElement.ForegroundProperty) as SolidColorBrush;
                var family = range.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                py += size * 1.25;
                if (range.Text.Trim().Length > 0)
                    sb.Append("<text x=\"" + Num(px) + "\" y=\"" + Num(py) + "\" font-size=\"" + Num(size) + "\" font-family=\"" + Esc(family != null ? family.Source : FontName) + "\" fill=\"" + Css(brush != null ? brush.Color : InkColor) + "\">" + Esc(range.Text.TrimEnd('\r', '\n')) + "</text>\n");
            }
        }

        internal void ExportSvg()
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Imagen vectorial SVG (*.svg)|*.svg", FileName = "Hoja " + (SheetIndex + 1), DefaultExt = ".svg" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(d.FileName, BuildSvg(), new UTF8Encoding(false));
                Toast("SVG guardado");
            }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo guardar el SVG:\n" + ex.Message, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ---- Favoritos ----
        static string PresetsPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pizarra", "favoritos.txt"); }
        }

        internal void ApplyPreset(int i)
        {
            var p = Presets[i];
            FinishText();
            Pen = p.Pen;
            InkColor = p.Ink;
            PenSize = p.Size;
            Trace = p.Trace;
            InkOpacity = p.Opacity;
            SetTool(Tool.Pen);
            Toast("Favorito " + (i + 1) + ": " + ToolbarWindow.PenNames[(int)p.Pen]);
        }

        internal void StorePreset(int i)
        {
            Presets[i] = new PenPreset { Pen = Pen, Ink = InkColor, Size = PenSize, Trace = Trace, Opacity = InkOpacity };
            Bar.Refresh();
            Toast("Lápiz guardado en el favorito " + (i + 1));
            if (!AutosaveEnabled) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PresetsPath));
                File.WriteAllLines(PresetsPath, Presets.Select(FormatPreset));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string FormatPreset(PenPreset p)
        {
            return (int)p.Pen + ";" + p.Ink.R + ";" + p.Ink.G + ";" + p.Ink.B + ";" + p.Size.ToString(CultureInfo.InvariantCulture) + ";" + (int)p.Trace + ";" + p.Opacity.ToString(CultureInfo.InvariantCulture);
        }

        // Devuelve null si la línea no es válida: un archivo dañado no impide arrancar.
        internal static PenPreset ParsePreset(string line)
        {
            var f = line.Split(';');
            int pen, trace;
            byte r, g, b;
            double size, op;
            if (f.Length != 7 || !int.TryParse(f[0], out pen) || !byte.TryParse(f[1], out r) || !byte.TryParse(f[2], out g) || !byte.TryParse(f[3], out b)
                || !double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out size) || !int.TryParse(f[5], out trace)
                || !double.TryParse(f[6], NumberStyles.Float, CultureInfo.InvariantCulture, out op)) return null;
            if (!Enum.IsDefined(typeof(PenKind), pen) || !Enum.IsDefined(typeof(TraceMode), trace) || size < 1 || size > 40 || op < 0.1 || op > 1) return null;
            return new PenPreset { Pen = (PenKind)pen, Ink = Color.FromRgb(r, g, b), Size = size, Trace = (TraceMode)trace, Opacity = op };
        }

        void LoadPresets()
        {
            if (!AutosaveEnabled) return;
            try
            {
                if (!File.Exists(PresetsPath)) return;
                var lines = File.ReadAllLines(PresetsPath);
                for (int i = 0; i < Math.Min(lines.Length, Presets.Length); i++)
                {
                    var p = ParsePreset(lines[i]);
                    if (p != null) Presets[i] = p;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ---- Selección ----
        internal void SelectAll()
        {
            SetTool(Tool.Select);
            Ink.Select(Ink.Strokes, Ink.Children.OfType<UIElement>().ToList());
        }

        internal int DuplicateSelection()
        {
            var strokes = Ink.GetSelectedStrokes();
            var elements = Ink.GetSelectedElements().OfType<FrameworkElement>().ToList();
            if (strokes.Count == 0 && elements.Count == 0) { Toast("Selecciona algo para duplicarlo"); return 0; }
            var newStrokes = new StrokeCollection();
            foreach (var s in strokes)
            {
                var copy = s.Clone();
                copy.Transform(new Matrix(1, 0, 0, 1, 24, 24), false);
                newStrokes.Add(copy);
            }
            Ink.Strokes.Add(newStrokes);
            var newElements = new List<UIElement>();
            foreach (var el in elements)
            {
                var item = ItemOf(el);
                if (item == null) continue;
                item.X += 24;
                item.Y += 24;
                newElements.Add(AddElement(item));
            }
            Ink.Select(newStrokes, newElements);
            dirty = true;
            Commit();
            return newStrokes.Count + newElements.Count;
        }

        // Alinea cada elemento con el borde o el centro del conjunto seleccionado.
        internal void AlignSelection(string mode)
        {
            var strokes = Ink.GetSelectedStrokes();
            var elements = Ink.GetSelectedElements().OfType<FrameworkElement>().ToList();
            if (strokes.Count + elements.Count < 2) { Toast("Selecciona al menos dos elementos para alinearlos"); return; }
            Func<FrameworkElement, Rect> box = el => new Rect(InkCanvas.GetLeft(el), InkCanvas.GetTop(el), el.ActualWidth, el.ActualHeight);
            Rect all = Rect.Empty;
            foreach (var s in strokes) all.Union(s.GetBounds());
            foreach (var el in elements) all.Union(box(el));
            Func<Rect, Vector> shift = r =>
            {
                switch (mode)
                {
                    case "izquierda": return new Vector(all.Left - r.Left, 0);
                    case "centro": return new Vector(all.X + all.Width / 2 - (r.X + r.Width / 2), 0);
                    case "derecha": return new Vector(all.Right - r.Right, 0);
                    case "arriba": return new Vector(0, all.Top - r.Top);
                    case "medio": return new Vector(0, all.Y + all.Height / 2 - (r.Y + r.Height / 2));
                    default: return new Vector(0, all.Bottom - r.Bottom);
                }
            };
            foreach (var s in strokes)
            {
                Vector v = shift(s.GetBounds());
                s.Transform(new Matrix(1, 0, 0, 1, v.X, v.Y), false);
            }
            foreach (var el in elements)
            {
                Rect r = box(el);
                Vector v = shift(r);
                InkCanvas.SetLeft(el, r.X + v.X);
                InkCanvas.SetTop(el, r.Y + v.Y);
            }
            Ink.Select(strokes, elements.Cast<UIElement>().ToList());
            dirty = true;
            Commit();
        }

        // ---- Paleta de comandos ----
        internal List<Cmd> BuildCommands()
        {
            var l = new List<Cmd>
            {
                new Cmd("Lápiz", "P", delegate { SetTool(Tool.Pen); }),
                new Cmd("Resaltador", "H", delegate { SetTool(Tool.Highlighter); }),
                new Cmd("Borrador", "E", delegate { SetTool(Tool.Eraser); }),
                new Cmd("Borrar trazo completo", "", delegate { SetTool(Tool.StrokeEraser); }),
                new Cmd("Cuadro de texto", "T", delegate { SetTool(Tool.Text); }),
                new Cmd("Nota adhesiva", "N", delegate { SetTool(Tool.Sticky); }),
                new Cmd("Seleccionar", "V", delegate { SetTool(Tool.Select); }),
                new Cmd("Mano: mover la vista", "M", delegate { SetTool(Tool.Hand); }),
                new Cmd("Puntero láser", "K", delegate { SetTool(Tool.Laser); }),
                new Cmd("Foco", "", delegate { SetTool(Tool.Spotlight); }),
                new Cmd("Lupa", "", delegate { SetTool(Tool.Magnifier); }),
                new Cmd("Cuentagotas", "", delegate { SetTool(Tool.Eyedropper); }),
                new Cmd("Modo pantalla: dibujar sobre el escritorio", "", delegate { SetMode(true); }),
                new Cmd("Modo hojas", "", delegate { SetMode(false); }),
                new Cmd("Modo escritura", "W", delegate { EnterWriteMode(); }),
                new Cmd("Presentación", "F5", EnterPresent),
                new Cmd("Repetir el dibujo trazo a trazo", "", StartReplay),
                new Cmd("Panel de miniaturas", "F2", ToggleSheetPanel),
                new Cmd("Minimapa: mostrar u ocultar", "", ToggleMinimap),
                new Cmd("Ajustar la vista al contenido", "Ctrl+0", FitContent),
                new Cmd("Vista al 100 %", "", ResetView),
                new Cmd("Hoja nueva", "Ctrl+N", AddSheet),
                new Cmd("Duplicar hoja", "", DuplicateSheet),
                new Cmd("Hoja anterior", "RePág", delegate { GoToSheet(SheetIndex - 1); }),
                new Cmd("Hoja siguiente", "AvPág", delegate { GoToSheet(SheetIndex + 1); }),
                new Cmd("Deshacer", "Ctrl+Z", Undo),
                new Cmd("Rehacer", "Ctrl+Y", Redo),
                new Cmd("Borrar todo", "", ClearCurrent),
                new Cmd("Seleccionar todo", "Ctrl+A", SelectAll),
                new Cmd("Duplicar la selección", "Ctrl+D", delegate { DuplicateSelection(); }),
                new Cmd("Convertir escritura a texto", "", ConvertSelectionToText),
                new Cmd("Convertir dibujos en formas", "", delegate { ConvertSelectionToShapes(); }),
                new Cmd("Suavizar trazos", "", SmoothSelection),
                new Cmd("Grabar la pantalla en vídeo", "Ctrl+Alt+R", ToggleRecording),
                new Cmd("Capturar una región de la pantalla", "", StartRegion),
                new Cmd("Capturar la pantalla en una hoja nueva", "", CaptureToSheet),
                new Cmd("Insertar imagen", "", PickImage),
                new Cmd("Guardar", "Ctrl+S", delegate { Save(false); }),
                new Cmd("Guardar como", "Ctrl+Mayús+S", delegate { Save(true); }),
                new Cmd("Abrir cuaderno", "Ctrl+O", Open),
                new Cmd("Exportar imagen", "", SaveImage),
                new Cmd("Exportar a PDF", "", ExportPdf),
                new Cmd("Exportar a SVG", "", ExportSvg),
                new Cmd("Regla", "", delegate { ToggleGuide(GuideKind.Ruler); }),
                new Cmd("Transportador", "", delegate { ToggleGuide(GuideKind.Protractor); }),
                new Cmd("Ayuda y atajos", "F1", ShowHelp)
            };
            foreach (PenKind k in Enum.GetValues(typeof(PenKind)))
            {
                var kk = k;
                l.Add(new Cmd("Lápiz: " + ToolbarWindow.PenNames[(int)k], "", delegate { SetPen(kk); }));
            }
            foreach (ShapeKind k in Enum.GetValues(typeof(ShapeKind)))
            {
                var kk = k;
                l.Add(new Cmd("Forma: " + Shapes.Name(k).Split('(')[0].Trim(), "", delegate { SetShape(kk); }));
            }
            foreach (TraceMode m in Enum.GetValues(typeof(TraceMode)))
            {
                var mm = m;
                l.Add(new Cmd("Trazo: " + ToolbarWindow.TraceNames[(int)m], "", delegate { SetTrace(mm); }));
            }
            string[] papers = { "Blanca", "Cuadriculada", "Rayada", "Puntos", "Negra", "Verde", "Milimetrada", "Pentagrama", "Isométrica", "Pauta", "Cyber", "Plano técnico" };
            foreach (Paper p in Enum.GetValues(typeof(Paper)))
            {
                var pp = p;
                l.Add(new Cmd("Fondo: " + papers[(int)p], "", delegate { SetPaper(pp); }));
            }
            for (int i = 0; i < Presets.Length; i++)
            {
                int slot = i;
                l.Add(new Cmd("Favorito " + (i + 1), "Ctrl+" + (i + 1), delegate { ApplyPreset(slot); }));
            }
            return l;
        }

        static string Plain(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s.Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        // Todas las palabras deben aparecer, sin distinguir acentos; primero lo que empieza igual.
        internal static List<Cmd> FilterCommands(List<Cmd> all, string query)
        {
            var tokens = Plain(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string q = string.Join(" ", tokens);
            return all.Where(c => { string n = Plain(c.Name); return tokens.All(t => n.Contains(t)); })
                .OrderBy(c => Plain(c.Name).StartsWith(q) ? 0 : 1)
                .ToList();
        }

        TextBox paletteBox;
        StackPanel paletteList;
        List<Cmd> paletteAll, paletteShown;
        int paletteIndex;

        internal bool PaletteOpen { get { return paletteOpen; } }
        internal TextBox PaletteBox { get { return paletteBox; } }
        internal int PaletteIndex { get { return paletteIndex; } }

        internal void ShowPalette()
        {
            if (paletteOpen) return;
            if (Tool == Tool.Cursor) SetTool(lastDrawTool);
            FinishText();
            paletteAll = BuildCommands();
            paletteBox = new TextBox
            {
                FontFamily = Ui.Font,
                FontSize = 17,
                Foreground = new SolidColorBrush(Ui.Text),
                CaretBrush = new SolidColorBrush(Ui.Cyan),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            paletteBox.TextChanged += delegate { paletteIndex = 0; RefreshPalette(); };
            var search = new DockPanel { Margin = new Thickness(8, 6, 8, 8) };
            var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = new SolidColorBrush(Ui.Cyan), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            search.Children.Add(icon);
            search.Children.Add(paletteBox);
            paletteList = new StackPanel();
            var body = new StackPanel();
            body.Children.Add(search);
            body.Children.Add(new Border { Height = 1, Background = Ui.Edge, Margin = new Thickness(0, 0, 0, 6) });
            body.Children.Add(paletteList);
            var card = Ui.Card(body, 16, 8);
            card.Width = 560;
            card.VerticalAlignment = VerticalAlignment.Top;
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.Margin = new Thickness(0, 130, 0, 0);
            paletteRoot = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x70, 0x05, 0x0A, 0x16)) };
            paletteRoot.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.OriginalSource == paletteRoot) { ClosePalette(); e.Handled = true; } };
            paletteRoot.Children.Add(card);
            overlay.Children.Add(paletteRoot);
            paletteOpen = true;
            paletteIndex = 0;
            RefreshPalette();
            Ui.SlideIn(card, -12, 180);
            Activate();
            paletteBox.Focus();
            Keyboard.Focus(paletteBox);
        }

        void RefreshPalette()
        {
            paletteShown = FilterCommands(paletteAll, paletteBox.Text);
            paletteIndex = Math.Max(0, Math.Min(paletteIndex, Math.Min(8, paletteShown.Count - 1)));
            paletteList.Children.Clear();
            if (paletteShown.Count == 0)
                paletteList.Children.Add(Ui.Caption("Sin resultados"));
            for (int i = 0; i < Math.Min(9, paletteShown.Count); i++)
            {
                var cmd = paletteShown[i];
                bool on = i == paletteIndex;
                var keys = new TextBlock { Text = cmd.Keys, FontFamily = Ui.Font, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
                var name = new TextBlock { Text = cmd.Name, FontFamily = Ui.Font, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                var line = new DockPanel { LastChildFill = true, Width = 520 };
                DockPanel.SetDock(keys, Dock.Right);
                line.Children.Add(keys);
                line.Children.Add(name);
                var b = new Button { Style = Ui.ButtonStyle, Content = line, Width = double.NaN, Height = 34, HorizontalContentAlignment = HorizontalAlignment.Left, Tag = on ? "on" : null };
                b.Click += delegate { RunCommand(cmd); };
                paletteList.Children.Add(b);
            }
        }

        void RunCommand(Cmd cmd)
        {
            ClosePalette();
            cmd.Run();
        }

        internal void ClosePalette()
        {
            if (!paletteOpen) return;
            paletteOpen = false;
            overlay.Children.Remove(paletteRoot);
            paletteRoot = null;
            if (Tool != Tool.Cursor) Ink.Focus();
        }

        // ---- Menú radial ----
        internal const int RadialCount = 8;
        const double RadialOuter = 118, RadialInner = 42;

        struct RadialEntry { public string Glyph, Label; public Action Run; }

        RadialEntry[] RadialEntries()
        {
            return new[]
            {
                new RadialEntry { Glyph = "", Label = "Lápiz", Run = delegate { SetTool(Tool.Pen); } },
                new RadialEntry { Glyph = "", Label = "Resaltar", Run = delegate { SetTool(Tool.Highlighter); } },
                new RadialEntry { Glyph = "", Label = "Borrar", Run = delegate { SetTool(Tool.Eraser); } },
                new RadialEntry { Glyph = "", Label = "Texto", Run = delegate { SetTool(Tool.Text); } },
                new RadialEntry { Glyph = "", Label = "Elegir", Run = delegate { SetTool(Tool.Select); } },
                new RadialEntry { Glyph = "", Label = "Forma", Run = delegate { SetTool(Tool.Shape); } },
                new RadialEntry { Glyph = "", Label = "Láser", Run = delegate { SetTool(Tool.Laser); } },
                new RadialEntry { Glyph = "", Label = "Deshacer", Run = Undo }
            };
        }

        internal void RadialPick(int i)
        {
            var entries = RadialEntries();
            CloseRadial();
            entries[i].Run();
        }

        static Geometry Wedge(double a0, double a1)
        {
            Func<double, double, Point> at = (r, deg) => new Point(r * Math.Cos(deg * Math.PI / 180), r * Math.Sin(deg * Math.PI / 180));
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(at(RadialInner, a0), true, true);
                c.LineTo(at(RadialOuter, a0), true, true);
                c.ArcTo(at(RadialOuter, a1), new Size(RadialOuter, RadialOuter), 0, false, SweepDirection.Clockwise, true, true);
                c.LineTo(at(RadialInner, a1), true, true);
                c.ArcTo(at(RadialInner, a0), new Size(RadialInner, RadialInner), 0, false, SweepDirection.Counterclockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        internal bool RadialOpen { get { return radialOpen; } }

        internal void ShowRadial(Point at)
        {
            CloseRadial();
            double m = RadialOuter + 8;
            at = new Point(Math.Max(m, Math.Min(root.ActualWidth - m, at.X)), Math.Max(m, Math.Min(root.ActualHeight - m, at.Y)));
            var ring = new Canvas();
            Canvas.SetLeft(ring, at.X);
            Canvas.SetTop(ring, at.Y);
            var entries = RadialEntries();
            Brush idle = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x18, 0x2C));
            Brush line = new SolidColorBrush(Color.FromArgb(0x70, 0x22, 0xD3, 0xEE));
            for (int i = 0; i < entries.Length; i++)
            {
                int index = i;
                double mid = -90 + i * 360.0 / entries.Length, half = 180.0 / entries.Length - 1.2;
                var wedge = new System.Windows.Shapes.Path { Data = Wedge(mid - half, mid + half), Fill = idle, Stroke = line, StrokeThickness = 1, ToolTip = null };
                double r = (RadialInner + RadialOuter) / 2, x = r * Math.Cos(mid * Math.PI / 180), y = r * Math.Sin(mid * Math.PI / 180);
                var glyph = new TextBlock { Text = entries[i].Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Foreground = new SolidColorBrush(Ui.Text), TextAlignment = TextAlignment.Center, Width = 70, IsHitTestVisible = false };
                var label = new TextBlock { Text = entries[i].Label, FontFamily = Ui.Font, FontSize = 10, Foreground = new SolidColorBrush(Ui.Text), TextAlignment = TextAlignment.Center, Width = 70, IsHitTestVisible = false };
                Canvas.SetLeft(glyph, x - 35); Canvas.SetTop(glyph, y - 18);
                Canvas.SetLeft(label, x - 35); Canvas.SetTop(label, y + 4);
                wedge.MouseEnter += delegate
                {
                    wedge.Fill = Ui.Accent;
                    glyph.Foreground = label.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x12, 0x20));
                };
                wedge.MouseLeave += delegate
                {
                    wedge.Fill = idle;
                    glyph.Foreground = label.Foreground = new SolidColorBrush(Ui.Text);
                };
                // Clic en el sector, o arrastrar con el botón derecho y soltar sobre él.
                MouseButtonEventHandler pick = delegate(object s, MouseButtonEventArgs e) { RadialPick(index); e.Handled = true; };
                wedge.MouseLeftButtonUp += pick;
                wedge.MouseRightButtonUp += pick;
                wedge.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
                ring.Children.Add(wedge);
                ring.Children.Add(glyph);
                ring.Children.Add(label);
            }
            var hub = new Ellipse { Width = RadialInner * 2 - 8, Height = RadialInner * 2 - 8, Fill = idle, Stroke = Ui.Accent, StrokeThickness = 1.5, Effect = new DropShadowEffect { Color = Ui.Cyan, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 }, IsHitTestVisible = false };
            Canvas.SetLeft(hub, -hub.Width / 2);
            Canvas.SetTop(hub, -hub.Height / 2);
            ring.Children.Add(hub);
            var mark = new System.Windows.Shapes.Path { Data = Geometry.Parse("M9,1 L16,5 V13 L9,17 L2,13 V5 Z"), Fill = Ui.Accent, Width = 18, Height = 18, IsHitTestVisible = false };
            Canvas.SetLeft(mark, -9);
            Canvas.SetTop(mark, -9);
            ring.Children.Add(mark);
            radialRoot = new Grid { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            radialRoot.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { CloseRadial(); e.Handled = true; };
            radialRoot.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e) { CloseRadial(); e.Handled = true; };
            var surface = new Canvas();
            surface.Children.Add(ring);
            radialRoot.Children.Add(surface);
            overlay.Children.Add(radialRoot);
            radialOpen = true;
            if (Ui.Motion)
            {
                var scale = new ScaleTransform(0.6, 0.6);
                ring.RenderTransform = scale;
                var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }

        internal void CloseRadial()
        {
            if (!radialOpen) return;
            radialOpen = false;
            overlay.Children.Remove(radialRoot);
            radialRoot = null;
        }

        // ---- Ayuda ----
        internal bool HelpOpen { get { return helpOpen; } }

        internal void ShowHelpOverlay()
        {
            if (helpOpen) return;
            if (Tool == Tool.Cursor) SetTool(lastDrawTool);
            string[][] sections =
            {
                new[] { "Herramientas", "P lápiz · H resaltador · E borrador", "T texto · W escritura · N nota", "V seleccionar · M mano · K láser", "L línea · A flecha · R rectángulo · O elipse", "1-9 y 0 colores · [ y ] grosor", "Botón derecho: menú radial" },
                new[] { "Pro", "Ctrl+K paleta de comandos", "F5 presentación · F2 miniaturas", "Ctrl+Alt+R grabar la pantalla en vídeo", "Ctrl+0 ajustar la vista al contenido", "Ctrl+1 a 5 favoritos", "Ctrl+Mayús+1 a 5 guardar favorito", "Ctrl+A seleccionar todo · Ctrl+D duplicar" },
                new[] { "Trazo punto a punto", "Clic añade punto", "Doble clic, botón derecho o Enter termina", "Retroceso quita el último · Esc cancela", "Clic junto al inicio cierra", "Mayús ajusta a 45 grados" },
                new[] { "Letras a texto", "Escribe a mano y haz una pausa", "Enter acepta · Esc deja el trazo", "Seguir escribiendo acepta" },
                new[] { "Hojas", "Ctrl+N nueva · RePág y AvPág cambiar", "Rueda desplazar · Mayús+rueda horizontal", "Ctrl+rueda zoom · botón central arrastrar" },
                new[] { "General", "Ctrl+Z deshacer · Ctrl+Y rehacer", "Ctrl+S guardar · Ctrl+Mayús+S guardar como", "Ctrl+O abrir · Ctrl+V pegar · Supr borrar", "Esc o Ctrl+Alt+D: puntero", "Regla y transportador: arrastrar y girar con la rueda" }
            };
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Width = 900 };
            foreach (var sec in sections)
            {
                var col = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
                col.Children.Add(new TextBlock { Text = sec[0], Foreground = new SolidColorBrush(Ui.Cyan), FontFamily = Ui.Font, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
                foreach (var line in sec.Skip(1))
                    col.Children.Add(new TextBlock { Text = line, Foreground = new SolidColorBrush(Ui.Text), FontFamily = Ui.Font, FontSize = 12, Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap });
                grid.Children.Add(col);
            }
            var body = new StackPanel();
            var head = Ui.Row();
            head.Children.Add(Ui.Logo());
            head.Children.Add(new TextBlock { Text = "Atajos de Pizarra", Foreground = new SolidColorBrush(Ui.Text), FontFamily = Ui.Font, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
            head.Margin = new Thickness(10, 6, 10, 6);
            body.Children.Add(head);
            body.Children.Add(grid);
            body.Children.Add(Ui.Caption("Esc o clic para cerrar"));
            var card = Ui.Card(body, 16, 12);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            helpRoot = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x90, 0x05, 0x0A, 0x16)) };
            helpRoot.Children.Add(card);
            helpRoot.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { CloseHelp(); e.Handled = true; };
            overlay.Children.Add(helpRoot);
            helpOpen = true;
            Ui.SlideIn(card, 14, 200);
            Activate();
            Ink.Focus();
        }

        internal void CloseHelp()
        {
            if (!helpOpen) return;
            helpOpen = false;
            overlay.Children.Remove(helpRoot);
            helpRoot = null;
        }

        // ---- Teclado: true = atendida; false = dejarla pasar al texto; null = sigue el proceso normal ----
        bool? ProKey(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;
            if (paletteOpen)
            {
                if (e.Key == Key.Escape) { ClosePalette(); return true; }
                if (e.Key == Key.Down || e.Key == Key.Up)
                {
                    int count = Math.Min(9, paletteShown.Count);
                    if (count > 0) paletteIndex = (paletteIndex + (e.Key == Key.Down ? 1 : count - 1)) % count;
                    RefreshPalette();
                    return true;
                }
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    if (paletteShown.Count > 0) RunCommand(paletteShown[paletteIndex]);
                    return true;
                }
                return false;
            }
            if (CountingDown && e.Key == Key.Escape) { ToggleRecording(); return true; }
            if (regionOpen)
            {
                if (e.Key == Key.Escape)
                {
                    var cancel = regionCancel;
                    CloseRegion();
                    if (cancel != null) cancel();
                }
                return true;
            }
            if (Replaying)
            {
                if (e.Key == Key.Escape) StopReplay();
                return true;
            }
            if (helpOpen)
            {
                if (e.Key == Key.Escape || e.Key == Key.F1) CloseHelp();
                return true;
            }
            if (radialOpen)
            {
                if (e.Key == Key.Escape) CloseRadial();
                return true;
            }
            if (ctrl && e.Key == Key.K) { ShowPalette(); return true; }
            if (e.Key == Key.F1) { ShowHelp(); return true; }
            if (e.Key == Key.F5) { if (Presenting) ExitPresent(); else EnterPresent(); return true; }
            if (e.Key == Key.F2) { ToggleSheetPanel(); return true; }
            if (Presenting && !typing)
            {
                if (e.Key == Key.Escape && Tool == Tool.Laser) { SetTool(Tool.Pen); return true; }
                if (e.Key == Key.Escape) { ExitPresent(); return true; }
                if (e.Key == Key.Right || e.Key == Key.Down || e.Key == Key.Space || e.Key == Key.PageDown) { PresentStep(1); return true; }
                if (e.Key == Key.Left || e.Key == Key.Up || e.Key == Key.PageUp) { PresentStep(-1); return true; }
            }
            if (ctrl && !typing)
            {
                if (e.Key == Key.A) { SelectAll(); return true; }
                if (e.Key == Key.D) { DuplicateSelection(); return true; }
                if (e.Key == Key.D0 || e.Key == Key.NumPad0) { FitContent(); return true; }
                int digit = e.Key >= Key.D1 && e.Key <= Key.D5 ? e.Key - Key.D1 : e.Key >= Key.NumPad1 && e.Key <= Key.NumPad5 ? e.Key - Key.NumPad1 : -1;
                if (digit >= 0)
                {
                    if (shift) StorePreset(digit); else ApplyPreset(digit);
                    return true;
                }
            }
            return null;
        }
    }
}
