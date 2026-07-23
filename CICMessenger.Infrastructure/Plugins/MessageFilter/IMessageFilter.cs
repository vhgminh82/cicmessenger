using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Plugins.MessageFilter
{
    public interface IMessageFilter
    {
        FilterDirection Direction { get; }
        bool Filter(StringBuilder message, IChatWindow window);
    }
}
