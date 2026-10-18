using System;
using System.Globalization;
using System.IO;
using System.Text;
using NekoRayNT5.App;

namespace NekoRayNT5.Services
{
    // What is known about the CA bundle (cacert.pem) next to the program.
    internal sealed class CaBundleInfo
    {
        public bool Exists;
        public int Certificates;
        // "Certificate data from Mozilla as of: ..." from the header, or "" when there is none.
        public string MozillaDate = "";
        // Null when the bundle can be used, otherwise why not.
        public string Problem;

        public override string ToString()
        {
            if (!Exists)
            {
                return Lang.T("missing");
            }
            if (Problem != null)
            {
                return Problem;
            }
            return MozillaDate.Length > 0 ? Lang.F("{0} certificates, Mozilla data as of {1}", Certificates, MozillaDate)
                : Lang.F("{0} certificates", Certificates);
        }
    }

    // The root store of Windows XP is outdated, so nt5helper.exe trusts the CAs of the Mozilla bundle
    // shipped with the program. The bundle is read strictly: one damaged certificate makes the whole
    // file unusable, because a truncated bundle would silently trust only part of the CAs.
    internal static class CaBundle
    {
        public const string FileName = "cacert.pem";
        private const string Begin = "-----BEGIN CERTIFICATE-----";
        private const string End = "-----END CERTIFICATE-----";
        private const string DateMarker = "Certificate data from Mozilla as of:";
        private const int MaxBytes = 4 * 1024 * 1024;

        public static CaBundleInfo Inspect(string path)
        {
            CaBundleInfo info = new CaBundleInfo();
            string name = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                info.Problem = Lang.F("{0} is missing next to the program.", name);
                return info;
            }
            info.Exists = true;
            string text;
            try
            {
                if (new FileInfo(path).Length > MaxBytes)
                {
                    info.Problem = Lang.F("{0} is too large to be a CA bundle.", name);
                    return info;
                }
                text = File.ReadAllText(path, Encoding.ASCII);
            }
            catch (IOException ex)
            {
                info.Problem = Lang.F("{0} cannot be read: {1}", name, ex.Message);
                return info;
            }
            catch (UnauthorizedAccessException ex)
            {
                info.Problem = Lang.F("{0} cannot be read: {1}", name, ex.Message);
                return info;
            }
            info.Problem = Parse(text, name, info);
            return info;
        }

        // Fills in the count and the date; returns the problem or null.
        public static string Parse(string text, string name, CaBundleInfo info)
        {
            int marker = text.IndexOf(DateMarker, StringComparison.Ordinal);
            if (marker >= 0)
            {
                int start = marker + DateMarker.Length;
                int end = text.IndexOfAny(new char[] { '\r', '\n' }, start);
                info.MozillaDate = (end < 0 ? text.Substring(start) : text.Substring(start, end - start)).Trim();
            }
            int pos = 0;
            int count = 0;
            while (true)
            {
                int b = text.IndexOf(Begin, pos, StringComparison.Ordinal);
                if (b < 0)
                {
                    break;
                }
                int e = text.IndexOf(End, b + Begin.Length, StringComparison.Ordinal);
                int nextBegin = text.IndexOf(Begin, b + Begin.Length, StringComparison.Ordinal);
                if (e < 0 || (nextBegin >= 0 && nextBegin < e))
                {
                    return Lang.F("{0} is damaged (an incomplete certificate after {1} good ones). "
                           + "Replace it with the cacert.pem of the program package.", name, count);
                }
                string body = text.Substring(b + Begin.Length, e - b - Begin.Length);
                try
                {
                    byte[] der = Convert.FromBase64String(body.Replace("\r", "").Replace("\n", "").Trim());
                    if (!IsDerSequence(der))
                    {
                        throw new FormatException();
                    }
                }
                catch (FormatException)
                {
                    return Lang.F("{0} is damaged (certificate {1} cannot be read). "
                           + "Replace it with the cacert.pem of the program package.", name, count + 1);
                }
                count++;
                pos = e + End.Length;
            }
            info.Certificates = count;
            if (count == 0)
            {
                return Lang.F("{0} contains no certificates. Replace it with the cacert.pem of the program package.", name);
            }
            return null;
        }

        // The outer structure of a certificate: one DER SEQUENCE that fills the data exactly. The
        // certificates themselves are checked by nt5helper (Go's x509); the CryptoAPI of Windows XP
        // is not used for that, as it does not know all current key types.
        private static bool IsDerSequence(byte[] der)
        {
            if (der.Length < 4 || der[0] != 0x30)
            {
                return false;
            }
            int length;
            int header;
            if (der[1] < 0x80)
            {
                length = der[1];
                header = 2;
            }
            else
            {
                int n = der[1] & 0x7F;
                if (n < 1 || n > 3 || der.Length < 2 + n)
                {
                    return false;
                }
                length = 0;
                for (int i = 0; i < n; i++)
                {
                    length = (length << 8) | der[2 + i];
                }
                header = 2 + n;
            }
            return header + length == der.Length;
        }

        // Age of the Mozilla data in days, or -1 when the header has no readable date.
        public static int AgeDays(CaBundleInfo info, DateTime nowUtc)
        {
            // "Fri Sep 25 03:12:01 2026 GMT"
            string[] parts = info.MozillaDate.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
            {
                return -1;
            }
            DateTime date;
            string compact = parts[1] + " " + parts[2] + " " + parts[4];
            if (!DateTime.TryParseExact(compact, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                return -1;
            }
            return (int)(nowUtc.Date - date).TotalDays;
        }
    }
}
