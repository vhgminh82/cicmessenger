using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CICMessenger.FileTransfer
{
    /// <summary>
    /// Serializes/deserializes file transfer invite metadata (name and size)
    /// exchanged during the gRPC activity invitation handshake.
    /// </summary>
    class FileInviteData : IEnumerable<KeyValuePair<string, string>>
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }

        public FileInviteData() { }

        public FileInviteData(IEnumerable<KeyValuePair<string, string>> data)
        {
            var dictionary = data.ToDictionary(i => i.Key, i => i.Value);

            if (dictionary.TryGetValue("name", out var name))
                Name = Sanitize(name);

            if (dictionary.TryGetValue("size", out var sizeStr) && long.TryParse(sizeStr, out var size))
                Size = size;
        }

        /// <summary>
        /// Strips any directory component from a peer-supplied file name so callers can
        /// safely combine it with a local download folder without risking path traversal
        /// (e.g. a malicious peer sending "..\..\evil.exe" as the file name).
        /// </summary>
        static string Sanitize(string name)
        {
            var fileName = Path.GetFileName(name);
            return string.IsNullOrWhiteSpace(fileName) ? "unnamed_file" : fileName;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            yield return new KeyValuePair<string, string>("name", Name);
            yield return new KeyValuePair<string, string>("size", Size.ToString());
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
