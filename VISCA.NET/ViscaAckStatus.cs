namespace VISCA.NET
{
	public enum ViscaAckStatus
	{
		Waiting = 0xFF,
		Success = 0x00,
		SyntaxError = 0x02,
		CommandBufferFull = 0x03,
		CommandCancelled = 0x04,
		NoSocket = 0x05,
		CommandNotExecutable = 0x41
	}
}