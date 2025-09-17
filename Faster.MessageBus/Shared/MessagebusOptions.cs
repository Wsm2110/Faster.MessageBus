using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Shared;

public class MessageBusOptions
{
    /// <summary>
    /// Gets or sets the node timeout in seconds before considered inactive. Default is 60s.
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    public int RPCPort { get; set; }

    public int PubishPort {  get; set; }

    public TimeSpan MesssageTimeout { get; set; } = TimeSpan.FromSeconds(1);
}

