using System.Collections.Generic;

namespace PCL2Swap.Api
{
    public sealed class SurfaceMethodInfo
    {
        public string Name { get; set; }
        public bool Static { get; set; }
        public bool Virtual { get; set; }
        public bool Abstract { get; set; }
        public string Visibility { get; set; }
        public string ReturnType { get; set; }
        public IReadOnlyList<string> Parameters { get; set; }
        public string Token { get; set; }
    }

    public sealed class SurfacePropertyInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Get { get; set; }
        public bool Set { get; set; }
        public bool Static { get; set; }
    }

    public sealed class SurfaceFieldInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Static { get; set; }
        public string Visibility { get; set; }
        public bool Literal { get; set; }
    }

    public sealed class SurfaceTypeInfo
    {
        public string FullName { get; set; }
        public string Name { get; set; }
        public string Namespace { get; set; }
        public string Kind { get; set; }
        public string BaseType { get; set; }
        public string Visibility { get; set; }
        public IReadOnlyList<SurfaceMethodInfo> Methods { get; set; }
        public IReadOnlyList<SurfacePropertyInfo> Properties { get; set; }
        public IReadOnlyList<SurfaceFieldInfo> Fields { get; set; }
    }

    /// <summary>
    /// Runtime view of the PCL callable surface (see tools/PclSurface): every type and member the
    /// running host carries, including internal members the UI never exposes. Look a member up
    /// here, then reach it through <see cref="ISwapApi.Reflect"/>.
    /// </summary>
    public interface ISwapSurface
    {
        /// <summary>MVID of the host build this surface describes (empty when unavailable).</summary>
        string HostMvid { get; }
        int TypeCount { get; }
        IReadOnlyList<SurfaceTypeInfo> Types { get; }
        SurfaceTypeInfo FindType(string fullName);
        SurfaceMethodInfo FindMethod(string typeFullName, string methodName, int parameterCount = -1);
        IReadOnlyList<SurfaceTypeInfo> Search(string text, int limit = 50);
    }
}


// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
