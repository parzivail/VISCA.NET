using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
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

        private short GetExpandedShort(ViscaInquiryResponse data)
        {
            if (data.Payload.Length != 4)
                throw new ArgumentException("Expanded shorts can only be read from 4-byte payloads", nameof(data));

            // 0x0A 0x0B 0x0C 0x0D => 0xABCD
            return (short)(((((data.Payload[0] & 0x0F) << 4) | (data.Payload[1] & 0x0F)) << 8) | ((data.Payload[2] & 0x0F) << 4) | (data.Payload[3] & 0x0F));
        }

        public async Task<bool> GetPower(byte address)
        {
            Send(GetPacket(address, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, (byte)ViscaInquiryCameraOpcode.Power));
            var data = await GetInquiryResponse(1);
            
            return data.Payload[0] switch
            {
                0x02 => true,
                0x03 => false,
                _ => throw new InvalidDataException()
            };
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
            
            return data.Payload[0] switch
            {
                0x00 => ViscaDZoomCsMode.Combined,
                0x01 => ViscaDZoomCsMode.Separate,
                _ => throw new InvalidDataException()
            };
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
            
            return data.Payload[0] switch
            {
                0x02 => ViscaFocusMode.Auto,
                0x03 => ViscaFocusMode.Manual,
                _ => throw new InvalidDataException()
            };
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
            
            return data.Payload[0] switch
            {
                0x02 => ViscaAutoFocusSensitivity.Normal,
                0x03 => ViscaAutoFocusSensitivity.Low,
                _ => throw new InvalidDataException()
            };
        }
    }

    public enum ViscaDZoomCsMode
    {
        Combined = 0x02,
        Separate = 0x03
    }
}