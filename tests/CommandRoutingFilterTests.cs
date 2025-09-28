using Xunit;
using Faster.MessageBus.Features.Commands;
using System;

namespace UnitTests;

public class CommandRoutingFilterTests
{
    [Fact]
    public void Add_and_MightContain_basic_usage()
    {
        var filter = new CommandRoutingFilter();
        filter.Initialize(100);
        ulong hash = 0x123456789ABCDEF0UL;
        Assert.False(filter.MightContain(hash));
        filter.Add(hash);
        Assert.True(filter.MightContain(hash));
    }

    [Fact]
    public void MightContain_false_for_unadded_hash()
    {
        var filter = new CommandRoutingFilter();
        filter.Initialize(100);
        ulong hash1 = 0x1111111111111111UL;
        ulong hash2 = 0x2222222222222222UL;
        filter.Add(hash1);
        Assert.False(filter.MightContain(hash2));
    }

    [Fact]
    public void Add_Duplicates()
    {
        var filter = new CommandRoutingFilter();
        filter.Initialize(100);
        ulong hash = 0xDEADBEEFDEADBEEFUL;
        filter.Add(hash);
        filter.Add(hash);
        Assert.True(filter.MightContain(hash));
    }

    [Fact]
    public void TryContains_static_works()
    {
        var filter = new CommandRoutingFilter();
        filter.Initialize(100);
        ulong hash = 0xCAFEBABECAFEBABEUL;
        filter.Add(hash);
        var bitsField = typeof(CommandRoutingFilter).GetField("_bits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bits = (byte[])bitsField.GetValue(filter);
        Assert.True(CommandRoutingFilter.TryContains(bits, hash));
        Assert.False(CommandRoutingFilter.TryContains(bits, 0x1234567890ABCDEFUL));
    }

    [Fact]
    public void Initialize_with_different_false_positive_rate_affects_size()
    {
        var filter1 = new CommandRoutingFilter();
        filter1.Initialize(100, 0.01);
        var filter2 = new CommandRoutingFilter();
        filter2.Initialize(100, 0.0001);
        var bitsField = typeof(CommandRoutingFilter).GetField("_bits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bits1 = (byte[])bitsField.GetValue(filter1);
        var bits2 = (byte[])bitsField.GetValue(filter2);
        Assert.True(bits2.Length > bits1.Length);
    }

    [Fact]
    public void Add_null_bits_throws()
    {
        var filter = new CommandRoutingFilter();
        // _bits is null before Initialize
        Assert.Throws<NullReferenceException>(() => filter.Add(0x123UL));
    }

    [Fact]
    public void MightContain_null_bits_throws()
    {
        var filter = new CommandRoutingFilter();
        // _bits is null before Initialize
        Assert.Throws<NullReferenceException>(() => filter.MightContain(0x123UL));
    }

    [Fact]
    public void Initialize_zero_length_creates_minimum_size()
    {
        var filter = new CommandRoutingFilter();
        filter.Initialize(0);
        var bitsField = typeof(CommandRoutingFilter).GetField("_bits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bits = (byte[])bitsField.GetValue(filter);
        Assert.True(bits.Length > 0);
    }

    [Fact]
    public void Add_and_MightContain_extreme_hash_values()
    {
        var filter = new CommandRoutingFilter();
        filter.Initialize(10);
        ulong max = ulong.MaxValue;
        ulong min = ulong.MinValue;
        filter.Add(max);
        filter.Add(min);
        Assert.True(filter.MightContain(max));
        Assert.True(filter.MightContain(min));
    }

    [Fact]
    public void TryContains_with_null_bits_throws()
    {
        Assert.Throws<NullReferenceException>(() => CommandRoutingFilter.TryContains(null, 0x123UL));
    }
}
