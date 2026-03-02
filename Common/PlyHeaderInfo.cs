using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Gsplat.Common
{
    public class PlyHeaderInfo
    {
        public uint VertexCount = 0;
        public int PropertyCount = 0;
        public int SHPropertyCount = 0;
        public int PositionOffset = -1;
        public int ColorOffset = -1;
        public int SHOffset = -1;
        public int OpacityOffset = -1;
        public int ScaleOffset = -1;
        public int RotationOffset = -1;


        /// <summary>
        /// Read each line, used for header reading.
        /// </summary>
        /// <param name="fs"></param>
        /// <returns></returns>
        private static string ReadLine(FileStream fs)
        {
            List<byte> byteBuffer = new List<byte>();
            while (true)
            {
                int b = fs.ReadByte();
                if (b == -1 || b == '\n') break;
                byteBuffer.Add((byte)b);
            }

            // If line had CRLF line endings, remove the CR part
            if (byteBuffer.Count > 0 && byteBuffer.Last() == '\r')
            {
                byteBuffer.RemoveAt(byteBuffer.Count - 1);
            }

            return Encoding.UTF8.GetString(byteBuffer.ToArray());
        }

        public static void ReadPlyHeader(FileStream fs, out uint vertexCount, out int propertyCount)
        {
            vertexCount = 0;
            propertyCount = 0;

            string line;
            while ((line = ReadLine(fs)) != null && line != "end_header")
            {
                string[] tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    vertexCount = uint.Parse(tokens[2]);
                if (tokens.Length == 3 && tokens[0] == "property")
                    propertyCount++;
            }
        }

        public static PlyHeaderInfo ReadPlyHeader(FileStream fs)
        {
            var info = new PlyHeaderInfo();

            while (ReadLine(fs) is { } line && line != "end_header")
            {
                var tokens = line.Split(' ');
                if (tokens.Length == 3 && tokens[0] == "element" && tokens[1] == "vertex")
                    info.VertexCount = uint.Parse(tokens[2]);
                if (tokens.Length != 3 || tokens[0] != "property") continue;
                switch (tokens[2])
                {
                    case "x":
                        info.PositionOffset = info.PropertyCount;
                        break;
                    case "f_dc_0":
                        info.ColorOffset = info.PropertyCount;
                        break;
                    case "f_rest_0":
                        info.SHOffset = info.PropertyCount;
                        break;
                    case "opacity":
                        info.OpacityOffset = info.PropertyCount;
                        break;
                    case "scale_0":
                        info.ScaleOffset = info.PropertyCount;
                        break;
                    case "rot_0":
                        info.RotationOffset = info.PropertyCount;
                        break;
                }

                if (tokens[2].StartsWith("f_rest_"))
                    info.SHPropertyCount++;
                info.PropertyCount++;
            }

            return info;
        }
    }
}
