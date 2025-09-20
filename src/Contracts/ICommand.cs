using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Contracts;

/// <summary>
/// Marker interface to represent a message with a void response
/// </summary>

public interface ICommand { }

/// <summary>
/// Marker interface to represent a message with a response
/// </summary>
/// <typeparam name="TResponse">Response type</typeparam>
public interface ICommand<out TResponse> : ICommand { }

