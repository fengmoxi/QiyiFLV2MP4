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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QiyiFLV2MP4
{
	interface IAudioWriter {
		void WriteChunk(byte[] chunk, uint timeStamp);
		void Finish();
		string Path { get; }
	}

	interface IVideoWriter {
		void WriteChunk(byte[] chunk, uint timeStamp, int frameType);
		void Finish(FractionUInt32 averageFrameRate);
		string Path { get; }
	}

	public delegate bool OverwriteDelegate(string destPath);

	public class FLVFile : IDisposable {
		static readonly string[] _outputExtensions = new string[] { ".avi", ".mp3", ".264", ".aac", ".spx", ".txt" };

		string _inputPath, _outputDirectory, _outputPathBase;
		OverwriteDelegate _overwrite;
		FileStream _fs;
		long _fileOffset, _fileLength;
		IAudioWriter _audioWriter;
		IVideoWriter _videoWriter;
		TimeCodeWriter _timeCodeWriter;
		List<uint> _videoTimeStamps;
		bool _extractAudio, _extractVideo, _extractTimeCodes;
		bool _extractedAudio, _extractedVideo, _extractedTimeCodes;
		FractionUInt32? _averageFrameRate, _trueFrameRate;
		List<string> _warnings;

		public FLVFile(string path) {
			_inputPath = path;
			_outputDirectory = Path.GetDirectoryName(path);
			_warnings = new List<string>();
			_fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
			_fileOffset = 0;
			_fileLength = _fs.Length;
		}

		public void Dispose() {
			if (_fs != null) {
				_fs.Close();
				_fs = null;
			}
			CloseOutput(null, true);
		}

		public void Close() {
			Dispose();
		}

		public string OutputDirectory {
			get { return _outputDirectory; }
			set { _outputDirectory = value; }
		}

		public FractionUInt32? AverageFrameRate {
			get { return _averageFrameRate; }
		}

		public FractionUInt32? TrueFrameRate {
			get { return _trueFrameRate; }
		}

		public string[] Warnings {
			get { return _warnings.ToArray(); }
		}

		public bool ExtractedAudio {
			get { return _extractedAudio; }
		}

		public bool ExtractedVideo {
			get { return _extractedVideo; }
		}

		public bool ExtractedTimeCodes {
			get { return _extractedTimeCodes; }
		}

		public void ExtractStreams(bool extractAudio, bool extractVideo, bool extractTimeCodes, OverwriteDelegate overwrite) {
			uint dataOffset, flags, prevTagSize;

			_outputPathBase = Path.Combine(_outputDirectory, Path.GetFileNameWithoutExtension(_inputPath));
			_overwrite = overwrite;
			_extractAudio = extractAudio;
			_extractVideo = extractVideo;
			_extractTimeCodes = extractTimeCodes;
			_videoTimeStamps = new List<uint>();

			Seek(0);
			if (_fileLength < 4 || ReadUInt32() != 0x464C5601) {
				if (_fileLength >= 8 && ReadUInt32() == 0x66747970) {
					throw new Exception("This is a MP4 file. Please input a FLV file!");
				}
				else {
					throw new Exception("This isn't a FLV file. Please input a FLV file!");
				}
			}

			if (Array.IndexOf(_outputExtensions, Path.GetExtension(_inputPath).ToLowerInvariant()) != -1) {
				// Can't have the same extension as files we output
				throw new Exception("Please change the extension of this FLV file.");
			}

			if (!Directory.Exists(_outputDirectory)) {
				throw new Exception("Output directory doesn't exist.");
			}

			flags = ReadUInt8();
			dataOffset = ReadUInt32();

			Seek(dataOffset);

			prevTagSize = ReadUInt32();
			while (_fileOffset < _fileLength) {
				if (!ReadTag()) break;
				if ((_fileLength - _fileOffset) < 4) break;
				prevTagSize = ReadUInt32();
			}

			_averageFrameRate = CalculateAverageFrameRate();
			_trueFrameRate = CalculateTrueFrameRate();

			CloseOutput(_averageFrameRate, false);
		}

		private void CloseOutput(FractionUInt32? averageFrameRate, bool disposing) {
			if (_videoWriter != null) {
				_videoWriter.Finish(averageFrameRate ?? new FractionUInt32(25, 1));
				if (disposing && (_videoWriter.Path != null)) {
					try { File.Delete(_videoWriter.Path); }
					catch { }
				}
				_videoWriter = null;
			}
			if (_audioWriter != null) {
				_audioWriter.Finish();
				if (disposing && (_audioWriter.Path != null)) {
					try { File.Delete(_audioWriter.Path); }
					catch { }
				}
				_audioWriter = null;
			}
			if (_timeCodeWriter != null) {
				_timeCodeWriter.Finish();
				if (disposing && (_timeCodeWriter.Path != null)) {
					try { File.Delete(_timeCodeWriter.Path); }
					catch { }
				}
				_timeCodeWriter = null;
			}
		}

		private bool ReadTag() {
			uint tagType, dataSize, timeStamp, streamID, mediaInfo;
			byte[] data;

			if ((_fileLength - _fileOffset) < 11) {
				return false;
			}

			// Read tag header
			tagType = ReadUInt8();
			dataSize = ReadUInt24();
			timeStamp = ReadUInt24();
			timeStamp |= ReadUInt8() << 24;
			streamID = ReadUInt24();

			// Read tag data
			if (dataSize == 0) {
				return true;
			}
			if ((_fileLength - _fileOffset) < dataSize) {
				return false;
			}
			mediaInfo = ReadUInt8();
			dataSize -= 1;
			data = ReadBytes((int)dataSize);

			if (tagType == 0x8) {  // Audio
				if (_audioWriter == null) {
					_audioWriter = _extractAudio ? GetAudioWriter(mediaInfo) : new DummyAudioWriter();
					_extractedAudio = !(_audioWriter is DummyAudioWriter);
				}
				_audioWriter.WriteChunk(data, timeStamp);
			}
			else if ((tagType == 0x9) && ((mediaInfo >> 4) != 5)) { // Video
				if (_videoWriter == null) {
					_videoWriter = _extractVideo ? GetVideoWriter(mediaInfo) : new DummyVideoWriter();
					_extractedVideo = !(_videoWriter is DummyVideoWriter);
				}
				if (_timeCodeWriter == null) {
					string path = _outputPathBase + ".txt";
					_timeCodeWriter = new TimeCodeWriter((_extractTimeCodes && CanWriteTo(path)) ? path : null);
					_extractedTimeCodes = _extractTimeCodes;
				}
				_videoTimeStamps.Add(timeStamp);
				_videoWriter.WriteChunk(data, timeStamp, (int)((mediaInfo & 0xF0) >> 4));
				_timeCodeWriter.Write(timeStamp);
			}

			return true;
		}

		private IAudioWriter GetAudioWriter(uint mediaInfo) {
			uint format = mediaInfo >> 4;
			uint rate = (mediaInfo >> 2) & 0x3;
			uint bits = (mediaInfo >> 1) & 0x1;
			uint chans = mediaInfo & 0x1;
			string path;

			if ((format == 2) || (format == 14)) { // MP3
				path = _outputPathBase + ".mp3";
				if (!CanWriteTo(path)) return new DummyAudioWriter();
				return new MP3Writer(path, _warnings);
			}
			else if ((format == 0) || (format == 3)) { // PCM
				int sampleRate = 0;
				switch (rate) {
					case 0: sampleRate =  5512; break;
					case 1: sampleRate = 11025; break;
					case 2: sampleRate = 22050; break;
					case 3: sampleRate = 44100; break;
				}
				path = _outputPathBase + ".wav";
				if (!CanWriteTo(path)) return new DummyAudioWriter();
				if (format == 0) {
					_warnings.Add("PCM byte order unspecified, assuming little endian.");
				}
				return new WAVWriter(path, (bits == 1) ? 16 : 8,
					(chans == 1) ? 2 : 1, sampleRate);
			}
			else if (format == 10) { // AAC
				path = _outputPathBase + ".aac";
				if (!CanWriteTo(path)) return new DummyAudioWriter();
				return new AACWriter(path);
			}
			else if (format == 11) { // Speex
				path = _outputPathBase + ".spx";
				if (!CanWriteTo(path)) return new DummyAudioWriter();
				return new SpeexWriter(path, (int)(_fileLength & 0xFFFFFFFF));
			}
			else {
				string typeStr;

				if (format == 1)
					typeStr = "ADPCM";
				else if ((format == 4) || (format == 5) || (format == 6))
					typeStr = "Nellymoser";
				else
					typeStr = "format=" + format.ToString();

				_warnings.Add("Unable to extract audio (" + typeStr + " is unsupported).");

				return new DummyAudioWriter();
			}
		}

		private IVideoWriter GetVideoWriter(uint mediaInfo) {
			uint codecID = mediaInfo & 0x0F;
			string path;

			if ((codecID == 2) || (codecID == 4) || (codecID == 5)) {
				path = _outputPathBase + ".avi";
				if (!CanWriteTo(path)) return new DummyVideoWriter();
				return new AVIWriter(path, (int)codecID, _warnings);
			}
			else if (codecID == 7) {
				path = _outputPathBase + ".264";
				if (!CanWriteTo(path)) return new DummyVideoWriter();
				return new RawH264Writer(path);
			}
			else {
				string typeStr;

				if (codecID == 3)
					typeStr = "Screen";
				else if (codecID == 6)
					typeStr = "Screen2";
				else
					typeStr = "codecID=" + codecID.ToString();

				_warnings.Add("Unable to extract video (" + typeStr + " is unsupported).");

				return new DummyVideoWriter();
			}
		}

		private bool CanWriteTo(string path) {
			if (File.Exists(path) && (_overwrite != null)) {
				return _overwrite(path);
			}
			return true;
		}

		private FractionUInt32? CalculateAverageFrameRate() {
			FractionUInt32 frameRate;
			int frameCount = _videoTimeStamps.Count;

			if (frameCount > 1) {
				frameRate.N = (uint)(frameCount - 1) * 1000;
				frameRate.D = _videoTimeStamps[frameCount - 1] - _videoTimeStamps[0];
				frameRate.Reduce();
				return frameRate;
			}
			else {
				return null;
			}
		}

		private FractionUInt32? CalculateTrueFrameRate() {
			FractionUInt32 frameRate;
			Dictionary<uint, uint> deltaCount = new Dictionary<uint, uint>();
			int i, threshold;
			uint delta, count, minDelta;

			// Calculate the distance between the timestamps, count how many times each delta appears
			for (i = 1; i < _videoTimeStamps.Count; i++) {
				int deltaS = (int)((long)_videoTimeStamps[i] - (long)_videoTimeStamps[i - 1]);

				if (deltaS <= 0) continue;
				delta = (uint)deltaS;

				if (deltaCount.ContainsKey(delta)) {
					deltaCount[delta] += 1;
				}
				else {
					deltaCount.Add(delta, 1);
				}
			}

			threshold = _videoTimeStamps.Count / 10;
			minDelta = UInt32.MaxValue;

			// Find the smallest delta that made up at least 10% of the frames (grouping in delta+1
			// because of rounding, e.g. a NTSC video will have deltas of 33 and 34 ms)
			foreach (KeyValuePair<uint, uint> deltaItem in deltaCount) {
				delta = deltaItem.Key;
				count = deltaItem.Value;

				if (deltaCount.ContainsKey(delta + 1)) {
					count += deltaCount[delta + 1];
				}

				if ((count >= threshold) && (delta < minDelta)) {
					minDelta = delta;
				}
			}

			// Calculate the frame rate based on the smallest delta, and delta+1 if present
			if (minDelta != UInt32.MaxValue) {
				uint totalTime, totalFrames;

				count = deltaCount[minDelta];
				totalTime = minDelta * count;
				totalFrames = count;

				if (deltaCount.ContainsKey(minDelta + 1)) {
					count = deltaCount[minDelta + 1];
					totalTime += (minDelta + 1) * count;
					totalFrames += count;
				}

				if (totalTime != 0) {
					frameRate.N = totalFrames * 1000;
					frameRate.D = totalTime;
					frameRate.Reduce();
					return frameRate;
				}
			}

			// Unable to calculate frame rate
			return null;
		}

		private void Seek(long offset) {
			_fs.Seek(offset, SeekOrigin.Begin);
			_fileOffset = offset;
		}

		private uint ReadUInt8() {
			_fileOffset += 1;
			return (uint)_fs.ReadByte();
		}

		private uint ReadUInt24() {
			byte[] x = new byte[4];
			_fs.Read(x, 1, 3);
			_fileOffset += 3;
			return BitConverterBE.ToUInt32(x, 0);
		}

		private uint ReadUInt32() {
			byte[] x = new byte[4];
			_fs.Read(x, 0, 4);
			_fileOffset += 4;
			return BitConverterBE.ToUInt32(x, 0);
		}

		private byte[] ReadBytes(int length) {
			byte[] buff = new byte[length];
			_fs.Read(buff, 0, length);
			_fileOffset += length;
			return buff;
		}
	}

	class DummyAudioWriter : IAudioWriter {
		public DummyAudioWriter() {
		}

		public void WriteChunk(byte[] chunk, uint timeStamp) {
		}

		public void Finish() {
		}

		public string Path {
			get {
				return null;
			}
		}
	}

	class DummyVideoWriter : IVideoWriter {
		public DummyVideoWriter() {
		}

		public void WriteChunk(byte[] chunk, uint timeStamp, int frameType) {
		}

		public void Finish(FractionUInt32 averageFrameRate) {
		}

		public string Path {
			get {
				return null;
			}
		}
	}

	class MP3Writer : IAudioWriter {
		string _path;
		FileStream _fs;
		List<string> _warnings;
		List<byte[]> _chunkBuffer;
		List<uint> _frameOffsets;
		uint _totalFrameLength;
		bool _isVBR;
		bool _delayWrite;
		bool _hasVBRHeader;
		bool _writeVBRHeader;
		int _firstBitRate;
		int _mpegVersion;
		int _sampleRate;
		int _channelMode;
		uint _firstFrameHeader;

		public MP3Writer(string path, List<string> warnings) {
			_path = path;
			_fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);
			_warnings = warnings;
			_chunkBuffer = new List<byte[]>();
			_frameOffsets = new List<uint>();
			_delayWrite = true;
		}

		public void WriteChunk(byte[] chunk, uint timeStamp) {
			_chunkBuffer.Add(chunk);
			ParseMP3Frames(chunk);
			if (_delayWrite && _totalFrameLength >= 65536) {
				_delayWrite = false;
			}
			if (!_delayWrite) {
				Flush();
			}
		}

		public void Finish() {
			Flush();
			if (_writeVBRHeader) {
				_fs.Seek(0, SeekOrigin.Begin);
				WriteVBRHeader(false);
			}
			_fs.Close();
		}

		public string Path {
			get {
				return _path;
			}
		}

		private void Flush() {
			foreach (byte[] chunk in _chunkBuffer) {
				_fs.Write(chunk, 0, chunk.Length);
			}
			_chunkBuffer.Clear();
		}

		private void ParseMP3Frames(byte[] buff) {
			int[] MPEG1BitRate = new int[] { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
			int[] MPEG2XBitRate = new int[] { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
			int[] MPEG1SampleRate = new int[] { 44100, 48000, 32000, 0 };
			int[] MPEG20SampleRate = new int[] { 22050, 24000, 16000, 0 };
			int[] MPEG25SampleRate = new int[] { 11025, 12000, 8000, 0 };

			int offset = 0;
			int length = buff.Length;

			while (length >= 4) {
				ulong header;
				int mpegVersion, layer, bitRate, sampleRate, padding, channelMode;
				int frameLen;

				header = (ulong)BitConverterBE.ToUInt32(buff, offset) << 32;
				if (BitHelper.Read(ref header, 11) != 0x7FF) {
					break;
				}
				mpegVersion = BitHelper.Read(ref header, 2);
				layer = BitHelper.Read(ref header, 2);
				BitHelper.Read(ref header, 1);
				bitRate = BitHelper.Read(ref header, 4);
				sampleRate = BitHelper.Read(ref header, 2);
				padding = BitHelper.Read(ref header, 1);
				BitHelper.Read(ref header, 1);
				channelMode = BitHelper.Read(ref header, 2);

				if ((mpegVersion == 1) || (layer != 1) || (bitRate == 0) || (bitRate == 15) || (sampleRate == 3)) {
					break;
				}

				bitRate = ((mpegVersion == 3) ? MPEG1BitRate[bitRate] : MPEG2XBitRate[bitRate]) * 1000;

				if (mpegVersion == 3)
					sampleRate = MPEG1SampleRate[sampleRate];
				else if (mpegVersion == 2)
					sampleRate = MPEG20SampleRate[sampleRate];
				else
					sampleRate = MPEG25SampleRate[sampleRate];

				frameLen = GetFrameLength(mpegVersion, bitRate, sampleRate, padding);
				if (frameLen > length) {
					break;
				}

				bool isVBRHeaderFrame = false;
				if (_frameOffsets.Count == 0) {
					// Check for an existing VBR header just to be safe (I haven't seen any in FLVs)
					int o = offset + GetFrameDataOffset(mpegVersion, channelMode);
					if (BitConverterBE.ToUInt32(buff, o) == 0x58696E67) { // "Xing"
						isVBRHeaderFrame = true;
						_delayWrite = false;
						_hasVBRHeader = true;
					}
				}

				if (isVBRHeaderFrame) { }
				else if (_firstBitRate == 0) {
					_firstBitRate = bitRate;
					_mpegVersion = mpegVersion;
					_sampleRate = sampleRate;
					_channelMode = channelMode;
					_firstFrameHeader = BitConverterBE.ToUInt32(buff, offset);
				}
				else if (!_isVBR && (bitRate != _firstBitRate)) {
					_isVBR = true;
					if (_hasVBRHeader) { }
					else if (_delayWrite) {
						WriteVBRHeader(true);
						_writeVBRHeader = true;
						_delayWrite = false;
					}
					else {
						_warnings.Add("Detected VBR too late, cannot add VBR header.");
					}
				}

				_frameOffsets.Add(_totalFrameLength + (uint)offset);

				offset += frameLen;
				length -= frameLen;
			}

			_totalFrameLength += (uint)buff.Length;
		}

		private void WriteVBRHeader(bool isPlaceholder) {
			byte[] buff = new byte[GetFrameLength(_mpegVersion, 64000, _sampleRate, 0)];
			if (!isPlaceholder) {
				uint header = _firstFrameHeader;
				int dataOffset = GetFrameDataOffset(_mpegVersion, _channelMode);
				header &= 0xFFFF0DFF; // Clear bitrate and padding fields
				header |= 0x00010000; // Set protection bit (indicates that CRC is NOT present)
				header |= (uint)((_mpegVersion == 3) ? 5 : 8) << 12; // 64 kbit/sec
				General.CopyBytes(buff, 0, BitConverterBE.GetBytes(header));
				General.CopyBytes(buff, dataOffset, BitConverterBE.GetBytes((uint)0x58696E67)); // "Xing"
				General.CopyBytes(buff, dataOffset + 4, BitConverterBE.GetBytes((uint)0x7)); // Flags
				General.CopyBytes(buff, dataOffset + 8, BitConverterBE.GetBytes((uint)_frameOffsets.Count)); // Frame count
				General.CopyBytes(buff, dataOffset + 12, BitConverterBE.GetBytes((uint)_totalFrameLength)); // File length
				for (int i = 0; i < 100; i++) {
					int frameIndex = (int)((i / 100.0) * _frameOffsets.Count);
					buff[dataOffset + 16 + i] = (byte)((_frameOffsets[frameIndex] / (double)_totalFrameLength) * 256.0);
				}
			}
			_fs.Write(buff, 0, buff.Length);
		}

		private int GetFrameLength(int mpegVersion, int bitRate, int sampleRate, int padding) {
			return ((mpegVersion == 3) ? 144 : 72) * bitRate / sampleRate + padding;
		}

		private int GetFrameDataOffset(int mpegVersion, int channelMode) {
			return 4 + ((mpegVersion == 3) ?
				((channelMode == 3) ? 17 : 32) :
				((channelMode == 3) ?  9 : 17));
		}
	}

	class SpeexWriter : IAudioWriter {
		const string _vendorString = "FLV Extract";
		const uint _sampleRate = 16000;
		const uint _msPerFrame = 20;
		const uint _samplesPerFrame = _sampleRate / (1000 / _msPerFrame);
		const int _targetPageDataSize = 4096;

		string _path;
		FileStream _fs;
		int _serialNumber;
		List<OggPacket> _packetList;
		int _packetListDataSize;
		byte[] _pageBuff;
		int _pageBuffOffset;
		uint _pageSequenceNumber;
		ulong _granulePosition;

		public SpeexWriter(string path, int serialNumber) {
			_path = path;
			_serialNumber = serialNumber;
			_fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);
			_fs.Seek((28 + 80) + (28 + 8 + _vendorString.Length), SeekOrigin.Begin); // Speex header + Vorbis comment
			_packetList = new List<OggPacket>();
			_packetListDataSize = 0;
			_pageBuff = new byte[27 + 255 + _targetPageDataSize + 254]; // Header + max segment table + target data size + extra segment
			_pageBuffOffset = 0;
			_pageSequenceNumber = 2; // First audio packet
			_granulePosition = 0;
		}

		public void WriteChunk(byte[] chunk, uint timeStamp) {
			int[] subModeSizes = new int[] { 0, 43, 119, 160, 220, 300, 364, 492, 79 };
			int[] wideBandSizes = new int[] { 0, 36, 112, 192, 352 };
			int[] inBandSignalSizes = new int[] { 1, 1, 4, 4, 4, 4, 4, 4, 8, 8, 16, 16, 32, 32, 64, 64 };
			int frameStart = -1;
			int frameEnd = 0;
			int offset = 0;
			int length = chunk.Length * 8;
			int x;

			while (length - offset >= 5) {
				x = BitHelper.Read(chunk, ref offset, 1);
				if (x != 0) {
					// wideband frame
					x = BitHelper.Read(chunk, ref offset, 3);
					if (x < 1 || x > 4) goto Error;
					offset += wideBandSizes[x] - 4;
				}
				else {
					x = BitHelper.Read(chunk, ref offset, 4);
					if (x >= 1 && x <= 8) {
						// narrowband frame
						if (frameStart != -1) {
							WriteFramePacket(chunk, frameStart, frameEnd);
						}
						frameStart = frameEnd;
						offset += subModeSizes[x] - 5;
					}
					else if (x == 15) {
						// terminator
						break;
					}
					else if (x == 14) {
						// in-band signal
						if (length - offset < 4) goto Error;
						x = BitHelper.Read(chunk, ref offset, 4);
						offset += inBandSignalSizes[x];
					}
					else if (x == 13) {
						// custom in-band signal
						if (length - offset < 5) goto Error;
						x = BitHelper.Read(chunk, ref offset, 5);
						offset += x * 8;
					}
					else goto Error;
				}
				frameEnd = offset;
			}
			if (offset > length) goto Error;

			if (frameStart != -1) {
				WriteFramePacket(chunk, frameStart, frameEnd);
			}

			return;

		Error:
			throw new Exception("Invalid Speex data.");
		}

		public void Finish() {
			WritePage();
			FlushPage(true);
			_fs.Seek(0, SeekOrigin.Begin);
			_pageSequenceNumber = 0;
			_granulePosition = 0;
			WriteSpeexHeaderPacket();
			WriteVorbisCommentPacket();
			FlushPage(false);
			_fs.Close();
		}

		public string Path {
			get {
				return _path;
			}
		}

		private void WriteFramePacket(byte[] data, int startBit, int endBit) {
			int lengthBits = endBit - startBit;
			byte[] frame = BitHelper.CopyBlock(data, startBit, lengthBits);
			if (lengthBits % 8 != 0) {
				frame[frame.Length - 1] |= (byte)(0xFF >> ((lengthBits % 8) + 1)); // padding
			}
			AddPacket(frame, _samplesPerFrame, true);
		}

		private void WriteSpeexHeaderPacket() {
			byte[] data = new byte[80];
			General.CopyBytes(data, 0, Encoding.ASCII.GetBytes("Speex   ")); // speex_string
			General.CopyBytes(data, 8, Encoding.ASCII.GetBytes("unknown")); // speex_version
			data[28] = 1; // speex_version_id
			data[32] = 80; // header_size
			General.CopyBytes(data, 36, BitConverterLE.GetBytes((uint)_sampleRate)); // rate
			data[40] = 1; // mode (e.g. narrowband, wideband)
			data[44] = 4; // mode_bitstream_version
			data[48] = 1; // nb_channels
			General.CopyBytes(data, 52, BitConverterLE.GetBytes(unchecked((uint)-1))); // bitrate
			General.CopyBytes(data, 56, BitConverterLE.GetBytes((uint)_samplesPerFrame)); // frame_size
			data[60] = 0; // vbr
			data[64] = 1; // frames_per_packet
			AddPacket(data, 0, false);
		}

		private void WriteVorbisCommentPacket() {
			byte[] vendorStringBytes = Encoding.ASCII.GetBytes(_vendorString);
			byte[] data = new byte[8 + vendorStringBytes.Length];
			data[0] = (byte)vendorStringBytes.Length;
			General.CopyBytes(data, 4, vendorStringBytes);
			AddPacket(data, 0, false);
		}

		private void AddPacket(byte[] data, uint sampleLength, bool delayWrite) {
			OggPacket packet = new OggPacket();
			if (data.Length >= 255) {
				throw new Exception("Packet exceeds maximum size.");
			}
			_granulePosition += sampleLength;
			packet.Data = data;
			packet.GranulePosition = _granulePosition;
			_packetList.Add(packet);
			_packetListDataSize += data.Length;
			if (!delayWrite || (_packetListDataSize >= _targetPageDataSize) || (_packetList.Count == 255)) {
				WritePage();
			}
		}

		private void WritePage() {
			if (_packetList.Count == 0) return;
			FlushPage(false);
			WriteToPage(BitConverterBE.GetBytes(0x4F676753U), 0, 4); // "OggS"
			WriteToPage((byte)0); // Stream structure version
			WriteToPage((byte)((_pageSequenceNumber == 0) ? 0x02 : 0)); // Page flags
			WriteToPage((ulong)_packetList[_packetList.Count - 1].GranulePosition); // Position in samples
			WriteToPage((uint)_serialNumber); // Stream serial number
			WriteToPage((uint)_pageSequenceNumber); // Page sequence number
			WriteToPage((uint)0); // Checksum
			WriteToPage((byte)_packetList.Count); // Page segment count
			foreach (OggPacket packet in _packetList) {
				WriteToPage((byte)packet.Data.Length);
			}
			foreach (OggPacket packet in _packetList) {
				WriteToPage(packet.Data, 0, packet.Data.Length);
			}
			_packetList.Clear();
			_packetListDataSize = 0;
			_pageSequenceNumber++;
		}

		private void FlushPage(bool isLastPage) {
			if (_pageBuffOffset == 0) return;
			if (isLastPage) _pageBuff[5] |= 0x04;
			uint crc = OggCRC.Calculate(_pageBuff, 0, _pageBuffOffset);
			General.CopyBytes(_pageBuff, 22, BitConverterLE.GetBytes(crc));
			_fs.Write(_pageBuff, 0, _pageBuffOffset);
			_pageBuffOffset = 0;
		}

		private void WriteToPage(byte[] data, int offset, int length) {
			Buffer.BlockCopy(data, offset, _pageBuff, _pageBuffOffset, length);
			_pageBuffOffset += length;
		}

		private void WriteToPage(byte data) {
			WriteToPage(new byte[] { data }, 0, 1);
		}

		private void WriteToPage(uint data) {
			WriteToPage(BitConverterLE.GetBytes(data), 0, 4);
		}

		private void WriteToPage(ulong data) {
			WriteToPage(BitConverterLE.GetBytes(data), 0, 8);
		}

		class OggPacket {
			public ulong GranulePosition;
			public byte[] Data;
		}
	}

	class AACWriter : IAudioWriter {
		string _path;
		FileStream _fs;
		int _aacProfile;
		int _sampleRateIndex;
		int _channelConfig;

		public AACWriter(string path) {
			_path = path;
			_fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);
		}

		public void WriteChunk(byte[] chunk, uint timeStamp) {
			if (chunk.Length < 1) return;

			if (chunk[0] == 0) { // Header
				if (chunk.Length < 3) return;

				ulong bits = (ulong)BitConverterBE.ToUInt16(chunk, 1) << 48;

				_aacProfile = BitHelper.Read(ref bits, 5) - 1;
				_sampleRateIndex = BitHelper.Read(ref bits, 4);
				_channelConfig = BitHelper.Read(ref bits, 4);

				if ((_aacProfile < 0) || (_aacProfile > 3))
					throw new Exception("Unsupported AAC profile.");
				if (_sampleRateIndex > 12)
					throw new Exception("Invalid AAC sample rate index.");
				if (_channelConfig > 6)
					throw new Exception("Invalid AAC channel configuration.");
			}
			else { // Audio data
				int dataSize = chunk.Length - 1;
				ulong bits = 0;

				// Reference: WriteADTSHeader from FAAC's bitstream.c

				BitHelper.Write(ref bits, 12, 0xFFF);
				BitHelper.Write(ref bits,  1, 0);
				BitHelper.Write(ref bits,  2, 0);
				BitHelper.Write(ref bits,  1, 1);
				BitHelper.Write(ref bits,  2, _aacProfile);
				BitHelper.Write(ref bits,  4, _sampleRateIndex);
				BitHelper.Write(ref bits,  1, 0);
				BitHelper.Write(ref bits,  3, _channelConfig);
				BitHelper.Write(ref bits,  1, 0);
				BitHelper.Write(ref bits,  1, 0);
				BitHelper.Write(ref bits,  1, 0);
				BitHelper.Write(ref bits,  1, 0);
				BitHelper.Write(ref bits, 13, 7 + dataSize);
				BitHelper.Write(ref bits, 11, 0x7FF);
				BitHelper.Write(ref bits,  2, 0);

				_fs.Write(BitConverterBE.GetBytes(bits), 1, 7);
				_fs.Write(chunk, 1, dataSize);
			}
		}

		public void Finish() {
			_fs.Close();
		}

		public string Path {
			get {
				return _path;
			}
		}
	}

	class RawH264Writer : IVideoWriter {
		static readonly byte[] _startCode = new byte[] { 0, 0, 0, 1 };

		string _path;
		FileStream _fs;
		int _nalLengthSize;

		public RawH264Writer(string path) {
			_path = path;
			_fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 65536);
		}

		public void WriteChunk(byte[] chunk, uint timeStamp, int frameType) {
			if (chunk.Length < 4) return;

			// Reference: decode_frame from libavcodec's h264.c

			if (chunk[0] == 0) { // Headers
				if (chunk.Length < 10) return;

				int offset, spsCount, ppsCount;

				offset = 8;
				_nalLengthSize = (chunk[offset++] & 0x03) + 1;
				spsCount = chunk[offset++] & 0x1F;
				ppsCount = -1;

				while (offset <= chunk.Length - 2) {
					if ((spsCount == 0) && (ppsCount == -1)) {
						ppsCount = chunk[offset++];
						continue;
					}

					if (spsCount > 0) spsCount--;
					else if (ppsCount > 0) ppsCount--;
					else break;

					int len = (int)BitConverterBE.ToUInt16(chunk, offset);
					offset += 2;
					if (offset + len > chunk.Length) break;
					_fs.Write(_startCode, 0, _startCode.Length);
					_fs.Write(chunk, offset, len);
					offset += len;
				}
			}
			else { // Video data
				int offset = 4;

				if (_nalLengthSize != 2) {
					_nalLengthSize = 4;
				}

				while (offset <= chunk.Length - _nalLengthSize) {
					int len = (_nalLengthSize == 2) ?
						(int)BitConverterBE.ToUInt16(chunk, offset) :
						(int)BitConverterBE.ToUInt32(chunk, offset);
					offset += _nalLengthSize;
					if (offset + len > chunk.Length) break;
					_fs.Write(_startCode, 0, _startCode.Length);
					_fs.Write(chunk, offset, len);
					offset += len;
				}
			}
		}

		public void Finish(FractionUInt32 averageFrameRate) {
			_fs.Close();
		}

		public string Path {
			get {
				return _path;
			}
		}
	}

	class WAVWriter : IAudioWriter {
		string _path;
		WAVTools.WAVWriter _wr;
		int blockAlign;

		public WAVWriter(string path, int bitsPerSample, int channelCount, int sampleRate) {
			_path = path;
			_wr = new WAVTools.WAVWriter(path, bitsPerSample, channelCount, sampleRate);
			blockAlign = (bitsPerSample / 8) * channelCount;
		}

		public void WriteChunk(byte[] chunk, uint timeStamp) {
			_wr.Write(chunk, chunk.Length / blockAlign);
		}

		public void Finish() {
			_wr.Close();
		}

		public string Path {
			get {
				return _path;
			}
		}
	}

	class AVIWriter : IVideoWriter {
		string _path;
		BinaryWriter _bw;
		int _codecID;
		int _width, _height, _frameCount;
		uint _moviDataSize, _indexChunkSize;
		List<uint> _index;
		bool _isAlphaWriter;
		AVIWriter _alphaWriter;
		List<string> _warnings;

		// Chunk:          Off:  Len:
		//
		// RIFF AVI          0    12
		//   LIST hdrl      12    12
		//     avih         24    64
		//     LIST strl    88    12
		//       strh      100    64
		//       strf      164    48
		//   LIST movi     212    12
		//     (frames)    224   ???
		//   idx1          ???   ???

		public AVIWriter(string path, int codecID, List<string> warnings) :
			this(path, codecID, warnings, false) { }

		private AVIWriter(string path, int codecID, List<string> warnings, bool isAlphaWriter) {
			if ((codecID != 2) && (codecID != 4) && (codecID != 5)) {
				throw new Exception("Unsupported video codec.");
			}

			FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);

			_path = path;
			_bw = new BinaryWriter(fs);
			_codecID = codecID;
			_warnings = warnings;
			_isAlphaWriter = isAlphaWriter;

			if ((codecID == 5) && !_isAlphaWriter) {
				_alphaWriter = new AVIWriter(path.Substring(0, path.Length - 4) + ".alpha.avi", codecID, warnings, true);
			}

			WriteFourCC("RIFF");
			_bw.Write((uint)0); // chunk size
			WriteFourCC("AVI ");

			WriteFourCC("LIST");
			_bw.Write((uint)192);
			WriteFourCC("hdrl");

			WriteFourCC("avih");
			_bw.Write((uint)56);
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0x10);
			_bw.Write((uint)0); // frame count
			_bw.Write((uint)0);
			_bw.Write((uint)1);
			_bw.Write((uint)0);
			_bw.Write((uint)0); // width
			_bw.Write((uint)0); // height
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0);

			WriteFourCC("LIST");
			_bw.Write((uint)116);
			WriteFourCC("strl");

			WriteFourCC("strh");
			_bw.Write((uint)56);
			WriteFourCC("vids");
			WriteFourCC(CodecFourCC);
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0); // frame rate denominator
			_bw.Write((uint)0); // frame rate numerator
			_bw.Write((uint)0);
			_bw.Write((uint)0); // frame count
			_bw.Write((uint)0);
			_bw.Write((int)-1);
			_bw.Write((uint)0);
			_bw.Write((ushort)0);
			_bw.Write((ushort)0);
			_bw.Write((ushort)0); // width
			_bw.Write((ushort)0); // height

			WriteFourCC("strf");
			_bw.Write((uint)40);
			_bw.Write((uint)40);
			_bw.Write((uint)0); // width
			_bw.Write((uint)0); // height
			_bw.Write((ushort)1);
			_bw.Write((ushort)24);
			WriteFourCC(CodecFourCC);
			_bw.Write((uint)0); // biSizeImage
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0);
			_bw.Write((uint)0);

			WriteFourCC("LIST");
			_bw.Write((uint)0); // chunk size
			WriteFourCC("movi");

			_index = new List<uint>();
		}

		public void WriteChunk(byte[] chunk, uint timeStamp, int frameType) {
			int offset, len;

			offset = 0;
			len = chunk.Length;
			if (_codecID == 4) {
				offset = 1;
				len -= 1;
			}
			if (_codecID == 5) {
				offset = 4;
				if (len >= 4) {
					int alphaOffset = (int)BitConverterBE.ToUInt32(chunk, 0) & 0xFFFFFF;
					if (!_isAlphaWriter) {
						len = alphaOffset;
					}
					else {
						offset += alphaOffset;
						len -= offset;
					}
				}
				else {
					len = 0;
				}
			}
			len = Math.Max(len, 0);
			len = Math.Min(len, chunk.Length - offset);

			_index.Add((frameType == 1) ? (uint)0x10 : (uint)0);
			_index.Add(_moviDataSize + 4);
			_index.Add((uint)len);

			if ((_width == 0) && (_height == 0)) {
				GetFrameSize(chunk);
			}

			WriteFourCC("00dc");
			_bw.Write(len);
			_bw.Write(chunk, offset, len);

			if ((len % 2) != 0) {
				_bw.Write((byte)0);
				len++;
			}
			_moviDataSize += (uint)len + 8;
			_frameCount++;

			if (_alphaWriter != null) {
				_alphaWriter.WriteChunk(chunk, timeStamp, frameType);
			}
		}

		private void GetFrameSize(byte[] chunk) {
			if (_codecID == 2) {
				// Reference: flv_h263_decode_picture_header from libavcodec's h263.c

				if (chunk.Length < 10) return;

				if ((chunk[0] != 0) || (chunk[1] != 0)) {
					return;
				}

				ulong x = BitConverterBE.ToUInt64(chunk, 2);
				int format;

				if (BitHelper.Read(ref x, 1) != 1) {
					return;
				}
				BitHelper.Read(ref x, 5);
				BitHelper.Read(ref x, 8);

				format = BitHelper.Read(ref x, 3);
				switch (format) {
					case 0:
						_width = BitHelper.Read(ref x, 8);
						_height = BitHelper.Read(ref x, 8);
						break;
					case 1:
						_width = BitHelper.Read(ref x, 16);
						_height = BitHelper.Read(ref x, 16);
						break;
					case 2:
						_width = 352;
						_height = 288;
						break;
					case 3:
						_width = 176;
						_height = 144;
						break;
					case 4:
						_width = 128;
						_height = 96;
						break;
					case 5:
						_width = 320;
						_height = 240;
						break;
					case 6:
						_width = 160;
						_height = 120;
						break;
					default:
						return;
				}
			}
			else if ((_codecID == 4) || (_codecID == 5)) {
				// Reference: vp6_parse_header from libavcodec's vp6.c

				int skip = (_codecID == 4) ? 1 : 4;
				if (chunk.Length < (skip + 8)) return;
				ulong x = BitConverterBE.ToUInt64(chunk, skip);

				int deltaFrameFlag = BitHelper.Read(ref x, 1);
				int quant = BitHelper.Read(ref x, 6);
				int separatedCoeffFlag = BitHelper.Read(ref x, 1);
				int subVersion = BitHelper.Read(ref x, 5);
				int filterHeader = BitHelper.Read(ref x, 2);
				int interlacedFlag = BitHelper.Read(ref x, 1);

				if (deltaFrameFlag != 0) {
					return;
				}
				if ((separatedCoeffFlag != 0) || (filterHeader == 0)) {
					BitHelper.Read(ref x, 16);
				}

				_height = BitHelper.Read(ref x, 8) * 16;
				_width = BitHelper.Read(ref x, 8) * 16;

				// chunk[0] contains the width and height (4 bits each, respectively) that should
				// be cropped off during playback, which will be non-zero if the encoder padded
				// the frames to a macroblock boundary.  But if you use this adjusted size in the
				// AVI header, DirectShow seems to ignore it, and it can cause stride or chroma
				// alignment problems with VFW if the width/height aren't multiples of 4.
				if (!_isAlphaWriter) {
					int cropX = chunk[0] >> 4;
					int cropY = chunk[0] & 0x0F;
					if (((cropX != 0) || (cropY != 0)) && !_isAlphaWriter) {
						_warnings.Add(String.Format("Suggested cropping: {0} pixels from right, {1} pixels from bottom.", cropX, cropY));
					}
				}
			}
		}

		private string CodecFourCC {
			get {
				if (_codecID == 2) {
					return "FLV1";
				}
				if ((_codecID == 4) || (_codecID == 5)) {
					return "VP6F";
				}
				return "NULL";
			}
		}

		private void WriteIndexChunk() {
			uint indexDataSize = (uint)_frameCount * 16;

			WriteFourCC("idx1");
			_bw.Write(indexDataSize);

			for (int i = 0; i < _frameCount; i++) {
				WriteFourCC("00dc");
				_bw.Write(_index[(i * 3) + 0]);
				_bw.Write(_index[(i * 3) + 1]);
				_bw.Write(_index[(i * 3) + 2]);
			}

			_indexChunkSize = indexDataSize + 8;
		}

		public void Finish(FractionUInt32 averageFrameRate) {
			WriteIndexChunk();

			_bw.BaseStream.Seek(4, SeekOrigin.Begin);
			_bw.Write((uint)(224 + _moviDataSize + _indexChunkSize - 8));

			_bw.BaseStream.Seek(24 + 8, SeekOrigin.Begin);
			_bw.Write((uint)0);
			_bw.BaseStream.Seek(12, SeekOrigin.Current);
			_bw.Write((uint)_frameCount);
			_bw.BaseStream.Seek(12, SeekOrigin.Current);
			_bw.Write((uint)_width);
			_bw.Write((uint)_height);

			_bw.BaseStream.Seek(100 + 28, SeekOrigin.Begin);
			_bw.Write((uint)averageFrameRate.D);
			_bw.Write((uint)averageFrameRate.N);
			_bw.BaseStream.Seek(4, SeekOrigin.Current);
			_bw.Write((uint)_frameCount);
			_bw.BaseStream.Seek(16, SeekOrigin.Current);
			_bw.Write((ushort)_width);
			_bw.Write((ushort)_height);

			_bw.BaseStream.Seek(164 + 12, SeekOrigin.Begin);
			_bw.Write((uint)_width);
			_bw.Write((uint)_height);
			_bw.BaseStream.Seek(8, SeekOrigin.Current);
			_bw.Write((uint)(_width * _height * 6));

			_bw.BaseStream.Seek(212 + 4, SeekOrigin.Begin);
			_bw.Write((uint)(_moviDataSize + 4));

			_bw.Close();

			if (_alphaWriter != null) {
				_alphaWriter.Finish(averageFrameRate);
			}
		}

		private void WriteFourCC(string fourCC) {
			byte[] bytes = System.Text.Encoding.ASCII.GetBytes(fourCC);
			if (bytes.Length != 4) {
				throw new Exception("Invalid FourCC length.");
			}
			_bw.Write(bytes);
		}

		public string Path {
			get {
				return _path;
			}
		}
	}

	class TimeCodeWriter {
		string _path;
		StreamWriter _sw;

		public TimeCodeWriter(string path) {
			_path = path;
			if (path != null) {
				_sw = new StreamWriter(path, false, Encoding.ASCII);
				_sw.WriteLine("# timecode format v2");
			}
		}

		public void Write(uint timeStamp) {
			if (_sw != null) {
				_sw.WriteLine(timeStamp);
			}
		}

		public void Finish() {
			if (_sw != null) {
				_sw.Close();
				_sw = null;
			}
		}

		public string Path {
			get {
				return _path;
			}
		}
	}

	public struct FractionUInt32 {
		public uint N;
		public uint D;

		public FractionUInt32(uint n, uint d) {
			N = n;
			D = d;
		}

		public double ToDouble() {
			return (double)N / (double)D;
		}

		public void Reduce() {
			uint gcd = GCD(N, D);
			N /= gcd;
			D /= gcd;
		}

		private uint GCD(uint a, uint b) {
			uint r;

			while (b != 0) {
				r = a % b;
				a = b;
				b = r;
			}

			return a;
		}

		public override string ToString() {
			return ToString(true);
		}

		public string ToString(bool full) {
			if (full) {
				return ToDouble().ToString() + " (" + N.ToString() + "/" + D.ToString() + ")";
			}
			else {
				return ToDouble().ToString("0.####");
			}
		}
	}
}
