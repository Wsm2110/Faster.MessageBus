namespace Faster.MessageBus.Contracts
{
    public interface IMeshApplication
    {
        string Address { get; }
        string ApplicationName { get; }
        string ClusterName { get; set; }
        ulong[] CommandRoutingTable { get; set; }
        ushort PubPort { get; }
        ushort RpcPort { get; }     
    }
}