using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Plugins
{
    public interface IExtension
    {
        void Start(ICICMessengerContext context);
    }
}
