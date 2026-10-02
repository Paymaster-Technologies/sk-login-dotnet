using System.Buffers.Binary;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Paymastech.SkLogin.Crypto;

/// <summary>
/// XChaCha20-Poly1305 (draft-irtf-cfrg-xchacha): HChaCha20 derives a subkey from
/// the key and the first 16 bytes of the 24-byte nonce, then regular IETF
/// ChaCha20-Poly1305 runs with nonce = 4 zero bytes + the last 8 bytes.
/// BouncyCastle has no XChaCha, so HChaCha20 is implemented here.
/// </summary>
internal static class XChaCha20Poly1305
{
    public const int KeyLength = 32;
    public const int NonceLength = 24;
    public const int TagLength = 16;

    public static byte[] Encrypt(byte[] key, byte[] nonce, byte[] plaintext) => Process(true, key, nonce, plaintext);

    /// <exception cref="InvalidCipherTextException">tag mismatch</exception>
    public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext) => Process(false, key, nonce, ciphertext);

    private static byte[] Process(bool forEncryption, byte[] key, byte[] nonce, byte[] input)
    {
        if (key.Length != KeyLength) throw new ArgumentException("key must be 32 bytes", nameof(key));
        if (nonce.Length != NonceLength) throw new ArgumentException("nonce must be 24 bytes", nameof(nonce));
        var subkey = HChaCha20(key, nonce.AsSpan(0, 16));
        var ietfNonce = new byte[12];
        nonce.AsSpan(16, 8).CopyTo(ietfNonce.AsSpan(4));

        var cipher = new ChaCha20Poly1305();
        cipher.Init(forEncryption, new AeadParameters(new KeyParameter(subkey), TagLength * 8, ietfNonce));
        var output = new byte[cipher.GetOutputSize(input.Length)];
        var len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        len += cipher.DoFinal(output, len);
        if (len != output.Length) Array.Resize(ref output, len);
        return output;
    }

    private static byte[] HChaCha20(byte[] key, ReadOnlySpan<byte> nonce16)
    {
        Span<uint> x = stackalloc uint[16];
        x[0] = 0x61707865; x[1] = 0x3320646e; x[2] = 0x79622d32; x[3] = 0x6b206574;
        for (var i = 0; i < 8; i++) x[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4, 4));
        for (var i = 0; i < 4; i++) x[12 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce16.Slice(i * 4, 4));

        for (var i = 0; i < 10; i++)
        {
            QuarterRound(x, 0, 4, 8, 12);
            QuarterRound(x, 1, 5, 9, 13);
            QuarterRound(x, 2, 6, 10, 14);
            QuarterRound(x, 3, 7, 11, 15);
            QuarterRound(x, 0, 5, 10, 15);
            QuarterRound(x, 1, 6, 11, 12);
            QuarterRound(x, 2, 7, 8, 13);
            QuarterRound(x, 3, 4, 9, 14);
        }

        var output = new byte[32];
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(i * 4, 4), x[i]);
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16 + i * 4, 4), x[12 + i]);
        return output;
    }

    private static void QuarterRound(Span<uint> x, int a, int b, int c, int d)
    {
        x[a] += x[b]; x[d] = Rotl(x[d] ^ x[a], 16);
        x[c] += x[d]; x[b] = Rotl(x[b] ^ x[c], 12);
        x[a] += x[b]; x[d] = Rotl(x[d] ^ x[a], 8);
        x[c] += x[d]; x[b] = Rotl(x[b] ^ x[c], 7);
    }

    private static uint Rotl(uint v, int n) => (v << n) | (v >> (32 - n));
}
