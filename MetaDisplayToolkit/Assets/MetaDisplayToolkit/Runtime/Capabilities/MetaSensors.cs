using System;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// IMU dos óculos e da Neural Band (experimental: canais dev/beta).
    /// Amostras chegam em lote a cada frame do Unity pelo evento <see cref="SampleReceived"/>.
    /// </summary>
    public static class MetaMotion
    {
        const string Cls = "MDMotion";
        const int Stride = 17;
        static bool s_Active;

        public static MotionState State { get; private set; } = MotionState.Stopped;
        /// <summary>Amostra mais recente dos óculos.</summary>
        public static MotionSample LatestGlasses { get; private set; }
        /// <summary>Amostra mais recente da Neural Band.</summary>
        public static MotionSample LatestNeuralBand { get; private set; }

        public static event Action<MotionState> StateChanged;
        public static event Action<MotionSample> SampleReceived;

        public static void Start(MotionRate rate = MotionRate.Hz30)
        {
            MetaDispatcher.EnsureExists();
            if (!s_Active)
            {
                s_Active = true;
                MetaDispatcher.Tick += Poll;
            }
            MetaNative.Call(Cls, "start", (int)rate);
        }

        public static void Stop()
        {
            MetaNative.Call(Cls, "stop");
            if (s_Active)
            {
                s_Active = false;
                MetaDispatcher.Tick -= Poll;
            }
        }

        static void Poll()
        {
            if (!MetaNative.IsNative) return;
            var data = MetaNative.Call<double[]>(Cls, "drainSamples");
            if (data == null) return;
            for (int o = 0; o + Stride <= data.Length; o += Stride)
            {
                int flags = (int)data[o + 2];
                var s = new MotionSample
                {
                    TimestampNs = (long)data[o],
                    Source = (MotionSource)(int)data[o + 1],
                    HasAccelerometer = (flags & 1) != 0,
                    HasGyroscope = (flags & 2) != 0,
                    HasMagnetometer = (flags & 4) != 0,
                    HasOrientation = (flags & 8) != 0,
                    Accelerometer = new Vector3((float)data[o + 3], (float)data[o + 4], (float)data[o + 5]),
                    Gyroscope = new Vector3((float)data[o + 6], (float)data[o + 7], (float)data[o + 8]),
                    Magnetometer = new Vector3((float)data[o + 9], (float)data[o + 10], (float)data[o + 11]),
                    Orientation = new Quaternion((float)data[o + 12], (float)data[o + 13], (float)data[o + 14], (float)data[o + 15]),
                };
                if (s.Source == MotionSource.NeuralBand) LatestNeuralBand = s;
                else LatestGlasses = s;
                SampleReceived?.Invoke(s);
            }
        }

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "motion.state":
                    State = MetaEnum.Parse(p.state, MotionState.Stopped);
                    StateChanged?.Invoke(State);
                    break;
                case "motion.error":
                    MetaWearables.RaiseError(new MetaError("motion", p.error, p.message));
                    break;
            }
        }
    }

    /// <summary>
    /// Inputs semânticos dos óculos/Neural Band: navegação, seleção, voltar, botões, captura e arraste
    /// (experimental). Útil para controlar o app inteiro pelos óculos.
    /// </summary>
    public static class MetaInputs
    {
        const string Cls = "MDInputs";

        public static InputsState State { get; private set; } = InputsState.Inactive;
        public static event Action<InputsState> StateChanged;
        public static event Action<GlassesInputEvent> InputReceived;

        /// <param name="consumeBack">True: o app recebe "Voltar" em vez do sistema.</param>
        /// <param name="sources">Vazio = todas as fontes conhecidas.</param>
        public static void Start(bool consumeBack = true, params InputSource[] sources)
        {
            MetaDispatcher.EnsureExists();
            var csv = sources == null || sources.Length == 0 ? "" : string.Join(",", Array.ConvertAll(sources, s => MetaEnum.ToSdk(s)));
            MetaNative.Call(Cls, "start", consumeBack, csv);
        }

        public static void Stop() => MetaNative.Call(Cls, "stop");

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "inputs.state":
                    State = MetaEnum.Parse(p.state, InputsState.Inactive);
                    StateChanged?.Invoke(State);
                    break;
                case "inputs.error":
                    MetaWearables.RaiseError(new MetaError("inputs", p.error, p.message));
                    break;
                case "input":
                    InputReceived?.Invoke(new GlassesInputEvent
                    {
                        Kind = MetaEnum.Parse(p.kind, InputKind.Unknown),
                        Source = MetaEnum.Parse(p.source, InputSource.Unknown),
                        TimestampMs = p.timestampMs,
                        Direction = MetaEnum.Parse(p.direction, NavDirection.Up),
                        PressType = MetaEnum.Parse(p.pressType, CapturePressType.ShortPress),
                        Button = p.button,
                        DragAction = MetaEnum.Parse(p.action, DragAction.Move),
                        Position = new Vector2(p.x, p.y),
                        Delta = new Vector2(p.dx, p.dy),
                    });
                    break;
            }
        }
    }

    /// <summary>
    /// Reconhecimento de fala nos próprios óculos com transcrição parcial e final (experimental).
    /// Pede a permissão de microfone no Meta AI automaticamente.
    /// </summary>
    public static class MetaSpeech
    {
        const string Cls = "MDSpeech";

        public static SpeechState State { get; private set; } = SpeechState.Stopped;
        public static string Locale { get; private set; }
        /// <summary>Última transcrição (parcial ou final).</summary>
        public static SpeechResult Latest { get; private set; }

        public static event Action<SpeechState> StateChanged;
        public static event Action<SpeechResult> TranscriptionReceived;
        /// <summary>Só os resultados finais (frase completa).</summary>
        public static event Action<string> FinalTranscription;

        public static void Start()
        {
            MetaDispatcher.EnsureExists();
            MetaNative.Call(Cls, "start");
        }

        public static void Stop() => MetaNative.Call(Cls, "stop");

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "speech.state":
                    State = MetaEnum.Parse(p.state, SpeechState.Stopped);
                    StateChanged?.Invoke(State);
                    break;
                case "speech.locale":
                    Locale = p.locale;
                    break;
                case "speech.error":
                    MetaWearables.RaiseError(new MetaError("speech", p.error, string.IsNullOrEmpty(p.details) ? p.message : p.message + " — " + p.details));
                    break;
                case "speech.transcription":
                    Latest = new SpeechResult { Text = p.text, IsFinal = p.isFinal, Confidence = p.confidence };
                    TranscriptionReceived?.Invoke(Latest);
                    if (p.isFinal) FinalTranscription?.Invoke(p.text);
                    break;
            }
        }
    }
}
