using System;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using VISCA.NET;
using VISCA.NET.Enums;

namespace Sandbox
{
	internal class Program
	{
		private static async Task Main(string[] args)
		{
			var port = SelectComPort();
			var con = new ViscaConnection(port, ViscaBaudRate.Rate38400);

			var device = new ViscaDevice(1);

			// var (pan, tilt) = await con.GetPanTiltPosition(device);
			// Console.WriteLine($"Pan: {pan}, Tilt: {tilt}");

			// var packet = con.SetTitle(device, ViscaBinaryState.On);
			// if (packet.WaitForAck() != ViscaAckStatus.Success)
			// 	throw new Exception();
			//
			// packet = con.ClearTitle(device);
			// if (packet.WaitForAck() != ViscaAckStatus.Success)
			// 	throw new Exception();
			//
			// packet = con.SetTitleTextA(device, "HELLO!");
			// if (packet.WaitForAck() != ViscaAckStatus.Success)
			// 	throw new Exception();

			var packet = con.PanTiltHome(device);
			if (packet.WaitForAck() != ViscaAckStatus.Success)
				throw new Exception();
			
			packet.WaitForCompletion();
			
			// absolute minimum
			packet = con.PanTiltAbsolute(device, 24, 23, -2267, -400);
			if (packet.WaitForAck() != ViscaAckStatus.Success)
				throw new Exception();
			
			packet.WaitForCompletion();
			
			// absolute maximum
			packet = con.PanTiltAbsolute(device, 24, 23, 2267, 1200);
			if (packet.WaitForAck() != ViscaAckStatus.Success)
				throw new Exception();

			var mres = new ManualResetEventSlim();
			mres.Wait();
		}

		private static string SelectComPort()
		{
			var ports = SerialPort.GetPortNames();

			for (var i = 0; i < ports.Length; i++)
				Console.WriteLine($"[{i}] {ports[i]}");

			string input;
			int selectedPort;
			do
			{
				Console.Write("> ");
				input = Console.ReadLine();
			} while (!int.TryParse(input, out selectedPort) || selectedPort < 0 || selectedPort >= ports.Length);

			return ports[selectedPort];
		}
	}
}