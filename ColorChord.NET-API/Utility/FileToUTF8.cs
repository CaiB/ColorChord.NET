using System.Buffers.Binary;

namespace ColorChord.NET.API.Utility;

public static class FileToUTF8
{
    // .NET doesn't yet provide a single-allocation way of converting an input file of arbitrary encoding to UTF8, hence this implementation.
    // I make no guarantees as to the correctness nor full security against bad input. Does not work on files larger than int.MaxValue bytes.
    public static ReadOnlySpan<byte> ConvertSpan(ReadOnlySpan<byte> input)
    {
        // BOM detection from https://stackoverflow.com/questions/3825390/effective-way-to-find-any-files-encoding/19283954#19283954
        if (input.Length < 4) { throw new Exception("File too short to contain BOM"); }
        
        if (input[0] == 0x2B && input[1] == 0x2F && input[2] == 0x76) { throw new Exception("The file appears to be encoded in UTF7, this format is unsupported."); }

        if (input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF) { return input.Slice(3); } // UTF8
        if (input[0] == 0xFF && input[1] == 0xFE && input[2] == 0x00 && input[3] == 0x00) { return ConvertUTF32(input.Slice(4), false); } // UTF32LE
        if (input[0] == 0x00 && input[1] == 0x00 && input[2] == 0xFE && input[3] == 0xFF) { return ConvertUTF32(input.Slice(4), true); } // UTF32BE
        if (input[0] == 0xFF && input[1] == 0xFE) { return ConvertUTF16(input.Slice(2), false); } // UTF16LE
        if (input[0] == 0xFE && input[1] == 0xFF) { return ConvertUTF16(input.Slice(2), true); } // UTF16BE

        // No BOM detected, try UTF8
        int Offset = 0;
        bool FoundInvalid = false;
        while (Offset < input.Length)
        {
            if (input[Offset] < 0x80) { Offset++; continue; }
            else if ((input[Offset] & 0xC0) == 0xC0) { FoundInvalid = true; break; }
            else if ((input[Offset] & 0xE0) == 0xC0) // 2B sequence
            {
                if ((input[Offset + 1] & 0xC0) == 0x80) { Offset += 2; continue; }
                else { FoundInvalid = true; break; }
            }
            else if ((input[Offset] & 0xF0) == 0xE0) // 3B sequence
            {
                if ((input[Offset + 1] & 0xC0) == 0x80 &&
                    (input[Offset + 2] & 0xC0) == 0x80) { Offset += 3; continue; }
                else { FoundInvalid = true; break; }
            }
            else if ((input[Offset] & 0xF8) == 0xF0) // 4B sequence
            {
                if ((input[Offset + 1] & 0xC0) == 0x80 &&
                    (input[Offset + 2] & 0xC0) == 0x80 &&
                    (input[Offset + 3] & 0xC0) == 0x80) { Offset += 4; continue; }
                else { FoundInvalid = true; break; }
            }
            else { FoundInvalid = true; break; }
        }
        if (!FoundInvalid) { return input; }

        throw new Exception("Could not determine encoding of input file");
    }

    private static ReadOnlySpan<byte> ConvertUTF32(ReadOnlySpan<byte> input, bool isBigEndian)
    {
        byte[] UTF8Data = new byte[input.Length];
        int OutputLength = 0;

        int InputOffset = 0;
        while (InputOffset + 4 <= input.Length)
        {
            // Based on https://en.wikipedia.org/wiki/UTF-8#Description
            ReadOnlySpan<byte> CharBuffer = input.Slice(InputOffset, 4);
            uint Char = isBigEndian ? BinaryPrimitives.ReadUInt32BigEndian(CharBuffer) : BinaryPrimitives.ReadUInt32LittleEndian(CharBuffer);
            if (Char < 0x80) { UTF8Data[OutputLength++] = (byte)Char; }
            else if (Char < 0x800)
            {
                UTF8Data[OutputLength++] = (byte)(0xC0 | (Char >> 6));
                UTF8Data[OutputLength++] = (byte)(0x80 | (Char & 0x3F));
            }
            else if (Char < 0x10000)
            {
                UTF8Data[OutputLength++] = (byte)(0xE0 | (Char >> 12));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((Char >> 6) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | (Char & 0x3F));
            }
            else if (Char < 0x200000)
            {
                UTF8Data[OutputLength++] = (byte)(0xF0 | (Char >> 18));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((Char >> 12) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((Char >> 6) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | (Char & 0x3F));
            }
            else { throw new Exception($"UTF-32 encoded file has invalid character at byte {InputOffset + 4}"); }
            InputOffset += 4;
        }
        return ((Span<byte>)UTF8Data).Slice(0, OutputLength);
    }

    private static Span<byte> ConvertUTF16(ReadOnlySpan<byte> input, bool isBigEndian)
    {
        byte[] UTF8Data = new byte[input.Length];
        int OutputLength = 0;

        int InputOffset = 0;
        while (InputOffset + 2 <= input.Length)
        {
            // Based on https://en.wikipedia.org/wiki/UTF-16#Description and https://en.wikipedia.org/wiki/UTF-8#Description
            ReadOnlySpan<byte> CharBuffer = input.Slice(InputOffset, 2);
            ushort Char = isBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(CharBuffer) : BinaryPrimitives.ReadUInt16LittleEndian(CharBuffer);
            if (Char < 0x80) { UTF8Data[OutputLength++] = (byte)Char; }
            else if (Char < 0x800)
            {
                UTF8Data[OutputLength++] = (byte)(0xC0 | (Char >> 6));
                UTF8Data[OutputLength++] = (byte)(0x80 | (Char & 0x3F));
            }
            else if (Char < 0xD800)
            {
                UTF8Data[OutputLength++] = (byte)(0xE0 | (Char >> 12));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((Char >> 6) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | (Char & 0x3F));
            }
            else if (Char < 0xDC00) // High surrogate
            {
                if (InputOffset + 4 > input.Length) { throw new Exception("UTF-16 encoded file terminated before required low surrogate data"); }
                CharBuffer = input.Slice(InputOffset + 2, 2);
                ushort LowSurrogate = isBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(CharBuffer) : BinaryPrimitives.ReadUInt16LittleEndian(CharBuffer);
                if (LowSurrogate < 0xDC00 || LowSurrogate > 0xDFFF) { throw new Exception($"UTF-16 encoded file has invalid low surrogate at byte {InputOffset + 2}"); }
                uint CodePoint = (uint)(((Char & 0x03FF) << 10) | (LowSurrogate & 0x03FF)) + 0x10000;
                UTF8Data[OutputLength++] = (byte)(0xF0 | (CodePoint >> 18));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((CodePoint >> 12) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((CodePoint >> 6) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | (CodePoint & 0x3F));
                InputOffset += 2;
            }
            else if (Char < 0xE000) { throw new Exception($"UTF-16 encoded file has low surrogate without preceding high surrogate at byte {InputOffset + 2}"); }
            else
            {
                UTF8Data[OutputLength++] = (byte)(0xE0 | (Char >> 12));
                UTF8Data[OutputLength++] = (byte)(0x80 | ((Char >> 6) & 0x3F));
                UTF8Data[OutputLength++] = (byte)(0x80 | (Char & 0x3F));
            }
            InputOffset += 2;
        }
        return ((Span<byte>)UTF8Data).Slice(0, OutputLength);
    }
}
