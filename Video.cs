// Grabación de la pantalla a MP4 (H.264) con Media Foundation, incluido en Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pizarra
{
    // Solo se declaran hasta el último método usado; los a00... ocupan los huecos de IMFAttributes.
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void a00(); void a01(); void a02(); void a03(); void a04(); void a05(); void a06(); void a07(); void a08();
        void a09(); void a10(); void a11(); void a12(); void a13(); void a14(); void a15(); void a16(); void a17();
        void SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
        void SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
        void a20();
        void SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        void Lock(out IntPtr data, out int maxLength, out int currentLength);
        void Unlock();
        void GetCurrentLength(out int length);
        void SetCurrentLength(int length);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void a00(); void a01(); void a02(); void a03(); void a04(); void a05(); void a06(); void a07(); void a08(); void a09();
        void a10(); void a11(); void a12(); void a13(); void a14(); void a15(); void a16(); void a17(); void a18(); void a19();
        void a20(); void a21(); void a22(); void a23(); void a24(); void a25(); void a26(); void a27(); void a28(); void a29();
        void GetSampleFlags(out int flags);
        void SetSampleFlags(int flags);
        void GetSampleTime(out long time);
        void SetSampleTime(long time);
        void GetSampleDuration(out long duration);
        void SetSampleDuration(long duration);
        void GetBufferCount(out int count);
        void GetBufferByIndex(int index, out IMFMediaBuffer buffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        void AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        void AddStream(IMFMediaType type, out int stream);
        void SetInputMediaType(int stream, IMFMediaType type, IntPtr encodingParameters);
        void BeginWriting();
        void WriteSample(int stream, IMFSample sample);
        void SendStreamTick(int stream, long time);
        void PlaceMarker(int stream, IntPtr context);
        void NotifyEndOfSegment(int stream);
        void Flush(int stream);
        void Finish(); // IMFSinkWriter::Finalize
    }

    static class Mf
    {
        const int Version = 0x00020070;
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFStartup(int version, int flags);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFShutdown();
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateMemoryBuffer(int length, out IMFMediaBuffer buffer);
        [DllImport("mfreadwrite.dll", PreserveSig = false)] public static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr stream, IntPtr attributes, out IMFSinkWriter writer);
        public static void Start() { MFStartup(Version, 0); }

        public static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid Bitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid Interlace = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid PixelAspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static readonly Guid Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        public static readonly Guid Aac = new Guid("00001610-0000-0010-8000-00AA00389B71");
        public static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00AA00389B71");
        public static readonly Guid AudioChannels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        public static readonly Guid AudioRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        public static readonly Guid AudioBytesPerSecond = new Guid("1aab75c8-cfef-451c-ab95-ac034b8e1731");
        public static readonly Guid AudioBlock = new Guid("322de230-9eeb-43bd-ab7a-ff412251541d");
        public static readonly Guid AudioBits = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    }

    static class Gdi
    {
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, int usage, out IntPtr bits, IntPtr section, int offset);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, int affinity);
        [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr h, out int affinity);
        [DllImport("winmm.dll")] public static extern int timeBeginPeriod(int ms);
        [DllImport("winmm.dll")] public static extern int timeEndPeriod(int ms);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void CopyMemory(IntPtr dst, IntPtr src, IntPtr length);
    }

    // Captura y codifica en su propio hilo: dibujar sigue siendo fluido mientras se graba.
    class ScreenRecorder
    {
        internal const int Fps = 30;
        internal const int MaxWidth = 3840, MaxHeight = 2160;
        readonly string path;
        readonly System.Drawing.Rectangle area;
        readonly int width, height;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly Stopwatch clock = new Stopwatch();
        Thread thread;
        volatile bool stop;
        volatile Exception error;
        volatile int frames;
        IntPtr lastCursor;
        int hotX, hotY;

        // Margen para que lleguen los paquetes de audio antes de escribirlos: no retrasa el sonido.
        const int AudioMargin = AudioMath.Rate * 150 / 1000;
        readonly bool wantMic, wantSystem;
        readonly List<AudioSource> sources = new List<AudioSource>();
        readonly List<string> notes = new List<string>();
        float[] audioMix = new float[0];
        long audioWritten, origin;

        public ScreenRecorder(string file, System.Drawing.Rectangle screenArea) : this(file, screenArea, false, false) { }

        public ScreenRecorder(string file, System.Drawing.Rectangle screenArea, bool mic, bool systemAudio)
        {
            path = file;
            area = screenArea;
            wantMic = mic;
            wantSystem = systemAudio;
            var size = OutputSize(area.Width, area.Height);
            width = (int)size.Width;
            height = (int)size.Height;
        }

        public string FilePath { get { return path; } }
        public Exception Error { get { return error; } }
        public int Frames { get { return frames; } }
        public TimeSpan Elapsed { get { return clock.Elapsed; } }
        public bool HasAudio { get { return sources.Count > 0; } }
        // Avisos sobre el sonido; no se modifican mientras el hilo de grabación solo los lee.
        public string Notes { get { lock (notes) return string.Join("\n", notes); } }

        void Note(string text) { lock (notes) notes.Add(text); }

        // H.264 exige medidas pares; las pantallas enormes se reducen a 4K.
        internal static Size OutputSize(int w, int h)
        {
            double f = Math.Min(1, Math.Min((double)MaxWidth / w, (double)MaxHeight / h));
            return new Size((int)(w * f) & ~1, (int)(h * f) & ~1);
        }

        // Devuelve cuando la grabación ya ha empezado o ha fallado (ver Error).
        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "Grabación" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            ready.WaitOne();
        }

        public void Stop()
        {
            stop = true;
            if (thread != null) thread.Join();
            clock.Stop();
        }

        static IMFMediaType VideoType(Guid subtype, int w, int h)
        {
            IMFMediaType t;
            Mf.MFCreateMediaType(out t);
            t.SetGUID(Mf.MajorType, Mf.Video);
            t.SetGUID(Mf.Subtype, subtype);
            t.SetUINT32(Mf.Interlace, 2); // progresivo
            t.SetUINT64(Mf.FrameSize, ((long)w << 32) | (uint)h);
            t.SetUINT64(Mf.FrameRate, ((long)Fps << 32) | 1);
            t.SetUINT64(Mf.PixelAspect, (1L << 32) | 1);
            return t;
        }

        static IMFMediaType AudioType(Guid subtype, int bytesPerSecond)
        {
            IMFMediaType t;
            Mf.MFCreateMediaType(out t);
            t.SetGUID(Mf.MajorType, Mf.Audio);
            t.SetGUID(Mf.Subtype, subtype);
            t.SetUINT32(Mf.AudioBits, 16);
            t.SetUINT32(Mf.AudioRate, AudioMath.Rate);
            t.SetUINT32(Mf.AudioChannels, 2);
            t.SetUINT32(Mf.AudioBytesPerSecond, bytesPerSecond);
            return t;
        }

        // Un fallo de sonido nunca impide grabar la imagen.
        void OpenAudio(IMFSinkWriter writer, out int stream)
        {
            stream = -1;
            foreach (var system in new[] { false, true })
            {
                if (!(system ? wantSystem : wantMic)) continue;
                string name = system ? "audio del equipo" : "micrófono";
                try { sources.Add(new AudioSource(system)); }
                catch (Exception ex) { if (ex is COMException || ex is InvalidCastException) Note(AudioSource.Explain(name, ex)); else throw; }
            }
            if (sources.Count == 0) return;
            try
            {
                var output = AudioType(Mf.Aac, 24000); // 192 kbit/s
                writer.AddStream(output, out stream);
                Marshal.ReleaseComObject(output);
                var input = AudioType(Mf.Pcm, AudioMath.Rate * 4);
                input.SetUINT32(Mf.AudioBlock, 4);
                writer.SetInputMediaType(stream, input, IntPtr.Zero);
                Marshal.ReleaseComObject(input);
            }
            catch (COMException ex)
            {
                Note("El vídeo se grabó sin sonido: " + ex.Message);
                foreach (var s in sources) s.Close();
                sources.Clear();
                stream = -1;
            }
        }

        static long Now100ns() { return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency)); }

        // Recoge el audio y escribe la mezcla hasta upTo. Todo se escribe desde este hilo.
        void PumpAudio(IMFSinkWriter writer, int stream, long upTo)
        {
            foreach (var s in sources.ToList())
            {
                try { s.Poll(origin); }
                catch (COMException ex)
                {
                    Note("Se perdió el " + s.Name + " durante la grabación: " + ex.Message);
                    sources.Remove(s);
                }
            }
            while (audioWritten < upTo)
            {
                int frames = (int)Math.Min(upTo - audioWritten, AudioMath.Rate / 10);
                if (audioMix.Length < frames * 2) audioMix = new float[frames * 2];
                Array.Clear(audioMix, 0, frames * 2);
                foreach (var s in sources) s.Track.MixInto(audioWritten, frames, audioMix);
                WriteAudio(writer, stream, frames);
                audioWritten += frames;
            }
        }

        void WriteAudio(IMFSinkWriter writer, int stream, int frames)
        {
            IMFMediaBuffer buffer = null;
            IMFSample sample = null;
            try
            {
                int length = frames * 4;
                Mf.MFCreateMemoryBuffer(length, out buffer);
                IntPtr data;
                int max, cur;
                buffer.Lock(out data, out max, out cur);
                for (int i = 0; i < frames * 2; i++) Marshal.WriteInt16(data, i * 2, AudioMath.ToPcm16(audioMix[i]));
                buffer.Unlock();
                buffer.SetCurrentLength(length);
                Mf.MFCreateSample(out sample);
                sample.AddBuffer(buffer);
                sample.SetSampleTime(audioWritten * 10000000 / AudioMath.Rate);
                sample.SetSampleDuration(frames * 10000000L / AudioMath.Rate);
                writer.WriteSample(stream, sample);
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                if (buffer != null) Marshal.ReleaseComObject(buffer);
            }
        }

        void Run()
        {
            bool started = false, timer = false;
            IMFSinkWriter writer = null;
            IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, dib = IntPtr.Zero, oldBmp = IntPtr.Zero;
            try
            {
                Mf.Start();
                started = true;
                Mf.MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out writer);
                var output = VideoType(Mf.H264, width, height);
                output.SetUINT32(Mf.Bitrate, (int)Math.Min(40000000, Math.Max(2000000, width * height * Fps * 0.13)));
                int stream;
                writer.AddStream(output, out stream);
                Marshal.ReleaseComObject(output);
                var input = VideoType(Mf.Rgb32, width, height);
                writer.SetInputMediaType(stream, input, IntPtr.Zero);
                Marshal.ReleaseComObject(input);
                int audioStream;
                OpenAudio(writer, out audioStream);
                writer.BeginWriting();

                // Altura positiva: filas de abajo arriba, el orden que espera RGB32 en Media Foundation.
                var info = new Gdi.BITMAPINFOHEADER { biSize = 40, biWidth = width, biHeight = height, biPlanes = 1, biBitCount = 32 };
                IntPtr bits;
                screen = Native.GetDC(IntPtr.Zero);
                mem = Gdi.CreateCompatibleDC(screen);
                dib = Gdi.CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new InvalidOperationException("No se pudo reservar memoria para el vídeo.");
                oldBmp = Gdi.SelectObject(mem, dib);
                Gdi.SetStretchBltMode(mem, 4); // HALFTONE: mejor calidad al reducir
                Gdi.timeBeginPeriod(1);
                timer = true;

                int length = width * height * 4;
                long duration = TimeSpan.TicksPerSecond / Fps, next = 0;
                foreach (var s in sources) s.Start();
                origin = Now100ns();
                clock.Start();
                ready.Set();
                do
                {
                    long time = clock.Elapsed.Ticks;
                    Gdi.StretchBlt(mem, 0, 0, width, height, screen, area.X, area.Y, area.Width, area.Height, Native.SRCCOPY | Native.CAPTUREBLT);
                    DrawCursor(mem);
                    Gdi.GdiFlush();
                    WriteFrame(writer, stream, bits, length, time, duration);
                    frames++;
                    if (audioStream >= 0) PumpAudio(writer, audioStream, Math.Max(0, clock.Elapsed.Ticks * AudioMath.Rate / 10000000 - AudioMargin));
                    next += duration;
                    long wait = (next - clock.Elapsed.Ticks) / TimeSpan.TicksPerMillisecond;
                    if (wait > 0) Thread.Sleep((int)wait);
                    else if (wait < -500) next = clock.Elapsed.Ticks; // no intentar recuperar un retraso largo
                }
                while (!stop);
                if (audioStream >= 0) PumpAudio(writer, audioStream, clock.Elapsed.Ticks * AudioMath.Rate / 10000000);
                writer.Finish();
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                foreach (var s in sources) s.Close();
                if (timer) Gdi.timeEndPeriod(1);
                if (oldBmp != IntPtr.Zero) Gdi.SelectObject(mem, oldBmp);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                if (mem != IntPtr.Zero) Gdi.DeleteDC(mem);
                if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen);
                if (writer != null) Marshal.ReleaseComObject(writer);
                if (started) Mf.MFShutdown();
                ready.Set();
            }
        }

        static void WriteFrame(IMFSinkWriter writer, int stream, IntPtr bits, int length, long time, long duration)
        {
            IMFMediaBuffer buffer = null;
            IMFSample sample = null;
            try
            {
                Mf.MFCreateMemoryBuffer(length, out buffer);
                IntPtr data;
                int max, cur;
                buffer.Lock(out data, out max, out cur);
                Gdi.CopyMemory(data, bits, (IntPtr)length);
                buffer.Unlock();
                buffer.SetCurrentLength(length);
                Mf.MFCreateSample(out sample);
                sample.AddBuffer(buffer);
                sample.SetSampleTime(time);
                sample.SetSampleDuration(duration);
                writer.WriteSample(stream, sample);
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                if (buffer != null) Marshal.ReleaseComObject(buffer);
            }
        }

        // BitBlt no incluye el cursor: se dibuja encima de cada fotograma.
        void DrawCursor(IntPtr dc)
        {
            var ci = new Gdi.CURSORINFO { cbSize = Marshal.SizeOf(typeof(Gdi.CURSORINFO)) };
            if (!Gdi.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
            if (ci.hCursor != lastCursor)
            {
                Gdi.ICONINFO icon;
                if (!Gdi.GetIconInfo(ci.hCursor, out icon)) return;
                hotX = icon.xHotspot;
                hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
                lastCursor = ci.hCursor;
            }
            double sx = (double)width / area.Width, sy = (double)height / area.Height;
            Gdi.DrawIconEx(dc, (int)((ci.pt.X - area.X) * sx) - hotX, (int)((ci.pt.Y - area.Y) * sy) - hotY, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3);
        }
    }

    partial class CanvasWindow
    {
        internal const int RecHotkeyId = 0x5A2;
        const int ExcludeFromCapture = 0x11;

        ScreenRecorder recorder;
        DispatcherTimer recTimer, countdownTimer;
        Grid countdownRoot;
        System.Windows.Controls.TextBlock countdownText;
        int countdown;

        internal bool RecordMic = true, RecordSystem = true;

        static string AudioSettingsPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pizarra", "grabacion.txt"); }
        }

        internal string RecordSoundLabel
        {
            get { return RecordMic && RecordSystem ? "Micrófono y audio del equipo" : RecordMic ? "Solo micrófono" : RecordSystem ? "Solo audio del equipo" : "Sin sonido"; }
        }

        internal void SetRecordSound(bool mic, bool system)
        {
            RecordMic = mic;
            RecordSystem = system;
            Bar.UpdateRec();
            if (!AutosaveEnabled) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AudioSettingsPath));
                File.WriteAllText(AudioSettingsPath, (mic ? "1" : "0") + (system ? "1" : "0"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void LoadRecordSound()
        {
            if (!AutosaveEnabled) return;
            try
            {
                if (!File.Exists(AudioSettingsPath)) return;
                string v = File.ReadAllText(AudioSettingsPath).Trim();
                if (v.Length == 2) { RecordMic = v[0] == '1'; RecordSystem = v[1] == '1'; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal bool Recording { get { return recorder != null; } }
        internal bool CountingDown { get { return countdownRoot != null; } }
        internal TimeSpan RecordingTime { get { return recorder != null ? recorder.Elapsed : TimeSpan.Zero; } }

        internal static string DefaultVideoPath()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Pizarra");
            return System.IO.Path.Combine(dir, "Pizarra " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".mp4");
        }

        // Botón REC y Ctrl+Alt+R: empieza con cuenta atrás, o para lo que esté en marcha.
        internal void ToggleRecording()
        {
            if (CountingDown) { CancelCountdown(); return; }
            if (!Recording) { StartCountdown(); return; }
            var saved = EndRecording();
            if (saved != null) Bar.ShowVideoSaved(saved.Value);
        }

        void StartCountdown()
        {
            countdown = 3;
            countdownText = new System.Windows.Controls.TextBlock { Text = "3", Foreground = Ui.Accent, FontFamily = Ui.Font, FontSize = 150, TextAlignment = TextAlignment.Center, Margin = new Thickness(50, 0, 50, 0) };
            var body = new StackPanel();
            body.Children.Add(countdownText);
            var hint = Ui.Caption("Sonido: " + RecordSoundLabel + "\nVuelve a pulsar REC para cancelar");
            hint.TextAlignment = TextAlignment.Center;
            body.Children.Add(hint);
            var card = Ui.Card(body, 20, 14);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            countdownRoot = new Grid { IsHitTestVisible = false };
            countdownRoot.Children.Add(card);
            overlay.Children.Add(countdownRoot);
            countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdownTimer.Tick += delegate { CountdownTick(); };
            countdownTimer.Start();
            Bar.UpdateRec();
        }

        void CountdownTick()
        {
            countdown--;
            if (countdown > 0) { countdownText.Text = countdown.ToString(); return; }
            if (countdown == 0)
            {
                // Se quita y se espera a que desaparezca, para que no salga en el vídeo.
                countdownRoot.Visibility = Visibility.Collapsed;
                countdownTimer.Interval = TimeSpan.FromMilliseconds(150);
                return;
            }
            CancelCountdown();
            BeginRecording(DefaultVideoPath());
        }

        void CancelCountdown()
        {
            if (countdownTimer != null) countdownTimer.Stop();
            if (countdownRoot != null) overlay.Children.Remove(countdownRoot);
            countdownRoot = null;
            Bar.UpdateRec();
        }

        void ExcludeBarFromVideo(bool exclude)
        {
            var h = new WindowInteropHelper(Bar).Handle;
            // Si Windows es anterior a 2004 no se admite y la barra sale en el vídeo: no impide grabar.
            if (h != IntPtr.Zero) Gdi.SetWindowDisplayAffinity(h, exclude ? ExcludeFromCapture : 0);
        }

        internal int BarDisplayAffinity
        {
            get
            {
                int a;
                Gdi.GetWindowDisplayAffinity(new WindowInteropHelper(Bar).Handle, out a);
                return a;
            }
        }

        internal bool BeginRecording(string file)
        {
            if (Recording) return true;
            Exception problem = null;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                ExcludeBarFromVideo(true);
                var b = System.Windows.Forms.Screen.AllScreens[Math.Min(MonitorIndex, MonitorCount - 1)].Bounds;
                var r = new ScreenRecorder(file, b, RecordMic, RecordSystem);
                r.Start();
                problem = r.Error;
                if (problem == null) recorder = r;
            }
            catch (IOException ex) { problem = ex; }
            catch (UnauthorizedAccessException ex) { problem = ex; }
            if (problem != null)
            {
                ExcludeBarFromVideo(false);
                DeleteQuietly(file);
                ShowRecordingError(problem);
                return false;
            }
            if (recTimer == null)
            {
                recTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                recTimer.Tick += delegate { RecordingTick(); };
            }
            recTimer.Start();
            Bar.UpdateRec();
            if (recorder.Notes.Length > 0) Toast(recorder.Notes);
            return true;
        }

        void RecordingTick()
        {
            if (recorder == null) return;
            if (recorder.Error != null)
            {
                var problem = recorder.Error;
                string file = recorder.FilePath;
                EndRecording();
                DeleteQuietly(file);
                ShowRecordingError(problem);
                return;
            }
            Bar.UpdateRec();
        }

        // Cierra el archivo; devuelve su ruta y duración, o null si no hay vídeo válido.
        internal SavedVideo? EndRecording()
        {
            if (recorder == null) return null;
            var r = recorder;
            recorder = null;
            if (recTimer != null) recTimer.Stop();
            r.Stop();
            ExcludeBarFromVideo(false);
            Bar.UpdateRec();
            if (r.Error != null) return null;
            return new SavedVideo(r.FilePath, r.Elapsed, r.HasAudio, r.Notes);
        }

        void ShowRecordingError(Exception ex)
        {
            string text = ex is DllNotFoundException
                ? "Esta edición de Windows no incluye los componentes de vídeo.\nInstala el Media Feature Pack desde Configuración > Aplicaciones > Características opcionales."
                : "No se pudo grabar el vídeo:\n" + ex.Message;
            MessageBox.Show(this, text, "Pizarra", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        static void DeleteQuietly(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    struct SavedVideo
    {
        public readonly string Path;
        public readonly TimeSpan Time;
        public readonly bool HasAudio;
        public readonly string Notes;
        public SavedVideo(string path, TimeSpan time, bool hasAudio, string notes) { Path = path; Time = time; HasAudio = hasAudio; Notes = notes; }
    }
}
