using System.Text;

namespace Paymastech.SkLogin.Crypto;

/// <summary>Bech32 (BIP-173): sk1… addresses over a 32-byte X25519 key.</summary>
internal static class Bech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly uint[] Generator = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };

    public static string Encode(string hrp, byte[] data)
    {
        var words = ToWords(data);
        var checksum = CreateChecksum(hrp, words);
        var sb = new StringBuilder(hrp.Length + 1 + words.Length + 6);
        sb.Append(hrp).Append('1');
        foreach (var w in words) sb.Append(Charset[w]);
        foreach (var w in checksum) sb.Append(Charset[w]);
        return sb.ToString();
    }

    public static (string Hrp, byte[] Data) Decode(string address)
    {
        if (address.Length < 8 || address.Length > 90) throw new FormatException("bech32: bad length");
        var lower = address.ToLowerInvariant();
        if (lower != address && address.ToUpperInvariant() != address) throw new FormatException("bech32: mixed case");
        var pos = lower.LastIndexOf('1');
        if (pos < 1 || pos + 7 > lower.Length) throw new FormatException("bech32: separator");
        var hrp = lower[..pos];
        var words = new byte[lower.Length - pos - 1];
        for (var i = 0; i < words.Length; i++)
        {
            var idx = Charset.IndexOf(lower[pos + 1 + i]);
            if (idx < 0) throw new FormatException("bech32: bad character");
            words[i] = (byte)idx;
        }
        if (Polymod(HrpExpand(hrp).Concat(words).ToArray()) != 1) throw new FormatException("bech32: checksum");
        return (hrp, FromWords(words[..^6]));
    }

    private static byte[] ToWords(byte[] data) => Convert(data, 8, 5, true);
    private static byte[] FromWords(byte[] words) => Convert(words, 5, 8, false);

    private static byte[] Convert(byte[] data, int from, int to, bool pad)
    {
        var acc = 0;
        var bits = 0;
        var result = new List<byte>();
        var maxv = (1 << to) - 1;
        foreach (var value in data)
        {
            acc = (acc << from) | value;
            bits += from;
            while (bits >= to)
            {
                bits -= to;
                result.Add((byte)((acc >> bits) & maxv));
            }
        }
        if (pad)
        {
            if (bits > 0) result.Add((byte)((acc << (to - bits)) & maxv));
        }
        else if (bits >= from || ((acc << (to - bits)) & maxv) != 0)
        {
            throw new FormatException("bech32: padding");
        }
        return result.ToArray();
    }

    private static uint Polymod(byte[] values)
    {
        uint chk = 1;
        foreach (var v in values)
        {
            var top = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (var i = 0; i < 5; i++)
                if (((top >> i) & 1) != 0) chk ^= Generator[i];
        }
        return chk;
    }

    private static byte[] HrpExpand(string hrp)
    {
        var result = new byte[hrp.Length * 2 + 1];
        for (var i = 0; i < hrp.Length; i++)
        {
            result[i] = (byte)(hrp[i] >> 5);
            result[hrp.Length + 1 + i] = (byte)(hrp[i] & 31);
        }
        return result;
    }

    private static byte[] CreateChecksum(string hrp, byte[] words)
    {
        var values = HrpExpand(hrp).Concat(words).Concat(new byte[6]).ToArray();
        var polymod = Polymod(values) ^ 1;
        var checksum = new byte[6];
        for (var i = 0; i < 6; i++) checksum[i] = (byte)((polymod >> (5 * (5 - i))) & 31);
        return checksum;
    }
}
