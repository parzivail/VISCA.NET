namespace VISCA.NET
{
	public class ViscaDevice
	{
		public byte Address { get; }

		private readonly ViscaPacket[] _sockets;

		public ViscaDevice(byte address)
		{
			Address = address;
			_sockets = new ViscaPacket[2];
		}

		public void SetSocket(int socketNumber, ViscaPacket packet)
		{
			_sockets[socketNumber] = packet;
		}

		public void ClearSocket(int socketNumber)
		{
			_sockets[socketNumber].ConsumeCompleted();
			_sockets[socketNumber] = null;
		}
	}
}