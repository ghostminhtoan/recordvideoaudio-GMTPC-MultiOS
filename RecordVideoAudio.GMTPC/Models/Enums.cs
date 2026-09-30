namespace RecordVideoAudio.GMTPC.Models;

public enum ContainerFormat
{
    MKV,
    MP4
}

public enum VideoCodecType
{
    H264,
    HEVC
}

public enum AudioCodecType
{
    AAC,
    MP3
}

public enum RateControlMode
{
    CRF,
    CQP,
    CBR,
    VBR
}

public enum CaptureSourceType
{
    FullScreen,
    CustomArea,
    ActiveWindow,
    CameraPiP
}

public enum RecordingState
{
    Idle,
    Recording,
    Paused,
    Finalizing
}

public enum PresetSpeed
{
    Ultrafast,
    Superfast,
    Veryfast,
    Faster,
    Fast,
    Medium,
    Slow
}

public enum HwAccelType
{
    Auto,
    NVENC,
    QSV,
    AMF,
    VAAPI,
    MediaCodec,
    SoftwareCPU
}

public enum PipPosition
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft
}

public enum PipSize
{
    Small,
    Medium,
    Large
}

public enum AudioTrackMode
{
    MixToSingleTrack,
    SeparateTracks
}

