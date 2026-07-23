using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CICMessenger.Client;
using CICMessenger.Core.Presence;

namespace CICMessenger.Client
{
    public interface ISelfBuddy: IBuddy
    {
        new string DisplayName { get;  set; }
        new UserStatus Status { get; set; }
    }
}
