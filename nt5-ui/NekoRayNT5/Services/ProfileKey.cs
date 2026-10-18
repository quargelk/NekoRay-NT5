using System;
using System.Text;
using NekoRayNT5.Model;

namespace NekoRayNT5.Services
{
    // Identity of a connection: two profiles with the same key connect the same way.
    // The name and test results are not part of it. Used for duplicates and subscription updates.
    internal static class ProfileKey
    {
        public static string Of(ProfileEntry p)
        {
            string[] parts = new string[] {
                p.Type, p.Address.ToLowerInvariant(), p.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                p.Uuid.ToLowerInvariant(), p.Username, p.Password, p.Encryption, p.Flow, p.Network, p.Host, p.Path,
                p.Security, p.Sni.ToLowerInvariant(), p.PublicKey, p.ShortId };
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                // the separator cannot occur in validated fields (no control characters)
                sb.Append(parts[i]).Append('\u0001');
            }
            return sb.ToString();
        }
    }
}
