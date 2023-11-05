using System;

namespace ColorChord.NET.API.Config;

/// <summary>This should not be used directly, instead it is the parent of the type-specific configurable attributes like <see cref="ConfigIntAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ConfigAttribute : Attribute
{
    public string Name { get; private set; }
    public ConfigAttribute(string name) { this.Name = name; }
}

/// <summary>Used to get integer values from the config. Supports all integral numeric types, signed or unsigned.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigIntAttribute : ConfigAttribute
{
    public long MinValue { get; private set; }
    public long MaxValue { get; private set; }
    public long DefaultValue { get; private set; }

    public ConfigIntAttribute(string name, long minValue, long maxValue, long defaultValue) : base(name)
    {
        this.MinValue = minValue;
        this.MaxValue = maxValue;
        this.DefaultValue = defaultValue;
    }
}

/// <summary>Used to get floating-point values from the config. Supports any floating-point types, but precision is limited to float32.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigFloatAttribute : ConfigAttribute
{
    public float MinValue { get; private set; }
    public float MaxValue { get; private set; }
    public float DefaultValue { get; private set; }

    public ConfigFloatAttribute(string name, float minValue, float maxValue, float defaultValue) : base(name)
    {
        this.MinValue = minValue;
        this.MaxValue = maxValue;
        this.DefaultValue = defaultValue;
    }
}

/// <summary>Used to get a string value from the config. Note that there is currently no validity checking on string values.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigStringAttribute : ConfigAttribute
{
    public string DefaultValue { get; private set; }

    // TODO: Create string validity check system, perhaps via a custom check method delegate?

    public ConfigStringAttribute(string name, string defaultValue) : base(name)
    {
        this.DefaultValue = defaultValue;
    }
}

/// <summary>Used to get a boolean value from the config.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigBoolAttribute : ConfigAttribute
{
    public bool DefaultValue { get; private set; }

    public ConfigBoolAttribute(string name, bool defaultValue) : base(name)
    {
        this.DefaultValue = defaultValue;
    }
}

/// <summary>Used to get a list of string values from the config.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ConfigStringListAttribute : ConfigAttribute
{
    public ConfigStringListAttribute(string name) : base(name) { }
}