using CICMessenger.Client;
using CICMessenger.Plugins;
using System;
namespace CICMessenger.Plugins
{
    public interface ICICMessengerContext
    {
        IChatClient ChatClient { get; set; }
        IMainWindow MainWindow { get; set; }
    }
}
