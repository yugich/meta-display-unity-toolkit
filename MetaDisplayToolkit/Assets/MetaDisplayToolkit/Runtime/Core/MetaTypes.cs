using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MetaDisplay
{
    // ───────────────────────── Estados ─────────────────────────

    public enum RegistrationState { Unknown, Unavailable, Available, Registering, Registered, Unregistering }

    public enum SessionState { None, Idle, Starting, Started, Paused, Stopping, Stopped }

    public enum DisplayState { Stopped, Starting, Started, Stopping, Closed }

    public enum VideoState { Idle, Starting, Playing, Pause, Ended }

    public enum CameraStreamState { Stopped, Starting, Started, Streaming, Paused, Stopping, Closed }

    public enum MotionState { Stopped, Starting, Started, Paused, Stopping }

    public enum InputsState { Inactive, Activating, Active, Deactivating }

    public enum SpeechState { Stopped, Starting, Started, Stopping }

    public enum VoiceInvocationState { Stopped, Starting, Started }

    // ───────────────────────── Dispositivo ─────────────────────────

    public enum DeviceSelection
    {
        [Tooltip("Primeiros óculos conectados que tenham display (Meta Ray-Ban Display).")]
        DisplayCapable,
        [Tooltip("Qualquer óculos Meta conectado.")]
        Any,
        [Tooltip("Um dispositivo específico (informe o id).")]
        Specific,
    }

    public enum DeviceType { Unknown, RaybanMeta, OakleyMetaHstn, OakleyMetaVanguard, MetaRaybanDisplay, RaybanMetaOptics, MetaGlasses }

    public enum LinkState { Disconnected, Connecting, Connected }

    public enum DeviceCompatibility { Undefined, Compatible, DeviceUpdateRequired, SdkUpdateRequired }

    public enum ChargingState { Unknown, Charging, NotCharging }

    public enum DonState { Unknown, Doffed, Donned }

    public enum HingeState { Unknown, Closed, Open }

    public enum ThermalLevel { Unknown, None, Light, Moderate, Severe, Critical, Emergency, Shutdown }

    public enum MetaPermission { Camera, Microphone }

    public enum PermissionResult { Unknown, Granted, Denied, Error }

    // ───────────────────────── Display (UI dos óculos) ─────────────────────────

    public enum GlassesDirection { Column, Row, ColumnReverse, RowReverse }

    public enum GlassesAlignment { Start, Center, End, Stretch }

    public enum GlassesBackground { None, Card }

    public enum GlassesTextStyle { Heading, Body, Meta }

    public enum GlassesTextColor { Primary, Secondary }

    public enum GlassesButtonStyle { Primary, Secondary, Outline }

    public enum GlassesIconStyle { Filled, Outline }

    public enum GlassesImageSize { Fill, Icon }

    public enum GlassesCornerRadius { None, Small, Medium }

    public enum GlassesButtonGroupAlignment { Start, Center, End }

    // ───────────────────────── Câmera / sensores ─────────────────────────

    public enum CameraQuality
    {
        [Tooltip("360 x 640")] Low,
        [Tooltip("504 x 896")] Medium,
        [Tooltip("720 x 1280")] High,
    }

    public enum CameraFps { Fps2 = 2, Fps7 = 7, Fps15 = 15, Fps24 = 24, Fps30 = 30 }

    public enum CameraAudio { None = 0, Hz16000 = 16000, Hz44100 = 44100, Hz48000 = 48000 }

    public enum PhotoResolution { Small, Medium, Large, Full }

    public enum PhotoQuality { Low, Medium, High }

    /// <summary>Layout do YUV 4:2:0 recebido. Auto detecta planar vs. semi-planar.</summary>
    public enum YuvLayout { Auto = 0, I420 = 1, NV12 = 2, NV21 = 3 }

    public enum MotionRate { Hz5 = 5, Hz10 = 10, Hz15 = 15, Hz24 = 24, Hz30 = 30, Hz60 = 60 }

    public enum MotionSource { Glasses = 0, NeuralBand = 1, Unknown = 2 }

    public enum InputSource { Captouch, NeuralBand, CaptureButton, ActionButton, NeuralBandDrag, Unknown }

    public enum InputKind { Nav, Select, Back, Button, Capture, Drag, Unknown }

    public enum NavDirection { Up, Down, Left, Right }

    public enum CapturePressType { ShortPress, Hold, DoublePress }

    public enum DragAction { Down, Move, Up }

    // ───────────────────────── Dados ─────────────────────────

    /// <summary>Metadados de um óculos conhecido pelo app Meta AI.</summary>
    [Serializable]
    public class DeviceInfo
    {
        public string id;
        public string name;
        public string type;
        public string linkState;
        public string compatibility;
        public string firmware;
        public int battery;
        public string charging;
        public string don;
        public string hinge;
        public string thermal;
        public bool displayCapable;

        public DeviceType Type => MetaEnum.Parse(type, DeviceType.Unknown);
        public LinkState Link => MetaEnum.Parse(linkState, LinkState.Disconnected);
        public DeviceCompatibility Compatibility => MetaEnum.Parse(compatibility, DeviceCompatibility.Undefined);
        public ChargingState Charging => MetaEnum.Parse(charging, ChargingState.Unknown);
        public DonState Don => MetaEnum.Parse(don, DonState.Unknown);
        public HingeState Hinge => MetaEnum.Parse(hinge, HingeState.Unknown);
        public ThermalLevel Thermal => MetaEnum.Parse(thermal, ThermalLevel.Unknown);
        public bool IsConnected => Link == LinkState.Connected;

        public override string ToString() =>
            $"{name} ({type}) {linkState} bateria={battery}% display={displayCapable} {compatibility}";
    }

    /// <summary>Erro reportado pelo SDK ou pela ponte.</summary>
    public readonly struct MetaError
    {
        /// <summary>Área: session, display, camera, motion, inputs, speech, voice, registration, permission, mock, bridge.</summary>
        public readonly string Source;
        /// <summary>Código do SDK (ex.: DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED, PERMISSION_DENIED).</summary>
        public readonly string Code;
        public readonly string Message;

        public MetaError(string source, string code, string message)
        {
            Source = source;
            Code = code;
            Message = message;
        }

        public override string ToString() => $"[{Source}] {Code}: {Message}";
    }

    /// <summary>Uma amostra de IMU. Campos ausentes vêm com Has* = false.</summary>
    public struct MotionSample
    {
        public long TimestampNs;
        public MotionSource Source;
        public bool HasAccelerometer, HasGyroscope, HasMagnetometer, HasOrientation;
        /// <summary>m/s² (inclui a gravidade).</summary>
        public Vector3 Accelerometer;
        /// <summary>rad/s.</summary>
        public Vector3 Gyroscope;
        /// <summary>µT.</summary>
        public Vector3 Magnetometer;
        /// <summary>Orientação fundida, no sistema de eixos do dispositivo (x, y, z, w como recebido).</summary>
        public Quaternion Orientation;
    }

    /// <summary>Evento de input vindo dos óculos ou da Neural Band.</summary>
    public struct GlassesInputEvent
    {
        public InputKind Kind;
        public InputSource Source;
        public long TimestampMs;
        public NavDirection Direction;
        public CapturePressType PressType;
        public string Button;
        public DragAction DragAction;
        public Vector2 Position;
        public Vector2 Delta;

        public override string ToString() =>
            Kind switch
            {
                InputKind.Nav => $"Nav {Direction} ({Source})",
                InputKind.Capture => $"Capture {PressType} ({Source})",
                InputKind.Drag => $"Drag {DragAction} pos={Position} delta={Delta} ({Source})",
                InputKind.Button => $"Button {Button} ({Source})",
                _ => $"{Kind} ({Source})",
            };
    }

    public struct SpeechResult
    {
        public string Text;
        public bool IsFinal;
        /// <summary>0..1, ou -1 quando indisponível.</summary>
        public float Confidence;
    }

    // ───────────────────────── Conversão de enums ─────────────────────────

    /// <summary>Converte entre os nomes do SDK (UPPER_SNAKE) e os enums C# (PascalCase).</summary>
    public static class MetaEnum
    {
        static readonly Dictionary<Enum, string> s_ToSdk = new Dictionary<Enum, string>();

        public static string ToSdk(Enum value)
        {
            if (value == null) return null;
            if (value is GlassesIconName icon) return GlassesIconNames.ToSdk(icon);
            lock (s_ToSdk)
            {
                if (s_ToSdk.TryGetValue(value, out var cached)) return cached;
                var name = value.ToString();
                var sb = new StringBuilder(name.Length + 8);
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    bool boundary = i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(name[i - 1])));
                    if (boundary) sb.Append('_');
                    sb.Append(char.ToUpperInvariant(c));
                }
                var sdk = sb.ToString();
                s_ToSdk[value] = sdk;
                return sdk;
            }
        }

        public static T Parse<T>(string sdkName, T fallback) where T : struct, Enum
        {
            if (string.IsNullOrEmpty(sdkName)) return fallback;
            var compact = sdkName.Replace("_", "");
            return Enum.TryParse(compact, true, out T result) ? result : fallback;
        }
    }
}
