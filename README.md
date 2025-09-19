# Faster.MessageBus

![Work in Progress](https://img.shields.io/badge/status-work%20in%20progress-yellow)
![License](https://img.shields.io/badge/license-MIT-blue)

A high-performance, low-allocation messaging library for .NET, built for speed and efficiency in distributed systems.  
It uses a pub/sub model with automatic service discovery, leveraging the power of NetMQ for networking and MessagePack for serialization.

> **Warning:** This project is currently under active development and should be considered experimental.  
> The API is subject to change, and it is not yet recommended for production use.

---

## ✨ Features

- ⚡ **High Performance:** Minimized allocations and reduced GC pressure using `Span<T>`, `ArrayPool<T>`, and `IBufferWriter<T>`.  
- 📢 **Pub/Sub Messaging:** Topic-based publish/subscribe for decoupled, one-to-many event distribution.  
- 🔍 **Automatic Service Discovery:** Nodes auto-discover and form a mesh, simplifying configuration.  
- 🔒 **Thread-Safe:** Dedicated scheduler thread for all network ops; no locks needed in application code.  
- 📦 **Efficient Serialization:** MessagePack + LZ4 compression for fast, compact serialization.  

---

## 🚀 Getting Started

Since this project is a work in progress, it is not yet available on NuGet.  
To use it, clone the repository and build it locally.

```bash
# Clone the repository
git clone https://github.com/your-username/Faster.MessageBus.git

# Navigate to the project directory
cd Faster.MessageBus

# Build the project
dotnet build -c Release
```
## 📚 Core Concepts: Events and Commands

The message bus is designed to handle two primary types of messages: **Events** and **Commands**.

---

### 📢 Events (Pub/Sub)

An **event** notifies other parts of the system that something has happened.  
The publisher doesn’t know or care who is listening.  
This follows a **one-to-many** communication pattern.

- **Use Case:** Notify services when a new user registers, an order is placed, or a process completes.  
- **Implementation:** Events are published to a *topic*. Any service subscribed to that topic receives a copy.  
- **Focus:** The current implementation is heavily optimized for events.  

**Example:**  
The `Ordering` service publishes an `OrderPlaced` event.  

- `Inventory`, `Billing`, and `Shipping` services subscribe and act on it.

---

### 🛠️ Commands (Request/Reply or Fire-and-Forget)

A **command** tells another part of the system to do something.  
Unlike an event, a command is directed at a **specific handler** and often implies an **expectation of action**.  
This follows a **one-to-one** communication pattern.

- **Use Case:** Instructing a service to create a user, submit an order, or process a payment.  
- **Implementation:** While the same pub/sub transport can be used, a command is logically sent to a **single owner/handler**.  
- **Focus:** The architecture is designed to support commands in the future.  

**Example:**  
A `SubmitOrder` command is sent from a web client to the `Ordering` service.  
The `Ordering` service is the sole owner of this command and processes it.

## 🛠️ How to Use

The following example demonstrates how to configure the message bus, define an event, create a handler, and publish the event.

### 1️⃣ Configure Services  

Register the message bus components with your dependency injection container:

```csharp
// In your Program.cs or startup configuration
services.AddMessageBus(options =>
{
    options.PublishPort = 10000; // Starting port for the publisher
    // ... other options
});
```
### 2️⃣ Define an Event  

Events are simple objects that implement the `IEvent` interface.

```csharp
using Faster.MessageBus.Contracts;

public class UserCreatedEvent : IEvent
{
    public Guid UserId { get; set; }
    public string UserName { get; set; }
}
```
### 3️⃣ Create an Event Handler

Create a class that implements `IEventHandler<TEvent>` to process the event.  
The DI container automatically discovers and registers this handler.

```csharp
using Faster.MessageBus.Contracts;

public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    private readonly ILogger<UserCreatedEventHandler> _logger;

    public UserCreatedEventHandler(ILogger<UserCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public void Handle(UserCreatedEvent @event)
    {
        _logger.LogInformation($"New user created! ID: {@event.UserId}, Name: {@event.UserName}");
        // ... add business logic here ...
    }
}
```
### 4️⃣ Publish the Event

Inject `IEventDispatcher` into any service and use it to publish events.

```csharp
public class UserService
{
    private readonly IEventDispatcher _eventDispatcher;

    public UserService(IEventDispatcher eventDispatcher)
    {
        _eventDispatcher = eventDispatcher;
    }

    public void CreateUser(string name)
    {
        var newUserEvent = new UserCreatedEvent
        {
            UserId = Guid.NewGuid(),
            UserName = name
        };

        // Dispatch the event to the message bus
        _eventDispatcher.Publish(newUserEvent);
    }
}
```
## How to Use (Commands)

Here is how you would define, handle, and dispatch a command.

---

### 1️⃣ Define a Command

Commands are objects that implement the `ICommand` interface.

```csharp
using Faster.MessageBus.Contracts;

public class SubmitOrderCommand : ICommand
{
    public Guid OrderId { get; set; }
    public string CustomerName { get; set; }
}
```
### 2️⃣ Create a Command Handler

Create a class that implements `ICommandHandler<TCommand>`.  
Each command must have exactly **one handler**.

```csharp
using Faster.MessageBus.Contracts;

public class SubmitOrderCommandHandler : ICommandHandler<SubmitOrderCommand>
{
    private readonly ILogger<SubmitOrderCommandHandler> _logger;

    public SubmitOrderCommandHandler(ILogger<SubmitOrderCommandHandler> logger)
    {
        _logger = logger;
    }

    public void Handle(SubmitOrderCommand command)
    {
        _logger.LogInformation($"Processing order {command.OrderId}: {command.Quantity} x {command.Product}");
        // ... add business logic to process the order ...
    }
}
```
### 3️⃣ Dispatch the Command

Inject `ICommandDispatcher` into any service to send commands.

```csharp
public class OrderService
{
    private readonly ICommandDispatcher _commandDispatcher;

    public OrderService(ICommandDispatcher commandDispatcher)
    {
        _commandDispatcher = commandDispatcher;
    }

    public void SubmitOrder(string product, int quantity)
    {
        var command = new SubmitOrderCommand
        {
            OrderId = Guid.NewGuid(),
            Product = product,
            Quantity = quantity
        };

        // Send the command to the handler
        _commandDispatcher.Send(command);
    }
}
```

## 🛤️ Roadmap


This project is evolving. Future plans include:

[X] Complete the Command (Request/Reply) pattern implementation.
[ ] Enhanced error handling and resilience (e.g., dead-letter queues).
[ ] Comprehensive performance benchmarking.
[ ] More detailed documentation and examples.
[ ] Official release on NuGet.

---

## 🤝 Contributing

Contributions are welcome! 🎉  
If you'd like to contribute, please open an issue first to discuss proposed changes.

