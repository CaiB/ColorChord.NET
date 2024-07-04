namespace ColorChord.NET.API.Utility;

public interface IThreadedInstance
{
    /// <summary> This method is called on the managed instance thread after the constructor is finished. </summary>
    /// <remarks> For example, this is useful if you need to run a continued loop on the instance thread. </remarks>
    public void InstThreadPostInit();
}
