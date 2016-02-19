// ****************************************************************************
// 
// FLV Extract
// Copyright (C) 2006-2011  J.D. Purcell (moitah@yahoo.com)
// 
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
// 
// ****************************************************************************

using System;
using System.Reflection;

namespace QiyiFLV2MP4_GUI
{
	public static class General {
		public static string Version {
			get {
				Version ver = Assembly.GetExecutingAssembly().GetName().Version;
				return ver.Major + "." + ver.Minor + "." + ver.Revision;
			}
		}

		public static void CopyBytes(byte[] dst, int dstOffset, byte[] src) {
			Buffer.BlockCopy(src, 0, dst, dstOffset, src.Length);
		}
	}

	public static class BitHelper {
		public static int Read(ref ulong x, int length) {
			int r = (int)(x >> (64 - length));
			x <<= length;
			return r;
		}

		public static int Read(byte[] bytes, ref int offset, int length) {
			int startByte = offset / 8;
			int endByte = (offset + length - 1) / 8;
			int skipBits = offset % 8;
			ulong bits = 0;
			for (int i = 0; i <= Math.Min(endByte - startByte, 7); i++) {
				bits |= (ulong)bytes[startByte + i] << (56 - (i * 8));
			}
			if (skipBits != 0) Read(ref bits, skipBits);
			offset += length;
			return Read(ref bits, length);
		}

		public static void Write(ref ulong x, int length, int value) {
			ulong mask = 0xFFFFFFFFFFFFFFFF >> (64 - length);
			x = (x << length) | ((ulong)value & mask);
		}

		public static byte[] CopyBlock(byte[] bytes, int offset, int length) {
			int startByte = offset / 8;
			int endByte = (offset + length - 1) / 8;
			int shiftA = offset % 8;
			int shiftB = 8 - shiftA;
			byte[] dst = new byte[(length + 7) / 8];
			if (shiftA == 0) {
				Buffer.BlockCopy(bytes, startByte, dst, 0, dst.Length);
			}
			else {
				int i;
				for (i = 0; i < endByte - startByte; i++) {
					dst[i] = (byte)((bytes[startByte + i] << shiftA) | (bytes[startByte + i + 1] >> shiftB));
				}
				if (i < dst.Length) {
					dst[i] = (byte)(bytes[startByte + i] << shiftA);
				}
			}
			dst[dst.Length - 1] &= (byte)(0xFF << ((dst.Length * 8) - length));
			return dst;
		}
	}

	public static class BitConverterBE {
		public static ulong ToUInt64(byte[] value, int startIndex) {
			return
				((ulong)value[startIndex    ] << 56) |
				((ulong)value[startIndex + 1] << 48) |
				((ulong)value[startIndex + 2] << 40) |
				((ulong)value[startIndex + 3] << 32) |
				((ulong)value[startIndex + 4] << 24) |
				((ulong)value[startIndex + 5] << 16) |
				((ulong)value[startIndex + 6] <<  8) |
				((ulong)value[startIndex + 7]      );
		}

		public static uint ToUInt32(byte[] value, int startIndex) {
			return
				((uint)value[startIndex    ] << 24) |
				((uint)value[startIndex + 1] << 16) |
				((uint)value[startIndex + 2] <<  8) |
				((uint)value[startIndex + 3]      );
		}

		public static ushort ToUInt16(byte[] value, int startIndex) {
			return (ushort)(
				(value[startIndex    ] <<  8) |
				(value[startIndex + 1]      ));
		}

		public static byte[] GetBytes(ulong value) {
			byte[] buff = new byte[8];
			buff[0] = (byte)(value >> 56);
			buff[1] = (byte)(value >> 48);
			buff[2] = (byte)(value >> 40);
			buff[3] = (byte)(value >> 32);
			buff[4] = (byte)(value >> 24);
			buff[5] = (byte)(value >> 16);
			buff[6] = (byte)(value >>  8);
			buff[7] = (byte)(value      );
			return buff;
		}

		public static byte[] GetBytes(uint value) {
			byte[] buff = new byte[4];
			buff[0] = (byte)(value >> 24);
			buff[1] = (byte)(value >> 16);
			buff[2] = (byte)(value >>  8);
			buff[3] = (byte)(value      );
			return buff;
		}

		public static byte[] GetBytes(ushort value) {
			byte[] buff = new byte[2];
			buff[0] = (byte)(value >>  8);
			buff[1] = (byte)(value      );
			return buff;
		}
	}

	public static class BitConverterLE {
		public static byte[] GetBytes(ulong value) {
			byte[] buff = new byte[8];
			buff[0] = (byte)(value      );
			buff[1] = (byte)(value >>  8);
			buff[2] = (byte)(value >> 16);
			buff[3] = (byte)(value >> 24);
			buff[4] = (byte)(value >> 32);
			buff[5] = (byte)(value >> 40);
			buff[6] = (byte)(value >> 48);
			buff[7] = (byte)(value >> 56);
			return buff;
		}

		public static byte[] GetBytes(uint value) {
			byte[] buff = new byte[4];
			buff[0] = (byte)(value      );
			buff[1] = (byte)(value >>  8);
			buff[2] = (byte)(value >> 16);
			buff[3] = (byte)(value >> 24);
			return buff;
		}

		public static byte[] GetBytes(ushort value) {
			byte[] buff = new byte[2];
			buff[0] = (byte)(value      );
			buff[1] = (byte)(value >>  8);
			return buff;
		}
	}

	public static class OggCRC {
		static uint[] _lut = new uint[256];

		static OggCRC() {
			for (uint i = 0; i < 256; i++) {
				uint x = i << 24;
				for (uint j = 0; j < 8; j++) {
					x = ((x & 0x80000000U) != 0) ? ((x << 1) ^ 0x04C11DB7) : (x << 1);
				}
				_lut[i] = x;
			}
		}

		public static uint Calculate(byte[] buff, int offset, int length) {
			uint crc = 0;
			for (int i = 0; i < length; i++) {
				crc = _lut[((crc >> 24) ^ buff[offset + i]) & 0xFF] ^ (crc << 8);
			}
			return crc;
		}
	}
}
