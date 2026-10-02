using System.Text.Json;
using Paymastech.SkLogin.Crypto;

namespace Paymastech.SkLogin.Tests;

/// <summary>
/// The same vectors as the app's Dart implementation (test/protocol_cross_test.dart)
/// and the TS core (@paymastech/sk-login-core): test_vectors.json is produced by the
/// tools/test_vectors generator, app_fixtures.json is recorded by the app itself.
/// </summary>
public class CryptoTests
{
    private static readonly JsonElement Vectors = Load("test_vectors.json");
    private static readonly JsonElement App = Load("app_fixtures.json");

    private static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))).RootElement;

    private static string[] Words(JsonElement e) => e.EnumerateArray().Select(w => w.GetString()!).ToArray();
    private static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    [Fact]
    public void DerivesKeysAndAddressFromMnemonic()
    {
        var mnemonic = Words(Vectors.GetProperty("mnemonic"));
        Assert.True(Identity.ValidateMnemonic(mnemonic));
        Assert.Equal(Vectors.GetProperty("seed_hex").GetString(), Hex(Identity.MnemonicToSeed(mnemonic)));
        var keys = Identity.DeriveIdentityKeys(mnemonic);
        Assert.Equal(Vectors.GetProperty("x25519_private_hex").GetString(), Hex(keys.X25519Private));
        Assert.Equal(Vectors.GetProperty("x25519_public_hex").GetString(), Hex(keys.X25519Public));
        Assert.Equal(Vectors.GetProperty("ed25519_private_hex").GetString(), Hex(keys.Ed25519Private));
        Assert.Equal(Vectors.GetProperty("ed25519_public_hex").GetString(), Hex(keys.Ed25519Public));
        Assert.Equal(Vectors.GetProperty("sender_address").GetString(), keys.Address);
        Assert.Equal(keys.X25519Public, Identity.DecodeIdentityAddress(keys.Address));
    }

    [Fact]
    public void GeneratedMnemonicIsValidAndRoundTrips()
    {
        var m = Identity.GenerateMnemonic();
        Assert.Equal(12, m.Length);
        Assert.True(Identity.ValidateMnemonic(m));
        var broken = (string[])m.Clone();
        broken[0] = broken[0] == "zoo" ? "abandon" : "zoo";
        Assert.False(Identity.ValidateMnemonic(broken));
        Assert.Equal(Identity.DeriveIdentityKeys(m).Address, Identity.FromMnemonic(string.Join(' ', m)).Address);
    }

    // Slots per the generator: envelope* is for recipient and recipient2, envelope_to* is for
    // recipient and recipient3; the sender can read any envelope of its own.
    [Theory]
    [InlineData("envelope", "recipient2")]
    [InlineData("envelope_meta", "recipient2")]
    [InlineData("envelope_to", "recipient3")]
    [InlineData("envelope_to_meta", "recipient3")]
    public void DecryptsGeneratorVectorsAsEveryRecipient(string name, string alsoReader)
    {
        var v = Vectors.GetProperty(name);
        var sender = Identity.DeriveIdentityKeys(Words(Vectors.GetProperty("mnemonic")));
        byte[] Priv(string r) => Convert.FromHexString(Vectors.GetProperty(r).GetProperty("x25519_private_hex").GetString()!);
        var stranger = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());
        Assert.Throws<FormatException>(() => Envelope.Decrypt(stranger.X25519Private, v.GetProperty("armor").GetString()!));
        foreach (var priv in new[] { Priv("recipient"), Priv(alsoReader), sender.X25519Private })
        {
            var content = Envelope.Decrypt(priv, v.GetProperty("armor").GetString()!);
            Assert.Equal(v.GetProperty("plaintext").GetString(), content.Text);
            Assert.Equal(v.GetProperty("sent_at_ms").GetInt64(), content.SentAtMs);
            Assert.Equal(v.TryGetProperty("meta", out var meta) ? meta.GetString() : null, content.Meta);
            if (v.TryGetProperty("recipients", out var rec)) Assert.Equal(Words(rec), content.Recipients);
            else Assert.Null(content.Recipients);
        }
        Assert.Equal(sender.Address, Envelope.SenderAddress(v.GetProperty("armor").GetString()!));
    }

    [Fact]
    public void RejectsUnknownPayloadFlags()
    {
        var v = Vectors.GetProperty("envelope_future_flags");
        var priv = Convert.FromHexString(Vectors.GetProperty("recipient").GetProperty("x25519_private_hex").GetString()!);
        var e = Assert.Throws<FormatException>(() => Envelope.Decrypt(priv, v.GetProperty("armor").GetString()!));
        Assert.Contains("flags", e.Message);
    }

    [Fact]
    public void DecryptsEnvelopeWrittenByTheApp()
    {
        var v = App.GetProperty("envelopes").GetProperty("to_reader_meta");
        var reader = Identity.DeriveIdentityKeys(Words(App.GetProperty("reader").GetProperty("mnemonic")));
        var sender = Identity.DeriveIdentityKeys(Words(App.GetProperty("sender").GetProperty("mnemonic")));
        Assert.Equal(App.GetProperty("reader").GetProperty("address").GetString(), reader.Address);

        var content = Envelope.Decrypt(reader.X25519Private, v.GetProperty("armored").GetString()!);
        Assert.Equal(v.GetProperty("text").GetString(), content.Text);
        Assert.Equal(v.GetProperty("meta").GetString(), content.Meta);
        Assert.Equal(sender.Address, Envelope.SenderAddress(v.GetProperty("armored").GetString()!));

        // The sender can read its own envelope, a stranger cannot.
        Envelope.Decrypt(sender.X25519Private, v.GetProperty("armored").GetString()!);
        var stranger = Identity.DeriveIdentityKeys(Identity.GenerateMnemonic());
        Assert.Throws<FormatException>(() => Envelope.Decrypt(stranger.X25519Private, v.GetProperty("armored").GetString()!));
    }

    [Fact]
    public void RoundTripsOwnEnvelopeBackToTheAppIdentity()
    {
        var reader = Identity.DeriveIdentityKeys(Words(App.GetProperty("reader").GetProperty("mnemonic")));
        var sender = Identity.DeriveIdentityKeys(Words(App.GetProperty("sender").GetProperty("mnemonic")));
        var armored = Envelope.Encrypt(reader, sender.Address, "123456", "{\"type\":\"sk-login-challenge\"}", 1700000000000);
        var content = Envelope.Decrypt(sender.X25519Private, armored);
        Assert.Equal("123456", content.Text);
        Assert.Equal("{\"type\":\"sk-login-challenge\"}", content.Meta);
        Assert.Equal(1700000000000, content.SentAtMs);
        Assert.Equal(new[] { sender.Address }, content.Recipients);
        // Text around the armor (messenger captions) does not get in the way.
        Assert.Equal("123456", Envelope.Decrypt(sender.X25519Private, "Igor, 19:20:\n" + armored + "\nreply").Text);
    }

    [Fact]
    public void CheckDigitsAreFiveGroupsOfFive()
    {
        var keys = Identity.DeriveIdentityKeys(Words(Vectors.GetProperty("mnemonic")));
        var digits = Identity.KeyCheckDigits(keys.X25519Public);
        Assert.Matches(@"^\d{5}( \d{5}){4}$", digits);
        Assert.Equal(Identity.SafetyNumbers(keys.X25519Public, keys.Ed25519Public), Identity.SafetyNumbers(keys.Ed25519Public, keys.X25519Public));
    }
}
