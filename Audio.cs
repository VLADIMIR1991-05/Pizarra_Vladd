// Captura de audio para los vídeos: micrófono y sonido del equipo (WASAPI), mezclados a 48 kHz estéreo.
using System;
using System.Runtime.InteropServices;

namespace Pizarra
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, int state, out IntPtr devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object client);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        void GetBufferSize(out int frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out int frames);
        void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        void GetMixFormat(out IntPtr format);
        void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(IntPtr handle);
        void GetService([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
        void ReleaseBuffer(int frames);
        void GetNextPacketSize(out int frames);
    }

    // Conversión de muestras, sin dependencias del dispositivo.
    static class AudioMath
    {
        internal const int Rate = 48000;

        // Pasa cualquier formato de Windows a estéreo en coma flotante.
        internal static void Decode(byte[] raw, int frames, int channels, int bytesPerSample, bool isFloat, float[] stereo)
        {
            int block = channels * bytesPerSample;
            for (int i = 0; i < frames; i++)
            {
                float left = Sample(raw, i * block, bytesPerSample, isFloat);
                stereo[i * 2] = left;
                stereo[i * 2 + 1] = channels > 1 ? Sample(raw, i * block + bytesPerSample, bytesPerSample, isFloat) : left;
            }
        }

        static float Sample(byte[] b, int at, int size, bool isFloat)
        {
            if (isFloat) return size == 8 ? (float)BitConverter.ToDouble(b, at) : BitConverter.ToSingle(b, at);
            switch (size)
            {
                case 1: return (b[at] - 128) / 128f;
                case 2: return BitConverter.ToInt16(b, at) / 32768f;
                case 3: return (short)(b[at + 1] | (b[at + 2] << 8)) / 32768f + b[at] / 8388608f;
                default: return BitConverter.ToInt32(b, at) / 2147483648f;
            }
        }

        internal static short ToPcm16(float v)
        {
            if (v > 1) v = 1;
            else if (v < -1) v = -1;
            return (short)(v * 32767);
        }
    }

    // Remuestreo lineal a 48 kHz; conserva el estado entre paquetes para no producir chasquidos.
    class Resampler
    {
        readonly double step;
        double phase;
        float lastL, lastR;

        public Resampler(int inputRate) { step = (double)inputRate / AudioMath.Rate; }

        // Devuelve los fotogramas de salida; output crece si hace falta.
        public int Process(float[] input, int frames, ref float[] output)
        {
            if (frames == 0) return 0;
            int needed = (int)((frames + 2) / step) + 2;
            if (output.Length < needed * 2) output = new float[needed * 2];
            int n = 0;
            double pos = phase;
            while (pos < frames - 1)
            {
                int i = (int)Math.Floor(pos);
                float f = (float)(pos - i);
                float aL = i < 0 ? lastL : input[i * 2], aR = i < 0 ? lastR : input[i * 2 + 1];
                float bL = input[(i + 1) * 2], bR = input[(i + 1) * 2 + 1];
                output[n * 2] = aL + (bL - aL) * f;
                output[n * 2 + 1] = aR + (bR - aR) * f;
                n++;
                pos += step;
            }
            phase = pos - frames;
            lastL = input[(frames - 1) * 2];
            lastR = input[(frames - 1) * 2 + 1];
            return n;
        }
    }

    // Línea de tiempo de una fuente: cada muestra ocupa su lugar real aunque haya silencios sin paquetes.
    class AudioTrack
    {
        const int Tolerance = AudioMath.Rate / 50; // 20 ms
        float[] data = new float[AudioMath.Rate * 2];
        int head, count; // en fotogramas
        long start;      // posición absoluta del primer fotograma guardado

        internal long End { get { return start + count; } }
        internal int Capacity { get { return data.Length; } }

        // pos < 0: sin marca de tiempo fiable, se coloca a continuación de lo anterior.
        public void Put(long pos, float[] stereo, int frames)
        {
            if (pos < 0) pos = End;
            long gap = pos - End;
            int skip = 0;
            if (gap > Tolerance) Reserve((int)gap, true);
            else if (gap < -Tolerance) skip = (int)Math.Min(frames, -gap);
            int n = frames - skip;
            if (n <= 0) return;
            Reserve(n, false);
            Array.Copy(stereo, skip * 2, data, (head + count - n) * 2, n * 2);
        }

        // Suma a mix lo que corresponde a [from, from+frames) y libera lo ya leído.
        public void MixInto(long from, int frames, float[] mix)
        {
            long to = from + frames;
            long a = Math.Max(from, start), b = Math.Min(to, End);
            for (long p = a; p < b; p++)
            {
                int src = (int)(head + (p - start)) * 2, dst = (int)(p - from) * 2;
                mix[dst] += data[src];
                mix[dst + 1] += data[src + 1];
            }
            if (to >= End) { head = 0; count = 0; start = to; }
            else if (to > start)
            {
                int used = (int)(to - start);
                head += used;
                count -= used;
                start = to;
            }
        }

        void Reserve(int frames, bool silence)
        {
            if (head + count + frames > data.Length / 2)
            {
                int total = count + frames;
                var target = total * 2 > data.Length ? new float[total * 4] : data;
                Array.Copy(data, head * 2, target, 0, count * 2);
                data = target;
                head = 0;
            }
            if (silence) Array.Clear(data, (head + count) * 2, frames * 2);
            count += frames;
        }
    }

    // Una fuente de sonido del sistema. Lanza una excepción si no se puede abrir.
    class AudioSource
    {
        static readonly Guid ClientId = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), CaptureId = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        const int Loopback = 0x00020000, FlagSilent = 2, FlagBadTime = 4;
        public readonly string Name;
        public readonly AudioTrack Track = new AudioTrack();
        readonly IAudioClient client;
        readonly IAudioCaptureClient capture;
        readonly int channels, bytesPerSample;
        readonly bool isFloat;
        readonly Resampler resampler;
        byte[] raw = new byte[0];
        float[] stereo = new float[0], output = new float[0];

        public AudioSource(bool system)
        {
            Name = system ? "audio del equipo" : "micrófono";
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice device;
            enumerator.GetDefaultAudioEndpoint(system ? 0 : 1, 0, out device);
            object o;
            device.Activate(ClientId, 1, IntPtr.Zero, out o);
            client = (IAudioClient)o;
            IntPtr format;
            client.GetMixFormat(out format);
            try
            {
                int tag = (ushort)Marshal.ReadInt16(format, 0);
                channels = Marshal.ReadInt16(format, 2);
                int rate = Marshal.ReadInt32(format, 4);
                bytesPerSample = Marshal.ReadInt16(format, 12) / channels;
                // WAVE_FORMAT_EXTENSIBLE guarda el tipo real en los primeros bytes de SubFormat.
                int kind = tag == 0xFFFE ? Marshal.ReadInt16(format, 24) : tag;
                isFloat = kind == 3;
                resampler = new Resampler(rate);
                client.Initialize(0, system ? Loopback : 0, 10000000, 0, format, IntPtr.Zero); // búfer de 1 s
            }
            finally { Marshal.FreeCoTaskMem(format); }
            client.GetService(CaptureId, out o);
            capture = (IAudioCaptureClient)o;
        }

        public void Start() { client.Start(); }

        // Vacía los paquetes pendientes; origin es el inicio de la grabación en unidades de 100 ns.
        public void Poll(long origin)
        {
            int next;
            capture.GetNextPacketSize(out next);
            while (next > 0)
            {
                IntPtr ptr;
                int frames, flags;
                long dev, qpc;
                capture.GetBuffer(out ptr, out frames, out flags, out dev, out qpc);
                if (frames > 0)
                {
                    if (stereo.Length < frames * 2) stereo = new float[frames * 2];
                    if ((flags & FlagSilent) != 0) Array.Clear(stereo, 0, frames * 2);
                    else
                    {
                        int size = frames * channels * bytesPerSample;
                        if (raw.Length < size) raw = new byte[size];
                        Marshal.Copy(ptr, raw, 0, size);
                        AudioMath.Decode(raw, frames, channels, bytesPerSample, isFloat, stereo);
                    }
                }
                capture.ReleaseBuffer(frames);
                int n = resampler.Process(stereo, frames, ref output);
                long pos = (flags & FlagBadTime) != 0 ? -1 : (qpc - origin) * AudioMath.Rate / 10000000;
                // Una marca anterior al inicio es audio previo a la grabación: se ajusta al principio.
                if (pos < 0 && (flags & FlagBadTime) == 0) pos = 0;
                Track.Put(pos, output, n);
                capture.GetNextPacketSize(out next);
            }
        }

        public void Close()
        {
            try { client.Stop(); }
            catch (COMException) { }
            Marshal.ReleaseComObject(capture);
            Marshal.ReleaseComObject(client);
        }

        // Mensaje comprensible para quien graba.
        public static string Explain(string name, Exception ex)
        {
            int code = ex is COMException ? ((COMException)ex).ErrorCode : 0;
            if (code == unchecked((int)0x80070490)) return "No se grabó el " + name + ": no hay ningún dispositivo.";
            if (code == unchecked((int)0x80070005)) return "No se grabó el " + name + ": Windows no da permiso (Configuración > Privacidad > Micrófono).";
            return "No se grabó el " + name + ": " + ex.Message;
        }
    }
}
