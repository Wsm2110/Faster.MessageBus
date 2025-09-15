using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Features.Events.Contracts;

internal interface IEventSerializer
{
    object? Deserialize(byte[] data, Type type);
    T Deserialize<T>(byte[] data);
    byte[] Serialize<T>(T obj);

}

