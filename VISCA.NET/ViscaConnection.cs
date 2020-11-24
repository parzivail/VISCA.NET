using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using VISCA.NET.Extensions;

namespace VISCA.NET
{
    public class ViscaConnection
    {
        private readonly SerialPort _port;

        public ViscaConnection(string port, ViscaBaudRate baudRate)
        {
            _port = new SerialPort(port, (int)baudRate, Parity.None);
            _port.DataReceived += OnDataReceived;
            _port.Open();
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var buffer = new byte[_port.BytesToRead];
            _port.Read(buffer, 0, buffer.Length);
            Console.WriteLine($"!→ {GetHexString(buffer)}");
        }

        private static string GetHexString(IEnumerable<byte> arr)
        {
            return string.Join(" ", arr.Select(b => $"0x{b:X2}"));
        }

        private static byte[] GetPacket(byte address, ViscaPacketType packetType, ViscaPacketCategory packetCategory, params byte[] payload)
        {
            var packet = new byte[payload.Length + 4];
            packet[0] = (byte)(0x80 | (address & 0x0F));
            packet[1] = (byte)packetType;
            packet[2] = (byte)packetCategory;
            Array.Copy(payload, 0, packet, 3, payload.Length);
            packet[^1] = 0xFF;
            return packet;
        }

        private void Send(byte[] packet)
        {
            Console.WriteLine($"← {GetHexString(packet)}");
            _port.Write(packet, 0, packet.Length);
        }

        private async Task<ViscaInquiryResponse> GetInquiryResponse(int responseLength)
        {
            var buffer = await _port.ReadAsync(responseLength + 3);
            Console.WriteLine($"→ {GetHexString(buffer)}");
            
            var payload = new byte[responseLength];
            Array.Copy(buffer, 2, payload, 0, responseLength);
            return new ViscaInquiryResponse(buffer[0], payload);
        }

        private static T GetEnum<T>(ViscaInquiryResponse data, int offset = 0) where T : struct, Enum
        {
            var b = data.Payload[offset];
            if (Enum.GetValues<T>().Cast<byte>().Contains(b))
                return Unsafe.As<byte, T>(ref b);

            throw new InvalidDataException();
        }

        private static bool GetBoolean(ViscaInquiryResponse data, byte truthy, byte falsy, int offset = 0)
        {
            if (data.Payload[offset] == truthy)
                return true;
            if (data.Payload[offset] == falsy)
                return false;
            
            throw new InvalidDataException();
        }

        private static short GetExpandedShort(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 4)
                throw new ArgumentException("Expanded shorts can only be read from at least 4-byte payloads", nameof(data));

            // 0x0A 0x0B 0x0C 0x0D => 0xABCD
            return (short)(((((data.Payload[offset + 0] & 0x0F) << 4) | (data.Payload[offset + 1] & 0x0F)) << 8) | ((data.Payload[offset + 2] & 0x0F) << 4) | (data.Payload[offset + 3] & 0x0F));
        }

        private static short GetShort(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 2)
                throw new ArgumentException("Expanded shorts can only be read from at least 2-byte payloads", nameof(data));

            // 0xAB 0xCD => 0xABCD
            return BitConverter.ToInt16(data.Payload, offset);
        }

        private static byte GetExpandedByte(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 2)
                throw new ArgumentException("Expanded bytes can only be read from at least 2-byte payloads", nameof(data));

            // 0x0A 0x0B => 0xAB
            return (byte)(((data.Payload[offset + 2] & 0x0F) << 4) | (data.Payload[offset + 3] & 0x0F));
        }

        public async Task<bool> GetPower(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.Power));
            var data = await GetInquiryResponse(1);

            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<short> GetAutoPowerOffTimer(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AutoPowerOff));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }

        public async Task<short> GetNightPowerOffTimer(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.NightPowerOff));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }

        public async Task<short> GetZoomPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ZoomPos));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }

        public async Task<bool> GetDZoomMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.DZoomMode));
            var data = await GetInquiryResponse(1);
            
            return data.Payload[0] switch
            {
                0x02 => true,
                0x03 => false,
                _ => throw new InvalidDataException()
            };
        }

        public async Task<ViscaDZoomCsMode> GetDZoomCSMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.DZoomCSMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaDZoomCsMode>(data);
        }

        public async Task<short> GetDZoomPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.DZoomPos));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }

        public async Task<ViscaFocusMode> GetFocusMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.FocusMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaFocusMode>(data);
        }

        public async Task<short> GetFocusPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.FocusPos));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }

        public async Task<short> GetFocusNearLimit(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.FocusNearLimit));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }
        
        public async Task<ViscaAutoFocusSensitivity> GetAutoFocusSensitivity(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AFSensitivity));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaAutoFocusSensitivity>(data);
        }
        
        public async Task<ViscaAutoFocusMode> GetAutoFocusMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AFMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaAutoFocusMode>(data);
        }

        public async Task<(byte MovementTime, byte Interval)> GetAutoFocusTimeSetting(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AFTimeSetting));
            var data = await GetInquiryResponse(4);
            return (MovementTime: GetExpandedByte(data), Interval: GetExpandedByte(data, 2));
        }
        
        public async Task<ViscaWhiteBalanceMode> GetWhiteBalanceMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.WBMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaWhiteBalanceMode>(data);
        }
        
        public async Task<byte> GetRGain(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.RGain));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }
        
        public async Task<byte> GetBGain(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.BGain));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }
        
        public async Task<ViscaAutoExposureMode> GetAutoExposureMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AEMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaAutoExposureMode>(data);
        }
        
        public async Task<ViscaSlowShutterMode> GetSlowShutterMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.SlowShutterMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaSlowShutterMode>(data);
        }
        
        public async Task<byte> GetShutterPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ShutterPos));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }
        
        public async Task<byte> GetAperturePos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AperturePos));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }
        
        public async Task<byte> GetGainPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.GainPos));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }
        
        public async Task<byte> GetBrightPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.BrightPos));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }

        public async Task<bool> GetExpCompMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ExpCompMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }
        
        public async Task<byte> GetExpCompPos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ExpCompPos));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }

        public async Task<bool> GetBacklightMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.BacklightMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<bool> GetSpotAutoExposureMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.SpotAEMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<(byte X, byte Y)> GetSpotAutoExposurePos(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.SpotAEPos));
            var data = await GetInquiryResponse(4);
            
            return (X: GetExpandedByte(data), Y: GetExpandedByte(data, 2));
        }
        
        public async Task<byte> GetAperture(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.Aperture));
            var data = await GetInquiryResponse(4);

            return GetExpandedByte(data, 2);
        }

        public async Task<bool> GetLRReverseMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.LRReverseMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<bool> GetFreezeMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.FreezeMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }
        
        public async Task<ViscaPictureEffectMode> GetPictureEffectMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.PictureEffectMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaPictureEffectMode>(data);
        }

        public async Task<bool> GetICRMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ICRMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<bool> GetAutoICRMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.AutoICRMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<byte> GetMemory(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.Memory));
            var data = await GetInquiryResponse(1);
            
            return data.Payload[0];
        }

        public async Task<bool> GetDisplayMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.DisplayMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<bool> GetTitleDisplayMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.TitleDisplayMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<bool> GetMuteMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.MuteMode));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<bool> GetKeyLockMode(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.KeyLock));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }

        public async Task<short> GetID(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ID));
            var data = await GetInquiryResponse(4);
            return GetExpandedShort(data);
        }

        public async Task<(short ModelCode, short RomVersion, byte SocketNumber)> GetVersion(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.ID));
            var data = await GetInquiryResponse(7);
            return (ModelCode: GetShort(data, 2), RomVersion: GetShort(data, 4), SocketNumber: data.Payload[6]);
        }

        public async Task<bool> GetAlarm(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.Alarm));
            var data = await GetInquiryResponse(1);
            
            return GetBoolean(data, 0x02, 0x03);
        }
    }

    public enum ViscaPictureEffectMode
    {
        Off = 0x00,
        NegativeArt = 0x02,
        BlackAndWhite = 0x04
    }
}