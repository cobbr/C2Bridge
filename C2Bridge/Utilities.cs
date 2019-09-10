using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace C2Bridge
{
    public static class Utilities
    {
        public static async Task WriteStreamAsync(Stream stream, string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                byte[] size = new byte[4];
                size[0] = (byte)(dataBytes.Length >> 24);
                size[1] = (byte)(dataBytes.Length >> 16);
                size[2] = (byte)(dataBytes.Length >> 8);
                size[3] = (byte)dataBytes.Length;
                await stream.WriteAsync(size);
                int writtenBytes = 0;
                while (writtenBytes < dataBytes.Length)
                {
                    int bytesToWrite = Math.Min(dataBytes.Length - writtenBytes, 1024);
                    await stream.WriteAsync(dataBytes, writtenBytes, bytesToWrite);
                    writtenBytes += bytesToWrite;
                }
            }
        }

        public static async Task<string> ReadStreamAsync(Stream stream)
        {
            byte[] size = new byte[4];
            int totalReadBytes = 0;
            int readBytes;
            do
            {
                readBytes = await stream.ReadAsync(size, totalReadBytes, size.Length - totalReadBytes);
                if (readBytes == 0) { return null; }
                totalReadBytes += readBytes;
            } while (totalReadBytes < size.Length);
            int len = (size[0] << 24) + (size[1] << 16) + (size[2] << 8) + size[3];
            byte[] buffer = new byte[1024];
            using (var ms = new MemoryStream())
            {
                totalReadBytes = 0;
                readBytes = 0;
                do
                {
                    readBytes = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (readBytes == 0) { return null; }
                    ms.Write(buffer, 0, readBytes);
                    totalReadBytes += readBytes;
                } while (totalReadBytes < len);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static List<string> Parse(string data, string format)
        {
            format = Regex.Escape(format).Replace("\\{", "{").Replace("{{", "{").Replace("}}", "}");
            if (format.Contains("{0}")) { format = format.Replace("{0}", "(?'group0'.*)"); }
            if (format.Contains("{1}")) { format = format.Replace("{1}", "(?'group1'.*)"); }
            Match match = new Regex(format).Match(data);
            List<string> matches = new List<string>();
            if (match.Groups["group0"] != null) { matches.Add(match.Groups["group0"].Value); }
            if (match.Groups["group1"] != null) { matches.Add(match.Groups["group1"].Value); }
            return matches;
        }
    }
}
