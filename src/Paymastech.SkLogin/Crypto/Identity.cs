using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;

namespace Paymastech.SkLogin.Crypto;

/// <summary>Keys of a server (or any other) Secret Keeper identity and its sk1… address.</summary>
public sealed class IdentityKeys
{
    public required byte[] X25519Private { get; init; }
    public required byte[] X25519Public { get; init; }
    public required byte[] Ed25519Private { get; init; }
    public required byte[] Ed25519Public { get; init; }
    /// <summary>bech32 with HRP sk over the X25519 key.</summary>
    public required string Address { get; init; }
}

/// <summary>
/// Port of lib/crypto/{identity_keys,bech32,bip39}.dart from the Secret Keeper app.
/// Compatibility is checked with the same vectors as the Dart and TS implementations
/// (tests/Fixtures/test_vectors.json).
/// </summary>
public static class Identity
{
    private static readonly byte[] HkdfX25519Info = Encoding.UTF8.GetBytes("secret-keeper/x25519/v1");
    private static readonly byte[] HkdfEd25519Info = Encoding.UTF8.GetBytes("secret-keeper/ed25519/v1");
    private const string Hrp = "sk";

    private static readonly Lazy<string[]> Wordlist = new(LoadWordlist);

    /// <summary>12 words from the English wordlist (128 bits of entropy), as in the app.</summary>
    public static string[] GenerateMnemonic()
    {
        var entropy = RandomNumberGenerator.GetBytes(16);
        return EntropyToMnemonic(entropy);
    }

    public static bool ValidateMnemonic(IReadOnlyList<string> mnemonic)
    {
        if (mnemonic.Count != 12) return false;
        var words = Wordlist.Value;
        var bits = new BigInteger(0);
        foreach (var word in mnemonic)
        {
            var idx = Array.BinarySearch(words, word.Trim().ToLowerInvariant(), StringComparer.Ordinal);
            if (idx < 0) return false;
            bits = (bits << 11) | idx;
        }
        // 132 bits: 128 of entropy + 4 of checksum.
        var checksum = (int)(bits & 0xF);
        var entropyBits = bits >> 4;
        var entropy = entropyBits.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (entropy.Length < 16)
        {
            var padded = new byte[16];
            entropy.CopyTo(padded, 16 - entropy.Length);
            entropy = padded;
        }
        return (SHA256.HashData(entropy)[0] >> 4) == checksum;
    }

    /// <summary>A space-separated string of 12 words (as in env) into a word list.</summary>
    public static string[] ParseMnemonic(string mnemonic) =>
        mnemonic.Split(new[] { ' ', '\n', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant()).ToArray();

    /// <summary>BIP-39: PBKDF2-HMAC-SHA512, 2048 iterations, salt "mnemonic".</summary>
    public static byte[] MnemonicToSeed(IReadOnlyList<string> mnemonic)
    {
        var phrase = string.Join(' ', mnemonic).Normalize(NormalizationForm.FormKD);
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(phrase), Encoding.UTF8.GetBytes("mnemonic"), 2048, HashAlgorithmName.SHA512, 64);
    }

    public static IdentityKeys DeriveIdentityKeys(IReadOnlyList<string> mnemonic) => DeriveIdentityKeysFromSeed(MnemonicToSeed(mnemonic));

    /// <summary>Identity from a mnemonic string; throws unless it is 12 wordlist words with a valid checksum.</summary>
    public static IdentityKeys FromMnemonic(string mnemonic)
    {
        var words = ParseMnemonic(mnemonic);
        if (!ValidateMnemonic(words)) throw new ArgumentException("mnemonic must be 12 valid BIP-39 words", nameof(mnemonic));
        return DeriveIdentityKeys(words);
    }

    public static IdentityKeys DeriveIdentityKeysFromSeed(byte[] seed)
    {
        var x25519Private = ClampX25519(HKDF.DeriveKey(HashAlgorithmName.SHA256, seed, 32, salt: null, info: HkdfX25519Info));
        var ed25519Private = HKDF.DeriveKey(HashAlgorithmName.SHA256, seed, 32, salt: null, info: HkdfEd25519Info);
        var x25519Public = new X25519PrivateKeyParameters(x25519Private).GeneratePublicKey().GetEncoded();
        var ed25519Public = new Ed25519PrivateKeyParameters(ed25519Private).GeneratePublicKey().GetEncoded();
        return new IdentityKeys
        {
            X25519Private = x25519Private,
            X25519Public = x25519Public,
            Ed25519Private = ed25519Private,
            Ed25519Public = ed25519Public,
            Address = EncodeIdentityAddress(x25519Public),
        };
    }

    public static string EncodeIdentityAddress(byte[] x25519Public)
    {
        if (x25519Public.Length != 32) throw new ArgumentException("X25519 public key must be 32 bytes", nameof(x25519Public));
        return Bech32.Encode(Hrp, x25519Public);
    }

    public static byte[] DecodeIdentityAddress(string address)
    {
        var (hrp, data) = Bech32.Decode(address.ToLowerInvariant());
        if (hrp != Hrp) throw new FormatException($"Expected HRP {Hrp}, got {hrp}");
        if (data.Length != 32) throw new FormatException($"Expected 32-byte pubkey, got {data.Length}");
        return data;
    }

    /// <summary>Check digits of a single key (25 digits in 5 groups), like keyCheckDigits in the app.</summary>
    public static string KeyCheckDigits(byte[] publicKey) => DigestDigits(publicKey);

    /// <summary>Safety numbers of a contact pair: both keys in lexicographic order.</summary>
    public static string SafetyNumbers(byte[] myPublic, byte[] contactPublic)
    {
        var ordered = CompareBytes(myPublic, contactPublic) <= 0
            ? myPublic.Concat(contactPublic).ToArray()
            : contactPublic.Concat(myPublic).ToArray();
        return DigestDigits(ordered);
    }

    internal static byte[] HkdfSha256(byte[] ikm, byte[] info) => HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, salt: null, info: info);

    internal static byte[] X25519Shared(byte[] privateKey, byte[] publicKey)
    {
        var shared = new byte[32];
        new X25519PrivateKeyParameters(privateKey).GenerateSecret(new X25519PublicKeyParameters(publicKey), shared, 0);
        return shared;
    }

    internal static byte[] X25519PublicOf(byte[] privateKey) => new X25519PrivateKeyParameters(privateKey).GeneratePublicKey().GetEncoded();

    private static byte[] ClampX25519(byte[] key)
    {
        var o = (byte[])key.Clone();
        o[0] &= 248;
        o[31] &= 127;
        o[31] |= 64;
        return o;
    }

    private static string DigestDigits(byte[] data)
    {
        var digest = SHA256.HashData(data);
        var value = new BigInteger(digest.AsSpan(0, 10), isUnsigned: true, isBigEndian: true);
        var digits = value.ToString().PadLeft(25, '0')[..25];
        return string.Join(' ', Enumerable.Range(0, 5).Select(i => digits.Substring(i * 5, 5)));
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length && i < b.Length; i++)
            if (a[i] != b[i]) return a[i] - b[i];
        return a.Length - b.Length;
    }

    private static string[] EntropyToMnemonic(byte[] entropy)
    {
        var checksum = SHA256.HashData(entropy)[0] >> 4;
        var bits = (new BigInteger(entropy, isUnsigned: true, isBigEndian: true) << 4) | checksum;
        var words = Wordlist.Value;
        var result = new string[12];
        for (var i = 11; i >= 0; i--)
        {
            result[i] = words[(int)(bits & 0x7FF)];
            bits >>= 11;
        }
        return result;
    }

    private static string[] LoadWordlist()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("bip39-english.txt")
            ?? throw new InvalidOperationException("bip39-english.txt resource is missing");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var words = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length != 2048) throw new InvalidOperationException("bip39 wordlist must have 2048 words");
        return words;
    }
}
