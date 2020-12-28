namespace VISCA.NET
{
    public class ViscaDevice
    {
        public byte Address { get; }
        
        private readonly bool[] _sockets;

        public ViscaDevice(byte address)
        {
            Address = address;
            _sockets = new bool[2];
        }

        public void SetSocket(int socketNumber)
        {
            _sockets[socketNumber] = true;
        }

        public void ClearSocket(int socketNumber)
        {
            _sockets[socketNumber] = false;
        }
    }
}