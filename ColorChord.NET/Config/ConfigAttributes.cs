using System;

namespace ColorChord.NET.Config
{
    /// <summary>This should not be used directly, instead it is the parent of the type-specific configurable attributes.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ConfigAttribute : Attribute
    {
        public string Name { get; private set; }
        public ConfigAttribute(string name) { this.Name = name; }
    }

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

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigStringAttribute : ConfigAttribute
    {
        public string DefaultValue { get; private set; }

        public ConfigStringAttribute(string name, string defaultValue) : base(name)
        {
            this.DefaultValue = defaultValue;
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ConfigBoolAttribute : ConfigAttribute
    {
        public bool DefaultValue { get; private set; }

        public ConfigBoolAttribute(string name, bool defaultValue) : base(name)
        {
            this.DefaultValue = defaultValue;
        }
    }
}
