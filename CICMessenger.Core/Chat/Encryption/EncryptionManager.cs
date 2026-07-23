using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CICMessenger.Core.Chat.Encryption
{
    /// <summary>
    /// Manages per-peer encryption sessions: key pairs, public key exchange,
    /// and shared key derivation. Thread-safe.
    /// </summary>
    public sealed class EncryptionManager : IDisposable
    {
        readonly ConcurrentDictionary<string, PeerEncryptionState> peerStates = new();
        // Keys seen for each peer since process start, kept across session end/reconnect
        // so a silently-changed key (possible MITM) can be detected — TOFU pinning.
        readonly ConcurrentDictionary<string, byte[]> knownPeerKeys = new();
        readonly ILogger logger;

        /// <summary>
        /// Raised when a peer's public key differs from the one previously seen for them,
        /// which may indicate a man-in-the-middle attack. The caller should surface this
        /// to the user rather than silently trusting the new key.
        /// </summary>
        public event EventHandler<PeerKeyChangedEventArgs>? PeerKeyChanged;

        public EncryptionManager(ILogger? logger = null)
        {
            this.logger = logger ?? NullLogger.Instance;
        }

        /// <summary>
        /// Gets or creates the encryption state for a peer, generating our local key pair.
        /// Returns our public key bytes to send to the peer.
        /// </summary>
        public byte[] GetOrCreateLocalPublicKey(string peerId)
        {
            var state = peerStates.GetOrAdd(peerId, _ =>
            {
                logger.LogDebug("Creating new encryption session for peer {PeerId}", peerId);
                return new PeerEncryptionState();
            });
            return state.Service.GetPublicKey();
        }

        /// <summary>
        /// Processes a peer's public key, deriving the shared secret.
        /// Returns our public key if we haven't sent one yet (for the response leg).
        /// </summary>
        public byte[]? OnPeerPublicKeyReceived(string peerId, byte[] peerPublicKey)
        {
            var state = peerStates.GetOrAdd(peerId, _ =>
            {
                logger.LogDebug("Creating new encryption session for peer {PeerId} (initiated by peer)", peerId);
                return new PeerEncryptionState();
            });

            // Only a byte-identical repeat of the key we already derived from is a true
            // duplicate. A *different* key means the peer restarted and generated a fresh
            // keypair; we must re-derive, or every later message fails to decrypt with an
            // authentication tag mismatch.
            if (state.SharedKey != null && state.PeerPublicKey != null
                && state.PeerPublicKey.AsSpan().SequenceEqual(peerPublicKey))
            {
                logger.LogDebug("Shared key already derived for peer {PeerId}, ignoring duplicate key exchange", peerId);
                return null;
            }

            var previousKey = knownPeerKeys.GetOrAdd(peerId, peerPublicKey);
            if (!previousKey.AsSpan().SequenceEqual(peerPublicKey))
            {
                // Expected whenever the peer relaunches the app, since keypairs are
                // per-session. Informational only — not treated as an attack.
                logger.LogInformation("Public key for peer {PeerId} changed (peer likely restarted). " +
                    "Old fingerprint={OldFingerprint}, new fingerprint={NewFingerprint}",
                    peerId, E2EEncryptionService.ComputeFingerprint(previousKey), E2EEncryptionService.ComputeFingerprint(peerPublicKey));
                knownPeerKeys[peerId] = peerPublicKey;
                PeerKeyChanged?.Invoke(this, new PeerKeyChangedEventArgs(peerId, previousKey, peerPublicKey));
            }

            state.SharedKey = state.Service.DeriveSharedKey(peerPublicKey);
            state.PeerPublicKey = peerPublicKey;
            logger.LogInformation("E2EE shared key derived for peer {PeerId}, fingerprint={Fingerprint}",
                peerId, E2EEncryptionService.ComputeFingerprint(peerPublicKey));

            // Return our public key so the caller can send it back if needed
            return state.NeedsSendKey ? state.Service.GetPublicKey() : null;
        }

        /// <summary>
        /// Marks that we've sent our key to this peer (so we don't send it again).
        /// </summary>
        public void MarkKeySent(string peerId)
        {
            if (peerStates.TryGetValue(peerId, out var state))
                state.NeedsSendKey = false;
        }

        /// <summary>
        /// Returns true if encryption is established (shared key derived) for this peer.
        /// </summary>
        public bool IsEncrypted(string peerId)
        {
            return peerStates.TryGetValue(peerId, out var state) && state.SharedKey != null;
        }

        /// <summary>
        /// Encrypts data for a specific peer. Returns null if encryption is not yet established.
        /// </summary>
        public (byte[] Ciphertext, byte[] Nonce)? Encrypt(string peerId, byte[] plaintext)
        {
            if (!peerStates.TryGetValue(peerId, out var state) || state.SharedKey == null)
                return null;

            return E2EEncryptionService.Encrypt(plaintext, state.SharedKey);
        }

        /// <summary>
        /// Decrypts data from a specific peer. Returns null if encryption is not established.
        /// </summary>
        public byte[]? Decrypt(string peerId, byte[] ciphertext, byte[] nonce)
        {
            if (!peerStates.TryGetValue(peerId, out var state) || state.SharedKey == null)
            {
                logger.LogWarning("Cannot decrypt — no shared key for peer {PeerId}", peerId);
                return null;
            }

            return E2EEncryptionService.Decrypt(ciphertext, nonce, state.SharedKey);
        }

        /// <summary>
        /// Removes encryption state for a peer (e.g., when session ends).
        /// </summary>
        public void RemovePeer(string peerId)
        {
            if (peerStates.TryRemove(peerId, out var state))
            {
                state.Dispose();
                logger.LogDebug("Removed encryption state for peer {PeerId}", peerId);
            }
        }

        public void Dispose()
        {
            foreach (var kvp in peerStates)
                kvp.Value.Dispose();
            peerStates.Clear();
        }

        sealed class PeerEncryptionState : IDisposable
        {
            public E2EEncryptionService Service { get; } = new();
            public byte[]? SharedKey { get; set; }

            /// <summary>The peer key the current <see cref="SharedKey"/> was derived from.</summary>
            public byte[]? PeerPublicKey { get; set; }

            public bool NeedsSendKey { get; set; } = true;

            public void Dispose() => Service.Dispose();
        }
    }

    public sealed class PeerKeyChangedEventArgs : EventArgs
    {
        public string PeerId { get; }
        public byte[] OldPublicKey { get; }
        public byte[] NewPublicKey { get; }

        public PeerKeyChangedEventArgs(string peerId, byte[] oldPublicKey, byte[] newPublicKey)
        {
            PeerId = peerId;
            OldPublicKey = oldPublicKey;
            NewPublicKey = newPublicKey;
        }
    }
}
