using System;
using System.ComponentModel;
using System.IO;
using CICMessenger.Core.Chat.Activity;

namespace CICMessenger.Client.Activities
{
    public interface IFileTransferHandler: IActivityHandler
    {
        long Size { get; }
        string Name { get; }

        void Accept(string filePath);
    }
}
