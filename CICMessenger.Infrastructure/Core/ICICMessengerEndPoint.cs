using System;
namespace CICMessenger.Core
{
    public interface ICICMessengerEndPoint
    {
        System.Net.IPEndPoint Address { get; set; }
        string ClientID { get; set; }
    }
}
