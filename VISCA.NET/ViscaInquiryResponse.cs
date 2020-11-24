namespace VISCA.NET
{
    public class ViscaInquiryResponse
    {
        public byte CommandSocket { get; set; }
        public byte[] Payload { get; set; }
        
        public ViscaInquiryResponse(byte commandSocket, byte[] payload)
        {
            CommandSocket = commandSocket;
            Payload = payload;
        }
    }
}