using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using VISCA.NET.Enums;
using VISCA.NET.Extensions;
using VISCA.NET.Opcode;

namespace VISCA.NET
{
    public class ViscaConnection
    {
        private readonly SerialPort _port;
        private readonly Dictionary<byte, ViscaDevice> _devices;

        public ViscaConnection(string port, ViscaBaudRate baudRate)
        {
            _port = new SerialPort(port, (int)baudRate, Parity.None);
            _port.DataReceived += OnDataReceived;
            _port.Open();

            _devices = new Dictionary<byte, ViscaDevice>();
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var buffer = new byte[_port.BytesToRead];
            _port.Read(buffer, 0, buffer.Length);
            Console.WriteLine($"!→ {GetHexString(buffer)}");
            
            var deviceAddr = (byte)(((buffer[0] & 0xF0) >> 4) - 8);
            
            var firstNibble = buffer[1] & 0xF0;
            var socketNumber = buffer[1] & 0x0F;

            if (buffer[1] == 0x38)
            {
                // Network change
                return;
            }

            if (firstNibble == 0x50)
            {
                // Command completion
                _devices[deviceAddr].ClearSocket(socketNumber);

                _devices.Remove(deviceAddr);
            }
        }

        private static string GetHexString(IEnumerable<byte> arr)
        {
            return string.Join(" ", arr.Select(b => $"0x{b:X2}"));
        }

        private static ViscaPacket GetPacket<T>(ViscaDevice device, ViscaPacketType packetType, ViscaPacketCategory packetCategory, T opcode, params byte[] payload) where T : Enum
        {
            var packet = new byte[payload.Length + 4];
            packet[0] = (byte)(0x80 | (device.Address & 0x0F));
            packet[1] = (byte)packetType;
            packet[2] = (byte)packetCategory;
            packet[3] = Unsafe.As<T, byte>(ref opcode);
            Array.Copy(payload, 0, packet, 4, payload.Length);
            packet[^1] = 0xFF;
            return new ViscaPacket(device, packet);
        }

        private void Send(ViscaPacket packet)
        {
            _devices[packet.Device.Address] = packet.Device;
            
            Console.WriteLine($"← {GetHexString(packet.Data)}");
            _port.Write(packet.Data, 0, packet.Data.Length);
        }

        private async Task<ViscaAckStatus> GetCommandAck()
        {
            var buffer = await _port.ReadAsync(3);
            Console.WriteLine($"→ {GetHexString(buffer)}");

            var deviceAddr = (byte)(((buffer[0] & 0xF0) >> 4) - 8);

            var firstNibble = buffer[1] & 0xF0;
            var socketNumber = buffer[1] & 0x0F;
            
            if (firstNibble == 0x60)
            {
                // rid the buffer of the trailing 0xFF of the oversized packet
                await _port.ReadAsync(1);
                var ackStatus = (ViscaAckStatus)buffer[2];

                _devices.Remove(deviceAddr);
                
                return ackStatus;
            }
            
            var device = _devices[deviceAddr];
            device.SetSocket(socketNumber);

            return ViscaAckStatus.Success;
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

        private static byte[] PackExpandedShort(short val)
        {
            return new []
            {
                (byte)(((val & 0xF000) >> 12) & 0x0F),
                (byte)(((val & 0xF00) >> 8) & 0x0F),
                (byte)(((val & 0xF0) >> 4) & 0x0F),
                (byte)(val & 0xF)
            };
        }

        private static byte[] PackDoubleExpandedShort(short valA, short valB)
        {
            return new []
            {
                (byte)(((valA & 0xF000) >> 12) & 0x0F),
                (byte)(((valA & 0xF00) >> 8) & 0x0F),
                (byte)(((valA & 0xF0) >> 4) & 0x0F),
                (byte)(valA & 0xF),
                (byte)(((valB & 0xF000) >> 12) & 0x0F),
                (byte)(((valB & 0xF00) >> 8) & 0x0F),
                (byte)(((valB & 0xF0) >> 4) & 0x0F),
                (byte)(valB & 0xF)
            };
        }

        private static short UnpackExpandedShort(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 4)
                throw new ArgumentException("Expanded shorts can only be read from at least 4-byte payloads", nameof(data));

            // 0x0A 0x0B 0x0C 0x0D => 0xABCD
            return (short)(((((data.Payload[offset + 0] & 0x0F) << 12) | (data.Payload[offset + 1] & 0x0F)) << 8) | ((data.Payload[offset + 2] & 0x0F) << 4) | (data.Payload[offset + 3] & 0x0F));
        }

        private static byte[] PackExpandedInt12(short val)
        {
            return new []
            {
                (byte)(((val & 0xF00) >> 8) & 0x0F),
                (byte)(((val & 0xF0) >> 4) & 0x0F),
                (byte)(val & 0xF)
            };
        }

        private static byte[] PackDoubleExpandedInt12(short valA, short valB)
        {
            return new []
            {
                (byte)(((valA & 0xF00) >> 8) & 0x0F),
                (byte)(((valA & 0xF0) >> 4) & 0x0F),
                (byte)(valA & 0xF),
                (byte)(((valB & 0xF00) >> 8) & 0x0F),
                (byte)(((valB & 0xF0) >> 4) & 0x0F),
                (byte)(valB & 0xF)
            };
        }

        private static short UnpackExpandedInt12(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 3)
                throw new ArgumentException("Expanded int12s can only be read from at least 3-byte payloads", nameof(data));

            // 0x0A 0x0B 0x0C => 0xABC
            return (short)(((data.Payload[offset + 0] & 0x0F) << 8) | ((data.Payload[offset + 1] & 0x0F) << 4) | (data.Payload[offset + 2] & 0x0F));
        }

        private static byte[] PackExpandedByte(byte val)
        {
            return new []
            {
                (byte)(((val & 0xF0) >> 4) & 0x0F),
                (byte)(val & 0xF)
            };
        }

        private static byte[] PackDoubleExpandedByte(byte valA, byte valB)
        {
            return new []
            {
                (byte)(((valA & 0xF0) >> 4) & 0x0F),
                (byte)(valA & 0xF),
                (byte)(((valB & 0xF0) >> 4) & 0x0F),
                (byte)(valB & 0xF)
            };
        }

        private static byte UnpackExpandedByte(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 2)
                throw new ArgumentException("Expanded bytes can only be read from at least 2-byte payloads", nameof(data));

            // 0x0A 0x0B => 0xAB
            return (byte)(((data.Payload[offset + 2] & 0x0F) << 4) | (data.Payload[offset + 3] & 0x0F));
        }

        private static short UnpackShort(ViscaInquiryResponse data, int offset = 0)
        {
            if (data.Payload.Length < 2)
                throw new ArgumentException("Shorts can only be read from at least 2-byte payloads", nameof(data));

            // 0xAB 0xCD => 0xABCD
            return BitConverter.ToInt16(data.Payload, offset);
        }

        #region Command

        public async Task<ViscaAckStatus> SetPower(ViscaDevice device, ViscaBinaryState power)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Power, (byte)power));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoPowerOff(ViscaDevice device, short timer)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoPowerOff, PackExpandedShort(timer)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetNightPowerOff(ViscaDevice device, short timer)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.NightPowerOff, PackExpandedShort(timer)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetZoom(ViscaDevice device, ViscaZoomStandard zoom)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Zoom, (byte)zoom));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetZoom(ViscaDevice device, ViscaZoomVariable zoom, byte zoomLevel)
        {
            // Level is valid from 0-7
            var level = zoomLevel & 0b111;
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Zoom, (byte)(((byte)zoom & 0xF0) | level)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetZoomDirect(ViscaDevice device, short zoom)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ZoomDirect, PackExpandedShort(zoom)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetDigitalZoom(ViscaDevice device, ViscaDigitalZoomStandard zoom)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.DigitalZoom, (byte)zoom));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetDigitalZoom(ViscaDevice device, ViscaZoomVariable zoom, byte zoomLevel)
        {
            // Level is valid from 0-7
            var level = zoomLevel & 0b111;
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.DigitalZoom, (byte)(((byte)zoom & 0xF0) | level)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetDigitalZoomMode(ViscaDevice device, ViscaDigitalZoomMode mode)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.DigitalZoomMode, (byte)mode));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetDigitalZoomDirect(ViscaDevice device, byte zoom)
        {
            // Payload is [00 00 0p 0q] instead of [0p 0q 0r 0s]
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.DigitalZoomDirect, PackExpandedShort(zoom)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetFocus(ViscaDevice device, ViscaFocusStandard focus)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Focus, (byte)focus));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetFocus(ViscaDevice device, ViscaFocusVariable focus, byte focusLevel)
        {
            // Level is valid from 0-7
            var level = focusLevel & 0b111;
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Focus, (byte)(((byte)focus & 0xF0) | level)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetFocusDirect(ViscaDevice device, short focus)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.FocusDirect, PackExpandedShort(focus)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoFocus(ViscaDevice device, ViscaAutoFocusMode focus)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoFocus, (byte)focus));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetFocusSpecial(ViscaDevice device, ViscaFocusSpecial focus)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.FocusSpecial, (byte)focus));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetFocusNearLimit(ViscaDevice device, short focus)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.FocusNearLimit, PackExpandedShort(focus)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoFocusSensitivity(ViscaDevice device, ViscaAutoFocusSensitivity value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoFocusSensitivity, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoFocusMode(ViscaDevice device, ViscaAutoFocusMode value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoFocusMode, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoFocusActiveIntervalTime(ViscaDevice device, byte movementTime, byte interval)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoFocusActiveIntervalTime, PackDoubleExpandedByte(movementTime, interval)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetZoomFocus(ViscaDevice device, short zoom, short focus)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoFocusActiveIntervalTime, PackDoubleExpandedShort(zoom, focus)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> TriggerInitialize(ViscaDevice device, ViscaInitializeTarget value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Initialize, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetWhiteBalance(ViscaDevice device, ViscaWhiteBalanceMode value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.WhiteBalance, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> TriggerWhiteBalance(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.WhiteBalanceTrigger, 0x05));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetRGain(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.RGain, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetRGain(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.RGainDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetBGain(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.BGain, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetBGain(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.BGainDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoExposure(ViscaDevice device, ViscaAutoExposureMode value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoExposure, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetSlowShutter(ViscaDevice device, ViscaSlowShutterMode value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.SlowShutter, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetShutter(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Shutter, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetShutter(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ShutterDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetIris(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Iris, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetIris(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.IrisDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetGain(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Gain, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetGain(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.GainDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetBright(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Bright, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetBright(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.BrightDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetExposureCompensation(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ExposureCompensationEnable, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetExposureCompensation(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ExposureCompensation, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetExposureCompensation(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ExposureCompensationDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetBacklight(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Backlight, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetSpotAutoExposure(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.SpotAutoExposure, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetSpotAutoExposurePosition(ViscaDevice device, byte x, byte y)
        {
            x &= 0x0F;
            y &= 0x0F;
            
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.SpotAutoExposurePosition, PackDoubleExpandedByte(x, y)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAperture(ViscaDevice device, ViscaDeltaState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Aperture, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAperture(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ApertureDirect, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetLRReverse(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.LRReverse, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetFreeze(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Freeze, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetPictureEffect(ViscaDevice device, ViscaPictureEffect value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.PictureEffect, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetICR(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.ICR, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAutoICR(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AutoICR, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetMemory(ViscaDevice device, ViscaMemoryCommand command, byte memoryNumber)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Memory, (byte)command, (byte)memoryNumber));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetDisplay(ViscaDevice device, ViscaBinaryStateToggle value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Display, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetTitleParams(ViscaDevice device, byte x, byte y, ViscaTitleColor color, ViscaTitleBlink blink)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.TitleSet, 0x00, y, x, (byte)color, (byte)blink, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetTitleTextA(ViscaDevice device, string str)
        {
            str = str.Substring(0, Math.Min(str.Length, 10));
            var bytes = Encoding.ASCII.GetBytes(str);
            var payload = new byte[11];
            payload[0] = 0x01;
            Array.Copy(bytes, 0, payload, 1, bytes.Length);
            
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.TitleSet, payload));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetTitleTextB(ViscaDevice device, string str)
        {
            str = str.Substring(0, Math.Min(str.Length, 10));
            var bytes = Encoding.ASCII.GetBytes(str);
            var payload = new byte[11];
            payload[0] = 0x02;
            Array.Copy(bytes, 0, payload, 1, bytes.Length);
            
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.TitleSet, payload));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> ClearTitle(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Title, 0x00));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetTitle(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Title, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetMute(ViscaDevice device, ViscaBinaryStateToggle value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Mute, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetMute(ViscaDevice device, ViscaKeyLock value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.KeyLock, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetId(ViscaDevice device, short value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.IDWrite, PackExpandedShort(value)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAlarm(ViscaDevice device, ViscaBinaryState value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.Alarm, (byte)value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAlarmMode(ViscaDevice device, byte value)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AlarmSetMode, value));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetAlarmDayNightLevel(ViscaDevice device, short dayAeLevel, short nightAeLevel)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera, ViscaCommandCameraOpcode.AlarmSetDayNightLevel, PackDoubleExpandedShort(dayAeLevel, nightAeLevel)));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> PanTilt(ViscaDevice device, byte panSpeed, byte tiltSpeed, ViscaMotionPan pan, ViscaMotionTilt tilt)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.Drive, panSpeed, tiltSpeed, (byte)pan, (byte)tilt));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> PanTiltAbsolute(ViscaDevice device, byte panSpeed, byte tiltSpeed, short pan, short tilt)
        {
            var payload = new []
            {
                panSpeed,
                tiltSpeed,
                (byte)(((pan & 0xF000) >> 12) & 0x0F),
                (byte)(((pan & 0xF00) >> 8) & 0x0F),
                (byte)(((pan & 0xF0) >> 4) & 0x0F),
                (byte)(pan & 0xF),
                (byte)(((tilt & 0xF000) >> 12) & 0x0F),
                (byte)(((tilt & 0xF00) >> 8) & 0x0F),
                (byte)(((tilt & 0xF0) >> 4) & 0x0F),
                (byte)(tilt & 0xF)
            };
            
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.AbsolutePosition, payload));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> PanTiltRelative(ViscaDevice device, byte panSpeed, byte tiltSpeed, short pan, short tilt)
        {
            var payload = new []
            {
                panSpeed,
                tiltSpeed,
                (byte)(((pan & 0xF000) >> 12) & 0x0F),
                (byte)(((pan & 0xF00) >> 8) & 0x0F),
                (byte)(((pan & 0xF0) >> 4) & 0x0F),
                (byte)(pan & 0xF),
                (byte)(((tilt & 0xF000) >> 12) & 0x0F),
                (byte)(((tilt & 0xF00) >> 8) & 0x0F),
                (byte)(((tilt & 0xF0) >> 4) & 0x0F),
                (byte)(tilt & 0xF)
            };
            
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.RelativePosition, payload));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> PanTiltHome(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.Home));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> PanTiltReset(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.Reset));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> SetPanTiltLimit(ViscaDevice device, ViscaPanTiltLimitCorner corner, short panLimit, short tiltLimit)
        {
            var payload = new byte[]
            {
                0x00,
                (byte)corner,
                (byte)(((panLimit & 0xF000) >> 12) & 0x0F),
                (byte)(((panLimit & 0xF00) >> 8) & 0x0F),
                (byte)(((panLimit & 0xF0) >> 4) & 0x0F),
                (byte)(panLimit & 0xF),
                (byte)(((tiltLimit & 0xF000) >> 12) & 0x0F),
                (byte)(((tiltLimit & 0xF00) >> 8) & 0x0F),
                (byte)(((tiltLimit & 0xF0) >> 4) & 0x0F),
                (byte)(tiltLimit & 0xF)
            };
            
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.Limit, payload));
            return await GetCommandAck();
        }
        
        public async Task<ViscaAckStatus> ClearPanTiltLimit(ViscaDevice device, ViscaPanTiltLimitCorner corner)
        {
            Send(GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter, ViscaCommandPanTilterOpcode.Limit, 0x01, (byte)corner, 0x07, 0x0F, 0x0F, 0x0F, 0x07, 0x0F, 0x0F, 0x0F));
            return await GetCommandAck();
        }

        #endregion

        #region Inquiry

        public async Task<ViscaBinaryState> GetPower(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.Power));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<short> GetAutoPowerOffTimer(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AutoPowerOff));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }

        public async Task<short> GetNightPowerOffTimer(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.NightPowerOff));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }

        public async Task<short> GetZoomPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.ZoomPos));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }

        public async Task<ViscaBinaryState> GetDZoomMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.DZoomMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaDZoomCsMode> GetDZoomCSMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.DZoomCSMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaDZoomCsMode>(data);
        }

        public async Task<short> GetDZoomPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.DZoomPos));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }

        public async Task<ViscaFocusMode> GetFocusMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.FocusMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaFocusMode>(data);
        }

        public async Task<short> GetFocusPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.FocusPos));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }

        public async Task<short> GetFocusNearLimit(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.FocusNearLimit));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }
        
        public async Task<ViscaAutoFocusSensitivity> GetAutoFocusSensitivity(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AFSensitivity));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaAutoFocusSensitivity>(data);
        }
        
        public async Task<ViscaAutoFocusMode> GetAutoFocusMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AFMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaAutoFocusMode>(data);
        }

        public async Task<(byte MovementTime, byte Interval)> GetAutoFocusTimeSetting(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AFTimeSetting));
            var data = await GetInquiryResponse(4);
            return (MovementTime: UnpackExpandedByte(data), Interval: UnpackExpandedByte(data, 2));
        }
        
        public async Task<ViscaWhiteBalanceMode> GetWhiteBalanceMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.WBMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaWhiteBalanceMode>(data);
        }
        
        public async Task<byte> GetRGain(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.RGain));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }
        
        public async Task<byte> GetBGain(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.BGain));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }
        
        public async Task<ViscaAutoExposureMode> GetAutoExposureMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AEMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaAutoExposureMode>(data);
        }
        
        public async Task<ViscaSlowShutterMode> GetSlowShutterMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.SlowShutterMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaSlowShutterMode>(data);
        }
        
        public async Task<byte> GetShutterPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.ShutterPos));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }
        
        public async Task<byte> GetAperturePos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AperturePos));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }
        
        public async Task<byte> GetGainPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.GainPos));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }
        
        public async Task<byte> GetBrightPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.BrightPos));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }

        public async Task<ViscaBinaryState> GetExpCompMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.ExpCompMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }
        
        public async Task<byte> GetExpCompPos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.ExpCompPos));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }

        public async Task<ViscaBinaryState> GetBacklightMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.BacklightMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaBinaryState> GetSpotAutoExposureMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.SpotAEMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<(byte X, byte Y)> GetSpotAutoExposurePos(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.SpotAEPos));
            var data = await GetInquiryResponse(4);
            
            return (X: UnpackExpandedByte(data), Y: UnpackExpandedByte(data, 2));
        }
        
        public async Task<byte> GetAperture(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.Aperture));
            var data = await GetInquiryResponse(4);

            return UnpackExpandedByte(data, 2);
        }

        public async Task<ViscaBinaryState> GetLRReverseMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.LRReverseMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaBinaryState> GetFreezeMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.FreezeMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }
        
        public async Task<ViscaPictureEffect> GetPictureEffectMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.PictureEffectMode));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaPictureEffect>(data);
        }

        public async Task<ViscaBinaryState> GetICRMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.ICRMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaBinaryState> GetAutoICRMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AutoICRMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<byte> GetMemory(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.Memory));
            var data = await GetInquiryResponse(1);
            
            return data.Payload[0];
        }

        public async Task<ViscaBinaryState> GetDisplayMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.DisplayMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaBinaryState> GetTitleDisplayMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.TitleDisplayMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaBinaryState> GetMuteMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.MuteMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<ViscaKeyLock> GetKeyLockMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.KeyLock));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaKeyLock>(data);
        }

        public async Task<short> GetID(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.ID));
            var data = await GetInquiryResponse(4);
            return UnpackExpandedShort(data);
        }

        public async Task<(short ModelCode, short RomVersion, byte SocketNumber)> GetVersion(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Interface, ViscaInquiryInterfaceOpcode.Version));
            var data = await GetInquiryResponse(7);
            return (ModelCode: UnpackShort(data, 2), RomVersion: UnpackShort(data, 4), SocketNumber: data.Payload[6]);
        }

        public async Task<ViscaBinaryState> GetAlarm(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.Alarm));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }

        public async Task<byte> GetAlarmMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AlarmMode));
            var data = await GetInquiryResponse(1);
            
            return data.Payload[0];
        }

        public async Task<(short DayAELevel, short NightAELevel, short NowAELevel)> GetAlarmDayNightLevel(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AlarmDayNightLevel));
            var data = await GetInquiryResponse(9);
            
            return (DayAELevel: UnpackExpandedInt12(data), NightAELevel: UnpackExpandedInt12(data, 3), NowAELevel: UnpackExpandedInt12(data, 6));
        }

        public async Task<ViscaBinaryState> GetPictureFlipMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.PictureFlipMode));
            var data = await GetInquiryResponse(1);
            
            return GetEnum<ViscaBinaryState>(data);
        }
        
        public async Task<ViscaAlarmDetectLevel> GetAlarmDetectLevel(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera, ViscaInquiryCameraOpcode.AlarmDetectLevel));
            var data = await GetInquiryResponse(1);

            return GetEnum<ViscaAlarmDetectLevel>(data);
        }

        public async Task<short> GetPanTiltMode(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.PanTilter, ViscaInquiryPanTilterOpcode.Mode));
            var data = await GetInquiryResponse(2);
            return UnpackShort(data);
        }

        public async Task<(byte Pan, byte Tilt)> GetPanTiltMaxSpeed(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.PanTilter, ViscaInquiryPanTilterOpcode.MaxSpeed));
            var data = await GetInquiryResponse(2);
            return (Pan: data.Payload[0], Tilt: data.Payload[1]);
        }

        public async Task<(short Pan, short Tilt)> GetPanTiltPosition(ViscaDevice device)
        {
            Send(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.PanTilter, ViscaInquiryPanTilterOpcode.Pos));
            var data = await GetInquiryResponse(8);
            return (Pan: UnpackExpandedShort(data), Tilt: UnpackExpandedShort(data, 4));
        }
        
        #endregion
    }
}