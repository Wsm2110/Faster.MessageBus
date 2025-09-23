using Faster.MessageBus.Features.Commands.Contracts;
using NetMQ;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Faster.MessageBus.Shared;

public static class NetMqUtils
{
    public static void DisposePollerSafely(
        NetMQPoller poller,
        Thread pollerThread,
        NetMQQueue<Action<NetMQPoller>> actions,     
        int timeoutMs = 500)
    {
        if (poller == null || pollerThread == null)
            return;

        try
        {
            actions.Enqueue(poller => poller.Stop());

            // Step 2: wait for thread exit with timeout
            if (pollerThread.IsAlive)
            {
                if (!pollerThread.Join(timeoutMs))
                {
                    // Timeout hit — log or take emergency action
                    Console.Error.WriteLine("⚠ Poller did not stop within timeout.");
                    try { poller.StopAsync(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error stopping poller: {ex}");
        }

        // Step 4: cleanup poller and queue
        poller.Dispose();
    }
} 