using System;
using System.IO;
using System.Text;

namespace NekoRayNT5.Services
{
    internal static class PercentEncoding
    {
        // Strict URL decoding: %XX sequences are bytes of a UTF-8 string.
        // '+' is kept as is, like QUrl does in Nekoray.
        // Throws FormatException on a broken escape or invalid UTF-8.
        public static string Decode(string text)
        {
            if (text.IndexOf('%') < 0)
            {
                return text;
            }

            UTF8Encoding utf8 = new UTF8Encoding(false, true);
            MemoryStream bytes = new MemoryStream(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '%')
                {
                    if (i + 2 >= text.Length || !IsHex(text[i + 1]) || !IsHex(text[i + 2]))
                    {
                        throw new FormatException("Broken percent-escape.");
                    }
                    bytes.WriteByte((byte)(HexValue(text[i + 1]) * 16 + HexValue(text[i + 2])));
                    i += 3;
                }
                else
                {
                    int count = char.IsHighSurrogate(c) && i + 1 < text.Length ? 2 : 1;
                    byte[] raw;
                    try
                    {
                        raw = utf8.GetBytes(text.Substring(i, count));
                    }
                    catch (ArgumentException)
                    {
                        throw new FormatException("Invalid character.");
                    }
                    bytes.Write(raw, 0, raw.Length);
                    i += count;
                }
            }

            try
            {
                return utf8.GetString(bytes.ToArray());
            }
            catch (ArgumentException)
            {
                throw new FormatException("Escaped bytes are not valid UTF-8.");
            }
        }

        // Encodes everything except RFC 3986 unreserved characters.
        public static string Encode(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length + 8);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
            {
                char c = (char)bytes[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '-' || c == '.' || c == '_' || c == '~')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('%').Append(bytes[i].ToString("X2"));
                }
            }
            return sb.ToString();
        }

        // Base64 in the standard or URL-safe alphabet, padding optional. Throws FormatException.
        public static byte[] DecodeBase64(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length + 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '=' || char.IsWhiteSpace(c))
                {
                    continue;
                }
                sb.Append(c == '-' ? '+' : (c == '_' ? '/' : c));
            }
            if (sb.Length % 4 == 1)
            {
                throw new FormatException("Invalid Base64 length.");
            }
            while (sb.Length % 4 != 0)
            {
                sb.Append('=');
            }
            return Convert.FromBase64String(sb.ToString());
        }

        // Throws FormatException when the data is not Base64 or not UTF-8.
        public static string DecodeBase64Text(string text)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(DecodeBase64(text));
            }
            catch (ArgumentException)
            {
                throw new FormatException("Decoded data is not valid UTF-8.");
            }
        }

        public static string EncodeBase64Url(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool IsHex(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }
            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }
            return c - 'A' + 10;
        }
    }
}
