using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto;

namespace Paymastech.SkLogin.Crypto;

/// <summary>Decrypted envelope content.</summary>
/// <param name="Text">Message text (what is shown to the user).</param>
/// <param name="Meta">Metadata for the recipient's automation (convention: JSON {type, data}).</param>
/// <param name="SentAtMs">When the sender built the envelope: ms since Unix epoch, UTC.</param>
/// <param name="Recipients">Envelope recipients excluding the sender; null if the field is absent.</param>
public sealed record EnvelopeContent(string Text, string? Meta, long SentAtMs, IReadOnlyList<string>? Recipients);

/// <summary>
/// Port of lib/crypto/{envelope,envelope_parse}.dart.
/// Format: [0x02][sender_pub 32][eph_pub 32][N 1][slot 48 x N][nonce 24][ct+tag].
/// The payload is encrypted with a random message key (XChaCha20-Poly1305); the key
/// is wrapped into a slot for every recipient and for the sender itself:
/// slot = AEAD(message_key, KEK_i, nonce 0), KEK_i = HKDF(ECDH(eph, R_i) || ECDH(sender, R_i)).
/// The reader computes its own KEK and tries the slots; the tag rejects foreign ones.
/// Payload: [flags u8][sentAt u64 BE][metaLen u32 BE + meta when flags&amp;1]
/// [count u8 + (len u8 + address) when flags&amp;2][text UTF-8].
/// </summary>
public static class Envelope
{
    private const byte EnvelopeVersion = 0x02;
    private const byte FlagHasMeta = 0x01;
    private const byte FlagHasTo = 0x02;
    private static readonly byte[] HkdfKekInfo = Encoding.UTF8.GetBytes("secret-keeper/kek/v1");
    private const int SlotLength = 48;
    private const int MaxSlots = 255;
    private static readonly byte[] SlotNonce = new byte[24];

    public const string ArmorBegin = "-----BEGIN SECRET MESSAGE V1-----";
    public const string ArmorEnd = "-----END SECRET MESSAGE V1-----";

    public static string Encrypt(IdentityKeys sender, string recipientAddress, string plaintext, string? meta = null, long? sentAtMs = null) =>
        Encrypt(sender, new[] { recipientAddress }, plaintext, meta, sentAtMs);

    public static string Encrypt(IdentityKeys sender, IReadOnlyList<string> recipientAddresses, string plaintext, string? meta = null, long? sentAtMs = null)
    {
        var messageKey = RandomNumberGenerator.GetBytes(32);
        var (ephPublic, slots) = WrapKeySlots(sender, recipientAddresses, messageKey);

        var to = recipientAddresses.Distinct().Where(a => a != sender.Address).ToList();
        var nonce = RandomNumberGenerator.GetBytes(24);
        var ciphertext = XChaCha20Poly1305.Encrypt(messageKey, nonce, BuildPayload(plaintext, meta, sentAtMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), to));

        var binary = new byte[1 + 32 + 32 + 1 + slots.Count * SlotLength + 24 + ciphertext.Length];
        var offset = 0;
        binary[offset++] = EnvelopeVersion;
        sender.X25519Public.CopyTo(binary, offset); offset += 32;
        ephPublic.CopyTo(binary, offset); offset += 32;
        binary[offset++] = (byte)slots.Count;
        foreach (var slot in slots) { slot.CopyTo(binary, offset); offset += SlotLength; }
        nonce.CopyTo(binary, offset); offset += 24;
        ciphertext.CopyTo(binary, offset);

        return $"{ArmorBegin}\n{Convert.ToBase64String(binary)}\n{ArmorEnd}";
    }

    /// <exception cref="FormatException">not an envelope, not addressed to us, or damaged</exception>
    public static EnvelopeContent Decrypt(byte[] recipientX25519Private, string armored)
    {
        var binary = ParseArmor(armored);
        if (binary.Length == 0) throw new FormatException("Envelope too short");
        if (binary[0] != EnvelopeVersion) throw new FormatException($"Unsupported envelope version: {binary[0]}");

        const int slotsOffset = 1 + 32 + 32 + 1;
        if (binary.Length < slotsOffset) throw new FormatException("Envelope too short");
        var senderPub = binary[1..33];
        var ephPub = binary[33..65];
        int slotCount = binary[65];
        var nonceOffset = slotsOffset + slotCount * SlotLength;
        if (slotCount == 0 || binary.Length < nonceOffset + 24 + 16) throw new FormatException("Envelope too short");

        var slots = Enumerable.Range(0, slotCount).Select(i => binary[(slotsOffset + i * SlotLength)..(slotsOffset + (i + 1) * SlotLength)]).ToList();
        var messageKey = UnwrapKeySlots(recipientX25519Private, senderPub, ephPub, slots)
            ?? throw new FormatException("invalid tag");

        var nonce = binary[nonceOffset..(nonceOffset + 24)];
        var ciphertext = binary[(nonceOffset + 24)..];
        byte[] clear;
        try
        {
            clear = XChaCha20Poly1305.Decrypt(messageKey, nonce, ciphertext);
        }
        catch (InvalidCipherTextException e)
        {
            throw new FormatException("invalid tag", e);
        }
        return ParsePayload(clear);
    }

    /// <summary>Sender address from the armored envelope header, without decrypting.
    /// Authenticated only after a successful decryption: the KEK includes an ECDH with the sender's static key.</summary>
    public static string SenderAddress(string armored)
    {
        var binary = ParseArmor(armored);
        if (binary.Length < 33) throw new FormatException("Envelope too short");
        return Identity.EncodeIdentityAddress(binary[1..33]);
    }

    /// <summary>The canonical armor block (BEGIN…END) inside arbitrary text.</summary>
    /// <exception cref="FormatException">the marker pair is missing</exception>
    public static string ExtractArmor(string input)
    {
        var begin = input.IndexOf(ArmorBegin, StringComparison.Ordinal);
        var end = input.IndexOf(ArmorEnd, StringComparison.Ordinal);
        if (begin < 0 || end < 0 || end <= begin) throw new FormatException("Missing armor markers");
        return input.Substring(begin, end + ArmorEnd.Length - begin);
    }

    public static bool ContainsArmor(string text)
    {
        var begin = text.IndexOf(ArmorBegin, StringComparison.Ordinal);
        return begin >= 0 && text.IndexOf(ArmorEnd, StringComparison.Ordinal) > begin;
    }

    private static (byte[] EphPublic, List<byte[]> Slots) WrapKeySlots(IdentityKeys sender, IReadOnlyList<string> recipientAddresses, byte[] messageKey)
    {
        var recipientPubs = new Dictionary<string, byte[]>();
        void AddPub(byte[] pub) => recipientPubs[Convert.ToHexString(pub)] = pub;
        foreach (var address in recipientAddresses) AddPub(Identity.DecodeIdentityAddress(address));
        if (recipientPubs.Count == 0) throw new ArgumentException("At least one recipient is required");
        AddPub(sender.X25519Public);
        if (recipientPubs.Count > MaxSlots) throw new ArgumentException($"Too many recipients: {recipientPubs.Count}");

        var ephPrivate = RandomNumberGenerator.GetBytes(32);
        var ephPublic = Identity.X25519PublicOf(ephPrivate);

        var slots = new List<byte[]>();
        foreach (var pub in recipientPubs.Values)
        {
            var kek = Kek(Identity.X25519Shared(ephPrivate, pub), Identity.X25519Shared(sender.X25519Private, pub));
            slots.Add(XChaCha20Poly1305.Encrypt(kek, SlotNonce, messageKey));
        }
        Shuffle(slots);
        return (ephPublic, slots);
    }

    private static byte[]? UnwrapKeySlots(byte[] recipientPrivate, byte[] senderPub, byte[] ephPub, List<byte[]> slots)
    {
        var kek = Kek(Identity.X25519Shared(recipientPrivate, ephPub), Identity.X25519Shared(recipientPrivate, senderPub));
        foreach (var slot in slots)
        {
            try
            {
                return XChaCha20Poly1305.Decrypt(kek, SlotNonce, slot);
            }
            catch (InvalidCipherTextException)
            {
                // Not our slot.
            }
        }
        return null;
    }

    private static byte[] Kek(byte[] shared1, byte[] shared2) => Identity.HkdfSha256(shared1.Concat(shared2).ToArray(), HkdfKekInfo);

    private static byte[] BuildPayload(string text, string? meta, long sentAtMs, List<string> recipients)
    {
        var metaBytes = meta is null ? null : Encoding.UTF8.GetBytes(meta);
        var textBytes = Encoding.UTF8.GetBytes(text);
        var toBytes = recipients.Select(a =>
        {
            var b = Encoding.UTF8.GetBytes(a);
            if (b.Length > 0xff) throw new ArgumentException($"Recipient address too long: {a}");
            return b;
        }).ToList();
        var toLength = toBytes.Count > 0 ? toBytes.Sum(b => 1 + b.Length) + 1 : 0;
        var headerLength = 1 + 8 + (metaBytes is null ? 0 : 4);
        var payload = new byte[headerLength + (metaBytes?.Length ?? 0) + toLength + textBytes.Length];
        payload[0] = (byte)((metaBytes is null ? 0 : FlagHasMeta) | (toBytes.Count > 0 ? FlagHasTo : 0));
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(1, 8), (ulong)sentAtMs);
        var offset = 9;
        if (metaBytes is not null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(offset, 4), (uint)metaBytes.Length);
            offset += 4;
            metaBytes.CopyTo(payload, offset);
            offset += metaBytes.Length;
        }
        if (toBytes.Count > 0)
        {
            payload[offset++] = (byte)toBytes.Count;
            foreach (var b in toBytes)
            {
                payload[offset++] = (byte)b.Length;
                b.CopyTo(payload, offset);
                offset += b.Length;
            }
        }
        textBytes.CopyTo(payload, offset);
        return payload;
    }

    private static EnvelopeContent ParsePayload(byte[] payload)
    {
        if (payload.Length < 9) throw new FormatException("Envelope payload too short");
        var flags = payload[0];
        if ((flags & ~(FlagHasMeta | FlagHasTo)) != 0) throw new FormatException($"Unsupported payload flags: {flags}");
        var sentAtMs = (long)BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(1, 8));
        var offset = 9;
        string? meta = null;
        if ((flags & FlagHasMeta) != 0)
        {
            if (payload.Length < offset + 4) throw new FormatException("Envelope payload too short");
            var metaLength = (int)BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4));
            offset += 4;
            if (payload.Length < offset + metaLength) throw new FormatException("Envelope payload too short");
            meta = Encoding.UTF8.GetString(payload, offset, metaLength);
            offset += metaLength;
        }
        List<string>? recipients = null;
        if ((flags & FlagHasTo) != 0)
        {
            if (payload.Length < offset + 1) throw new FormatException("Envelope payload too short");
            int count = payload[offset++];
            recipients = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                if (payload.Length < offset + 1) throw new FormatException("Envelope payload too short");
                int length = payload[offset++];
                if (payload.Length < offset + length) throw new FormatException("Envelope payload too short");
                recipients.Add(Encoding.UTF8.GetString(payload, offset, length));
                offset += length;
            }
        }
        return new EnvelopeContent(Encoding.UTF8.GetString(payload, offset, payload.Length - offset), meta, sentAtMs, recipients);
    }

    private static byte[] ParseArmor(string armored)
    {
        var block = ExtractArmor(armored);
        var payload = Regex.Replace(block.Substring(ArmorBegin.Length, block.Length - ArmorBegin.Length - ArmorEnd.Length), @"\s+", "");
        try
        {
            return Convert.FromBase64String(payload);
        }
        catch (FormatException e)
        {
            throw new FormatException("Envelope is not base64", e);
        }
    }

    private static void Shuffle(List<byte[]> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
