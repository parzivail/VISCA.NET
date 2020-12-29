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

		private ViscaPacket _outgoingPacket;

		public ViscaConnection(string port, ViscaBaudRate baudRate)
		{
			_port = new SerialPort(port, (int)baudRate, Parity.None);
			_port.DataReceived += OnDataReceived;
			_port.Open();

			_devices = new Dictionary<byte, ViscaDevice>();
		}

		private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
		{
			if (_port.BytesToRead == 0)
				// Another function has already handled the response
				return;

			while (_port.BytesToRead > 0)
			{
				var addrByte = (byte)_port.ReadByte();
				var deviceAddr = (byte)(((addrByte & 0xF0) >> 4) - 8);

				var opcodeByte = (byte)_port.ReadByte();
				var firstNibble = opcodeByte & 0xF0;
				var socketNumber = opcodeByte & 0x0F;

				if (opcodeByte == 0x38)
					// Network change
					return;

				switch (firstNibble)
				{
					case 0x40:
					case 0x60:
					{
						// ack
						if (_outgoingPacket == null)
							throw new InvalidOperationException("Acknowledged unknown packet");

						var error = firstNibble == 0x60;
						ViscaAckStatus ackStatus;
						
						if (error)
						{
							var ackStatusByte = (byte)_port.ReadByte();
							ackStatus = (ViscaAckStatus)ackStatusByte;
							Console.WriteLine($"E→ {GetHexString(addrByte, opcodeByte, ackStatusByte, 0xFF)}");
						}
						else
						{
							ackStatus = ViscaAckStatus.Success;
							Console.WriteLine($"A→ {GetHexString(addrByte, opcodeByte, 0xFF)}");
						}

						_devices[deviceAddr].SetSocket(socketNumber - 1, _outgoingPacket);
						_outgoingPacket.ConsumeAck(ackStatus);
						_outgoingPacket = null;

						break;
					}
					case 0x50:
					{
						// Command completion
						_devices[deviceAddr].ClearSocket(socketNumber - 1);
						
						Console.WriteLine($"C→ {GetHexString(addrByte, opcodeByte, 0xFF)}");
						break;
					}
				}

				var packetEndByte = _port.ReadByte();
				if (packetEndByte != 0xFF)
					throw new InvalidDataException();
			}
		}

		private static string GetHexString(params byte[] arr)
		{
			return string.Join(" ", arr.Select(b => $"0x{b:X2}"));
		}

		private static ViscaPacket GetPacket<T>(ViscaDevice device, ViscaPacketType packetType,
			ViscaPacketCategory packetCategory, T opcode, params byte[] payload) where T : Enum
		{
			var packet = new byte[payload.Length + 5];
			packet[0] = (byte)(0x80 | (device.Address & 0x0F));
			packet[1] = (byte)packetType;
			packet[2] = (byte)packetCategory;
			packet[3] = Unsafe.As<T, byte>(ref opcode);
			Array.Copy(payload, 0, packet, 4, payload.Length);
			packet[^1] = 0xFF;
			return new ViscaPacket(device, packet);
		}

		private void SendCommand(ViscaPacket packet)
		{
			if (_outgoingPacket != null)
				throw new InvalidOperationException(
					"Cannot send a packet before the previous packet has been acknowledged");

			_outgoingPacket = packet;
			_devices[packet.Device.Address] = packet.Device;

			Console.WriteLine($"← {GetHexString(packet.Data)}");
			_port.Write(packet.Data, 0, packet.Data.Length);
		}

		private void SendInquiry(ViscaPacket packet)
		{
			if (_outgoingPacket != null)
				throw new InvalidOperationException(
					"Cannot send a packet before the previous packet has been acknowledged");

			_outgoingPacket = packet;

			Console.WriteLine($"← {GetHexString(packet.Data)}");
			_port.Write(packet.Data, 0, packet.Data.Length);
		}

		private async Task<ViscaInquiryResponse> GetInquiryResponse(int responseLength)
		{
			var buffer = await _port.ReadAsync(responseLength + 3);
			Console.WriteLine($"→ {GetHexString(buffer)}");

			_outgoingPacket = null;

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
			return new[]
			{
				(byte)(((val & 0xF000) >> 12) & 0x0F),
				(byte)(((val & 0xF00) >> 8) & 0x0F),
				(byte)(((val & 0xF0) >> 4) & 0x0F),
				(byte)(val & 0xF)
			};
		}

		private static byte[] PackDoubleExpandedShort(short valA, short valB)
		{
			return new[]
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
				throw new ArgumentException("Expanded shorts can only be read from at least 4-byte payloads",
					nameof(data));

			// 0x0A 0x0B 0x0C 0x0D => 0xABCD
			return (short)(((((data.Payload[offset + 0] & 0x0F) << 12) | (data.Payload[offset + 1] & 0x0F)) << 8) |
			               ((data.Payload[offset + 2] & 0x0F) << 4) | (data.Payload[offset + 3] & 0x0F));
		}

		private static short UnpackExpandedInt12(ViscaInquiryResponse data, int offset = 0)
		{
			if (data.Payload.Length < 3)
				throw new ArgumentException("Expanded int12s can only be read from at least 3-byte payloads",
					nameof(data));

			// 0x0A 0x0B 0x0C => 0xABC
			return (short)(((data.Payload[offset + 0] & 0x0F) << 8) | ((data.Payload[offset + 1] & 0x0F) << 4) |
			               (data.Payload[offset + 2] & 0x0F));
		}

		private static byte[] PackDoubleExpandedByte(byte valA, byte valB)
		{
			return new[]
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
				throw new ArgumentException("Expanded bytes can only be read from at least 2-byte payloads",
					nameof(data));

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

		public ViscaPacket SetPower(ViscaDevice device, ViscaBinaryState power)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Power, (byte)power);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoPowerOff(ViscaDevice device, short timer)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoPowerOff, PackExpandedShort(timer));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetNightPowerOff(ViscaDevice device, short timer)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.NightPowerOff, PackExpandedShort(timer));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetZoom(ViscaDevice device, ViscaZoomStandard zoom)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Zoom, (byte)zoom);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetZoom(ViscaDevice device, ViscaZoomVariable zoom, byte zoomLevel)
		{
			// Level is valid from 0-7
			var level = zoomLevel & 0b111;
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Zoom, (byte)(((byte)zoom & 0xF0) | level));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetZoomDirect(ViscaDevice device, short zoom)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ZoomDirect, PackExpandedShort(zoom));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetDigitalZoom(ViscaDevice device, ViscaDigitalZoomStandard zoom)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.DigitalZoom, (byte)zoom);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetDigitalZoom(ViscaDevice device, ViscaZoomVariable zoom, byte zoomLevel)
		{
			// Level is valid from 0-7
			var level = zoomLevel & 0b111;
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.DigitalZoom, (byte)(((byte)zoom & 0xF0) | level));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetDigitalZoomMode(ViscaDevice device, ViscaDigitalZoomMode mode)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.DigitalZoomMode, (byte)mode);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetDigitalZoomDirect(ViscaDevice device, byte zoom)
		{
			// Payload is [00 00 0p 0q] instead of [0p 0q 0r 0s]
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.DigitalZoomDirect, PackExpandedShort(zoom));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetFocus(ViscaDevice device, ViscaFocusStandard focus)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Focus, (byte)focus);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetFocus(ViscaDevice device, ViscaFocusVariable focus, byte focusLevel)
		{
			// Level is valid from 0-7
			var level = focusLevel & 0b111;
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Focus, (byte)(((byte)focus & 0xF0) | level));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetFocusDirect(ViscaDevice device, short focus)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.FocusDirect, PackExpandedShort(focus));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoFocus(ViscaDevice device, ViscaAutoFocusMode focus)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoFocus, (byte)focus);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetFocusSpecial(ViscaDevice device, ViscaFocusSpecial focus)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.FocusSpecial, (byte)focus);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetFocusNearLimit(ViscaDevice device, short focus)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.FocusNearLimit, PackExpandedShort(focus));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoFocusSensitivity(ViscaDevice device, ViscaAutoFocusSensitivity value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoFocusSensitivity, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoFocusMode(ViscaDevice device, ViscaAutoFocusMode value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoFocusMode, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoFocusActiveIntervalTime(ViscaDevice device, byte movementTime,
			byte interval)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoFocusActiveIntervalTime, PackDoubleExpandedByte(movementTime, interval));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetZoomFocus(ViscaDevice device, short zoom, short focus)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoFocusActiveIntervalTime, PackDoubleExpandedShort(zoom, focus));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket TriggerInitialize(ViscaDevice device, ViscaInitializeTarget value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Initialize, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetWhiteBalance(ViscaDevice device, ViscaWhiteBalanceMode value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.WhiteBalance, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket TriggerWhiteBalance(ViscaDevice device)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.WhiteBalanceTrigger, 0x05);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetRGain(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.RGain, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetRGain(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.RGainDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetBGain(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.BGain, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetBGain(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.BGainDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoExposure(ViscaDevice device, ViscaAutoExposureMode value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoExposure, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetSlowShutter(ViscaDevice device, ViscaSlowShutterMode value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.SlowShutter, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetShutter(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Shutter, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetShutter(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ShutterDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetIris(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Iris, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetIris(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.IrisDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetGain(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Gain, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetGain(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.GainDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetBright(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Bright, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetBright(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.BrightDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetExposureCompensation(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ExposureCompensationEnable, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetExposureCompensation(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ExposureCompensation, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetExposureCompensation(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ExposureCompensationDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetBacklight(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Backlight, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetSpotAutoExposure(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.SpotAutoExposure, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetSpotAutoExposurePosition(ViscaDevice device, byte x, byte y)
		{
			x &= 0x0F;
			y &= 0x0F;

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.SpotAutoExposurePosition, PackDoubleExpandedByte(x, y));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAperture(ViscaDevice device, ViscaDeltaState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Aperture, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAperture(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ApertureDirect, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetLRReverse(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.LRReverse, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetFreeze(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Freeze, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetPictureEffect(ViscaDevice device, ViscaPictureEffect value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.PictureEffect, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetICR(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.ICR, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAutoICR(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AutoICR, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetMemory(ViscaDevice device, ViscaMemoryCommand command, byte memoryNumber)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Memory, (byte)command, (byte)memoryNumber);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetDisplay(ViscaDevice device, ViscaBinaryStateToggle value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Display, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetTitleParams(ViscaDevice device, byte x, byte y, ViscaTitleColor color,
			ViscaTitleBlink blink)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.TitleSet, 0x00, y, x, (byte)color, (byte)blink, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetTitleTextARaw(ViscaDevice device, params byte[] bytes)
		{
			var payload = new byte[11];
			payload[0] = 0x01;
			Array.Copy(bytes, 0, payload, 1, bytes.Length);

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.TitleSet, payload);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetTitleTextA(ViscaDevice device, string str)
		{
			str = str.Substring(0, Math.Min(str.Length, 10));
			var bytes = ViscaTextEncoding.GetBytes(str);
			var payload = new byte[11];
			Array.Fill(payload, ViscaTextEncoding.EmptyCharacter);
			payload[0] = 0x01;
			Array.Copy(bytes, 0, payload, 1, bytes.Length);

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.TitleSet, payload);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetTitleTextB(ViscaDevice device, string str)
		{
			str = str.Substring(0, Math.Min(str.Length, 10));
			var bytes = ViscaTextEncoding.GetBytes(str);
			var payload = new byte[11];
			Array.Fill(payload, ViscaTextEncoding.EmptyCharacter);
			payload[0] = 0x02;
			Array.Copy(bytes, 0, payload, 1, bytes.Length);

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.TitleSet, payload);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket ClearTitle(ViscaDevice device)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Title, 0x00);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetTitle(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Title, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetMute(ViscaDevice device, ViscaBinaryStateToggle value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Mute, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetMute(ViscaDevice device, ViscaKeyLock value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.KeyLock, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetId(ViscaDevice device, short value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.IDWrite, PackExpandedShort(value));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAlarm(ViscaDevice device, ViscaBinaryState value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.Alarm, (byte)value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAlarmMode(ViscaDevice device, byte value)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AlarmSetMode, value);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetAlarmDayNightLevel(ViscaDevice device, short dayAeLevel, short nightAeLevel)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.Camera,
				ViscaCommandCameraOpcode.AlarmSetDayNightLevel, PackDoubleExpandedShort(dayAeLevel, nightAeLevel));
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket PanTilt(ViscaDevice device, byte panSpeed, byte tiltSpeed, ViscaMotionPan pan,
			ViscaMotionTilt tilt)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.Drive, panSpeed, tiltSpeed, (byte)pan, (byte)tilt);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket PanTiltAbsolute(ViscaDevice device, byte panSpeed, byte tiltSpeed, short pan,
			short tilt)
		{
			var payload = new[]
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

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.AbsolutePosition, payload);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket PanTiltRelative(ViscaDevice device, byte panSpeed, byte tiltSpeed, short pan,
			short tilt)
		{
			var payload = new[]
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

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.RelativePosition, payload);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket PanTiltHome(ViscaDevice device)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.Home);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket PanTiltReset(ViscaDevice device)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.Reset);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket SetPanTiltLimit(ViscaDevice device, ViscaPanTiltLimitCorner corner,
			short panLimit, short tiltLimit)
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

			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.Limit, payload);
			SendCommand(packet);
			return packet;
		}

		public ViscaPacket ClearPanTiltLimit(ViscaDevice device, ViscaPanTiltLimitCorner corner)
		{
			var packet = GetPacket(device, ViscaPacketType.Command, ViscaPacketCategory.PanTilter,
				ViscaCommandPanTilterOpcode.Limit, 0x01, (byte)corner, 0x07, 0x0F, 0x0F, 0x0F, 0x07, 0x0F, 0x0F, 0x0F);
			SendCommand(packet);
			return packet;
		}

		#endregion

		#region Inquiry

		public async Task<ViscaBinaryState> GetPower(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.Power));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<short> GetAutoPowerOffTimer(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AutoPowerOff));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<short> GetNightPowerOffTimer(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.NightPowerOff));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<short> GetZoomPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.ZoomPos));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<ViscaBinaryState> GetDZoomMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.DZoomMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaDZoomCsMode> GetDZoomCSMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.DZoomCSMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaDZoomCsMode>(data);
		}

		public async Task<short> GetDZoomPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.DZoomPos));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<ViscaFocusMode> GetFocusMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.FocusMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaFocusMode>(data);
		}

		public async Task<short> GetFocusPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.FocusPos));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<short> GetFocusNearLimit(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.FocusNearLimit));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<ViscaAutoFocusSensitivity> GetAutoFocusSensitivity(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AFSensitivity));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaAutoFocusSensitivity>(data);
		}

		public async Task<ViscaAutoFocusMode> GetAutoFocusMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AFMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaAutoFocusMode>(data);
		}

		public async Task<(byte MovementTime, byte Interval)> GetAutoFocusTimeSetting(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AFTimeSetting));
			var data = await GetInquiryResponse(4);
			return (MovementTime: UnpackExpandedByte(data), Interval: UnpackExpandedByte(data, 2));
		}

		public async Task<ViscaWhiteBalanceMode> GetWhiteBalanceMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.WBMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaWhiteBalanceMode>(data);
		}

		public async Task<byte> GetRGain(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.RGain));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<byte> GetBGain(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.BGain));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<ViscaAutoExposureMode> GetAutoExposureMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AEMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaAutoExposureMode>(data);
		}

		public async Task<ViscaSlowShutterMode> GetSlowShutterMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.SlowShutterMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaSlowShutterMode>(data);
		}

		public async Task<byte> GetShutterPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.ShutterPos));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<byte> GetAperturePos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AperturePos));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<byte> GetGainPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.GainPos));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<byte> GetBrightPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.BrightPos));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<ViscaBinaryState> GetExpCompMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.ExpCompMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<byte> GetExpCompPos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.ExpCompPos));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<ViscaBinaryState> GetBacklightMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.BacklightMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaBinaryState> GetSpotAutoExposureMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.SpotAEMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<(byte X, byte Y)> GetSpotAutoExposurePos(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.SpotAEPos));
			var data = await GetInquiryResponse(4);

			return (X: UnpackExpandedByte(data), Y: UnpackExpandedByte(data, 2));
		}

		public async Task<byte> GetAperture(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.Aperture));
			var data = await GetInquiryResponse(4);

			return UnpackExpandedByte(data, 2);
		}

		public async Task<ViscaBinaryState> GetLRReverseMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.LRReverseMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaBinaryState> GetFreezeMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.FreezeMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaPictureEffect> GetPictureEffectMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.PictureEffectMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaPictureEffect>(data);
		}

		public async Task<ViscaBinaryState> GetICRMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.ICRMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaBinaryState> GetAutoICRMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AutoICRMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<byte> GetMemory(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.Memory));
			var data = await GetInquiryResponse(1);

			return data.Payload[0];
		}

		public async Task<ViscaBinaryState> GetDisplayMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.DisplayMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaBinaryState> GetTitleDisplayMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.TitleDisplayMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaBinaryState> GetMuteMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.MuteMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaKeyLock> GetKeyLockMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.KeyLock));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaKeyLock>(data);
		}

		public async Task<short> GetID(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.ID));
			var data = await GetInquiryResponse(4);
			return UnpackExpandedShort(data);
		}

		public async Task<(short ModelCode, short RomVersion, byte SocketNumber)> GetVersion(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Interface,
				ViscaInquiryInterfaceOpcode.Version));
			var data = await GetInquiryResponse(7);
			return (ModelCode: UnpackShort(data, 2), RomVersion: UnpackShort(data, 4), SocketNumber: data.Payload[6]);
		}

		public async Task<ViscaBinaryState> GetAlarm(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.Alarm));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<byte> GetAlarmMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AlarmMode));
			var data = await GetInquiryResponse(1);

			return data.Payload[0];
		}

		public async Task<(short DayAELevel, short NightAELevel, short NowAELevel)> GetAlarmDayNightLevel(
			ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AlarmDayNightLevel));
			var data = await GetInquiryResponse(9);

			return (DayAELevel: UnpackExpandedInt12(data), NightAELevel: UnpackExpandedInt12(data, 3),
				NowAELevel: UnpackExpandedInt12(data, 6));
		}

		public async Task<ViscaBinaryState> GetPictureFlipMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.PictureFlipMode));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaBinaryState>(data);
		}

		public async Task<ViscaAlarmDetectLevel> GetAlarmDetectLevel(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.Camera,
				ViscaInquiryCameraOpcode.AlarmDetectLevel));
			var data = await GetInquiryResponse(1);

			return GetEnum<ViscaAlarmDetectLevel>(data);
		}

		public async Task<short> GetPanTiltMode(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.PanTilter,
				ViscaInquiryPanTilterOpcode.Mode));
			var data = await GetInquiryResponse(2);
			return UnpackShort(data);
		}

		public async Task<(byte Pan, byte Tilt)> GetPanTiltMaxSpeed(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.PanTilter,
				ViscaInquiryPanTilterOpcode.MaxSpeed));
			var data = await GetInquiryResponse(2);
			return (Pan: data.Payload[0], Tilt: data.Payload[1]);
		}

		public async Task<(short Pan, short Tilt)> GetPanTiltPosition(ViscaDevice device)
		{
			SendInquiry(GetPacket(device, ViscaPacketType.Inquiry, ViscaPacketCategory.PanTilter,
				ViscaInquiryPanTilterOpcode.Pos));
			var data = await GetInquiryResponse(8);
			return (Pan: UnpackExpandedShort(data), Tilt: UnpackExpandedShort(data, 4));
		}

		#endregion
	}
}