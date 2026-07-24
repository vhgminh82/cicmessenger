using System;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using CICMessenger.Core.Chat.Encryption;
using Xunit;

namespace CICMessenger.Tests.CoreTests
{
    public class E2EEncryptionServiceTests
    {
        [Fact]
        public void DeriveSharedKey_MatchesOnBothSides()
        {
            using var alice = new E2EEncryptionService();
            using var bob = new E2EEncryptionService();

            byte[] aliceKey = alice.DeriveSharedKey(bob.GetPublicKey());
            byte[] bobKey = bob.DeriveSharedKey(alice.GetPublicKey());

            aliceKey.Should().BeEquivalentTo(bobKey);
        }

        [Fact]
        public void DeriveSharedKey_DiffersForDifferentPeers()
        {
            using var alice = new E2EEncryptionService();
            using var bob = new E2EEncryptionService();
            using var carol = new E2EEncryptionService();

            byte[] withBob = alice.DeriveSharedKey(bob.GetPublicKey());
            byte[] withCarol = alice.DeriveSharedKey(carol.GetPublicKey());

            withBob.Should().NotBeEquivalentTo(withCarol);
        }

        [Fact]
        public void Encrypt_Decrypt_RoundTrip_RecoversPlaintext()
        {
            using var alice = new E2EEncryptionService();
            using var bob = new E2EEncryptionService();
            byte[] sharedKey = alice.DeriveSharedKey(bob.GetPublicKey());

            byte[] plaintext = Encoding.UTF8.GetBytes("hello over LAN");
            var (ciphertext, nonce) = E2EEncryptionService.Encrypt(plaintext, sharedKey);
            byte[] decrypted = E2EEncryptionService.Decrypt(ciphertext, nonce, sharedKey);

            decrypted.Should().BeEquivalentTo(plaintext);
        }

        [Fact]
        public void Decrypt_WithWrongKey_ThrowsAuthenticationTagMismatch()
        {
            using var alice = new E2EEncryptionService();
            using var bob = new E2EEncryptionService();
            using var mallory = new E2EEncryptionService();

            byte[] sharedKey = alice.DeriveSharedKey(bob.GetPublicKey());
            byte[] wrongKey = mallory.DeriveSharedKey(bob.GetPublicKey());

            byte[] plaintext = Encoding.UTF8.GetBytes("secret");
            var (ciphertext, nonce) = E2EEncryptionService.Encrypt(plaintext, sharedKey);

            Action act = () => E2EEncryptionService.Decrypt(ciphertext, nonce, wrongKey);

            act.Should().Throw<CryptographicException>();
        }

        [Fact]
        public void Decrypt_WithTamperedCiphertext_ThrowsAuthenticationTagMismatch()
        {
            using var alice = new E2EEncryptionService();
            using var bob = new E2EEncryptionService();
            byte[] sharedKey = alice.DeriveSharedKey(bob.GetPublicKey());

            byte[] plaintext = Encoding.UTF8.GetBytes("secret");
            var (ciphertext, nonce) = E2EEncryptionService.Encrypt(plaintext, sharedKey);
            ciphertext[0] ^= 0xFF;

            Action act = () => E2EEncryptionService.Decrypt(ciphertext, nonce, sharedKey);

            act.Should().Throw<CryptographicException>();
        }

        [Fact]
        public void Decrypt_WithTooShortCiphertext_ThrowsBeforeTouchingAesGcm()
        {
            using var alice = new E2EEncryptionService();
            byte[] sharedKey = alice.DeriveSharedKey(alice.GetPublicKey());
            byte[] nonce = RandomNumberGenerator.GetBytes(12);

            Action act = () => E2EEncryptionService.Decrypt(new byte[4], nonce, sharedKey);

            act.Should().Throw<CryptographicException>()
                .WithMessage("*authentication tag*");
        }

        [Fact]
        public void ComputeFingerprint_IsStableForSameKey()
        {
            using var alice = new E2EEncryptionService();
            byte[] publicKey = alice.GetPublicKey();

            string first = E2EEncryptionService.ComputeFingerprint(publicKey);
            string second = E2EEncryptionService.ComputeFingerprint(publicKey);

            first.Should().Be(second);
            first.Should().Contain(":");
        }

        [Fact]
        public void ComputeFingerprint_DiffersForDifferentKeys()
        {
            using var alice = new E2EEncryptionService();
            using var bob = new E2EEncryptionService();

            string aliceFingerprint = E2EEncryptionService.ComputeFingerprint(alice.GetPublicKey());
            string bobFingerprint = E2EEncryptionService.ComputeFingerprint(bob.GetPublicKey());

            aliceFingerprint.Should().NotBe(bobFingerprint);
        }
    }
}
