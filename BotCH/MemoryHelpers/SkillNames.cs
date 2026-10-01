using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace BotCH.MemoryHelpers
{
    /*
     * Названия скиллов из файла игры: element\configs.pck → configs\skillstr.txt (UTF-16).
     * В skillstr.txt строки вида  2990  "Жалящий рой": ключ ID*10 — название, ID*10+1 — описание.
     *
     * Формат архива .pck (Angelica File Package, версия 0x00020002):
     *   хвост файла (0x118 байт): 0xFDFDFEEE, версия, (смещение оглавления ^ KEY1), 0, текст описания 0xFC байт, 0xF00DBEEF, число файлов, версия.
     *   оглавление: для каждого файла (размер записи ^ KEY1), (размер записи ^ KEY2), запись — 0x114 байт (если размер меньше, она сжата zlib):
     *   путь 260 байт (GBK), смещение данных, размер, сжатый размер. Данные файла сжаты zlib, если сжатый размер меньше размера.
     */
    public static class SkillNames
    {
        private const uint KEY1 = 0xA8937462;
        private const uint KEY2 = 0xF1A43653;
        private const string SkillStrFile = "\\skillstr.txt";

        private static Dictionary<uint, string> _names;
        private static string _loadedFrom;

        /// <summary>
        /// Название скилла по ID или null, если его нет (или файл игры не прочитался).
        /// </summary>
        public static string Get(uint skillId)
        {
            Load();

            return _names != null && _names.TryGetValue(skillId, out string name) ? name : null;
        }

        // Читаем один раз на папку игры; configs.pck, если там нет — configs2.pck
        private static void Load()
        {
            string dir;

            try
            {
                dir = Path.GetDirectoryName(Reader.process.MainModule.FileName);
            }
            catch
            {
                return;
            }

            if (_names != null && _loadedFrom == dir)
            {
                return;
            }

            foreach (string pck in new[] { "configs.pck", "configs2.pck" })
            {
                try
                {
                    byte[] data = ReadFromPck(Path.Combine(dir, pck), SkillStrFile);

                    if (data != null)
                    {
                        _names = Parse(Encoding.Unicode.GetString(data));
                        _loadedFrom = dir;
                        Logger.setLog("Skill names: " + _names.Count + " from " + pck);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Logger.setLog("Skill names: can't read " + pck + ": " + e.Message);
                }
            }

            _names = new Dictionary<uint, string>();
            _loadedFrom = dir;
        }

        private static Dictionary<uint, string> Parse(string text)
        {
            var names = new Dictionary<uint, string>();

            // Значение в кавычках может занимать несколько строк (у описаний), поэтому разбираем весь текст сразу
            foreach (Match m in Regex.Matches(text, "^\\s*(\\d+)\\s+\"([^\"]*)\"", RegexOptions.Multiline))
            {
                if (uint.TryParse(m.Groups[1].Value, out uint key) && key % 10 == 0)
                {
                    names[key / 10] = m.Groups[2].Value;
                }
            }

            return names;
        }

        // Первый файл в архиве, путь которого заканчивается на fileSuffix
        private static byte[] ReadFromPck(string pckFile, string fileSuffix)
        {
            if (!File.Exists(pckFile))
            {
                return null;
            }

            // Игра держит архив открытым (иногда и на запись) — читаем, не мешая ей: FileShare.ReadWrite
            byte[] d;

            using (var fs = new FileStream(pckFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                d = new byte[fs.Length];
                int read = 0;

                while (read < d.Length)
                {
                    int n = fs.Read(d, read, d.Length - read);

                    if (n == 0)
                    {
                        throw new EndOfStreamException();
                    }

                    read += n;
                }
            }
            int len = d.Length;

            if (BitConverter.ToUInt32(d, len - 0x118) != 0xFDFDFEEE)
            {
                throw new InvalidDataException("unknown pck format");
            }

            uint count = BitConverter.ToUInt32(d, len - 8);
            int pos = (int)(BitConverter.ToUInt32(d, len - 0x110) ^ KEY1);

            for (uint i = 0; i < count; i++)
            {
                int size = (int)(BitConverter.ToUInt32(d, pos) ^ KEY1);

                if ((uint)size != (BitConverter.ToUInt32(d, pos + 4) ^ KEY2))
                {
                    throw new InvalidDataException("bad pck entry");
                }

                pos += 8;
                byte[] entry = new byte[size];
                Array.Copy(d, pos, entry, 0, size);
                pos += size;

                if (size != 0x114)
                {
                    entry = Inflate(entry, 0, entry.Length);
                }

                int nameLen = Array.IndexOf(entry, (byte)0, 0, 260);
                string path = Encoding.ASCII.GetString(entry, 0, nameLen < 0 ? 260 : nameLen);

                if (!path.EndsWith(fileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int offset = (int)BitConverter.ToUInt32(entry, 260);
                int fileSize = (int)BitConverter.ToUInt32(entry, 264);
                int packedSize = (int)BitConverter.ToUInt32(entry, 268);

                if (packedSize == fileSize)
                {
                    byte[] plain = new byte[fileSize];
                    Array.Copy(d, offset, plain, 0, fileSize);
                    return plain;
                }

                return Inflate(d, offset, packedSize);
            }

            return null;
        }

        // zlib = 2 байта заголовка + deflate; DeflateStream понимает только deflate
        private static byte[] Inflate(byte[] src, int offset, int length)
        {
            using (var input = new MemoryStream(src, offset + 2, length - 2))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                deflate.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
