using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using System.Collections.Concurrent;
using static Faster.MessageBus.Shared.RuntimeUnionResolver;

namespace Faster.MessageBus.Shared;

#region Standalone Formatter Helper Class (Unchanged)

/// <summary>
/// Internal helper to create a strongly-typed formatter wrapper.
/// It is not nested to simplify reflection during instantiation.
/// </summary>
internal sealed class UnionTypeFormatter<TInterface, TConcrete> : RuntimeUnionFormatter<TInterface>.IUnionTypeFormatter
    where TInterface : class
    where TConcrete : TInterface
{
    public void Serialize(ref MessagePackWriter writer, object value, MessagePackSerializerOptions options)
    {
        options.Resolver.GetFormatter<TConcrete>().Serialize(ref writer, (TConcrete)value, options);
    }

    public object Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        return options.Resolver.GetFormatter<TConcrete>().Deserialize(ref reader, options);
    }
}

#endregion

#region Optimized Formatter (Unchanged)

/// <summary>
/// A high-performance, runtime union formatter that uses pre-built maps for serialization and deserialization.
/// It uses fast array lookups for deserialization and a dictionary for serialization to avoid reflection.
/// </summary>
/// <typeparam name="T">The interface type being handled.</typeparam>
public sealed class RuntimeUnionFormatter<T> : IMessagePackFormatter<T>
{
    private readonly IReadOnlyDictionary<Type, ushort> _typeToKeyMap;
    private readonly IUnionTypeFormatter[] _keyToFormatterMap;

    public RuntimeUnionFormatter(IReadOnlyDictionary<Type, ushort> typeToKeyMap, IUnionTypeFormatter[] keyToFormatterMap)
    {
        _typeToKeyMap = typeToKeyMap ?? throw new ArgumentNullException(nameof(typeToKeyMap));
        _keyToFormatterMap = keyToFormatterMap ?? throw new ArgumentNullException(nameof(keyToFormatterMap));
    }

    public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNil();
            return;
        }

        var type = value.GetType();
        if (!_typeToKeyMap.TryGetValue(type, out ushort key))
        {
            throw new MessagePackSerializationException($"The type '{type.FullName}' is not registered for interface '{typeof(T).FullName}'.");
        }

        var formatter = _keyToFormatterMap[key];

        writer.WriteArrayHeader(2);
        writer.Write(key);
        formatter.Serialize(ref writer, value, options);
    }

    public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return default;
        }

        var count = reader.ReadArrayHeader();
        if (count != 2)
        {
            throw new MessagePackSerializationException("Invalid array header count for a union type.");
        }

        ushort key = reader.ReadUInt16();

        if (key >= _keyToFormatterMap.Length || _keyToFormatterMap[key] == null)
        {
            throw new MessagePackSerializationException($"The key '{key}' is not registered for interface '{typeof(T).FullName}'.");
        }

        var formatter = _keyToFormatterMap[key];
        return (T)formatter.Deserialize(ref reader, options);
    }

    public interface IUnionTypeFormatter
    {
        void Serialize(ref MessagePackWriter writer, object value, MessagePackSerializerOptions options);
        object Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options);
    }
}

#endregion

#region Optimized Resolver (Updated with Bugfix)

/// <summary>
/// A high-performance resolver for interface-based unions, supporting both specific and open generic interfaces.
/// It requires explicit registration of types, eliminating slow assembly scanning at startup.
/// </summary>
public sealed class RuntimeUnionResolver : IFormatterResolver
{
    /// <summary>
    /// The singleton instance that can be used.
    /// </summary>
    public static readonly RuntimeUnionResolver Instance = new RuntimeUnionResolver();

    public static readonly RegistrationContext Context = new RegistrationContext();


    private readonly IFormatterResolver _fallbackResolver = ContractlessStandardResolver.Instance;
    
    public IMessagePackFormatter<T> GetFormatter<T>()
    {
        var targetType = typeof(T);

        // 1. Check for an exact match (non-generic interfaces)
        if (Context.GetFormatters().TryGetValue(targetType, out var formatter))
        {
            return (IMessagePackFormatter<T>)formatter;
        }

        // 2. Check if it's a constructed generic type whose definition is registered
        if (targetType.IsGenericType)
        {
            var genericDefinition = targetType.GetGenericTypeDefinition();
            if (Context.GetGenericRegistrars().TryGetValue(genericDefinition, out var registrar))
            {              
                return registrar.GetOrBuildFormatter<T>(targetType);
            }
        }

        // 3. Fallback for all other types
        return _fallbackResolver.GetFormatter<T>();
    }

    #region Registration Builder & Interfaces

    public sealed class RegistrationContext
    {
        private readonly Dictionary<Type, object> _formatters = new();
        private readonly Dictionary<Type, IInternalGenericRegistrar> _genericRegistrars = new();

        public void RegisterInterface<TInterface>(Action<InterfaceRegistrar<TInterface>> registrationAction) where TInterface : class
        {
            if (!typeof(TInterface).IsInterface)
                throw new ArgumentException($"Type '{typeof(TInterface).FullName}' must be a non-generic interface.");
            if (typeof(TInterface).IsGenericType)
                throw new ArgumentException("Use RegisterGenericInterface for generic interfaces.");

            var registrar = new InterfaceRegistrar<TInterface>();
            registrationAction(registrar);
            _formatters[typeof(TInterface)] = registrar.BuildFormatter();
        }

        public void RegisterGenericInterface(Type openGenericInterfaceType, Action<IGenericInterfaceRegistrar> registrationAction)
        {
            if (!openGenericInterfaceType.IsInterface || !openGenericInterfaceType.IsGenericTypeDefinition)
                throw new ArgumentException($"Type '{openGenericInterfaceType.FullName}' must be an open generic interface definition (e.g., typeof(ICommand<>)).");

            var registrar = new GenericInterfaceRegistrar(openGenericInterfaceType);
            registrationAction(registrar);
            _genericRegistrars[openGenericInterfaceType] = registrar;
        }

        internal IReadOnlyDictionary<Type, object> GetFormatters() => _formatters;
        internal IReadOnlyDictionary<Type, IInternalGenericRegistrar> GetGenericRegistrars() => _genericRegistrars;
    }

    public interface IGenericInterfaceRegistrar
    {
        void Register<TConcrete>(ushort key);
    }

    public interface IInternalGenericRegistrar
    {
        IMessagePackFormatter<TClosedInterface> GetOrBuildFormatter<TClosedInterface>(Type type);
    }

    public sealed class InterfaceRegistrar<TInterface> where TInterface : class
    {
        private readonly Dictionary<Type, ushort> _typeToKeyMap = new();
        private readonly Dictionary<ushort, Type> _keyToTypeMap = new();
        private ushort _maxKey = 0;

        public void Register<TConcrete>(ushort key) where TConcrete : TInterface
        {
            var type = typeof(TConcrete);
            if (_keyToTypeMap.ContainsKey(key)) throw new ArgumentException($"Key '{key}' is already registered for type '{_keyToTypeMap[key].FullName}'.");
            if (_typeToKeyMap.ContainsKey(type)) throw new ArgumentException($"Type '{type.FullName}' is already registered with key '{_typeToKeyMap[type]}'.");
            _typeToKeyMap.Add(type, key); _keyToTypeMap.Add(key, type); if (key > _maxKey) _maxKey = key;
        }

        internal IMessagePackFormatter<TInterface> BuildFormatter()
        {
            var keyToFormatterMap = new RuntimeUnionFormatter<TInterface>.IUnionTypeFormatter[_maxKey + 1];
            var formatterGenericType = typeof(UnionTypeFormatter<,>);
            foreach (var (key, type) in _keyToTypeMap)
            {
                var formatterClosedType = formatterGenericType.MakeGenericType(typeof(TInterface), type);
                keyToFormatterMap[key] = (RuntimeUnionFormatter<TInterface>.IUnionTypeFormatter)Activator.CreateInstance(formatterClosedType);
            }
            return new RuntimeUnionFormatter<TInterface>(_typeToKeyMap, keyToFormatterMap);
        }
    }

    internal sealed class GenericInterfaceRegistrar : IGenericInterfaceRegistrar, IInternalGenericRegistrar
    {
        private readonly Type _openGenericDefinition;
        private readonly ConcurrentDictionary<Type, object> _builtFormatters = new();
        private readonly Dictionary<Type, ushort> _typeToKeyMap = new();
        private readonly Dictionary<ushort, Type> _keyToTypeMap = new();
        private ushort _maxKey = 0;

        public GenericInterfaceRegistrar(Type openGenericDefinition) => _openGenericDefinition = openGenericDefinition;

        public void Register<TConcrete>(ushort key)
        {
            var type = typeof(TConcrete);
            var implements = type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == _openGenericDefinition);
            if (!implements) throw new ArgumentException($"Type '{type.FullName}' does not implement the generic interface '{_openGenericDefinition.FullName}'.");

            if (_keyToTypeMap.ContainsKey(key)) throw new ArgumentException($"Key '{key}' is already registered for type '{_keyToTypeMap[key].FullName}'.");
            if (_typeToKeyMap.ContainsKey(type)) throw new ArgumentException($"Type '{type.FullName}' is already registered with key '{_typeToKeyMap[type]}'.");
            _typeToKeyMap.Add(type, key); _keyToTypeMap.Add(key, type); if (key > _maxKey) _maxKey = key;
        }

        public IMessagePackFormatter<TClosedInterface> GetOrBuildFormatter<TClosedInterface>(Type type) 
        {
            return (IMessagePackFormatter<TClosedInterface>)_builtFormatters.GetOrAdd(type, _ =>
            {
                var keyToFormatterMap = new RuntimeUnionFormatter<TClosedInterface>.IUnionTypeFormatter[_maxKey + 1];
                var formatterGenericType = typeof(UnionTypeFormatter<,>);

                foreach (var (key, concreteType) in _keyToTypeMap)
                {
                    if (typeof(TClosedInterface).IsAssignableFrom(concreteType))
                    {
                        var formatterClosedType = formatterGenericType.MakeGenericType(typeof(TClosedInterface), concreteType);
                        keyToFormatterMap[key] = (RuntimeUnionFormatter<TClosedInterface>.IUnionTypeFormatter)Activator.CreateInstance(formatterClosedType);
                    }
                }
                return new RuntimeUnionFormatter<TClosedInterface>(new Dictionary<Type, ushort>(_typeToKeyMap), keyToFormatterMap);
            });
        }
    }

    #endregion
}

#endregion
