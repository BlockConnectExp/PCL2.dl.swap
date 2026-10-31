using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Signature-aware hook target. Parameter type names accept full names ("System.String"),
    /// short names ("String") or C# aliases ("string"); the host resolves exactly one method.
    /// </summary>
    public sealed class SwapMethodTarget
    {
        public string DeclaringType { get; set; }
        public string MethodName { get; set; }
        public List<string> ParameterTypes { get; set; } = new List<string>();
        public string ReturnType { get; set; }
        public bool? Static { get; set; }

        public override string ToString()
        {
            var parameters = ParameterTypes == null ? "?" : string.Join(", ", ParameterTypes);
            return (DeclaringType ?? "?") + "." + (MethodName ?? "?") +
                   "(" + parameters + ")" +
                   (string.IsNullOrWhiteSpace(ReturnType) ? string.Empty : " : " + ReturnType);
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
