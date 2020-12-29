using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace VISCA.NET.Extensions
{
	public static class SerialPortExtensions
	{
		public static async Task ReadAsync(this SerialPort serialPort, byte[] buffer, int offset, int count)
		{
			var bytesToRead = count;
			var temp = new byte[count];

			while (bytesToRead > 0)
			{
				var readBytes = await serialPort.BaseStream.ReadAsync(temp, 0, bytesToRead);
				Array.Copy(temp, 0, buffer, offset + count - bytesToRead, readBytes);
				bytesToRead -= readBytes;
			}
		}

		public static async Task<byte[]> ReadAsync(this SerialPort serialPort, int count)
		{
			var buffer = new byte[count];
			await serialPort.ReadAsync(buffer, 0, count);
			return buffer;
		}
	}
}