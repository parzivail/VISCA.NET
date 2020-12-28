﻿namespace VISCA.NET.Opcode
{
    public enum ViscaInquiryCameraOpcode : byte
    {
        Power = 0x00,
        AutoPowerOff = 0x40,
        NightPowerOff = 0x41,
        ZoomPos = 0x47,
        DZoomMode = 0x06,
        DZoomCSMode = 0x36,
        DZoomPos = 0x46,
        FocusMode = 0x38,
        FocusPos = 0x48,
        FocusNearLimit = 0x28,
        AFSensitivity = 0x58,
        AFMode = 0x57,
        AFTimeSetting = 0x27,
        WBMode = 0x35,
        RGain = 0x43,
        BGain = 0x44,
        AEMode = 0x39,
        SlowShutterMode = 0x5A,
        ShutterPos = 0x4A,
        AperturePos = 0x4B,
        GainPos = 0x4C,
        BrightPos = 0x4D,
        ExpCompMode = 0x3E,
        ExpCompPos = 0x4E,
        BacklightMode = 0x33,
        SpotAEMode = 0x59,
        SpotAEPos = 0x29,
        Aperture = 0x42,
        LRReverseMode = 0x61,
        FreezeMode = 0x62,
        PictureEffectMode = 0x63,
        ICRMode = 0x01,
        AutoICRMode = 0x51,
        Memory = 0x3F,
        DisplayMode = 0x15,
        TitleDisplayMode = 0x74,
        MuteMode = 0x75,
        KeyLock = 0x17,
        ID = 0x22,
        Alarm = 0x6B,
        AlarmMode = 0x6C,
        AlarmDayNightLevel = 0x6D,
        PictureFlipMode = 0x66,
        AlarmDetectLevel = 0x6E
    }
}