using System;
using System.Text;

namespace VISCA.NET.Extensions
{
	public class ViscaTextEncoding
	{
		private const string CharMap =
			"ABCDEFGHIJKLMNOPQRSTUVWXYZ& ?!1234567890ÀÈÌÒÙÁÉÍÓÚÂÊÔÆŒÃÕÑÇßÄÏÖÜÅ$₣¥ℳ£¿¡ø\":'.,/-";

		// These are included because the internal symbol for them is different
		// than the one in the docs
		public const char CurrencySymbolFrancs = '₣';
		public const char CurrencySymbolDeutscheMarks = 'ℳ';

		public const byte EmptyCharacter = 0x1b;

		public static byte[] GetBytes(string str)
		{
			var bytes = new byte[str.Length];
			Array.Fill(bytes, EmptyCharacter);

			for (var i = 0; i < str.Length; i++)
			{
				var charCode = CharMap.IndexOf(str[i]);
				if (charCode == -1)
					continue;

				bytes[i] = (byte)charCode;
			}

			return bytes;
		}
	}
}