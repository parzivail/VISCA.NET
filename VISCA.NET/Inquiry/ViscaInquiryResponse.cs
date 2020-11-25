namespace VISCA.NET.Inquiry
{
    public class ViscaInquiryResponse
    {
        public readonly byte CommandSocket;
        public readonly byte[] Payload;
        
        public ViscaInquiryResponse(byte commandSocket, byte[] payload)
        {
            CommandSocket = commandSocket;
            Payload = payload;
        }
    }
}