using System;

namespace ColorChord.NET.API.Controllers;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ControllableAttribute : Attribute
{
    /// <summary>The name used to refer to this specific setting in the class.</summary>
    public string ControlName { get; private init; }
    /// <summary>An ID passed to the callback function when this setting gets changed.</summary>
    public int ControlID { get; private init; } = 0;
    /// <summary>Whether or not a change of this setting should cause the callback to be invoked.</summary>
    public bool NeedsCallback { get => CheckNeedsCallback(this.ControlID); }

    /// <summary>Exposes the attached field/property to controllers for runtime settings changes.</summary>
    /// <remarks>Using this constructor means that when the setting gets changed, no callback occurs.</remarks>
    /// <param name="name">The name that this setting goes by. Should be the same as the <see cref="Config.ConfigAttribute">Config*</see>'s name</param>
    public ControllableAttribute(string name) { this.ControlName = name; }

    /// <summary>Exposes the attached field/property to controllers for runtime settings changes, and enables the callback to be called when a change occurs.</summary>
    /// <param name="name">The name that this setting goes by. Should be the same as the <see cref="Config.ConfigAttribute">Config*</see>'s name</param>
    /// <param name="controlID">The ID that will be passed to the <see cref="IControllableAttr.SettingChanged(int)"/> callback as a parameter when the attached setting gets changed. Must not be 0</param>
    /// <exception cref="ArgumentException">Thrown if controlID is set to 0</exception>
    public ControllableAttribute(string name, int controlID)
    {
        if (controlID == 0) { throw new ArgumentException("Controllable attribute must not have " + nameof(controlID) + " set to 0."); }
        this.ControlName = name;
        this.ControlID = controlID;
    }

    /// <summary>Whether or not a change of a setting should cause the <see cref="IControllableAttr.SettingChanged(int)"/> callback to be invoked.</summary>
    /// <returns>Whether the callback needs to be invoked when this setting is changed</returns>
    /// <param name="controlID">The <see cref="ControlID"/> value of the controllable setting</param>
    public static bool CheckNeedsCallback(int controlID) => controlID != 0;
}
