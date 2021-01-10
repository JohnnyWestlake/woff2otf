/*
  Copyright 2012, Steffen Hanikel (https://github.com/hanikesn)
  Modified by Artemy Tregubenko, 2014 (https://github.com/arty-name/woff2otf)
  Modified by Johnny Westlake, 2021 (https://github.com/JohnnyWestlake/woff2otf)

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

  A tool to convert a WOFF back to a TTF/OTF font file, in pure C#
*/


using System;
using System.IO;
using System.IO.Compression;

namespace WoffToOtf
{
    public static class Converter
    {
        /// <summary>
        /// Big-Endian version of Binary Reader
        /// </summary>
        class BEBinaryReader : BinaryReader
        {
            public BEBinaryReader(Stream stream) : base(stream) { }

            public override UInt16 ReadUInt16()
            {
                var data = base.ReadBytes(2);
                Array.Reverse(data);
                return BitConverter.ToUInt16(data, 0);
            }

            public override UInt32 ReadUInt32()
            {
                var data = base.ReadBytes(4);
                Array.Reverse(data);
                return BitConverter.ToUInt32(data, 0);
            }
        }

        /// <summary>
        /// Big-Endian version of Binary Writer
        /// </summary>
        class BEBinaryWriter : BinaryWriter
        {
            public BEBinaryWriter(Stream stream) : base(stream) { }

            public override void Write(UInt16 value)
            {
                var data = BitConverter.GetBytes(value);
                Array.Reverse(data);
                base.Write(data);
            }

            public override void Write(UInt32 value)
            {
                var data = BitConverter.GetBytes(value);
                Array.Reverse(data);
                base.Write(data);
            }
        }

        class WoffHeader
        {
            public UInt32 Signature { get; set; }
            public UInt32 Flavor { get; set; }
            public UInt32 Length { get; set; }
            public UInt16 TableCount { get; set; }
            public UInt16 Reserved { get; set; }
            public UInt32 TotalSfntSize { get; set; }
            public UInt16 MajorVersion { get; set; }
            public UInt16 MinorVersion { get; set; }
            public UInt32 MetaOffset { get; set; }
            public UInt32 MetaLength { get; set; }
            public UInt32 MetaOrignalLength { get; set; }
            public UInt32 PrivOffset { get; set; }
            public UInt32 PrivLength { get; set; }
        }

        class TableDirectory
        {
            public UInt32 Tag { get; set; }
            public UInt32 Offset { get; set; }
            public UInt32 CompressedLength { get; set; }
            public UInt32 OriginalLength { get; set; }
            public UInt32 OriginalChecksum { get; set; }
            public UInt32 OutputOffset { get; set; }
        }

        public static void Convert(
            Stream input,
            Stream output)
        {
            using var reader = new BEBinaryReader(input);
            using var writer = new BEBinaryWriter(output);

            var header = new WoffHeader
            {
                Signature = reader.ReadUInt32(),
                Flavor = reader.ReadUInt32(),
                Length = reader.ReadUInt32(),
                TableCount = reader.ReadUInt16(),
                Reserved = reader.ReadUInt16(),
                TotalSfntSize = reader.ReadUInt32(),
                MajorVersion = reader.ReadUInt16(),
                MinorVersion = reader.ReadUInt16(),
                MetaOffset = reader.ReadUInt32(),
                MetaLength = reader.ReadUInt32(),
                MetaOrignalLength = reader.ReadUInt32(),
                PrivOffset = reader.ReadUInt32(),
                PrivLength = reader.ReadUInt32()
            };

            UInt16 entrySelector = 0;
            while (Math.Pow(2, entrySelector) <= header.TableCount)
            {
                entrySelector++;
            }
            entrySelector--;

            UInt16 searchRange = (UInt16)(Math.Pow(2, entrySelector) * 16);
            UInt16 rangeShift = (UInt16)(header.TableCount * 16 - searchRange);

            // Write Font Header
            writer.Write(header.Flavor);
            writer.Write(header.TableCount);
            writer.Write(searchRange);
            writer.Write(entrySelector);
            writer.Write(rangeShift);

            UInt32 offset = (uint)output.Position;

            // Read Table Headers
            var entries = new TableDirectory[header.TableCount];
            for (var i = 0; i < header.TableCount; i++)
            {
                entries[i] = new TableDirectory
                {
                    Tag = reader.ReadUInt32(),
                    Offset = reader.ReadUInt32(),
                    CompressedLength = reader.ReadUInt32(),
                    OriginalLength = reader.ReadUInt32(),
                    OriginalChecksum = reader.ReadUInt32()
                };

                offset += (4 * 4);
            }

            // Write Table Headers
            foreach (var entry in entries)
            {
                writer.Write(entry.Tag);
                writer.Write(entry.OriginalChecksum);
                writer.Write(offset);
                writer.Write(entry.OriginalLength);
                entry.OutputOffset = offset;
                offset += entry.OriginalLength;
                if ((offset % 4) != 0)
                    offset += 4 - (offset % 4);
            }

            // Write Table contents
            foreach (var entry in entries)
            {
                input.Seek(entry.Offset, SeekOrigin.Begin);
                byte[] compressed = reader.ReadBytes((int)entry.CompressedLength);
                byte[] uncompressed;
                if (entry.CompressedLength != entry.OriginalLength)
                {
                    using var comp = new MemoryStream(compressed.AsSpan().Slice(2).ToArray()); // Ignore the ZLib header (2 bytes long)
                    using var outs = new MemoryStream();
                    using var def = new DeflateStream(comp, CompressionMode.Decompress);
                    def.CopyTo(outs);
                    uncompressed = outs.ToArray();
                }
                else
                    uncompressed = compressed;

                if (uncompressed.Length != entry.OriginalLength)
                    throw new InvalidDataException();

                output.Seek(entry.OutputOffset, SeekOrigin.Begin);
                writer.Write(uncompressed);
                offset = entry.OutputOffset + entry.OriginalLength;
                if (offset % 4 != 0)
                {
                    uint padding = 4 - (offset % 4);
                    writer.Write(new byte[padding]);
                }
            }
            
            writer.Flush();
            output.Flush();
        }
    }
}