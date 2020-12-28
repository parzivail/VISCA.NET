namespace VISCA.NET
{
    public class ViscaPacket
    {
        public ViscaDevice Device { get; }
        public readonly byte[] Data;

        public ViscaPacket(ViscaDevice device, byte[] data)
        {
            Device = device;
            Data = data;
        }
    }
}