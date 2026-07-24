using System.Text;
using FluentAssertions;
using CICMessenger.Core.Chat.Encryption;
using Xunit;

namespace CICMessenger.Tests.CoreTests
{
    public class EncryptionManagerTests
    {
        [Fact]
        public void IsEncrypted_FalseBeforeKeyExchange()
        {
            using var manager = new EncryptionManager();

            manager.IsEncrypted("peer1").Should().BeFalse();
        }

        [Fact]
        public void OnPeerPublicKeyReceived_EstablishesEncryption()
        {
            using var alice = new EncryptionManager();
            using var bob = new EncryptionManager();

            byte[] alicePublicKey = alice.GetOrCreateLocalPublicKey("bob");
            byte[] bobPublicKey = bob.GetOrCreateLocalPublicKey("alice");

            alice.OnPeerPublicKeyReceived("bob", bobPublicKey);
            bob.OnPeerPublicKeyReceived("alice", alicePublicKey);

            alice.IsEncrypted("bob").Should().BeTrue();
            bob.IsEncrypted("alice").Should().BeTrue();
        }

        [Fact]
        public void Encrypt_Decrypt_RoundTrip_BetweenTwoManagers()
        {
            using var alice = new EncryptionManager();
            using var bob = new EncryptionManager();

            alice.OnPeerPublicKeyReceived("bob", bob.GetOrCreateLocalPublicKey("alice"));
            bob.OnPeerPublicKeyReceived("alice", alice.GetOrCreateLocalPublicKey("bob"));

            byte[] plaintext = Encoding.UTF8.GetBytes("hi bob");
            var encrypted = alice.Encrypt("bob", plaintext);

            encrypted.Should().NotBeNull();
            byte[]? decrypted = bob.Decrypt("alice", encrypted!.Value.Ciphertext, encrypted.Value.Nonce);

            decrypted.Should().BeEquivalentTo(plaintext);
        }

        [Fact]
        public void Encrypt_ReturnsNull_WhenNoSharedKeyEstablished()
        {
            using var manager = new EncryptionManager();

            var result = manager.Encrypt("stranger", Encoding.UTF8.GetBytes("data"));

            result.Should().BeNull();
        }

        [Fact]
        public void Decrypt_ReturnsNull_WhenNoSharedKeyEstablished()
        {
            using var manager = new EncryptionManager();

            byte[]? result = manager.Decrypt("stranger", new byte[16], new byte[12]);

            result.Should().BeNull();
        }

        [Fact]
        public void OnPeerPublicKeyReceived_SameKeyTwice_ReturnsNullSecondTime()
        {
            using var alice = new EncryptionManager();
            using var bob = new EncryptionManager();
            byte[] bobPublicKey = bob.GetOrCreateLocalPublicKey("alice");

            alice.OnPeerPublicKeyReceived("bob", bobPublicKey);
            alice.MarkKeySent("bob");
            var secondResult = alice.OnPeerPublicKeyReceived("bob", bobPublicKey);

            secondResult.Should().BeNull();
        }

        [Fact]
        public void OnPeerPublicKeyReceived_ChangedKey_RaisesPeerKeyChanged()
        {
            using var alice = new EncryptionManager();
            using var bob = new EncryptionManager();
            using var bobRestarted = new EncryptionManager();

            byte[] bobFirstKey = bob.GetOrCreateLocalPublicKey("alice");
            byte[] bobSecondKey = bobRestarted.GetOrCreateLocalPublicKey("alice");

            alice.OnPeerPublicKeyReceived("bob", bobFirstKey);

            PeerKeyChangedEventArgs? raised = null;
            alice.PeerKeyChanged += (_, e) => raised = e;

            alice.OnPeerPublicKeyReceived("bob", bobSecondKey);

            raised.Should().NotBeNull();
            raised!.PeerId.Should().Be("bob");
            raised.OldPublicKey.Should().BeEquivalentTo(bobFirstKey);
            raised.NewPublicKey.Should().BeEquivalentTo(bobSecondKey);
        }

        [Fact]
        public void RemovePeer_ClearsEncryptionState()
        {
            using var alice = new EncryptionManager();
            using var bob = new EncryptionManager();

            alice.OnPeerPublicKeyReceived("bob", bob.GetOrCreateLocalPublicKey("alice"));
            alice.IsEncrypted("bob").Should().BeTrue();

            alice.RemovePeer("bob");

            alice.IsEncrypted("bob").Should().BeFalse();
        }
    }
}
