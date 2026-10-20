using System;
using System.IO;
using System.Threading;
using NAudio.Wave;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Captures microphone audio as 16 kHz, 16-bit, mono PCM via NAudio (WaveInEvent).
    /// </summary>
    public sealed class MicrophoneRecorder : IDisposable
    {
        private readonly object _lock = new object();
        private WaveInEvent _waveIn;
        private MemoryStream _buffer;

        public bool IsRecording { get; private set; }

        /// <summary>Peak sample level in 0..1, raised on the NAudio callback thread.</summary>
        public event EventHandler<double> LevelChanged;

        public void Start()
        {
            lock (_lock)
            {
                if (IsRecording) return;

                _buffer = new MemoryStream();
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 100
                };
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsRecording = true;
            }
        }

        public byte[] Stop()
        {
            lock (_lock)
            {
                if (!IsRecording)
                    return _buffer?.ToArray() ?? Array.Empty<byte>();

                using (var stopped = new ManualResetEventSlim(false))
                {
                    EventHandler<StoppedEventArgs> handler = (s, e) => stopped.Set();
                    _waveIn.RecordingStopped += handler;
                    _waveIn.StopRecording();
                    stopped.Wait(TimeSpan.FromSeconds(2));
                    _waveIn.RecordingStopped -= handler;
                }

                IsRecording = false;
                var data = _buffer.ToArray();
                Cleanup();
                return data;
            }
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            lock (_lock)
            {
                if (_buffer != null)
                    _buffer.Write(e.Buffer, 0, e.BytesRecorded);
            }

            LevelChanged?.Invoke(this, ComputePeak(e.Buffer, e.BytesRecorded));
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
        }

        private static double ComputePeak(byte[] buffer, int bytesRecorded)
        {
            if (buffer == null || bytesRecorded < 2) return 0.0;

            var max = 0;
            for (var i = 0; i + 1 < bytesRecorded; i += 2)
            {
                var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                var abs = sample < 0 ? -sample : sample;
                if (abs > max) max = abs;
            }
            return Math.Min(1.0, max / 32768.0);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Cleanup();
            }
        }

        private void Cleanup()
        {
            if (_waveIn != null)
            {
                try
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.Dispose();
                }
                catch
                {
                    // Best-effort cleanup; the device may already be disposed.
                }
                _waveIn = null;
            }

            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }
        }
    }
}
