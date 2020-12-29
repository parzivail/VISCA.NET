namespace VISCA.NET.Opcode
{
	public enum ViscaCommandPanTilterOpcode : byte
	{
		Drive = 0x01,
		AbsolutePosition = 0x02,
		RelativePosition = 0x03,
		Home = 0x04,
		Reset = 0x05,
		Limit = 0x07
	}
}