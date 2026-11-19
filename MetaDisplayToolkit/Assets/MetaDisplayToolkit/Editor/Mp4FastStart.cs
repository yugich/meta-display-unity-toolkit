using System;
using System.Collections.Generic;
using System.IO;

namespace MetaDisplay.Editor
{
    /// <summary>
    /// Moves the "moov" atom of an MP4 before "mdat" (like qt-faststart), so a player can start while downloading.
    /// Chunk offsets (stco/co64) are shifted accordingly.
    /// </summary>
    public static class Mp4FastStart
    {
        struct Atom
        {
            public string Type;
            public long Start;
            public long Size;
        }

        /// <summary>Returns true if the file was rewritten, false if it was already fast-start.</summary>
        public static bool Process(string path)
        {
            var data = File.ReadAllBytes(path);
            var atoms = ReadAtoms(data, 0, data.Length);
            int moov = atoms.FindIndex(a => a.Type == "moov");
            int mdat = atoms.FindIndex(a => a.Type == "mdat");
            if (moov < 0 || mdat < 0) throw new InvalidDataException("Not an MP4 with moov and mdat atoms: " + path);
            if (moov < mdat) return false;

            var moovAtom = atoms[moov];
            var moovBytes = new byte[moovAtom.Size];
            Array.Copy(data, moovAtom.Start, moovBytes, 0, moovAtom.Size);
            // Everything from the first mdat onwards moves forward by the size of moov.
            long moovHeader = ReadU32(moovBytes, 0) == 1 ? 16 : 8;
            PatchOffsets(moovBytes, moovHeader, moovBytes.Length, moovAtom.Size);

            using (var output = new MemoryStream(data.Length))
            {
                for (int i = 0; i < atoms.Count; i++)
                {
                    if (i == moov) continue;
                    if (i == mdat) output.Write(moovBytes, 0, moovBytes.Length);
                    output.Write(data, (int)atoms[i].Start, (int)atoms[i].Size);
                }
                File.WriteAllBytes(path, output.ToArray());
            }
            return true;
        }

        static List<Atom> ReadAtoms(byte[] data, long start, long end)
        {
            var list = new List<Atom>();
            long pos = start;
            while (pos + 8 <= end)
            {
                long size = ReadU32(data, pos);
                string type = System.Text.Encoding.ASCII.GetString(data, (int)pos + 4, 4);
                if (size == 1) size = (long)ReadU64(data, pos + 8);
                else if (size == 0) size = end - pos;
                if (size < 8 || pos + size > end) throw new InvalidDataException($"Invalid atom '{type}' at {pos}");
                list.Add(new Atom { Type = type, Start = pos, Size = size });
                pos += size;
            }
            return list;
        }

        static readonly HashSet<string> s_Containers = new HashSet<string> { "moov", "trak", "mdia", "minf", "stbl", "edts", "dinf", "mvex" };

        static void PatchOffsets(byte[] moov, long start, long end, long delta)
        {
            foreach (var atom in ReadAtoms(moov, start, end))
            {
                long header = ReadU32(moov, atom.Start) == 1 ? 16 : 8;
                if (s_Containers.Contains(atom.Type))
                {
                    PatchOffsets(moov, atom.Start + header, atom.Start + atom.Size, delta);
                }
                else if (atom.Type == "stco")
                {
                    long p = atom.Start + header + 4;
                    long count = ReadU32(moov, p);
                    p += 4;
                    for (long i = 0; i < count; i++, p += 4)
                    {
                        long value = ReadU32(moov, p) + delta;
                        if (value > uint.MaxValue) throw new InvalidDataException("Chunk offset overflow (file too large for stco).");
                        WriteU32(moov, p, (uint)value);
                    }
                }
                else if (atom.Type == "co64")
                {
                    long p = atom.Start + header + 4;
                    long count = ReadU32(moov, p);
                    p += 4;
                    for (long i = 0; i < count; i++, p += 8) WriteU64(moov, p, ReadU64(moov, p) + (ulong)delta);
                }
            }
        }

        static long ReadU32(byte[] d, long p) => ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];

        static ulong ReadU64(byte[] d, long p) => ((ulong)ReadU32(d, p) << 32) | (ulong)ReadU32(d, p + 4);

        static void WriteU32(byte[] d, long p, uint v)
        {
            d[p] = (byte)(v >> 24);
            d[p + 1] = (byte)(v >> 16);
            d[p + 2] = (byte)(v >> 8);
            d[p + 3] = (byte)v;
        }

        static void WriteU64(byte[] d, long p, ulong v)
        {
            WriteU32(d, p, (uint)(v >> 32));
            WriteU32(d, p + 4, (uint)v);
        }
    }
}
