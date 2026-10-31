using System;
using System.Collections.Generic;

namespace PCL2Swap.Api
{
    /// <summary>One structured entry from the in-process Swap diagnostic timeline.</summary>
    public sealed class SwapDiagnosticEvent
    {
        public DateTime TimestampUtc { get; set; }
        public string Level { get; set; }
        public SwapHintType Severity { get; set; }
        public string Message { get; set; }
        public string ModuleId { get; set; }
    }

    /// <summary>Read-only access to the bounded runtime diagnostic timeline.</summary>
    public interface IDiagnosticsAccess
    {
        IReadOnlyList<SwapDiagnosticEvent> Recent(int limit = 100);
        string Export(int limit = 200);
        event EventHandler Changed;
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
